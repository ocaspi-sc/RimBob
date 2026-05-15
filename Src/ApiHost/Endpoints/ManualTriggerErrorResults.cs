using System.Net.Sockets;
using RimAI.Ingestion;

namespace RimAI.Host.Endpoints;

internal static class ManualTriggerErrorResults
{
    public static bool TryMap(Exception ex, out IResult result)
    {
        if (IsRimApiConnectionFailure(ex))
        {
            result = Results.Problem(
                title: "RimWorld is not running",
                detail: "RimWorld is not running. Start RimWorld, load a colony, then run RimAI again.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "rimworld_not_running",
                    ["source"] = "rimapi"
                });
            return true;
        }

        if (ex is RimApiException)
        {
            result = Results.Problem(
                title: "RIMAPI data could not be read",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "rimapi_schema_or_response_error",
                    ["source"] = "rimapi"
                });
            return true;
        }

        result = Results.Problem();
        return false;
    }

    private static bool IsRimApiConnectionFailure(Exception ex)
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
