using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.GeoLocationProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// POI import reads KML and KMZ from whatever tool a department used to convert its spreadsheet. A namespace-aware
	/// parse returned no placemarks at all for the files most converters write (the KML 2.0/2.1 earth.google.com
	/// namespaces, no namespace, a Document root, whitespace before the declaration), so the upload reported "no valid
	/// placemarks" for a file Google Earth opens fine.
	/// </summary>
	[TestFixture]
	public class KmlProviderTests
	{
		private const string TwoPoints =
			"<Document><name>Hydrants</name>" +
			"<Placemark><name>H-1</name><Point><coordinates>-122.08,37.42,0</coordinates></Point></Placemark>" +
			"<Placemark><name>H-2</name><Point><coordinates>-122.09, 37.43</coordinates></Point></Placemark>" +
			"</Document>";

		private static string Kml(string ns, string body = TwoPoints) =>
			"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<kml" + (ns == null ? "" : " xmlns=\"" + ns + "\"") + ">" + body + "</kml>";

		private static KmlImportResult Import(string xml, bool isKmz = false, Encoding encoding = null) =>
			new KmlProvider().ImportFile(new MemoryStream((encoding ?? new UTF8Encoding(false)).GetBytes(xml)), isKmz);

		private static byte[] Zip(string entryName, string xml)
		{
			using (var buffer = new MemoryStream())
			{
				using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
				using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open(), new UTF8Encoding(false)))
					writer.Write(xml);

				return buffer.ToArray();
			}
		}

		private static void ShouldHaveTwoHydrants(KmlImportResult result)
		{
			result.Error.Should().BeNull();
			result.SkippedCount.Should().Be(0);
			result.Coordinates.Select(c => c.Name).Should().Equal("H-1", "H-2");
			result.Coordinates[0].Latitude.Should().BeApproximately(37.42, 0.000001);
			result.Coordinates[0].Longitude.Should().BeApproximately(-122.08, 0.000001);
		}

		[TestCase("http://www.opengis.net/kml/2.2")]
		[TestCase("http://earth.google.com/kml/2.0")]
		[TestCase("http://earth.google.com/kml/2.1")]
		[TestCase("http://earth.google.com/kml/2.2")]
		[TestCase(null)]
		public void Reads_placemarks_whatever_the_kml_namespace(string ns)
		{
			ShouldHaveTwoHydrants(Import(Kml(ns)));
		}

		[Test]
		public void Reads_a_document_without_a_kml_root_element()
		{
			ShouldHaveTwoHydrants(Import("<?xml version=\"1.0\"?>" + TwoPoints.Replace("<Document>", "<Document xmlns=\"http://www.opengis.net/kml/2.2\">")));
		}

		[Test]
		public void Reads_a_file_with_whitespace_or_a_byte_order_mark_before_the_declaration()
		{
			ShouldHaveTwoHydrants(Import("\r\n  " + Kml("http://www.opengis.net/kml/2.2")));
			ShouldHaveTwoHydrants(Import(Kml("http://www.opengis.net/kml/2.2"), encoding: new UTF8Encoding(true)));
		}

		[Test]
		public void Reads_kmz_by_content_not_by_extension()
		{
			var kmz = Zip("files/layer.kml", Kml("http://earth.google.com/kml/2.1"));

			ShouldHaveTwoHydrants(new KmlProvider().ImportFile(new MemoryStream(kmz), true));
			ShouldHaveTwoHydrants(new KmlProvider().ImportFile(new MemoryStream(kmz), false));
			ShouldHaveTwoHydrants(Import(Kml("http://www.opengis.net/kml/2.2"), isKmz: true));
		}

		[Test]
		public void Takes_the_name_from_extended_data_when_the_placemark_has_none()
		{
			// The shape ogr2ogr and QGIS write when no name field is chosen.
			var result = Import(Kml("http://www.opengis.net/kml/2.2",
				"<Document id=\"root_doc\"><Schema name=\"pois\" id=\"pois\"><SimpleField name=\"NAME\" type=\"string\"></SimpleField></Schema>" +
				"<Folder><name>pois</name><Placemark><ExtendedData><SchemaData schemaUrl=\"#pois\">" +
				"<SimpleData name=\"ID\">7</SimpleData><SimpleData name=\"NAME\">Station 7 Tank</SimpleData>" +
				"</SchemaData></ExtendedData><Point><coordinates>-122.08,37.42</coordinates></Point></Placemark>" +
				"<Placemark><ExtendedData><Data name=\"Title\"><value>Dry Hydrant</value></Data></ExtendedData>" +
				"<Point><coordinates>-122.09,37.43</coordinates></Point></Placemark></Folder></Document>"));

			result.Coordinates.Select(c => c.Name).Should().Equal("Station 7 Tank", "Dry Hydrant");
		}

		[Test]
		public void Skips_and_counts_placemarks_without_a_usable_position()
		{
			var result = Import(Kml("http://www.opengis.net/kml/2.2",
				"<Document>" +
				"<Placemark><name>Good</name><Point><coordinates>-122.08,37.42</coordinates></Point></Placemark>" +
				"<Placemark><name>Swapped</name><Point><coordinates>37.42,-122.08</coordinates></Point></Placemark>" +
				"<Placemark><name>Not geocoded</name><Point><coordinates>0,0</coordinates></Point></Placemark>" +
				"<Placemark><name>No geometry</name></Placemark>" +
				"</Document>"));

			result.Error.Should().BeNull();
			result.Coordinates.Select(c => c.Name).Should().Equal("Good");
			result.SkippedCount.Should().Be(3);
		}

		[Test]
		public void Reports_why_a_malformed_file_could_not_be_read()
		{
			var result = Import(Kml("http://www.opengis.net/kml/2.2",
				"<Document><Placemark><name>Fire & Rescue</name><Point><coordinates>-122.08,37.42</coordinates></Point></Placemark></Document>"));

			result.Coordinates.Should().BeEmpty();
			result.Error.Should().StartWith("The file is not valid KML:");
		}

		[Test]
		public void Reports_a_file_that_is_not_kml_at_all()
		{
			new KmlProvider().ImportFile(new MemoryStream(new byte[0]), false).Error.Should().Be("The file does not contain any KML.");
			new KmlProvider().ImportFile(new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 }), true).Error.Should().NotBeNullOrEmpty();
		}

		[Test]
		public void Reads_latin1_bytes_from_a_file_that_declares_utf8()
		{
			var result = Import(Kml("http://www.opengis.net/kml/2.2",
				"<Placemark><name>Café Hydrant</name><Point><coordinates>-122.08,37.42</coordinates></Point></Placemark>"), encoding: Encoding.Latin1);

			result.Coordinates.Single().Name.Should().Be("Café Hydrant");
		}

		[Test]
		public void Does_not_follow_a_network_link_to_an_internal_address()
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			try
			{
				var port = ((IPEndPoint)listener.LocalEndpoint).Port;
				var result = Import(Kml("http://www.opengis.net/kml/2.2",
					"<Document><NetworkLink><Link><href>http://127.0.0.1:" + port + "/hydrants.kml</href></Link></NetworkLink></Document>"));

				result.Coordinates.Should().BeEmpty();
				listener.Pending().Should().BeFalse("the upload must not make the server connect to a loopback address");
			}
			finally
			{
				listener.Stop();
			}
		}
	}
}
