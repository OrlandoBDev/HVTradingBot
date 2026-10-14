using System.Text.Json;
using HVTradingBot.Infrastructure.Bridge;
using HVTradingBot.Infrastructure.Settings;

namespace HVTradingBot.Api.Endpoints;

/// <summary>
/// The MT5 bridge: the HVTradingBot Expert Advisor in MetaTrader 5 on this computer reports here every second and
/// receives the orders the trading worker queued (see <see cref="Mt5BridgeStore"/>). It signs in with the bridge key
/// shown in Settings › Broker account, not the dashboard login, and only while the bridge is switched on there.
/// </summary>
public static class BridgeEndpoints
{
    public const string KeyHeader = "X-HV-Bridge-Key";

    public static IEndpointRouteBuilder MapBridgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bridge/mt5", async (HttpContext http, Mt5SettingsStore settings, Mt5BridgeStore bridge, CancellationToken ct) =>
        {
            if (!await settings.IsValidBridgeKeyAsync(http.Request.Headers[KeyHeader].ToString(), ct))
            {
                return Results.Text("ERR|Not authorised: check the bridge key in the Expert Advisor's inputs, and that \"MT5 on this computer\" is switched on in Settings.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            using var reader = new StreamReader(http.Request.Body);
            var body = (await reader.ReadToEndAsync(ct)).TrimEnd('\0'); // MQL5 may send a trailing NUL
            BridgeSync? sync;
            try
            {
                sync = Mt5BridgeStore.Parse(body);
            }
            catch (JsonException ex)
            {
                return Results.Text($"ERR|Unreadable report: {ex.Message}", statusCode: StatusCodes.Status400BadRequest);
            }

            if (sync?.Account is null)
            {
                return Results.Text("ERR|The report has no account.", statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Text(await bridge.SyncAsync(sync, ct), "text/plain");
        }).AllowAnonymous();
        return app;
    }
}
