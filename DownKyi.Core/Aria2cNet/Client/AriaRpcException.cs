namespace DownKyi.Core.Aria2cNet.Client;

public sealed class AriaRpcException : InvalidOperationException
{
    public AriaRpcException()
    {
        RpcMessage = string.Empty;
    }

    public AriaRpcException(string message)
        : base(message)
    {
        RpcMessage = message;
    }

    public AriaRpcException(string message, Exception innerException)
        : base(message, innerException)
    {
        RpcMessage = message;
    }

    public AriaRpcException(int code, string rpcMessage)
        : base($"aria2 RPC failed with code {code}: {rpcMessage}")
    {
        ArgumentNullException.ThrowIfNull(rpcMessage);
        Code = code;
        RpcMessage = rpcMessage;
    }

    public int Code { get; }

    public string RpcMessage { get; }
}
