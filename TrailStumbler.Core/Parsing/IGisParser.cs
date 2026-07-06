using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing;

public interface IGisParser
{
    /// <summary>Parse the stream into features. Throws <see cref="FormatException"/>
    /// on unreadable input.</summary>
    List<ParsedFeature> Parse(Stream stream);
}
