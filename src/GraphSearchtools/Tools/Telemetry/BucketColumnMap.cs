using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Maps the <see cref="SearchLogBucket"/> property names to the columns DDS
/// allocated for them in <c>tblBigTable</c>. DDS' wide-row layout uses pooled
/// columns (<c>String01..10</c>, <c>Indexed_String01..03</c>, etc.) and
/// allocates them in declaration order at first run; once allocated the
/// mapping is stable for the lifetime of the store. We resolve it once on
/// startup and cache forever — the read fast path then composes raw SQL
/// against the resolved columns instead of paying DDS' per-row reflection
/// cost.
/// </summary>
internal sealed class BucketColumnMap
{
    private const string StoreName = "GraphSearchtools_SearchLogBucket";

    /// <summary>
    /// Whitelist for column-name interpolation. DDS only allocates names
    /// matching this pattern, so a malicious or corrupted store row can't
    /// inject SQL into the read query — column names that fail the check
    /// trip the fast path and force the LINQ-over-DDS fallback.
    /// </summary>
    private static readonly Regex SafeColumnName = new(@"\A[A-Za-z][A-Za-z0-9_]{0,63}\z", RegexOptions.Compiled);

    public required string BucketUtc    { get; init; }
    public required string ChannelKey   { get; init; }
    public required string Locale       { get; init; }
    public required string PhraseNorm   { get; init; }
    public required string DisplayPhrase{ get; init; }
    public required string Hits         { get; init; }
    public required string Zeroes       { get; init; }
    public required string Clicks1      { get; init; }
    public required string Clicks2      { get; init; }
    public required string Clicks3      { get; init; }

    public static async Task<BucketColumnMap?> ResolveAsync(string connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var cmd = conn.CreateCommand())
            {
                // Active = 1 filters out any column DDS marked obsolete during
                // a schema remap (AutomaticallyRemapStore = true). PropertyName
                // is the .NET property; ColumnName is its slot in tblBigTable.
                cmd.CommandText = @"
                    SELECT i.PropertyName, i.ColumnName
                    FROM tblBigTableStoreInfo i
                    JOIN tblBigTableStoreConfig c ON c.pkId = i.fkStoreId
                    WHERE c.StoreName = @StoreName AND i.Active = 1";
                cmd.Parameters.Add(new SqlParameter("@StoreName", StoreName));
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    raw[reader.GetString(0)] = reader.GetString(1);
                }
            }

            string Get(string name)
            {
                if (!raw.TryGetValue(name, out var col))
                    throw new InvalidOperationException($"Column for '{name}' not found in tblBigTableStoreInfo.");
                if (!SafeColumnName.IsMatch(col))
                    throw new InvalidOperationException($"Column '{col}' for '{name}' fails the whitelist check.");
                return col;
            }

            return new BucketColumnMap
            {
                BucketUtc     = Get(nameof(SearchLogBucket.BucketUtc)),
                ChannelKey    = Get(nameof(SearchLogBucket.ChannelKey)),
                Locale        = Get(nameof(SearchLogBucket.Locale)),
                PhraseNorm    = Get(nameof(SearchLogBucket.PhraseNorm)),
                DisplayPhrase = Get(nameof(SearchLogBucket.DisplayPhrase)),
                Hits          = Get(nameof(SearchLogBucket.Hits)),
                Zeroes        = Get(nameof(SearchLogBucket.Zeroes)),
                Clicks1       = Get(nameof(SearchLogBucket.Clicks1)),
                Clicks2       = Get(nameof(SearchLogBucket.Clicks2)),
                Clicks3       = Get(nameof(SearchLogBucket.Clicks3)),
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Either DDS hasn't created the store yet (fresh install pre-
            // first-write), the connection string is unavailable, or the
            // schema is degenerate. Either way, the LINQ fallback handles it.
            return null;
        }
    }
}
