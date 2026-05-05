using System.Security.Claims;
using EPiServer.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 2.5 §4.2: <see cref="SynonymsApiController"/> accepts a
/// <c>profileKey</c> query parameter that resolves the synonym slot from the
/// registered profile, and appends a <see cref="SearchProfileEdit"/> audit row
/// (Kind = "Synonym") on every successful write. Legacy <c>language</c>-only
/// callers still hit the global slot.
/// </summary>
public class SynonymsApiControllerProfileScopeTests
{
    [Fact]
    public async Task Write_WithProfileKey_UsesProfileSlot_AndAppendsAudit()
    {
        var profile = new SearchProfile
        {
            Key = "site-search",
            DisplayName = LocalizedString.Literal("Site search"),
            Locales = new[] { "en" },
            SynonymSlot = "site"
        };
        var registry = new StaticRegistry(profile);
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Strict);
        SynonymsRequest? captured = null;
        graph.Setup(c => c.UpdateSynonymsAsync(It.IsAny<SynonymsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<SynonymsRequest, CancellationToken>((r, _) => captured = r);

        var edits = new RecordingEditService();
        var controller = NewController(registry, graph.Object, edits);

        var request = new SynonymsRequest
        {
            Content = "phone, mobile, cell",
            // Client-side slot intentionally bogus — profile.SynonymSlot wins.
            Slot = "ignored",
            LanguageRouting = "en"
        };

        var result = await controller.Update(request, profileKey: "site-search", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        captured.Should().NotBeNull();
        captured!.Slot.Should().Be("site", because: "the profile's SynonymSlot must override any client-supplied slot");
        captured.LanguageRouting.Should().Be("en");

        edits.Saved.Should().ContainSingle();
        var entry = edits.Saved[0];
        entry.ProfileKey.Should().Be("site-search");
        entry.Kind.Should().Be("Synonym");
        entry.Action.Should().Be("Updated");
        entry.Locale.Should().Be("en");
        entry.Subject.Should().Contain("phone");
    }

    [Fact]
    public async Task Write_WithProfileKey_OnGenericProfile_Returns400()
    {
        // The synthesized Generic profile has SynonymSlot = null per §5 of the
        // design doc — there is no slot to write to, so the controller refuses
        // (400 BadRequest) before touching the Graph client.
        var generic = new SearchProfile
        {
            Key = "generic",
            DisplayName = LocalizedString.Literal("Generic"),
            SynonymSlot = null
        };
        var registry = new StaticRegistry(generic);
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Strict);
        var edits = new RecordingEditService();
        var controller = NewController(registry, graph.Object, edits);

        var result = await controller.Update(
            new SynonymsRequest { Content = "x => y" },
            profileKey: "generic",
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        graph.Verify(c => c.UpdateSynonymsAsync(It.IsAny<SynonymsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        edits.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Write_WithLanguageOnly_StillWorks_Globally()
    {
        // Back-compat: when profileKey is omitted the controller falls back to
        // the legacy slot/language semantics — clients written before Phase 2.5
        // continue to work without modification.
        var registry = new StaticRegistry();
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Strict);
        SynonymsRequest? captured = null;
        graph.Setup(c => c.UpdateSynonymsAsync(It.IsAny<SynonymsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<SynonymsRequest, CancellationToken>((r, _) => captured = r);

        var edits = new RecordingEditService();
        var controller = NewController(registry, graph.Object, edits);

        var request = new SynonymsRequest
        {
            Content = "tv, television",
            LanguageRouting = "da",
            Slot = "one"
        };

        var result = await controller.Update(request, profileKey: null, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        captured.Should().NotBeNull();
        captured!.Slot.Should().Be("one", because: "no profile means the client-supplied slot is used as-is");
        captured.LanguageRouting.Should().Be("da");

        // The audit row is still appended for global edits — using the
        // sentinel "global" key documented on the controller.
        edits.Saved.Should().ContainSingle();
        edits.Saved[0].ProfileKey.Should().Be(SynonymsApiController.GlobalAuditProfileKey);
        edits.Saved[0].Kind.Should().Be("Synonym");
    }

    [Fact]
    public async Task Write_AppendsAuditWith_Kind_Synonym()
    {
        // Belt-and-braces: every write surface (Update, Delete) must tag its
        // audit row with Kind = "Synonym" so the per-profile Audit log can
        // group correctly.
        var profile = new SearchProfile
        {
            Key = "kb-search",
            DisplayName = LocalizedString.Literal("Knowledge base"),
            Locales = new[] { "en" },
            SynonymSlot = "kb"
        };
        var registry = new StaticRegistry(profile);
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Strict);
        graph.Setup(c => c.UpdateSynonymsAsync(It.IsAny<SynonymsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        graph.Setup(c => c.DeleteSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var edits = new RecordingEditService();
        var controller = NewController(registry, graph.Object, edits);

        await controller.Update(
            new SynonymsRequest { Content = "a, b" },
            profileKey: "kb-search",
            CancellationToken.None);

        await controller.Delete(
            languageRouting: "en",
            sourceRouting: null,
            slot: null,
            profileKey: "kb-search",
            cancellationToken: CancellationToken.None);

        edits.Saved.Should().HaveCount(2);
        edits.Saved.Should().OnlyContain(e => e.Kind == "Synonym");
        edits.Saved.Select(e => e.Action).Should().Contain(new[] { "Updated", "Deleted" });
    }

    private static SynonymsApiController NewController(
        ISearchProfileRegistry registry,
        IGraphAdminClient graph,
        SearchProfileEditService edits)
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Synonyms = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, Array.Empty<object>()).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);
        var service = new SynonymsService(graph);

        var controller = new SynonymsApiController(
            service,
            accessChecker,
            registry,
            edits,
            NullLogger<SynonymsApiController>.Instance);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "test-user")
        }, "test"));
        var httpContext = new DefaultHttpContext { User = user };
        // RequireAjaxAttribute is an MVC filter, not a runtime check inside
        // controller actions — direct invocation in unit tests bypasses it.
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class StaticRegistry : ISearchProfileRegistry
    {
        public StaticRegistry(params SearchProfile[] profiles) { All = profiles; }
        public IReadOnlyList<SearchProfile> All { get; }
        public SearchProfile? Get(string key) => All.FirstOrDefault(p => p.Key == key);
        public IEnumerable<SearchProfile> ForSite(string siteName) => All;
    }

    /// <summary>
    /// Captures append calls without touching the DDS — the production
    /// service tolerates a missing store, so overriding <c>Append</c> is the
    /// cleanest path to a deterministic test.
    /// </summary>
    private sealed class RecordingEditService : SearchProfileEditService
    {
        public List<SearchProfileEdit> Saved { get; } = new();

        public override void Append(SearchProfileEdit entry)
        {
            if (entry.At == default) entry.At = DateTime.UtcNow;
            Saved.Add(entry);
        }
    }
}
