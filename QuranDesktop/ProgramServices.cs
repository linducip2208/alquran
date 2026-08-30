namespace QuranDesktop;

internal static class ProgramServices
{
    public static KsuApi Api { get; } = new();

    public static HttpClient Http { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(45),
    };

    static ProgramServices()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 QuranDesktop/2.0");
        Http.DefaultRequestHeaders.Referrer = new Uri("https://quran.ksu.edu.sa/index.php?ui=1&l=en");
    }
}
