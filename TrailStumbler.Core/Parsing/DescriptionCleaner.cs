using System.Text.RegularExpressions;

namespace TrailStumbler.Core.Parsing;

/// <summary>
/// Cleans HTML junk out of feature descriptions. GPS Trail Masters KMLs (and many
/// other KML exports) terminate every description with "&lt;br&gt;&lt;br&gt;" — the same
/// cleanup the user's atv-trail-map tippecanoe pipeline does with sed.
/// </summary>
public static partial class DescriptionCleaner
{
    [GeneratedRegex(@"(?i)(?:<br\s*/?>\s*){2,}")]
    private static partial Regex RepeatedBr();

    [GeneratedRegex(@"(?i)<br\s*/?>")]
    private static partial Regex SingleBr();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    /// <summary>Cleanup for the stored property value: drop repeated &lt;br&gt; runs
    /// (trailing junk), keep the rest of the text intact.</summary>
    public static string CleanProperty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return RepeatedBr().Replace(text, "").Trim();
    }

    /// <summary>Cleanup for popup display: repeated &lt;br&gt; runs dropped, single
    /// &lt;br&gt; becomes a newline, all remaining tags stripped.</summary>
    public static string CleanForDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = RepeatedBr().Replace(text, "\n");
        s = SingleBr().Replace(s, "\n");
        s = AnyTag().Replace(s, "");
        return s.Trim();
    }
}
