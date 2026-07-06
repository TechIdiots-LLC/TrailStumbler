using System.IO.Compression;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Parsing;

/// <summary>
/// KMZ = zip containing a KML. Prefers the conventional "doc.kml" entry, otherwise
/// the first *.kml (GPS Trail Masters KMZs use names like "atv-new-york.kml").
/// </summary>
public class KmzParser : IGisParser
{
    public List<ParsedFeature> Parse(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var entry = zip.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, "doc.kml", StringComparison.OrdinalIgnoreCase))
                    ?? zip.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new FormatException("KMZ archive contains no .kml document.");

        // ZipArchive entry streams aren't seekable; XDocument only needs forward reads.
        using var kml = entry.Open();
        return new KmlParser().Parse(kml);
    }
}
