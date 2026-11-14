using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Departments start a POI import from a spreadsheet of street addresses. Uploaded as CSV it was refused by extension;
	/// converted to KML by Google My Maps it carried an &lt;address&gt; and the address columns but no &lt;Point&gt;, so the
	/// import reported that none of the placemarks had coordinates. The upload now only reads the file and queues it; worker 79
	/// geocodes the addresses and saves the POIs, so a large file never runs into a web request timeout.
	/// </summary>
	[TestFixture]
	public class PoiImportServiceTests
	{
		private const int DepartmentId = 12;
		private const int PoiTypeId = 5;
		private const string ImportId = "a8d0c1f2-0000-4000-8000-000000000001";

		private Mock<IGeoLocationProvider> _geo;
		private Mock<IPoiImportsRepository> _imports;
		private Mock<IMappingService> _mapping;
		private List<Poi> _saved;
		private List<PoiImport> _writes;
		private List<string> _runTokens;
		private string _claimToken;
		private bool _claimHeld;

		[SetUp]
		public void SetUp()
		{
			_geo = new Mock<IGeoLocationProvider>();
			_imports = new Mock<IPoiImportsRepository>();
			_mapping = new Mock<IMappingService>();
			_saved = new List<Poi>();
			_writes = new List<PoiImport>();
			_runTokens = new List<string>();
			_claimToken = null;
			_claimHeld = true;

			_mapping.Setup(x => x.GetTypeByIdAsync(PoiTypeId)).ReturnsAsync(new PoiType { PoiTypeId = PoiTypeId, DepartmentId = DepartmentId });
			_mapping.Setup(x => x.SavePOIAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>()))
				.Callback((Poi p, CancellationToken _) => _saved.Add(p))
				.ReturnsAsync((Poi p, CancellationToken _) => p);
			// The upload's insert.
			_imports.Setup(x => x.SaveOrUpdateAsync(It.IsAny<PoiImport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((PoiImport i, CancellationToken _, bool __) => _writes.Add(Copy(i)))
				.ReturnsAsync((PoiImport i, CancellationToken _, bool __) => i);
			// The worker's writes, which only land while its claim holds.
			_imports.Setup(x => x.SaveRunAsync(It.IsAny<PoiImport>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((PoiImport i, string token, CancellationToken _) =>
				{
					_runTokens.Add(token);
					if (_claimHeld)
						_writes.Add(Copy(i));

					return _claimHeld;
				});
			_imports.Setup(x => x.HeartbeatAsync(ImportId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _claimHeld);
			_imports.Setup(x => x.CountSavedPoisAsync(ImportId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _saved.Count);
			_imports.Setup(x => x.GetLastSavedRowAsync(ImportId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _saved.Count == 0 ? (int?)null : _saved.Max(p => p.PoiImportRow));
		}

		private static PoiImport Copy(PoiImport import) => JsonConvert.DeserializeObject<PoiImport>(JsonConvert.SerializeObject(import));

		private PoiImportService Service() => new PoiImportService(new KmlProvider(), _geo.Object, _imports.Object, _mapping.Object);

		private static Stream Text(string text, Encoding encoding = null) => new MemoryStream((encoding ?? new UTF8Encoding(false)).GetBytes(text));

		private void Geocodes(string address, double latitude, double longitude) =>
			_geo.Setup(x => x.GeocodeAddressAsync(address, It.IsAny<CancellationToken>())).ReturnsAsync(GeocodeResult.Found(latitude, longitude));

		private void Geocodes(string address, GeocodeOutcome outcome) =>
			_geo.Setup(x => x.GeocodeAddressAsync(address, It.IsAny<CancellationToken>())).ReturnsAsync(GeocodeResult.Of(outcome));

		private static List<PoiImportRow> RowsOf(PoiImport import) => JsonConvert.DeserializeObject<List<PoiImportRow>>(import.RowsJson);

		/// <summary>An import the worker can claim, holding <paramref name="rows"/>, as a runnable row and after its claim.</summary>
		private PoiImport Runnable(PoiImportStatus status, int attempts, params PoiImportRow[] rows)
		{
			var claimedStatus = status == PoiImportStatus.Saving ? PoiImportStatus.Saving : PoiImportStatus.Locating;
			var import = new PoiImport
			{
				PoiImportId = ImportId, DepartmentId = DepartmentId, PoiTypeId = PoiTypeId, FileName = "stores.csv",
				Status = (int)claimedStatus, Attempts = attempts, TotalRows = rows.Length, RowsJson = JsonConvert.SerializeObject(rows)
			};

			_imports.Setup(x => x.GetRunnableAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(new List<PoiImport> { new PoiImport { PoiImportId = ImportId, Status = (int)status } });
			_imports.Setup(x => x.TryClaimAsync(ImportId, (int)status, (int)claimedStatus, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string _, int __, int ___, DateTime ____, string token, CancellationToken _____) => _claimToken = token)
				.ReturnsAsync(true);
			_imports.Setup(x => x.GetByIdAsync(ImportId)).ReturnsAsync(import);

			return import;
		}

		private PoiImport Final => _writes.Last();

		// The shape Google My Maps exports for a layer it located from address columns: no Point, an <address> built from
		// every location column (IDs and names included), and the sheet's columns as ExtendedData with ZIPs as "60611.0".
		private const string MyMapsKml =
			"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>Asset_List.csv</name>" +
			"<Placemark><name>Hy-Vee Health Infusion Care - Chicago</name>" +
			"<address>4116 Hy-Vee Health Infusion Care - Chicago 1 E Erie St Chicago Illinois</address>" +
			"<description><![CDATA[External ID: 4116<br>Notes: <br>Street Address 1: 1 E Erie St]]></description>" +
			"<ExtendedData><Data name=\"External ID\"><value>4116</value></Data><Data name=\"Notes\"><value/></Data>" +
			"<Data name=\"Street Address 1\"><value>1 E Erie St</value></Data><Data name=\"City\"><value>Chicago</value></Data>" +
			"<Data name=\"State\"><value>Illinois</value></Data><Data name=\"Postal Code\"><value>60611.0</value></Data>" +
			"<Data name=\"Central\"><value>East</value></Data></ExtendedData></Placemark>" +
			"<Placemark><name>Lexington Dollar Fresh Market</name><address>1375 Lexington 1001 S Plum Creek Pkwy</address>" +
			"<ExtendedData><Data name=\"Notes\"><value>Closed Store</value></Data><Data name=\"Street Address 1\"><value>1001 S Plum Creek Pkwy</value></Data>" +
			"<Data name=\"City\"><value>Lexington</value></Data><Data name=\"State\"><value>NE</value></Data><Data name=\"Postal Code\"><value>68850.0</value></Data>" +
			"</ExtendedData></Placemark>" +
			"<Placemark><name>Gretna</name><address>1227 Gretna S 191st St Omaha NE</address></Placemark>" +
			"</Document></kml>";

		#region Reading the upload

		[Test]
		public void Reads_a_google_my_maps_export_as_addresses_to_locate()
		{
			var file = Service().ReadFile(Text(MyMapsKml), "Asset_List.csv.kml");

			file.Error.Should().BeNull();
			file.SkippedCount.Should().Be(0);
			file.Rows.Select(r => r.Name).Should().Equal("Hy-Vee Health Infusion Care - Chicago", "Lexington Dollar Fresh Market", "Gretna");
			file.Rows.Should().OnlyContain(r => !r.HasPosition);
			file.Rows[0].Address.Should().Be("1 E Erie St, Chicago, Illinois, 60611", "address columns beat <address>, and a spreadsheet's 60611.0 is a ZIP");
			file.Rows[1].Note.Should().Be("Closed Store");
			file.Rows[2].Address.Should().Be("1227 Gretna S 191st St Omaha NE", "a placemark without address columns falls back to its <address>");
			_geo.Verify(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "the upload request never geocodes");
		}

		[Test]
		public void Reads_a_csv_of_addresses_and_ignores_columns_it_does_not_know()
		{
			var csv =
				"External ID,Name,Notes,Street Address 1,City,State,Postal Code,Central\r\n" +
				"4116,Hy-Vee Health Infusion Care - Chicago,,1 E Erie St,Chicago,Illinois,60611,East\r\n" +
				"4005-02,Cherokee Transportation,\"Lot POC, gate code 12\",1010 Riverview Dr,Cherokee,IA,51012,Dollar Fresh\r\n" +
				",,,,,,,\r\n";

			var file = Service().ReadFile(Text(csv), "Asset_List_10-16-26.csv");

			file.Error.Should().BeNull();
			file.Rows.Select(r => r.Name).Should().Equal("Hy-Vee Health Infusion Care - Chicago", "Cherokee Transportation");
			file.Rows[1].Address.Should().Be("1010 Riverview Dr, Cherokee, IA, 51012");
			file.Rows[1].Note.Should().Be("Lot POC, gate code 12");
			file.SkippedCount.Should().Be(0, "a blank row is not an entry");
		}

		[Test]
		public void Uses_latitude_and_longitude_columns()
		{
			var file = Service().ReadFile(Text("Name,Lat,Lng,Address\nTank 7,41.59,-93.62,1 Main St\nTank 8,,,\n"), "tanks.csv");

			file.Rows.Should().ContainSingle();
			file.Rows[0].Latitude.Should().Be(41.59);
			file.Rows[0].Longitude.Should().Be(-93.62);
			file.Rows[0].Address.Should().Be("1 Main St");
			file.SkippedCount.Should().Be(1);
		}

		[Test]
		public void Reads_a_semicolon_csv_with_decimal_commas_and_latin1_text()
		{
			var file = Service().ReadFile(Text("Name;Latitude;Longitude\nCafé 1;48,85;2,35\n", Encoding.Latin1), "points.csv");

			file.Rows.Single().Name.Should().Be("Café 1");
			file.Rows.Single().Latitude.Should().Be(48.85);
			file.Rows.Single().Longitude.Should().Be(2.35);
		}

		[Test]
		public void Detects_the_delimiter_from_separators_outside_quoted_headers()
		{
			// Two commas and two semicolons in the header row, but the commas are inside a quoted column name.
			var file = Service().ReadFile(Text("Name;Address;\"Notes, comments, remarks\"\nDepot;1 Main St, Ames, IA;\"Gate 4, code 12\"\n"), "sites.csv");

			file.Error.Should().BeNull();
			file.Rows.Single().Name.Should().Be("Depot");
			file.Rows.Single().Address.Should().Be("1 Main St, Ames, IA");
		}

		[Test]
		public void Explains_a_csv_without_position_or_address_columns()
		{
			var file = Service().ReadFile(Text("Station,Phone\n1,555-0100\n"), "stations.csv");

			file.Error.Should().StartWith("The first row of the CSV must name the columns").And.Contain("Columns found: Station, Phone.");
			file.Rows.Should().BeEmpty();
		}

		[Test]
		public void Treats_a_converters_0_0_placeholder_with_an_address_as_an_address()
		{
			var file = Service().ReadFile(Text("<kml><Placemark><name>Tank</name><address>1 Main St</address><Point><coordinates>0,0</coordinates></Point></Placemark></kml>"), "tanks.kml");

			file.Rows.Single().HasPosition.Should().BeFalse();
			file.Rows.Single().Address.Should().Be("1 Main St");
			file.SkippedCount.Should().Be(0);
		}

		[Test]
		public async Task Queues_the_rows_for_the_worker()
		{
			var file = Service().ReadFile(Text(MyMapsKml), "Asset_List.csv.kml");

			await Service().QueueImportAsync(DepartmentId, PoiTypeId, "Stores", "user-1", @"C:\fakepath\Asset_List.csv.kml", file);

			var queued = _writes.Single();
			queued.Status.Should().Be((int)PoiImportStatus.Queued);
			queued.DepartmentId.Should().Be(DepartmentId);
			queued.PoiTypeId.Should().Be(PoiTypeId);
			queued.PoiTypeName.Should().Be("Stores");
			queued.UserId.Should().Be("user-1");
			queued.FileName.Should().Be("Asset_List.csv.kml");
			queued.TotalRows.Should().Be(3);
			JsonConvert.DeserializeObject<List<PoiImportRow>>(queued.RowsJson).Select(r => r.Address).Should().Contain("1 E Erie St, Chicago, Illinois, 60611");
		}

		#endregion Reading the upload

		#region Worker

		[Test]
		public async Task Geocodes_and_saves_a_queued_import_tagging_each_poi_with_the_import()
		{
			Geocodes("1 E Erie St, Chicago, Illinois, 60611", 41.8938, -87.6268);
			Geocodes("1010 Riverview Dr, Cherokee, IA, 51012", 42.7494, -95.5517);
			Geocodes("99999 Imaginary Rd, Atlantis, ZZ", GeocodeOutcome.NotFound);
			Runnable(PoiImportStatus.Queued, 1,
				new PoiImportRow { Name = "Tank 7", Latitude = 41.59, Longitude = -93.62 },
				new PoiImportRow { Name = "Erie", Address = "1 E Erie St, Chicago, Illinois, 60611", Note = "Closed Store" },
				new PoiImportRow { Name = "Cherokee DC", Address = "1010 Riverview Dr, Cherokee, IA, 51012" },
				new PoiImportRow { Name = "Cherokee Transportation", Address = "1010 Riverview Dr, Cherokee, IA, 51012" },
				new PoiImportRow { Name = "Nowhere", Address = "99999 Imaginary Rd, Atlantis, ZZ" });

			(await Service().ProcessNextAsync()).Should().BeTrue();

			_saved.Select(p => p.Name).Should().Equal("Tank 7", "Erie", "Cherokee DC", "Cherokee Transportation");
			_saved.Should().OnlyContain(p => p.PoiImportId == ImportId && p.PoiTypeId == PoiTypeId);
			_saved[1].Latitude.Should().Be(41.8938);
			_saved[1].Address.Should().Be("1 E Erie St, Chicago, Illinois, 60611");
			_saved[1].Note.Should().Be("Closed Store");
			_geo.Verify(x => x.GeocodeAddressAsync("1010 Riverview Dr, Cherokee, IA, 51012", It.IsAny<CancellationToken>()), Times.Once, "rows sharing an address share one lookup");

			_writes.First().Status.Should().Be((int)PoiImportStatus.Saving, "positions are stored before any POI is saved");
			RowsOf(_writes.First()).Count(r => r.HasPosition).Should().Be(4);

			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			Final.ImportedCount.Should().Be(4);
			Final.GeocodedCount.Should().Be(3);
			Final.UnlocatedCount.Should().Be(1);
			Final.LookupFailedCount.Should().Be(0);
			Final.RowsJson.Should().BeNull();
			Service().GetUnlocatedRows(Final).Select(r => r.Name).Should().Equal("Nowhere");
		}

		[Test]
		public async Task An_address_no_geocoder_could_find_is_reported_and_never_retried()
		{
			Geocodes("99999 Imaginary Rd, Atlantis, ZZ", GeocodeOutcome.NotFound);
			Runnable(PoiImportStatus.Queued, 1, new PoiImportRow { Name = "Nowhere", Address = "99999 Imaginary Rd, Atlantis, ZZ" });

			await Service().ProcessNextAsync();

			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			Final.RetryAfter.Should().BeNull();
			Final.UnlocatedCount.Should().Be(1);
			Service().GetUnlocatedRows(Final).Single().Lookup.Should().Be((int)PoiImportRowLookup.NotFound);
			_geo.Verify(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Lookups_no_geocoder_answered_wait_for_a_retry_before_anything_is_saved()
		{
			Geocodes("1 E Erie St, Chicago, Illinois, 60611", 41.8938, -87.6268);
			Geocodes("1010 Riverview Dr, Cherokee, IA, 51012", GeocodeOutcome.Unavailable);
			Runnable(PoiImportStatus.Queued, 1,
				new PoiImportRow { Name = "Erie", Address = "1 E Erie St, Chicago, Illinois, 60611" },
				new PoiImportRow { Name = "Cherokee", Address = "1010 Riverview Dr, Cherokee, IA, 51012" });

			var before = DateTime.UtcNow;
			await Service().ProcessNextAsync();

			_saved.Should().BeEmpty("the import saves once every lookup has an answer");
			var waiting = _writes.Single();
			waiting.Status.Should().Be((int)PoiImportStatus.Locating);
			waiting.PendingRetryCount.Should().Be(1);
			waiting.RetryAfter.Should().BeOnOrAfter(before + PoiImportService.LookupRetryDelays[0]);
			waiting.HeartbeatOn.Should().BeNull("the worker hands the import back");
			waiting.Attempts.Should().Be(0, "waiting out an outage is not a failed run");
			var rows = RowsOf(waiting);
			rows.Single(r => r.Name == "Erie").HasPosition.Should().BeTrue("a found position is kept for the retry");
			rows.Single(r => r.Name == "Cherokee").Lookup.Should().Be((int)PoiImportRowLookup.Unavailable);
			rows.Single(r => r.Name == "Cherokee").LookupAttempts.Should().Be(1);
		}

		[Test]
		public async Task A_retry_asks_only_for_the_unanswered_lookups_and_then_saves()
		{
			Geocodes("1010 Riverview Dr, Cherokee, IA, 51012", 42.7494, -95.5517);
			Runnable(PoiImportStatus.Locating, 1,
				new PoiImportRow { Name = "Erie", Address = "1 E Erie St", Latitude = 41.89, Longitude = -87.62, Lookup = (int)PoiImportRowLookup.Found, LookupAttempts = 1 },
				new PoiImportRow { Name = "Nowhere", Address = "nowhere", Lookup = (int)PoiImportRowLookup.NotFound, LookupAttempts = 1 },
				new PoiImportRow { Name = "Cherokee", Address = "1010 Riverview Dr, Cherokee, IA, 51012", Lookup = (int)PoiImportRowLookup.Unavailable, LookupAttempts = 1 });

			await Service().ProcessNextAsync();

			_geo.Verify(x => x.GeocodeAddressAsync("1010 Riverview Dr, Cherokee, IA, 51012", It.IsAny<CancellationToken>()), Times.Once);
			_geo.Verify(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once, "found and not-found addresses are never asked again");
			_saved.Select(p => p.Name).Should().Equal("Erie", "Cherokee");
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			Final.GeocodedCount.Should().Be(2);
			Final.UnlocatedCount.Should().Be(1);
			Final.PendingRetryCount.Should().Be(0);
		}

		[Test]
		public async Task Backs_off_further_on_each_retry_and_reports_the_lookup_once_the_retries_run_out()
		{
			Geocodes("1010 Riverview Dr", GeocodeOutcome.Unavailable);
			Runnable(PoiImportStatus.Locating, 1,
				new PoiImportRow { Name = "Cherokee", Address = "1010 Riverview Dr", Lookup = (int)PoiImportRowLookup.Unavailable, LookupAttempts = 2 });

			var before = DateTime.UtcNow;
			await Service().ProcessNextAsync();

			_writes.Single().RetryAfter.Should().BeOnOrAfter(before + PoiImportService.LookupRetryDelays[2]);

			_writes.Clear();
			Runnable(PoiImportStatus.Locating, 1,
				new PoiImportRow { Name = "Cherokee", Address = "1010 Riverview Dr", Lookup = (int)PoiImportRowLookup.Unavailable, LookupAttempts = PoiImportService.MaximumLookupAttempts - 1 });

			await Service().ProcessNextAsync();

			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			Final.LookupFailedCount.Should().Be(1);
			Final.UnlocatedCount.Should().Be(0);
			Final.LookupNotConfigured.Should().BeFalse();
			Service().GetUnlocatedRows(Final).Single().LookupAttempts.Should().Be(PoiImportService.MaximumLookupAttempts);
		}

		[Test]
		public async Task Reports_at_once_when_no_geocoder_is_configured()
		{
			Geocodes("1 Main St", GeocodeOutcome.NotConfigured);
			Runnable(PoiImportStatus.Queued, 1,
				new PoiImportRow { Name = "Site", Address = "1 Main St" },
				new PoiImportRow { Name = "Tank", Latitude = 41.5, Longitude = -93.5 });

			await Service().ProcessNextAsync();

			_saved.Select(p => p.Name).Should().Equal("Tank");
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			Final.LookupFailedCount.Should().Be(1);
			Final.LookupNotConfigured.Should().BeTrue();
			Final.RetryAfter.Should().BeNull();
		}

		[Test]
		public async Task A_resumed_import_saves_only_the_pois_it_had_not_saved()
		{
			_saved.AddRange(new[] { new Poi { Name = "A", PoiImportRow = 0 }, new Poi { Name = "B", PoiImportRow = 1 } });
			Runnable(PoiImportStatus.Saving, 2,
				new PoiImportRow { Name = "A", Latitude = 41.1, Longitude = -93.1 },
				new PoiImportRow { Name = "Unlocated", Address = "nowhere", Lookup = (int)PoiImportRowLookup.NotFound, LookupAttempts = 1 },
				new PoiImportRow { Name = "B", Latitude = 41.2, Longitude = -93.2 },
				new PoiImportRow { Name = "C", Latitude = 41.3, Longitude = -93.3 });

			await Service().ProcessNextAsync();

			_saved.Select(p => p.Name).Should().Equal("A", "B", "C");
			_saved.Last().PoiImportRow.Should().Be(2, "rows are numbered among the located rows, in file order");
			_geo.Verify(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "a saving import already holds its positions");
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			Final.ImportedCount.Should().Be(3);
		}

		[Test]
		public async Task A_resumed_import_carries_on_after_its_last_row_when_a_saved_poi_was_deleted_meanwhile()
		{
			// Rows 0 and 2 were saved before the worker stopped; an administrator deleted row 1's POI while the import ran. Counting
			// the two surviving POIs would resume at row 2 and save it a second time.
			_saved.AddRange(new[] { new Poi { Name = "A", PoiImportRow = 0 }, new Poi { Name = "C", PoiImportRow = 2 } });
			Runnable(PoiImportStatus.Saving, 2,
				new PoiImportRow { Name = "A", Latitude = 41.1, Longitude = -93.1 },
				new PoiImportRow { Name = "B", Latitude = 41.2, Longitude = -93.2 },
				new PoiImportRow { Name = "C", Latitude = 41.3, Longitude = -93.3 },
				new PoiImportRow { Name = "D", Latitude = 41.4, Longitude = -93.4 });

			await Service().ProcessNextAsync();

			_saved.Select(p => p.Name).Should().Equal("A", "C", "D");
			_saved.Last().PoiImportRow.Should().Be(3);
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
		}

		[Test]
		public async Task Locating_checkpoints_the_lookups_made_so_far_with_the_heartbeat()
		{
			// 25 addresses: one chunk of 24 lookups, then one more. A run that stopped after the first chunk resumes from the
			// checkpoint instead of asking the geocoders about those 24 addresses again.
			var rows = Enumerable.Range(1, 25).Select(n => new PoiImportRow { Name = "Site " + n, Address = n + " Main St" }).ToArray();
			foreach (var row in rows)
				Geocodes(row.Address, 41.5, -93.5);
			Runnable(PoiImportStatus.Queued, 1, rows);
			var service = Service();
			service.HeartbeatInterval = TimeSpan.Zero;

			await service.ProcessNextAsync();

			var checkpoint = _writes.First();
			checkpoint.Status.Should().Be((int)PoiImportStatus.Locating);
			checkpoint.HeartbeatOn.Should().NotBeNull();
			RowsOf(checkpoint).Count(r => r.Lookup == (int)PoiImportRowLookup.Found).Should().Be(24);
			RowsOf(checkpoint).Count(r => r.Lookup == (int)PoiImportRowLookup.None).Should().Be(1);
			_runTokens.Should().OnlyContain(t => t == _claimToken, "checkpoints go through the claim-guarded write");
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			_saved.Should().HaveCount(25);
		}

		[Test]
		public async Task A_locating_import_resumed_from_a_checkpoint_asks_only_for_the_addresses_not_yet_looked_up()
		{
			Geocodes("2 Main St", 41.6, -93.6);
			Runnable(PoiImportStatus.Locating, 2,
				new PoiImportRow { Name = "One", Address = "1 Main St", Latitude = 41.5, Longitude = -93.5, Lookup = (int)PoiImportRowLookup.Found, LookupAttempts = 1 },
				new PoiImportRow { Name = "Two", Address = "2 Main St" });

			await Service().ProcessNextAsync();

			_geo.Verify(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_saved.Select(p => p.Name).Should().Equal("One", "Two");
		}

		[Test]
		public async Task Every_write_of_a_run_names_the_claim_it_took()
		{
			Runnable(PoiImportStatus.Queued, 1, new PoiImportRow { Name = "Tank", Latitude = 41.5, Longitude = -93.5 });

			await Service().ProcessNextAsync();

			_claimToken.Should().NotBeNullOrEmpty();
			_runTokens.Should().NotBeEmpty().And.OnlyContain(t => t == _claimToken);
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
		}

		[Test]
		public async Task A_run_whose_claim_was_taken_over_stops_without_saving_releasing_or_failing_the_import()
		{
			// This run stalled past the stale period and another worker took the import over: its first write finds the claim gone.
			_claimHeld = false;
			Runnable(PoiImportStatus.Queued, PoiImportService.MaximumAttempts, new PoiImportRow { Name = "Tank", Latitude = 41.5, Longitude = -93.5 });

			(await Service().ProcessNextAsync()).Should().BeTrue();

			_saved.Should().BeEmpty("the run that owns the import saves its POIs");
			_writes.Should().BeEmpty();
			_imports.Verify(x => x.SaveRunAsync(It.IsAny<PoiImport>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_imports.Verify(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Does_nothing_when_another_worker_claimed_the_import()
		{
			_imports.Setup(x => x.GetRunnableAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(new List<PoiImport> { new PoiImport { PoiImportId = ImportId, Status = (int)PoiImportStatus.Queued } });
			_imports.Setup(x => x.TryClaimAsync(ImportId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await Service().ProcessNextAsync()).Should().BeFalse();

			_imports.Verify(x => x.GetByIdAsync(It.IsAny<object>()), Times.Never);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task Fails_an_import_whose_poi_type_was_deleted()
		{
			_mapping.Setup(x => x.GetTypeByIdAsync(PoiTypeId)).ReturnsAsync((PoiType)null);
			Runnable(PoiImportStatus.Queued, 1, new PoiImportRow { Name = "Tank", Latitude = 41.5, Longitude = -93.5 });

			await Service().ProcessNextAsync();

			_saved.Should().BeEmpty();
			Final.Status.Should().Be((int)PoiImportStatus.Failed);
			Final.Error.Should().Be("The POI type was deleted before the import ran.");
		}

		[Test]
		public async Task A_lookup_the_geocoder_never_answers_is_abandoned_and_retried_later()
		{
			_geo.Setup(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(new TaskCompletionSource<GeocodeResult>().Task);
			Runnable(PoiImportStatus.Queued, 1, new PoiImportRow { Name = "Site", Address = "1 Main St" });
			var service = Service();
			service.LookupTimeout = TimeSpan.FromMilliseconds(100);

			await service.ProcessNextAsync();

			var waiting = _writes.Single();
			waiting.Status.Should().Be((int)PoiImportStatus.Locating);
			waiting.PendingRetryCount.Should().Be(1);
			RowsOf(waiting).Single().Lookup.Should().Be((int)PoiImportRowLookup.Unavailable);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task Releases_a_failed_import_for_retry_and_gives_up_after_the_last_attempt()
		{
			_mapping.Setup(x => x.SavePOIAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database unavailable"));
			Runnable(PoiImportStatus.Saving, 1, new PoiImportRow { Name = "Tank", Latitude = 41.5, Longitude = -93.5 });

			await Service().ProcessNextAsync();

			_imports.Verify(x => x.ReleaseAsync(ImportId, _claimToken, It.IsAny<CancellationToken>()), Times.Once);
			_writes.Should().BeEmpty();

			Runnable(PoiImportStatus.Saving, PoiImportService.MaximumAttempts, new PoiImportRow { Name = "Tank", Latitude = 41.5, Longitude = -93.5 });

			await Service().ProcessNextAsync();

			Final.Status.Should().Be((int)PoiImportStatus.Failed);
			Final.Error.Should().Contain("given up");
		}

		[Test]
		public async Task A_stopping_worker_releases_the_import_so_the_next_one_resumes_it()
		{
			_geo.Setup(x => x.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(new TaskCompletionSource<GeocodeResult>().Task);
			Runnable(PoiImportStatus.Queued, 1, new PoiImportRow { Name = "Site", Address = "1 Main St" });
			using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

			Func<Task> run = () => Service().ProcessNextAsync(stopping.Token);

			await run.Should().ThrowAsync<OperationCanceledException>();
			_imports.Verify(x => x.ReleaseAsync(ImportId, _claimToken, It.IsAny<CancellationToken>()), Times.Once);
			_writes.Should().BeEmpty();
		}

		#endregion Worker

		#region Capabilities

		// The Belgian EMS sheet of hospitals: what each one offers in a "Capabilities" column, separated by ; , or |.
		[Test]
		public void Reads_a_capabilities_column_split_on_semicolons_commas_and_bars()
		{
			var csv =
				"Name,Latitude,Longitude,Capabilities\r\n" +
				"UZ Gent,51.0234,3.7262,\"Cath Lab; Stroke Center, Trauma Center | cath lab\"\r\n" +
				"AZ Alma,51.1854,3.5634,\r\n" +
				"OLV Aalst,50.9364,4.0390,\" ; | \"\r\n";

			var file = Service().ReadFile(Text(csv), "hospitals.csv");

			file.Error.Should().BeNull();
			file.Rows[0].Capabilities.Should().Equal(new[] { "Cath Lab", "Stroke Center", "Trauma Center" }, "names are trimmed and a repeat (in any case) is dropped");
			file.Rows[1].Capabilities.Should().BeNull("a blank cell names none");
			file.Rows[2].Capabilities.Should().BeNull("separators alone name none");
		}

		[TestCase("Capability")]
		[TestCase("Specialties")]
		[TestCase("Specialities")]
		[TestCase("Services")]
		[TestCase("capabilities")]
		public void Recognizes_the_capabilities_column_by_its_other_names(string header)
		{
			var file = Service().ReadFile(Text("Name;Lat;Lng;" + header + "\nUZ Gent;51,0234;3,7262;Cath Lab|Burns\n"), "hospitals.csv");

			file.Rows.Single().Capabilities.Should().Equal("Cath Lab", "Burns");
		}

		[Test]
		public void Cuts_a_capability_name_to_the_column_length()
		{
			var file = Service().ReadFile(Text("Name,Lat,Lng,Capabilities\nSite,41.5,-93.6," + new string('x', PoiCapability.MaxNameLength + 20) + "\n"), "sites.csv");

			file.Rows.Single().Capabilities.Single().Should().HaveLength(PoiCapability.MaxNameLength);
		}

		[Test]
		public async Task Rows_without_the_column_are_queued_as_before()
		{
			var file = Service().ReadFile(Text("Name,Lat,Lng\nTank 7,41.59,-93.62\n"), "tanks.csv");

			await Service().QueueImportAsync(DepartmentId, PoiTypeId, "Tanks", "user-1", "tanks.csv", file);

			file.Rows.Single().Capabilities.Should().BeNull();
			_writes.Single().RowsJson.Should().NotContain("Capabilities", "a file without the column stores the rows it always did");
		}

		[Test]
		public void Reads_capabilities_from_kml_extended_data_with_or_without_a_point()
		{
			var kml =
				"<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>" +
				"<Placemark><name>UZ Gent</name><ExtendedData><Data name=\"Capabilities\"><value>Cath Lab; Stroke Center</value></Data></ExtendedData>" +
				"<Point><coordinates>3.7262,51.0234</coordinates></Point></Placemark>" +
				"<Placemark><name>AZ Alma</name><Point><coordinates>3.5634,51.1854</coordinates></Point></Placemark>" +
				"<Placemark><name>OLV Aalst</name><ExtendedData><Data name=\"Street Address 1\"><value>Moorselbaan 164</value></Data>" +
				"<Data name=\"City\"><value>Aalst</value></Data><Data name=\"Specialties\"><value>Trauma Center</value></Data></ExtendedData></Placemark>" +
				"</Document></kml>";

			var file = Service().ReadFile(Text(kml), "hospitals.kml");

			file.Error.Should().BeNull();
			file.Rows.Single(r => r.Name == "UZ Gent").Capabilities.Should().Equal("Cath Lab", "Stroke Center");
			file.Rows.Single(r => r.Name == "AZ Alma").Capabilities.Should().BeNull();
			var aalst = file.Rows.Single(r => r.Name == "OLV Aalst");
			aalst.HasPosition.Should().BeFalse();
			aalst.Capabilities.Should().Equal("Trauma Center");
		}

		/// <summary>Saved POIs get ids (100 + their row), as the database would give them.</summary>
		private void SavedPoisGetIds()
		{
			_mapping.Setup(x => x.SavePOIAsync(It.IsAny<Poi>(), It.IsAny<CancellationToken>()))
				.Callback((Poi p, CancellationToken _) => _saved.Add(p))
				.ReturnsAsync((Poi p, CancellationToken _) => { p.PoiId = 100 + (p.PoiImportRow ?? 0); return p; });
		}

		/// <summary>The department already has "Cath Lab"; any other name asked for is added with a new id.</summary>
		private List<List<string>> CapabilitiesAddedOrFound()
		{
			var asked = new List<List<string>>();
			_mapping.Setup(x => x.GetOrAddPoiCapabilitiesByNameAsync(DepartmentId, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, IEnumerable<string> names, CancellationToken __) =>
				{
					asked.Add(names.ToList());
					return names.Select(n => string.Equals(n, "cath lab", StringComparison.OrdinalIgnoreCase)
						? new PoiCapability { PoiCapabilityId = "cath", DepartmentId = DepartmentId, Name = "Cath Lab" }
						: new PoiCapability { PoiCapabilityId = "new-" + n.ToLowerInvariant().Replace(' ', '-'), DepartmentId = DepartmentId, Name = n }).ToList();
				});
			return asked;
		}

		[Test]
		public async Task Saving_tags_each_poi_with_its_capabilities_adding_the_missing_ones_once_per_run()
		{
			SavedPoisGetIds();
			var asked = CapabilitiesAddedOrFound();
			Runnable(PoiImportStatus.Saving, 1,
				new PoiImportRow { Name = "UZ Gent", Latitude = 51.0234, Longitude = 3.7262, Capabilities = new List<string> { "CATH LAB", "Stroke Center" } },
				new PoiImportRow { Name = "AZ Alma", Latitude = 51.1854, Longitude = 3.5634 },
				new PoiImportRow { Name = "OLV Aalst", Latitude = 50.9364, Longitude = 4.0390, Capabilities = new List<string> { "Stroke Center", "Trauma Center" } });

			await Service().ProcessNextAsync();

			Final.Status.Should().Be((int)PoiImportStatus.Complete);
			asked.Should().ContainSingle("the names are matched once per run, not per row");
			asked[0].Should().BeEquivalentTo(new[] { "CATH LAB", "Stroke Center", "Trauma Center" });
			_mapping.Verify(x => x.SetPoiCapabilitiesAsync(DepartmentId, 100, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "cath", "new-stroke-center" })), It.IsAny<CancellationToken>()), Times.Once);
			_mapping.Verify(x => x.SetPoiCapabilitiesAsync(DepartmentId, 102, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "new-stroke-center", "new-trauma-center" })), It.IsAny<CancellationToken>()), Times.Once);
			_mapping.Verify(x => x.SetPoiCapabilitiesAsync(It.IsAny<int>(), 101, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never, "a row without capabilities gets none written");
		}

		[Test]
		public async Task An_import_without_capabilities_never_touches_them()
		{
			SavedPoisGetIds();
			Runnable(PoiImportStatus.Saving, 1,
				new PoiImportRow { Name = "Tank 7", Latitude = 41.59, Longitude = -93.62 },
				new PoiImportRow { Name = "Tank 8", Latitude = 41.6, Longitude = -93.6 });

			await Service().ProcessNextAsync();

			_saved.Should().HaveCount(2);
			_mapping.Verify(x => x.GetOrAddPoiCapabilitiesByNameAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
			_mapping.Verify(x => x.SetPoiCapabilitiesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_resumed_import_sets_the_capabilities_of_the_poi_it_saved_last_again()
		{
			// The worker stopped after saving row 0's POI (id 100), before its capabilities were written.
			SavedPoisGetIds();
			CapabilitiesAddedOrFound();
			_saved.Add(new Poi { PoiId = 100, Name = "UZ Gent", PoiImportId = ImportId, PoiImportRow = 0 });
			_mapping.Setup(x => x.GetTypeByIdAsync(PoiTypeId)).ReturnsAsync(new PoiType { PoiTypeId = PoiTypeId, DepartmentId = DepartmentId, Pois = _saved.ToList() });
			Runnable(PoiImportStatus.Saving, 2,
				new PoiImportRow { Name = "UZ Gent", Latitude = 51.0234, Longitude = 3.7262, Capabilities = new List<string> { "Cath Lab" } },
				new PoiImportRow { Name = "OLV Aalst", Latitude = 50.9364, Longitude = 4.0390, Capabilities = new List<string> { "Trauma Center" } });

			await Service().ProcessNextAsync();

			_saved.Select(p => p.Name).Should().Equal(new[] { "UZ Gent", "OLV Aalst" }, "row 0 is not saved a second time");
			_mapping.Verify(x => x.SetPoiCapabilitiesAsync(DepartmentId, 100, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "cath" })), It.IsAny<CancellationToken>()), Times.Once);
			_mapping.Verify(x => x.SetPoiCapabilitiesAsync(DepartmentId, 101, It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "new-trauma-center" })), It.IsAny<CancellationToken>()), Times.Once);
			Final.Status.Should().Be((int)PoiImportStatus.Complete);
		}

		#endregion Capabilities
	}
}
