using Android.Content;

namespace HVTradingBot.Android;

/// <summary>
/// Remembers whether the user wants the engine running: opening the app turns it on, the notification's Stop button
/// turns it off. After a phone restart the engine only comes back if it was on.
/// </summary>
public static class EngineSwitch
{
    private const string Preferences = "engine";
    private const string EnabledKey = "enabled";

    public static bool IsOn(Context context) =>
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)!.GetBoolean(EnabledKey, false);

    public static void Set(Context context, bool on) =>
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)!.Edit()!.PutBoolean(EnabledKey, on)!.Apply();
}
