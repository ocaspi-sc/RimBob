using RimBob.Ingestion;
using RimBob.State;

namespace RimBob.Host.Endpoints;

internal static class ManualTriggerErrorResults
{
    public static bool TryMap(Exception ex, out IResult result)
    {
        if (RimApiConnectionFailure.IsConnectionFailure(ex))
        {
            result = Results.Problem(
                title: "RimWorld is not running",
                detail: "RimWorld is not running. Start RimWorld, load a colony, then run RimBob again.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "rimworld_not_running",
                    ["source"] = "rimapi"
                });
            return true;
        }

        if (ex is RimApiLiveStateUnavailableException liveStateUnavailable)
        {
            result = Results.Problem(
                title: "RimWorld map is not loaded",
                detail: liveStateUnavailable.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "rimworld_map_not_loaded",
                    ["source"] = "rimapi",
                    ["reason"] = liveStateUnavailable.Reason.ToString()
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
}
