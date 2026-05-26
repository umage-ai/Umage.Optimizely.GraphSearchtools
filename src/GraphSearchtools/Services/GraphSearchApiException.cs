using System.Net;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// Thrown when an Optimizely Graph admin API request fails.
/// The HTTP status code is preserved so callers can map it back to a sensible
/// response without leaking the upstream message body.
/// </summary>
internal sealed class GraphSearchApiException : Exception
{
    public GraphSearchApiException(HttpStatusCode statusCode, string responseContent)
        : base($"Graph API request failed with status {(int)statusCode}.")
    {
        StatusCode = (int)statusCode;
        ResponseContent = responseContent;
    }

    public int StatusCode { get; }

    public string ResponseContent { get; }
}
