namespace QuranDesktop;

public sealed record SurahInfo(int Number, string ArabicName, string EnglishName, int AyahCount);

public sealed record AyahData(int NumberInSurah, string Arabic, string Translation);

public sealed record Reciter(string Key, string Folder, string Display);

internal sealed class ComboItem
{
    public ComboItem(string text, object? value)
    {
        Text = text;
        Value = value;
    }

    public string Text { get; }
    public object? Value { get; }

    public override string ToString() => Text;
}

internal static class Utils
{
    public static string ToArabicDigits(int n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in n.ToString())
        {
            sb.Append(ch >= '0' && ch <= '9' ? (char)('\u0660' + (ch - '0')) : ch);
        }
        return sb.ToString();
    }
}
