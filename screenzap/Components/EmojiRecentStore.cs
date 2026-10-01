using System.Text;
using System.Text.Json;

namespace screenzap;

internal sealed class EmojiRecentStore
{
    internal const int Capacity = 8;
    private static readonly string[] Defaults = { "😀", "😂", "😍", "👍", "❤️", "🎉", "🔥", "👀" };
    private readonly string path;
    private readonly List<string> recent = new();

    internal EmojiRecentStore(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Screenzap", "recent-emoji.json");
        try
        {
            if (File.Exists(this.path))
                recent.AddRange((JsonSerializer.Deserialize<string[]>(File.ReadAllText(this.path)) ?? Array.Empty<string>())
                    .Where(IsEmoji).Distinct(StringComparer.Ordinal).Take(Capacity));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            lib.Logger.Log($"Could not load recent emoji: {ex.Message}");
        }
    }

    internal string[] Tiles => recent.Concat(Defaults).Distinct(StringComparer.Ordinal).Take(Capacity).ToArray();

    internal static bool IsEmoji(string text) => !string.IsNullOrEmpty(text)
        && System.Globalization.StringInfo.ParseCombiningCharacters(text).Length == 1
        && text.EnumerateRunes().All(rune => rune.Value != 0xFFFD)
        && text.EnumerateRunes().Any(rune => rune.Value is not (0xFE0F or 0x200D or 0x20E3))
        && EmojiTextRenderer.IsEmojiTextElement(text);

    internal void Record(string emoji)
    {
        if (!IsEmoji(emoji)) return;
        recent.Remove(emoji);
        recent.Insert(0, emoji);
        if (recent.Count > Capacity) recent.RemoveRange(Capacity, recent.Count - Capacity);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(recent));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lib.Logger.Log($"Could not save recent emoji: {ex.Message}");
        }
    }
}
