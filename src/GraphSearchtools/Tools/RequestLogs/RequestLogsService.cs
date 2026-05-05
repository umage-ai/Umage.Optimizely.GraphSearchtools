using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.RequestLogs.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RequestLogs;

/// <summary>
/// Orchestration around <see cref="IGraphAdminClient"/> for the Request Logs
/// tool. List-only — Graph's request log is a read surface; replay happens
/// client-side by piping the saved query into the Search Console.
/// </summary>
public sealed class RequestLogsService
{
    /// <summary>Default page size when the caller omits <c>take</c>.</summary>
    public const int DefaultTake = 100;

    /// <summary>Inclusive upper bound for <c>take</c>; mirrors the gateway ceiling.</summary>
    public const int MaxTake = 1000;

    private readonly IGraphAdminClient _client;

    public RequestLogsService(IGraphAdminClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Returns the most recent request-log entries. <paramref name="take"/> is
    /// clamped to <c>[1, <see cref="MaxTake"/>]</c> so a wild caller can't pin
    /// the gateway with a malformed paging parameter.
    /// </summary>
    public async Task<IReadOnlyList<RequestLogEntrySummary>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(take, 1, MaxTake);
        var entries = await _client.GetRequestLogsAsync(clamped, cancellationToken);
        return entries.Select(ToSummary).ToList();
    }

    private static RequestLogEntrySummary ToSummary(RequestLogEntryResult e) => new()
    {
        Id = e.Id,
        At = e.At,
        Method = string.IsNullOrWhiteSpace(e.Method) ? "POST" : e.Method,
        Operation = e.Operation,
        Query = e.Query,
        Variables = e.Variables,
        Status = e.Status,
        DurationMs = e.DurationMs,
        ResultCount = e.ResultCount,
        Ranking = e.Ranking,
        CallerIp = e.CallerIp,
        UserAgent = e.UserAgent
    };
}
