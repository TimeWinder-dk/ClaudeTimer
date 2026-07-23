using System.Net;

namespace ClaudeTimer.Services;

public sealed class ClaudeUsageException(
    string message,
    HttpStatusCode? statusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
