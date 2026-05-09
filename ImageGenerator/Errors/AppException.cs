namespace ImageGenerator.Errors;

public sealed class AppException : Exception
{
    public AppErrorCode Code { get; }

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
}
