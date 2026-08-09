namespace ImageGenerator.Errors;

using System.Net;

public sealed class AppException : Exception
{
    public AppErrorCode Code { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public AppException(AppErrorCode code, string logMessage)
        : base(logMessage)
    {
        Code = code;
    }

    public AppException(AppErrorCode code, string logMessage, Exception innerException)
        : base(logMessage, innerException)
    {
        Code = code;
    }

    public AppException(AppErrorCode code, string logMessage, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base(logMessage)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
