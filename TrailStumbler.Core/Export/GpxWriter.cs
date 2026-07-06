// GPX 1.1 writer. Pattern ported from VistumblerCS ExportService.ExportToGpxAsync (~line 628).
using System.Globalization;
using System.Xml;
using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Export;

public static class GpxWriter
{
    private const string Ns = "http://www.topografix.com/GPX/1/1";

    public static async Task WriteAsync(Stream stream, string trackName, IReadOnlyList<RecordingPoint> points)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = System.Text.Encoding.UTF8,
            Indent = true,
            IndentChars = "  ",
        };
        await using var writer = XmlWriter.Create(stream, settings);

        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "gpx", Ns);
        await writer.WriteAttributeStringAsync(null, "version", null, "1.1");
        await writer.WriteAttributeStringAsync(null, "creator", null, "TrailStumbler");
        await writer.WriteAttributeStringAsync("xmlns", "xsi", null,
            "http://www.w3.org/2001/XMLSchema-instance");
        await writer.WriteAttributeStringAsync("xsi", "schemaLocation", null,
            "http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd");

        await writer.WriteStartElementAsync(null, "trk", Ns);
        await writer.WriteElementStringAsync(null, "name", Ns, trackName);
        await writer.WriteStartElementAsync(null, "trkseg", Ns);

        foreach (var pt in points)
        {
            await writer.WriteStartElementAsync(null, "trkpt", Ns);
            await writer.WriteAttributeStringAsync(null, "lat", null,
                pt.Lat.ToString("F6", CultureInfo.InvariantCulture));
            await writer.WriteAttributeStringAsync(null, "lon", null,
                pt.Lon.ToString("F6", CultureInfo.InvariantCulture));

            if (pt.Ele.HasValue)
                await writer.WriteElementStringAsync(null, "ele", Ns,
                    pt.Ele.Value.ToString("F1", CultureInfo.InvariantCulture));

            await writer.WriteElementStringAsync(null, "time", Ns,
                new DateTime(pt.TimestampTicks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"));

            await writer.WriteEndElementAsync(); // trkpt
        }

        await writer.WriteEndElementAsync(); // trkseg
        await writer.WriteEndElementAsync(); // trk
        await writer.WriteEndElementAsync(); // gpx
        await writer.WriteEndDocumentAsync();
    }
}
