using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.Services;

/// <summary>
/// Quitting leaves the stack running so trading continues. Only with "Stop trading when the app quits" enabled is
/// <c>docker compose stop</c> started, in the background, because the app exits before the containers have stopped.
/// </summary>
public sealed class QuitHandler(AppSettings settings, AppSession session, ILogger<QuitHandler> logger)
{
    private int handled;

    /// <summary>Called from both the window's Destroying event and applicationWillTerminate; acts once.</summary>
    public void OnQuit()
    {
        if (Interlocked.Exchange(ref handled, 1) == 1)
        {
            return;
        }

        if (!settings.StopTradingOnQuit || session.Stack is not { } stack)
        {
            logger.LogInformation("App quits; the Docker stack keeps running");
            return;
        }

        try
        {
            stack.StopInBackground();
        }
        catch (Exception ex)
        {
            // The app is exiting: nothing can be shown any more, so the failure is only logged.
            logger.LogError(ex, "Could not stop the Docker stack on quit");
        }
    }
}
