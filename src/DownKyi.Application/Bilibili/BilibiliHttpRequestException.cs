using System.Net;

namespace DownKyi.Application.Bilibili;

public sealed class BilibiliHttpRequestException : HttpRequestException
{
    public BilibiliHttpRequestException()
        : this("A Bilibili HTTP request failed.", BilibiliHttpFailureKind.Transport)
    {
    }

    public BilibiliHttpRequestException(string message)
        : this(message, BilibiliHttpFailureKind.Transport)
    {
    }

    public BilibiliHttpRequestException(string message, Exception innerException)
        : this(message, BilibiliHttpFailureKind.Transport, innerException: innerException)
    {
    }

    public BilibiliHttpRequestException(
        string message,
        BilibiliHttpFailureKind failureKind,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException, statusCode)
    {
        FailureKind = failureKind;
    }

    public BilibiliHttpFailureKind FailureKind { get; }
}
