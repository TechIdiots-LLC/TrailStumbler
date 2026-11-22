using System.Xml.Linq;
using TrailStumbler.Core.Export;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Recording;
using Xunit;

namespace TrailStumbler.Core.Tests;

public class RecordedTrackTests
{
    // A ride east along 44.5, a point every 0.001° (~80 m)
    private static List<RecordingPoint> Ride(int n) => Enumerable.Range(0, n).Select(i => new RecordingPoint
    {
        Id = i + 1, Lat = 44.5, Lon = -71.5 + i * 0.001,
        TimestampTicks = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc).AddSeconds(i * 10).Ticks,
    }).ToList();

    [Fact]
    public void AStretchLeftOutIsAGapNotALineAcrossIt()
    {
        var points = Ride(10);
        foreach (var p in points.Skip(4).Take(3)) p.Excluded = true;   // points 5-7: the road by mistake
        var line = RecordedTrack.Build(points)!;
        Assert.Equal("MultiLineString", line.GeometryType);
        Assert.Equal(7, line.KeptPoints);
        var whole = RecordedTrack.Build(Ride(10))!;
        Assert.Equal("LineString", whole.GeometryType);
        // 3 + 2 steps kept of 9: the gap isn't measured
        Assert.InRange(line.DistanceMeters, whole.DistanceMeters * 5 / 9 * 0.99, whole.DistanceMeters * 5 / 9 * 1.01);
    }

    [Fact]
    public void ALonePointBetweenStretchesLeftOutIsNoStretch()
    {
        var points = Ride(7);
        points[2].Excluded = points[4].Excluded = true;   // point 4 alone between them
        var stretches = RecordedTrack.Stretches(points);
        Assert.Equal([2, 2], stretches.Select(s => s.Count));
        Assert.DoesNotContain(points[3], stretches.SelectMany(s => s));
    }

    [Fact]
    public void NothingKeptIsNoTrack()
    {
        var points = Ride(3);
        points[1].Excluded = true;   // no two kept in a row
        Assert.Null(RecordedTrack.Build(points));
    }

    [Fact]
    public async Task AGpxOfTheTrackLeavesThePointsOutAndKeepsTheGap()
    {
        var points = Ride(10);
        foreach (var p in points.Skip(4).Take(3)) p.Excluded = true;
        using var ms = new MemoryStream();
        await GpxWriter.WriteAsync(ms, "Saturday ride", points);
        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        var segments = XDocument.Load(new MemoryStream(ms.ToArray())).Descendants(ns + "trkseg").ToList();
        Assert.Equal([4, 3], segments.Select(s => s.Elements(ns + "trkpt").Count()));
    }
}

