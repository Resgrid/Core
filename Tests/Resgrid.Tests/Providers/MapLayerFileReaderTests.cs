using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.GeoLocationProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Event layers: what a KML, KMZ or GeoJSON file from Google My Maps, Google Earth or a GIS export becomes on a map
	/// layer. Each shape keeps its name, a plain-text description, its own color and its folder.
	/// </summary>
	[TestFixture]
	public class MapLayerFileReaderTests
	{
		private const string EventKml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<kml xmlns=""http://www.opengis.net/kml/2.2"" xmlns:gx=""http://www.google.com/kml/ext/2.2"">
  <Document>
    <name>City Marathon 2026</name>
    <Style id=""route-normal""><LineStyle><color>ff0000ff</color><width>4</width></LineStyle></Style>
    <Style id=""route-highlight""><LineStyle><color>ff00ff00</color></LineStyle></Style>
    <StyleMap id=""route""><Pair><key>normal</key><styleUrl>#route-normal</styleUrl></Pair><Pair><key>highlight</key><styleUrl>#route-highlight</styleUrl></Pair></StyleMap>
    <Style id=""aid""><IconStyle><color>ff00a000</color></IconStyle></Style>
    <Style id=""zone""><LineStyle><color>ffff0000</color></LineStyle><PolyStyle><color>40ff0000</color></PolyStyle></Style>
    <Folder>
      <name>Course</name>
      <Placemark>
        <name>Full course</name>
        <styleUrl>#route</styleUrl>
        <LineString><coordinates>4.3517,50.8466,0 4.3600,50.8500,0 4.3700,50.8550,0</coordinates></LineString>
      </Placemark>
      <Placemark>
        <name>Recorded lead bike</name>
        <gx:Track><gx:coord>4.35 50.84 0</gx:coord><gx:coord>4.36 50.85 0</gx:coord><gx:coord>4.37 50.86 0</gx:coord></gx:Track>
      </Placemark>
    </Folder>
    <Folder>
      <name>Aid stations</name>
      <Placemark>
        <name>Aid 1 &amp; water</name>
        <description><![CDATA[<b>Water</b> and first aid<br/>Open 08:00&ndash;14:00]]></description>
        <styleUrl>#aid</styleUrl>
        <ExtendedData><Data name=""Staff""><value>4 medics</value></Data></ExtendedData>
        <Point><coordinates>4.3550,50.8480,0</coordinates></Point>
      </Placemark>
      <Placemark>
        <name>Twin posts</name>
        <MultiGeometry>
          <Point><coordinates>4.3610,50.8510</coordinates></Point>
          <Point><coordinates>4.3620,50.8520</coordinates></Point>
        </MultiGeometry>
      </Placemark>
    </Folder>
    <Placemark>
      <name>Start village</name>
      <styleUrl>#zone</styleUrl>
      <Polygon>
        <outerBoundaryIs><LinearRing><coordinates>4.34,50.84 4.35,50.84 4.35,50.85 4.34,50.85 4.34,50.84</coordinates></LinearRing></outerBoundaryIs>
        <innerBoundaryIs><LinearRing><coordinates>4.342,50.842 4.344,50.842 4.344,50.844 4.342,50.842</coordinates></LinearRing></innerBoundaryIs>
      </Polygon>
    </Placemark>
    <Placemark>
      <name>Sponsor tent (by address only)</name>
      <address>Grand-Place 1, Brussels</address>
    </Placemark>
    <Placemark>
      <name>Broken</name>
      <Point><coordinates>200,95</coordinates></Point>
    </Placemark>
  </Document>
</kml>";

		private static Feature Named(MapLayerFileReadResult result, string name) =>
			result.Features.Features.First(f => (string)f.Properties["name"] == name);

		[Test]
		public void A_kml_event_file_keeps_names_colors_folders_and_descriptions()
		{
			var result = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(EventKml), "marathon.kml");

			result.Success.Should().BeTrue();
			result.Points.Should().Be(3);   // Aid 1 + the two halves of the MultiGeometry
			result.Lines.Should().Be(2);    // course + gx:Track
			result.Polygons.Should().Be(1);
			result.SkippedWithoutGeometry.Should().Be(1);
			result.SkippedInvalid.Should().Be(1);

			var course = Named(result, "Full course");
			course.Properties["color"].Should().Be("#ff0000", "a StyleMap resolves to its normal style; KML colors are aabbggrr");
			course.Properties["category"].Should().Be("Course");
			((LineString)course.Geometry).Coordinates.Should().HaveCount(3);

			var aid = Named(result, "Aid 1 & water");
			aid.Properties["color"].Should().Be("#00a000");
			aid.Properties["category"].Should().Be("Aid stations");
			aid.Properties["description"].Should().Be("Water and first aid\nOpen 08:00–14:00\nStaff: 4 medics", "HTML is stripped and ExtendedData appended");

			result.Features.Features.Count(f => (string)f.Properties["name"] == "Twin posts").Should().Be(2);

			var village = Named(result, "Start village");
			village.Properties["color"].Should().Be("#0000ff", "a polygon takes its outline color");
			((Polygon)village.Geometry).Coordinates.Should().HaveCount(2, "the hole is kept");
		}

		[Test]
		public void The_shapes_survive_the_layer_document()
		{
			var result = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(EventKml), "marathon.kml");
			var data = new MapLayerData();
			data.Hydrate(result.Features);

			var back = data.Convert();

			back.Features.Should().HaveCount(result.ShapeCount);
			var aid = back.Features.First(f => (string)f.Properties["name"] == "Aid 1 & water");
			aid.Properties["description"].Should().Be("Water and first aid\nOpen 08:00–14:00\nStaff: 4 medics");
			aid.Properties["color"].Should().Be("#00a000");
			aid.Properties["category"].Should().Be("Aid stations");
			((Polygon)back.Features.First(f => (string)f.Properties["name"] == "Start village").Geometry).Coordinates.Should().HaveCount(2);
			data.StoredBytes().Should().BeLessThan(MapLayerFileLimits.MaxStoredBytes);
		}

		[Test]
		public void One_description_repeated_over_many_shapes_is_measured_as_stored()
		{
			// A small file within the shape and point limits: one multipoint of 10,000 aid posts sharing a long description
			// becomes 10,000 shapes that each carry it, more than one stored layer document can hold.
			var points = string.Join(",", Enumerable.Range(0, 10000).Select(i => $"[{(4 + i * 0.00001).ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)},50]"));
			var geoJson = $@"{{""type"":""Feature"",""properties"":{{""name"":""Aid posts"",""description"":""{new string('x', 2000)}""}},""geometry"":{{""type"":""MultiPoint"",""coordinates"":[{points}]}}}}";

			var result = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(geoJson), "posts.geojson");
			result.Success.Should().BeTrue();
			result.Points.Should().Be(10000);

			var data = new MapLayerData();
			data.Hydrate(result.Features);
			data.StoredBytes().Should().BeGreaterThan(MapLayerFileLimits.MaxStoredBytes);
		}

		[Test]
		public void A_kmz_is_read_whatever_it_is_called()
		{
			byte[] kmz;
			using (var buffer = new MemoryStream())
			{
				using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
				using (var writer = new StreamWriter(zip.CreateEntry("doc.kml").Open()))
					writer.Write(EventKml);
				kmz = buffer.ToArray();
			}

			MapLayerFileReader.Read(kmz, "route.kmz").ShapeCount.Should().Be(6);
			MapLayerFileReader.Read(kmz, "route.kml").ShapeCount.Should().Be(6);
		}

		[Test]
		public void A_geojson_file_is_split_into_simple_shapes_with_its_stroke_color_and_extra_properties()
		{
			const string geoJson = @"{""type"":""FeatureCollection"",""features"":[
 {""type"":""Feature"",""properties"":{""name"":""Parade route"",""stroke"":""#123abc"",""stroke-width"":3},
  ""geometry"":{""type"":""MultiLineString"",""coordinates"":[[[4.1,50.1],[4.2,50.2]],[[4.3,50.3],[4.4,50.4,12.5]]]}},
 {""type"":""Feature"",""properties"":{""title"":""Food court"",""Vendor"":""Frites Nord"",""Stand"":12},
  ""geometry"":{""type"":""MultiPolygon"",""coordinates"":[[[[4,50],[4.01,50],[4.01,50.01],[4,50]]],[[[4.1,50],[4.11,50],[4.11,50.01],[4.1,50]]]]}},
 {""type"":""Feature"",""properties"":{""name"":""No geometry""},""geometry"":null},
 {""type"":""Feature"",""properties"":{""name"":""Bad""},""geometry"":{""type"":""Point"",""coordinates"":[""x"",1]}}
]}";
			var result = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(geoJson), "parade.geojson");

			result.Success.Should().BeTrue();
			result.Lines.Should().Be(2);
			result.Polygons.Should().Be(2);
			result.SkippedWithoutGeometry.Should().Be(1);
			result.SkippedInvalid.Should().Be(1);
			Named(result, "Parade route").Properties["color"].Should().Be("#123abc");
			Named(result, "Food court").Properties["description"].Should().Be("Vendor: Frites Nord\nStand: 12");
		}

		[Test]
		public void A_resgrid_layer_export_keeps_its_circles_and_text_labels()
		{
			const string geoJson = @"{""type"":""FeatureCollection"",""features"":[
 {""type"":""Feature"",""properties"":{""name"":""Staging"",""shape"":""Circle"",""radius"":150},""geometry"":{""type"":""Point"",""coordinates"":[4.1,50.1]}},
 {""type"":""Feature"",""properties"":{""name"":""Label"",""shape"":""Text"",""text"":""Gate B""},""geometry"":{""type"":""Point"",""coordinates"":[4.2,50.2]}}]}";

			var result = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(geoJson), "export.json");

			Named(result, "Staging").Properties["radius"].Should().Be(150d);
			Named(result, "Label").Properties["text"].Should().Be("Gate B");
		}

		[Test]
		public void A_file_with_only_addresses_says_so()
		{
			const string kml = @"<kml><Document><Placemark><name>Shop</name><address>Main St 1</address></Placemark></Document></kml>";

			var result = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(kml), "stores.kml");

			result.Error.Should().Be(MapLayerFileReadErrors.NoShapes);
			result.SkippedWithoutGeometry.Should().Be(1);
		}

		[Test]
		public void Too_many_shapes_or_points_is_refused_rather_than_cut_short()
		{
			var points = string.Join(",", Enumerable.Range(0, MapLayerFileLimits.MaxFeatures + 1)
				.Select(i => $@"{{""type"":""Feature"",""properties"":{{}},""geometry"":{{""type"":""Point"",""coordinates"":[{4 + i * 0.00001:0.00000},50]}}}}"));
			var many = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(@"{""type"":""FeatureCollection"",""features"":[" + points + "]}"), "many.geojson");
			many.Error.Should().Be(MapLayerFileReadErrors.TooManyShapes);
			many.Features.Features.Should().BeEmpty();

			var line = string.Join(",", Enumerable.Range(0, MapLayerFileLimits.MaxPositions + 1).Select(i => $"[{4 + i * 0.000001:0.000000},50]"));
			var long_ = MapLayerFileReader.Read(Encoding.UTF8.GetBytes(@"{""type"":""LineString"",""coordinates"":[" + line + "]}"), "long.geojson");
			long_.Error.Should().Be(MapLayerFileReadErrors.TooManyPoints);
		}

		[TestCase("not xml at all <", "x.kml", MapLayerFileReadErrors.Unreadable)]
		[TestCase("{ broken json", "x.geojson", MapLayerFileReadErrors.Unreadable)]
		[TestCase("plain text", "x.txt", MapLayerFileReadErrors.UnsupportedType)]
		[TestCase("<kml><Document></Document></kml>", "x.kml", MapLayerFileReadErrors.NoShapes)]
		[TestCase(@"{""type"":{}}", "x.geojson", MapLayerFileReadErrors.NoShapes)]
		[TestCase(@"{""type"":""Feature"",""geometry"":{""type"":[""Point""],""coordinates"":[4,50]}}", "x.geojson", MapLayerFileReadErrors.NoShapes)]
		public void Unusable_files_report_why(string content, string name, MapLayerFileReadErrors error)
		{
			MapLayerFileReader.Read(Encoding.UTF8.GetBytes(content), name).Error.Should().Be(error);
		}

		[Test]
		public void Descriptions_are_plain_text()
		{
			MapLayerFileReader.PlainText("<script>alert(1)</script><p>First</p><p>Second &amp; third</p>")
				.Should().Be("alert(1) First\nSecond & third");
		}
	}
}
