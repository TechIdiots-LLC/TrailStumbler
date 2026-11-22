using System.Buffers.Binary;
using System.Text;
using TrailStumbler.Core.Parsing;
using TrailStumbler.Core.Parsing.Garmin;
using Xunit;

namespace TrailStumbler.Core.Tests;

public class TrailKindTests
{
    [Theory]
    [InlineData("TrailIntel ATV Map NE", "TrailIntelAtv")]
    [InlineData("TrailIntel Snowmobile Map NE", "TrailIntelSnowmobile")]
    [InlineData("GPS Trailmasters ATV New England V12.00", "GpsTrailmastersAtv")]
    [InlineData("OSM street map", "Unknown")]
    public void TheMapsMakeIsKnownByItsName(string name, string product) =>
        Assert.Equal(product, GarminTypeClasses.ProductOf([name]).ToString());

    [Fact]
    public void EachMakeNumbersTheKindsItsOwnWay()
    {
        // Matched against GPS Trailmasters' KMZ: closed trails are 0x06 on TrailIntel's map, 0x1d on the 2023 one
        Assert.True(GarminTypeClasses.KindOf(GarminProduct.TrailIntelAtv, "0x06")!.Closed);
        Assert.True(GarminTypeClasses.KindOf(GarminProduct.GpsTrailmastersAtv, "0x1d")!.Closed);
        Assert.Equal("Corridor", GarminTypeClasses.KindOf(GarminProduct.TrailIntelAtv, "0x04")!.Kind);
        Assert.Equal("Corridor", GarminTypeClasses.KindOf(GarminProduct.GpsTrailmastersAtv, "0x03")!.Kind);
        Assert.True(GarminTypeClasses.KindOf(GarminProduct.TrailIntelAtv, "0x02")!.NoMotorcycles);
        Assert.Equal(50, GarminTypeClasses.KindOf(GarminProduct.TrailIntelAtv, "0x07")!.MaxWidthInches);
        Assert.False(GarminTypeClasses.KindOf(GarminProduct.TrailIntelAtv, "0x03")!.NoMotorcycles);
        Assert.Null(GarminTypeClasses.KindOf(GarminProduct.Unknown, "0x06"));   // another make's map: no kinds
        Assert.Equal("Danger / Caution", GarminTypeClasses.KindOf(GarminProduct.TrailIntelSnowmobile, "0x0c")!.Kind);
    }

    [Theory]
    [InlineData("0x2f01", "fuel")]
    [InlineData("0x2a00", "food")]
    [InlineData("0x2f0b", "parking")]
    [InlineData("0x2c04", "scenic_view")]
    [InlineData("0x6400", null)]
    public void PointsGetTheMapsSprites(string type, string? category) =>
        Assert.Equal(category, GarminTypeClasses.CategoryForPoint(type));

    [Theory]
    [InlineData("Closed ATV Trail", true, false, null)]
    [InlineData("UTV/ATV 60 Inches, No Motorcycles", false, true, 60)]
    [InlineData("UTV/ATV 60 Inches and Motorcycles", false, false, 60)]
    [InlineData("ATV 50 Inches and Motorcycles", false, false, 50)]
    [InlineData("Scenic loop past the closed mill", false, false, null)]   // only a description that begins so
    [InlineData("", false, false, null)]
    public void ATrailsDescriptionSaysWhoMayUseIt(string desc, bool closed, bool noMotorcycles, int? width) =>
        Assert.Equal(new TrailAccess(closed, noMotorcycles, width), TrailFlags.FromDescription(desc));

    [Fact]
    public void TheFlagsAreReadBackFromAFeature()
    {
        Assert.Equal(new TrailAccess(true, false, null), TrailFlags.FromProperties("{\"name\":\"x\",\"closed\":true}"));
        Assert.Equal(new TrailAccess(false, true, 60), TrailFlags.FromProperties("{\"no_motorcycles\":true,\"max_width_in\":60}"));
        Assert.Equal(default, TrailFlags.FromProperties("{\"name\":\"Closed Loop\",\"closed\":\"maybe\"}"));
        Assert.Equal(default, TrailFlags.FromProperties("not json"));
    }

