using System.Xml;

namespace TrailStumbler.Core.Parsing;

public enum GisFormat { Unknown, GeoJson, Kml, Kmz, Gpx }

/// <summary>Detects the GIS format of a file from its extension, falling back to
/// content sniffing (Android file pickers often lose the real MIME/extension).</summary>
public static class FormatDetector
{
    public static GisFormat Detect(string filePath)
    {
        var byExt = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".geojson" or ".json" => GisFormat.GeoJson,
            ".kml" => GisFormat.Kml,
            ".kmz" => GisFormat.Kmz,
            ".gpx" => GisFormat.Gpx,
            _ => GisFormat.Unknown,
        };
        if (byExt != GisFormat.Unknown) return byExt;

        using var stream = File.OpenRead(filePath);
        return Sniff(stream);
    }

    /// <summary>Sniff the format from leading bytes. Leaves the stream position unspecified.</summary>
    public static GisFormat Sniff(Stream stream)
    {
        Span<byte> head = stackalloc byte[4];
        int n = stream.Read(head);
        if (n >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04)
            return GisFormat.Kmz;   // zip magic "PK\x03\x04"

        stream.Position = 0;
        using var reader = new StreamReader(stream, leaveOpen: true);
        int c;
        while ((c = reader.Read()) != -1 && char.IsWhiteSpace((char)c)) { }
        if (c == '{') return GisFormat.GeoJson;
        if (c != '<') return GisFormat.Unknown;

        // XML: decide by the root element's local name.
        stream.Position = 0;
        try
        {
            using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element) continue;
                return xml.LocalName.ToLowerInvariant() switch
                {
                    "kml" => GisFormat.Kml,
                    "gpx" => GisFormat.Gpx,
                    _ => GisFormat.Unknown,
                };
            }
        }
        catch (XmlException) { }
        return GisFormat.Unknown;
    }
}
