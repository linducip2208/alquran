namespace QuranDesktop;

/// <summary>
/// Centralized endpoint configuration. Values are persisted in AppSettings so
/// deployments can point to a compatible mirror without recompiling the app.
/// </summary>
internal static class ProviderEndpoints
{
    public static string QuranBaseUrl => Normalize(AppSettings.Current.QuranBaseUrl, "https://quran.ksu.edu.sa");
    public static string PrayerBaseUrl => Normalize(AppSettings.Current.PrayerBaseUrl, "https://api.aladhan.com");
    public static string UpdateFeedUrl => Normalize(AppSettings.Current.UpdateFeedUrl,
        "https://api.github.com/repos/linducip2208/alquran/releases/latest");
    public static string WordByWordBaseUrl => Normalize(AppSettings.Current.WordByWordBaseUrl, "https://api.quran.com");

    public static string QuranInterfaceUrl => QuranBaseUrl + "/interface.php?ui=pc";

    private static string Normalize(string? value, string fallback)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")) return fallback;
        return uri.ToString().TrimEnd('/');
    }
}
