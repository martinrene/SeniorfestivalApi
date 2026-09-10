namespace Seniorfestival.Api;

/// <summary>
/// OneSignal endpoint and credentials, read from app settings.
/// <para>
/// The REST API key used to be compiled into <see cref="Seniorfestival.System.MyEventsTimerTrigger"/>,
/// which meant it lived in source control and could only be rotated by shipping a new
/// build. Both senders - the event reminders and the admin site's broadcast
/// (<see cref="PushAdmin"/>) - now read it from here.
/// </para>
/// <para>
/// The app id is not a secret: the mobile app passes the same value to
/// <c>OneSignal.initialize</c> and it ships in the app bundle. The API key is, and it
/// must never end up in the admin site's bundle or in a log line.
/// </para>
/// </summary>
public static class OneSignalConfig
{
    public const string Url = "https://api.onesignal.com/notifications";

    /// <summary>OneSignal's default segment of everyone who accepted notifications.</summary>
    private const string DefaultSegment = "Subscribed Users";

    public static string? AppId => Read("oneSignalAppId");

    public static string? ApiKey => Read("oneSignalApiKey");

    /// <summary>Who a broadcast goes to. Only used by <see cref="PushAdmin"/>.</summary>
    public static string Segment => Read("oneSignalSegment") ?? DefaultSegment;

    /// <summary>False when either credential is missing, so callers can refuse early.</summary>
    public static bool IsConfigured => AppId != null && ApiKey != null;

    private static string? Read(string setting)
    {
        string? value = Environment.GetEnvironmentVariable(setting)?.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
