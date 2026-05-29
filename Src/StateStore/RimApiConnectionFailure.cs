using System.Net.Http;
using System.Net.Sockets;

namespace RimBob.State;

public static class RimApiConnectionFailure
{
    public static bool IsConnectionFailure(Exception ex)
    {
        if (ex is not HttpRequestException requestException) return false;

        if (requestException.InnerException is SocketException socketException)
        {
            return socketException.SocketErrorCode is SocketError.ConnectionRefused
                or SocketError.ConnectionReset
                or SocketError.HostDown
                or SocketError.HostNotFound
                or SocketError.NetworkDown
                or SocketError.NetworkUnreachable
                or SocketError.TimedOut;
        }

        return requestException.Message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || requestException.Message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || requestException.Message.Contains("No such host", StringComparison.OrdinalIgnoreCase);
    }
}
