using EPiServer.Data;
using EPiServer.Data.Dynamic;
using EPiServer.DataAbstraction;
using EPiServer.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Permissions;

/// <summary>
/// Grants every Graph Search Tools <see cref="PermissionType"/> to the
/// configured <see cref="GraphSearchtoolsOptions.AuthorizedRoles"/> on first
/// run, so the default <c>CheckPermissionForEachFeature: true</c> never locks
/// anyone out of a fresh install.
/// </summary>
/// <remarks>
/// <para>The seeder is idempotent per-permission. For each
/// <see cref="GraphSearchtoolsPermissions.All"/> entry it:</para>
/// <list type="number">
///   <item><description>Skips the permission if a <see cref="PermissionSeederMarker"/>
///   already exists for it. The marker records "I've considered this
///   permission" — set both when the seeder writes grants and when it
///   intentionally leaves existing grants alone.</description></item>
///   <item><description>Reads the current grants. If the permission already
///   has any role grants, it leaves them alone (assumes the host has
///   configured this manually) and records a no-op marker.</description></item>
///   <item><description>Otherwise grants the permission to every role in
///   <see cref="GraphSearchtoolsOptions.AuthorizedRoles"/> and records a
///   marker. An admin who later wipes a permission's grants and restarts
///   will *not* see them re-seeded — the marker prevents that.</description></item>
/// </list>
/// <para>When <see cref="GraphSearchtoolsOptions.CheckPermissionForEachFeature"/>
/// is <c>false</c> the seeder does nothing — the per-permission layer is off,
/// so there's nothing to gate. If a host later flips the flag on, the seeder
/// runs at the next boot and primes the unseeded permissions.</para>
/// <para>EPiServer's <c>IDatabaseExecutor</c> + DynamicDataStore both refuse
/// calls from a thread that doesn't own a scoped DI context. The seeder
/// therefore opens a fresh service scope inside <see cref="StartAsync"/> and
/// wraps the actual DB work in <c>IDatabaseExecutor.Execute</c> so the
/// thread-safety guard sees a legitimate connection scope. Without that
/// wrapping the seeder hits
/// "Call on database executor not created on current context and on
/// different thread" on every permission.</para>
/// </remarks>
public sealed class PermissionSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PermissionSeeder> _logger;

    public PermissionSeeder(IServiceScopeFactory scopeFactory, ILogger<PermissionSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Run on a worker thread so a slow DB doesn't delay app start.
        // Failures are logged but never bubble up — the addon's other gates
        // (the AuthorizedRoles policy) still keep the surfaces sane.
        _ = Task.Run(() => SeedAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task SeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var opts = sp.GetRequiredService<IOptions<GraphSearchtoolsOptions>>().Value;
            if (!opts.CheckPermissionForEachFeature)
            {
                _logger.LogDebug("PermissionSeeder: CheckPermissionForEachFeature is false; nothing to seed.");
                return Task.CompletedTask;
            }

            var roles = opts.AuthorizedRoles ?? Array.Empty<string>();
            if (roles.Length == 0)
            {
                _logger.LogWarning("PermissionSeeder: AuthorizedRoles is empty; cannot seed grants.");
                return Task.CompletedTask;
            }

            var entities = roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => new SecurityEntity(r, SecurityEntityType.Role))
                .ToArray();

            var executor = sp.GetRequiredService<IDatabaseExecutor>();
            var permissions = sp.GetRequiredService<PermissionRepository>();

            // Sync IDatabaseExecutor.Execute keeps the whole seeding pass on
            // one thread so EPiServer's connection-scope guard doesn't trip.
            // The async variant fails as soon as `await` continues on a
            // worker-pool thread that doesn't own the scope. Bootstrap-time
            // blocking on the few async repository calls is fine — this
            // runs once per process, off the main thread.
            executor.Execute(() =>
            {
                var store = DynamicDataStoreFactory.Instance?.GetStore(typeof(PermissionSeederMarker))
                    ?? DynamicDataStoreFactory.Instance?.CreateStore(typeof(PermissionSeederMarker));
                if (store == null)
                {
                    _logger.LogWarning("PermissionSeeder: marker DDS unavailable; skipping seeding.");
                    return;
                }

                foreach (var perm in GraphSearchtoolsPermissions.All)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    try
                    {
                        SeedOne(permissions, store, perm, entities);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "PermissionSeeder: failed seeding {Permission}; continuing with the rest.",
                            perm.Name);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PermissionSeeder: aborted before any permission was seeded.");
        }
        return Task.CompletedTask;
    }

    private void SeedOne(
        PermissionRepository permissions,
        DynamicDataStore store,
        PermissionType perm,
        SecurityEntity[] entities)
    {
        // Marker check — fast path. The marker survives restarts so the seeder
        // never re-runs against a permission it has already considered, even
        // if an admin later revokes the grants we wrote.
        var existingMarker = store.Items<PermissionSeederMarker>()
            .FirstOrDefault(m => m.PermissionName == perm.Name);
        if (existingMarker != null) return;

        // No marker yet → first encounter with this permission. If the host
        // has already configured grants manually, leave them alone; we only
        // seed empty permissions. PermissionRepository is async-only, but
        // we're inside IDatabaseExecutor.Execute on a worker thread — block
        // is safe here and keeps the connection scope intact.
        var grants = permissions.GetPermissionsAsync(perm)
            .GetAwaiter().GetResult()?.ToList()
            ?? new List<SecurityEntity>();

        if (grants.Count == 0 && entities.Length > 0)
        {
            permissions.SavePermissionsAsync(perm, entities)
                .GetAwaiter().GetResult();
            _logger.LogInformation(
                "PermissionSeeder: granted {Permission} to {Roles}.",
                perm.Name,
                string.Join(", ", entities.Select(e => e.Name)));
            store.Save(new PermissionSeederMarker
            {
                PermissionName = perm.Name,
                SeededAt = DateTime.UtcNow,
                DidGrant = true
            });
        }
        else
        {
            // Either grants already exist or we have no roles to grant to —
            // either way, record the marker so future boots skip this perm.
            store.Save(new PermissionSeederMarker
            {
                PermissionName = perm.Name,
                SeededAt = DateTime.UtcNow,
                DidGrant = false
            });
        }
    }
}