    [Theory]
    // closed, no motorcycles, width -> motorcycle, ATV, UTV, any
    [InlineData(false, false, null, true, true, true, true)]   // a trail whose data says nothing: everyone's
    [InlineData(true, false, null, false, false, false, false)]
    [InlineData(false, true, 60, false, true, true, true)]
    [InlineData(false, false, 50, true, true, false, true)]
    public void EachVehicleUsesTheTrailsItMay(bool closed, bool noMoto, int? width, bool moto, bool atv, bool utv, bool any)
    {
        var access = new TrailAccess(closed, noMoto, width);
        Assert.Equal([moto, atv, utv, any],
            new[] { TrailVehicle.Motorcycle, TrailVehicle.Atv, TrailVehicle.Utv, TrailVehicle.Any }.Select(access.Allows));
    }

    [Fact]
    public void AKmlTrailsFlagsAreSavedFromItsDescriptionNotItsName()
    {
        const string kml = """
            <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
              <Placemark><name>Moose Muck</name><description>Closed ATV Trail</description>
                <LineString><coordinates>-71.5,44.5 -71.49,44.51</coordinates></LineString></Placemark>
              <Placemark><name>Closed Loop Trail</name><description>Easy riding</description>
                <LineString><coordinates>-71.5,44.5 -71.49,44.51</coordinates></LineString></Placemark>
              <Placemark><name>Gorham Parking</name><description>Closed on Sundays</description>
                <Point><coordinates>-71.5,44.5</coordinates></Point></Placemark>
            </Document></kml>
            """;
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(kml));
        var features = new KmlParser().Parse(s);
        Assert.True(TrailFlags.FromProperties(features.Single(f => f.Name == "Moose Muck").PropertiesJson).Closed);
        Assert.Equal(default, TrailFlags.FromProperties(features.Single(f => f.Name == "Closed Loop Trail").PropertiesJson));
        Assert.Equal(default, TrailFlags.FromProperties(features.Single(f => f.GeometryType == "Point").PropertiesJson));   // trails only
    }

    [Fact]
    public void ARoutableLinesNameIsInItsNetRoadRecord()
    {
        // NET header: roads at 0x20, 6 bytes, multiplier 1; the record at offset 1 (byte 0x22) has label 0x1234
        var net = new byte[0x30];
        BinaryPrimitives.WriteUInt32LittleEndian(net.AsSpan(0x15), 0x20);
        BinaryPrimitives.WriteUInt32LittleEndian(net.AsSpan(0x19), 6);
        net[0x1D] = 1;
        net[0x22] = 0x34; net[0x23] = 0x12; net[0x24] = 0x80;   // bit 23: the record's last label
        Assert.Equal(0x1234, new NetSubfile(net).RoadLabel(1));
        Assert.Equal(0, new NetSubfile(net).RoadLabel(40));   // past the section
    }

    [Fact]
    public void APoisNameIsInItsPoiRecord()
    {
        // LBL header (0x61 long): POI properties at 0x70, 8 bytes, multiplier 0; the record at 3 has label 0x0456
        var lbl = new byte[0x80];
        BinaryPrimitives.WriteUInt16LittleEndian(lbl, 0x61);
        BinaryPrimitives.WriteUInt32LittleEndian(lbl.AsSpan(0x57), 0x70);
        BinaryPrimitives.WriteUInt32LittleEndian(lbl.AsSpan(0x5B), 8);
        lbl[0x1E] = 6;
        lbl[0x73] = 0x56; lbl[0x74] = 0x04; lbl[0x75] = 0x80;   // bit 23: its local properties follow
        Assert.Equal(0x0456, new LabelSubfile(lbl).PoiLabel(3));
    }
}
