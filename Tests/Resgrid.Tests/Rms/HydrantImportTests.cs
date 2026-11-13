using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Resgrid.Framework;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class HydrantImportTests
	{
		[TestCase("\"number\":123,\"latitude\":45,\"longitude\":-122", "number")]
		[TestCase("\"number\":\"H\",\"latitude\":\"45\",\"longitude\":-122", "latitude")]
		[TestCase("\"number\":\"H\",\"longitude\":-122", "latitude")]
		[TestCase("\"number\":\"H\",\"latitude\":null,\"longitude\":-122", "latitude")]
		[TestCase("\"number\":\"H\",\"latitude\":91,\"longitude\":-122", "latitude")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-181", "longitude")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"flow_gpm\":12.5", "flow_gpm")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"flow_gpm\":2147483648", "flow_gpm")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"flow_gpm\":-1", "flow_gpm")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"main_size\":10000", "main_size")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"main_size\":1.234", "main_size")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"type\":99", "type")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"type\":true", "type")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"longitude\":-122,\"DepartmentId\":8", "unknown field")]
		[TestCase("\"number\":\"H\",\"latitude\":45,\"Latitude\":46,\"longitude\":-122", "duplicate field")]
		public async Task Json_rejects_invalid_fields_without_saving_other_rows(string properties, string correction)
		{
			var h = new RmsPreventionHarness();
			var result = await h.HydrantsService.ImportAsync(Dept, Admin, "[{" + properties + "},{\"number\":\"Good\",\"latitude\":45,\"longitude\":-122}]", "json");
			result.ValidationFailed.Should().BeTrue();
			result.Rejected.Should().ContainSingle().Which.Error.Should().Contain(correction);
			result.RowsRead.Should().Be(2);
			result.Created.Should().Be(0);
			h.Hydrants.Rows.Should().BeEmpty();
		}

		[TestCase("[]")]
		[TestCase("{}")]
		[TestCase("[{\"number\":\"H\",}]")]
		[TestCase("[{ /*comment*/ }]")]
		[TestCase("[NaN]")]
		[TestCase("[{\"latitude\":Infinity}]")]
		public void Invalid_json_documents_give_actionable_feedback(string input)
		{
			Action parse = () => HydrantImportParser.Parse(input, "json");
			parse.Should().Throw<ArgumentException>();
		}

		[Test]
		public async Task Valid_json_updates_without_erasing_operational_state_or_poi_link()
		{
			var h = new RmsPreventionHarness();
			var existing = await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H-101", Latitude = 1, Longitude = 2, PoiId = 12, Notes = "Preserve" });
			await h.HydrantsService.SetServiceStateAsync(Dept, Admin, existing.RmsHydrantId, false, "Maintenance");
			var result = await h.HydrantsService.ImportAsync(Dept, Admin, HydrantImportParser.JsonExample, "json");
			result.Updated.Should().Be(1);
			result.Rejected.Should().BeEmpty();
			existing.PoiId.Should().Be(12);
			existing.Notes.Should().Be("Preserve");
			existing.InService.Should().BeFalse();
			existing.FlowGpm.Should().Be(1100);
			var points = await h.HydrantsService.GetMapLayerAsync(Dept, Member, null, null, null, null);
			points.Should().ContainSingle().Which.PoiId.Should().Be(12);
			points[0].Color.Should().Be("#000000");
			Func<Task> otherDepartment = () => h.HydrantsService.GetMapLayerAsync(Dept + 1, Member, null, null, null, null);
			await otherDepartment.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[TestCase("type", "mystery", "type")]
		[TestCase("type", "-2", "type")]
		[TestCase("type", "0.5", "type")]
		[TestCase("main_size", "bad", "main_size")]
		[TestCase("flow_gpm", "12.5", "flow_gpm")]
		[TestCase("flow_gpm", "-1", "flow_gpm")]
		public void Csv_rejects_invalid_optional_values(string column, string value, string error)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,{column}\nH,45,-122,{value}", "csv");
			batch.Result.Rejected.Should().ContainSingle().Which.Error.Should().Contain(error);
		}

		[Test]
		public void Csv_handles_bom_quoted_commas_newlines_and_aliases()
		{
			var parsed = HydrantImportParser.Parse("\uFEFFhydrant_number,lat,lng,address,type\r\nH,45,-122,\"1 River Rd,\r\nRear\",wet barrel", "csv");
			parsed.Result.ValidationFailed.Should().BeFalse();
			parsed.Rows.Single().Hydrant.AddressText.Should().Contain("1 River Rd,").And.Contain("Rear");
			parsed.Rows.Single().Hydrant.Type.Should().Be((int)RmsHydrantType.WetBarrel);
		}

		[Test]
		public async Task Csv_from_another_system_imports_known_columns_and_reports_the_rest()
		{
			// Header shape of a third-party export a customer uploaded: 15 extra columns used to reject the whole file.
			const string csv = "number,status,exclude_from_auto_routing,flow_rate,flow_rating_color,hydrant_type,inspection_responsible_shift_id,inspection_responsible_station_id,"
				+ "last_inspection_date,latitude,location_address,longitude,next_inspection_date,notes,offline_edited_at,route_access_latitude,route_access_longitude,zone,\n"
				+ "H-1,Active,false,1250,green,Dry Hydrant,,7,2026-05-01,45.5,\"1 River Rd, Rear\",-122.6,2027-05-01,\"Note, with comma\",,45.49,-122.59,North,\n"
				+ "H-2,Out of Service,true,,,Wet,,,,45.6,,-122.7,,,,,,South,\n";
			var h = new RmsPreventionHarness();

			var result = await h.HydrantsService.ImportCsvAsync(Dept, Admin, csv);

			result.ValidationFailed.Should().BeFalse();
			result.Rejected.Should().BeEmpty();
			result.Created.Should().Be(2);
			result.TakenOutOfService.Should().Be(1);
			result.ReturnedToService.Should().Be(0);
			result.IgnoredColumns.Should().Equal("exclude_from_auto_routing", "inspection_responsible_shift_id",
				"inspection_responsible_station_id", "last_inspection_date", "next_inspection_date", "notes", "offline_edited_at", "route_access_latitude", "route_access_longitude", "zone");
			result.UnmappedValues.Should().BeEmpty();
			var first = h.Hydrants.Rows.Single(x => x.HydrantNumber == "H-1");
			first.Latitude.Should().Be(45.5m);
			first.Longitude.Should().Be(-122.6m);
			first.AddressText.Should().Be("1 River Rd, Rear", "location_address is an alias of address");
			first.Type.Should().Be((int)RmsHydrantType.DraftingSite, "a dry hydrant is an unpressurized drafting pipe");
			first.FlowGpm.Should().Be(1250, "flow_rate is an alias of flow_gpm");
			first.FlowClass.Should().Be((int)RmsHydrantFlowClass.A);
			first.Notes.Should().BeNull();
			var second = h.Hydrants.Rows.Single(x => x.HydrantNumber == "H-2");
			second.Type.Should().Be((int)RmsHydrantType.WetBarrel);
			second.FlowGpm.Should().BeNull();
			second.FlowClass.Should().Be((int)RmsHydrantFlowClass.Unknown);
			first.InService.Should().BeTrue();
			second.InService.Should().BeFalse();
			second.OutOfServiceReason.Should().Be("status: Out of Service");
			second.OutOfServiceSince.Should().NotBeNull();
		}

		[TestCase("Active", true)]
		[TestCase("In Service", true)]
		[TestCase("in-service", true)]
		[TestCase("Operational", true)]
		[TestCase("Yes", true)]
		[TestCase("1", true)]
		[TestCase("Out of Service", false)]
		[TestCase("OOS", false)]
		[TestCase("Temporarily Out of Service", false)]
		[TestCase("Out of service - no water", false)]
		[TestCase("Not In Service", false)]
		[TestCase("Inactive", false)]
		[TestCase("Broken", false)]
		[TestCase("Needs Repair", false)]
		[TestCase("FALSE", false)]
		[TestCase("", null)]
		[TestCase("N/A", null)]
		public void Foreign_status_values_map_to_service_state(string value, bool? expected)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,status\nH,45,-122,{value}", "csv");
			batch.Result.Rejected.Should().BeEmpty();
			batch.Result.UnmappedValues.Should().BeEmpty();
			batch.Result.IgnoredColumns.Should().BeEmpty();
			var item = batch.Rows.Single();
			item.InService.Should().Be(expected);
			item.Hydrant.OutOfServiceReason.Should().Be(expected == false ? "status: " + value : null);
		}

		[TestCase("Passed")]
		[TestCase("Due")]
		[TestCase("In Service - needs paint")]
		[TestCase("Not out of service")]
		[TestCase("No longer out of service")]
		[TestCase("Isn't out of order")]
		public void Unrecognized_status_leaves_service_state_alone_and_is_reported(string value)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,Hydrant Status\nH,45,-122,{value}", "csv");
			batch.Result.ValidationFailed.Should().BeFalse();
			batch.Rows.Single().InService.Should().BeNull();
			batch.Result.UnmappedValues.Should().ContainSingle().Which.Should().BeEquivalentTo(new HydrantImportUnmappedValue { Column = "Hydrant Status", Value = value, Rows = 1 });
		}

		[Test]
		public async Task Imported_status_changes_service_state_and_counts_the_changes()
		{
			var h = new RmsPreventionHarness();
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "Working", Latitude = 1, Longitude = 2 });
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "Untouched", Latitude = 1, Longitude = 2 });
			var repaired = await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "Repaired", Latitude = 1, Longitude = 2 });
			await h.HydrantsService.SetServiceStateAsync(Dept, Admin, repaired.RmsHydrantId, false, "Valve stuck");
			var stillDown = await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "StillDown", Latitude = 1, Longitude = 2 });
			await h.HydrantsService.SetServiceStateAsync(Dept, Admin, stillDown.RmsHydrantId, false, "Frozen");
			var since = Hydrant("StillDown").OutOfServiceSince;

			var result = await h.HydrantsService.ImportCsvAsync(Dept, Admin,
				"number,latitude,longitude,Status\nWorking,1,2,Broken\nUntouched,1,2,\nRepaired,1,2,Active\nStillDown,1,2,Out of service\nNew,1,2,Inactive\nNewActive,1,2,Active\n");

			result.FailureMessage.Should().BeNull();
			result.TakenOutOfService.Should().Be(2, "Working was taken out and New was created out of service");
			result.ReturnedToService.Should().Be(1);
			Hydrant("Working").InService.Should().BeFalse();
			Hydrant("Working").OutOfServiceReason.Should().Be("Status: Broken");
			Hydrant("Working").OutOfServiceSince.Should().NotBeNull();
			Hydrant("Untouched").InService.Should().BeTrue();
			Hydrant("Repaired").InService.Should().BeTrue();
			Hydrant("Repaired").OutOfServiceReason.Should().BeNull();
			Hydrant("Repaired").OutOfServiceSince.Should().BeNull();
			Hydrant("StillDown").OutOfServiceReason.Should().Be("Frozen", "a hydrant already out of service keeps its own reason");
			Hydrant("StillDown").OutOfServiceSince.Should().Be(since);
			Hydrant("New").InService.Should().BeFalse();
			Hydrant("New").OutOfServiceReason.Should().Be("Status: Inactive");
			Hydrant("NewActive").InService.Should().BeTrue();

			RmsHydrant Hydrant(string number) => h.Hydrants.Rows.Single(x => x.HydrantNumber == number);
		}

		[Test]
		public async Task Rolled_back_batch_does_not_count_its_service_changes_twice()
		{
			var h = new RmsPreventionHarness();
			var store = new List<RmsHydrant> { new RmsHydrant { RmsHydrantId = "existing", DepartmentId = Dept, HydrantNumber = "H1", Latitude = 1m, Longitude = 2m, InService = true } };
			List<RmsHydrant> snapshot = null;
			h.UnitOfWork.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).Callback(() => snapshot = store.Select(x => x.CloneJson()).ToList()).ReturnsAsync((System.Data.Common.DbConnection)null);
			h.UnitOfWork.Setup(u => u.DiscardChanges()).Callback(() => { store.Clear(); store.AddRange(snapshot); snapshot = null; });
			var repository = new Mock<IRmsHydrantsRepository>();
			repository.Setup(x => x.GetAllLiveAsync(Dept)).ReturnsAsync(() => store.ToList());
			repository.Setup(x => x.GetByNumberAsync(Dept, It.IsAny<string>())).ReturnsAsync((int _, string n) => store.FirstOrDefault(x => x.HydrantNumber == n));
			repository.Setup(x => x.GetByNumbersAsync(Dept, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int _, IEnumerable<string> numbers) => (IReadOnlyDictionary<string, RmsHydrant>)numbers.Select(n => store.FirstOrDefault(x => x.HydrantNumber == n)).Where(x => x != null).ToDictionary(x => x.HydrantNumber));
			repository.Setup(x => x.InsertAsync(It.IsAny<RmsHydrant>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((RmsHydrant e, CancellationToken _, bool __) => { store.Add(e); return e; });
			repository.Setup(x => x.InsertAsync(It.Is<RmsHydrant>(x => x.HydrantNumber == "H3"), It.IsAny<CancellationToken>(), true)).ThrowsAsync(new Exception("secret database details"));
			repository.Setup(x => x.UpdateAsync(It.IsAny<RmsHydrant>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((RmsHydrant e, CancellationToken _, bool __) => { store.RemoveAll(x => x.RmsHydrantId == e.RmsHydrantId); store.Add(e); return e; });
			var service = new RecordsHydrantsService(h.Gate, repository.Object, h.FlowTests, h.Maintenance, h.Attachments, h.UnitOfWork.Object);

			var result = await service.ImportCsvAsync(Dept, Admin, "number,latitude,longitude,status\nH1,1,2,broken\nH2,2,3,inactive\nH3,3,4,broken");

			result.FailureMessage.Should().Contain("row 4");
			result.TakenOutOfService.Should().Be(2, "only the replayed H1 and H2 count, not the rolled-back batch attempt as well");
			store.Single(x => x.HydrantNumber == "H1").InService.Should().BeFalse();
		}

		[TestCase("Light Blue", RmsHydrantFlowClass.AA)]
		[TestCase("BLUE", RmsHydrantFlowClass.AA)]
		[TestCase("Class AA", RmsHydrantFlowClass.AA)]
		[TestCase("green", RmsHydrantFlowClass.A)]
		[TestCase("A", RmsHydrantFlowClass.A)]
		[TestCase("Orange", RmsHydrantFlowClass.B)]
		[TestCase("class-b", RmsHydrantFlowClass.B)]
		[TestCase("Red", RmsHydrantFlowClass.C)]
		[TestCase("#23c6c8", RmsHydrantFlowClass.AA)]
		[TestCase("#ADD8E6", RmsHydrantFlowClass.AA)]
		[TestCase("#1ab394", RmsHydrantFlowClass.A)]
		[TestCase("#008000", RmsHydrantFlowClass.A)]
		[TestCase("#FFA500", RmsHydrantFlowClass.B)]
		[TestCase("#f8ac59", RmsHydrantFlowClass.B)]
		[TestCase("#f00", RmsHydrantFlowClass.C)]
		[TestCase("#ed5565", RmsHydrantFlowClass.C)]
		[TestCase("", RmsHydrantFlowClass.Unknown)]
		[TestCase("N/A", RmsHydrantFlowClass.Unknown)]
		public void Foreign_flow_colours_map_to_flow_classes(string value, RmsHydrantFlowClass expected)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,flow_rating_color\nH,45,-122,{value}", "csv");
			batch.Result.Rejected.Should().BeEmpty();
			batch.Result.UnmappedValues.Should().BeEmpty();
			batch.Result.IgnoredColumns.Should().BeEmpty();
			batch.Rows.Single().Hydrant.FlowClass.Should().Be((int)expected);
		}

		[TestCase("Black")]
		[TestCase("yellow")]
		[TestCase("#FFFF00")]
		[TestCase("#808080")]
		[TestCase("#000")]
		[TestCase("#800080")]
		[TestCase("Class AA (1500+ GPM)")]
		public void Unreadable_flow_colours_get_no_class_and_are_reported(string value)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,Cap Color\nH,45,-122,\"{value}\"", "csv");
			batch.Result.ValidationFailed.Should().BeFalse();
			batch.Rows.Single().Hydrant.FlowClass.Should().Be((int)RmsHydrantFlowClass.Unknown);
			batch.Result.UnmappedValues.Should().ContainSingle().Which.Should().BeEquivalentTo(new HydrantImportUnmappedValue { Column = "Cap Color", Value = value, Rows = 1 });
		}

		[Test]
		public async Task Imported_flow_colour_sets_the_class_only_where_no_flow_decides_it()
		{
			var h = new RmsPreventionHarness();
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "Tested", Latitude = 1, Longitude = 2, FlowGpm = 1200 });
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "Untested", Latitude = 1, Longitude = 2 });

			var result = await h.HydrantsService.ImportCsvAsync(Dept, Admin,
				"number,latitude,longitude,flow_rate,flow_rating_color\nTested,1,2,,red\nUntested,1,2,,orange\nNew,1,2,,light blue\nMeasured,1,2,1600,red\nZero,1,2,0,green\n");

			result.FailureMessage.Should().BeNull();
			FlowClassOf("Tested").Should().Be(RmsHydrantFlowClass.A, "the flow on record still decides the class");
			h.Hydrants.Rows.Single(x => x.HydrantNumber == "Tested").FlowGpm.Should().Be(1200);
			FlowClassOf("Untested").Should().Be(RmsHydrantFlowClass.B);
			FlowClassOf("New").Should().Be(RmsHydrantFlowClass.AA);
			FlowClassOf("Measured").Should().Be(RmsHydrantFlowClass.AA, "an imported flow decides the class over the colour");
			FlowClassOf("Zero").Should().Be(RmsHydrantFlowClass.A, "a zero flow cannot be classified, so the colour is used");

			RmsHydrantFlowClass FlowClassOf(string number) => (RmsHydrantFlowClass)h.Hydrants.Rows.Single(x => x.HydrantNumber == number).FlowClass;
		}

		[Test]
		public async Task Manual_save_does_not_take_a_flow_class_without_a_flow()
		{
			var h = new RmsPreventionHarness();
			var saved = await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H", Latitude = 1, Longitude = 2, FlowClass = (int)RmsHydrantFlowClass.C });
			saved.FlowClass.Should().Be((int)RmsHydrantFlowClass.Unknown);
		}

		[TestCase("Dry Barrel", RmsHydrantType.DryBarrel)]
		[TestCase("dry-barrel hydrant", RmsHydrantType.DryBarrel)]
		[TestCase("WET_BARREL", RmsHydrantType.WetBarrel)]
		[TestCase("Wet Hydrant", RmsHydrantType.WetBarrel)]
		[TestCase("Standpipe", RmsHydrantType.Standpipe)]
		[TestCase("Underground Tank", RmsHydrantType.Cistern)]
		[TestCase("Dry Hydrant", RmsHydrantType.DraftingSite)]
		[TestCase("Drafting Point", RmsHydrantType.DraftingSite)]
		[TestCase("Static Water Source", RmsHydrantType.DraftingSite)]
		[TestCase("4", RmsHydrantType.Cistern)]
		[TestCase("", RmsHydrantType.DryBarrel)]
		[TestCase("N/A", RmsHydrantType.DryBarrel)]
		[TestCase("Unknown", RmsHydrantType.DryBarrel)]
		public void Foreign_hydrant_type_values_map_to_types(string value, RmsHydrantType expected)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,hydrant_type\nH,45,-122,{value}", "csv");
			batch.Result.Rejected.Should().BeEmpty();
			batch.Result.UnmappedValues.Should().BeEmpty();
			batch.Rows.Single().Hydrant.Type.Should().Be((int)expected);
		}

		[Test]
		public void Unrecognized_foreign_types_import_as_other_and_are_counted()
		{
			var batch = HydrantImportParser.Parse("number,latitude,longitude,Hydrant Type\nH1,45,-122,Pillar\nH2,45,-122,pillar\nH3,45,-122,Flush\nH4,45,-122,Dry\nH5,45,-122,\"DryBarrel,WetBarrel\"", "csv");

			batch.Result.ValidationFailed.Should().BeFalse();
			batch.Rows.Should().HaveCount(5);
			batch.Rows.Where(r => r.Row.HydrantNumber != "H4").Should().OnlyContain(r => r.Hydrant.Type == (int)RmsHydrantType.Other, "a combined value is not read as a flags combination");
			batch.Result.UnmappedValues.Select(u => (u.Column, u.Value, u.Rows)).Should().Equal(("Hydrant Type", "Pillar", 2), ("Hydrant Type", "DryBarrel,WetBarrel", 1), ("Hydrant Type", "Flush", 1));
		}

		[TestCase("-2")]
		[TestCase("0.5")]
		[TestCase("99")]
		public void Signed_fractional_or_unknown_foreign_type_numbers_import_as_other_and_are_reported(string value)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,hydrant_type\nH,45,-122,{value}", "csv");

			batch.Result.ValidationFailed.Should().BeFalse();
			batch.Rows.Single().Hydrant.Type.Should().Be((int)RmsHydrantType.Other, "a number is not stripped of its sign or decimal point into another type");
			batch.Result.UnmappedValues.Should().ContainSingle().Which.Value.Should().Be(value);
		}

		[TestCase("1250", 1250)]
		[TestCase("1250.6", 1251)]
		[TestCase("1,250", 1250)]
		[TestCase("1,250.4 GPM", 1250)]
		[TestCase("1000 gal/min", 1000)]
		[TestCase("3785 L/min", 1000)]
		[TestCase("3785lpm", 1000)]
		[TestCase("0", 0)]
		[TestCase("", null)]
		[TestCase("n/a", null)]
		public void Foreign_flow_rate_values_are_read_as_gallons_per_minute(string value, int? expected)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,flow_rate\nH,45,-122,\"{value}\"", "csv");
			batch.Result.Rejected.Should().BeEmpty();
			batch.Result.UnmappedValues.Should().BeEmpty();
			batch.Rows.Single().Hydrant.FlowGpm.Should().Be(expected);
		}

		[TestCase("-5")]
		[TestCase("lots")]
		[TestCase("1250 m3/h")]
		[TestCase("1250,5")]
		[TestCase("99999999999999")]
		public void Unreadable_foreign_flow_is_left_blank_and_reported(string value)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,flow_rate\nH,45,-122,\"{value}\"", "csv");
			batch.Result.ValidationFailed.Should().BeFalse();
			batch.Rows.Single().Hydrant.FlowGpm.Should().BeNull();
			batch.Result.UnmappedValues.Should().ContainSingle().Which.Should().BeEquivalentTo(new HydrantImportUnmappedValue { Column = "flow_rate", Value = value, Rows = 1 });
		}

		[TestCase("type", "Pillar", "type")]
		[TestCase("flow_gpm", "1,250", "flow_gpm")]
		public void Documented_columns_stay_strict(string column, string value, string error)
		{
			var batch = HydrantImportParser.Parse($"number,latitude,longitude,{column}\nH,45,-122,\"{value}\"", "csv");
			batch.Result.Rejected.Should().ContainSingle().Which.Error.Should().Contain(error);
			batch.Result.UnmappedValues.Should().BeEmpty();
		}

		[Test]
		public void Csv_with_only_known_columns_reports_nothing_ignored()
		{
			HydrantImportParser.Parse(HydrantImportParser.CsvExample, "csv").Result.IgnoredColumns.Should().BeEmpty();
			var headerless = HydrantImportParser.Parse("H,45,-122,DryBarrel,1 River Rd,8,1100,City", "csv");
			headerless.Result.Rejected.Should().BeEmpty("a file without a header uses the eight documented columns");
			headerless.Result.IgnoredColumns.Should().BeEmpty();
		}

		[Test]
		public void Two_columns_for_one_field_name_both_columns()
		{
			Action parse = () => HydrantImportParser.Parse("id,hydrant_number,latitude,longitude\nabc,H,1,2", "csv");
			parse.Should().Throw<ArgumentException>().WithMessage("*more than one number column (id, hydrant_number)*");
		}

		[TestCase("number,latitude,longitude,latitude\nH,1,2,3")]
		[TestCase("number,address,location_address,latitude,longitude\nH,a,b,1,2")]
		[TestCase("number,latitude,longitude,flow_rating_color,cap_color\nH,1,2,red,red")]
		[TestCase("number,latitude\nH,1")]
		[TestCase("number,latitude,longitude\n\"H,1,2")]
		public void Invalid_csv_structure_is_rejected(string csv)
		{
			Action parse = () => HydrantImportParser.Parse(csv, "csv");
			parse.Should().Throw<ArgumentException>();
		}

		[Test]
		public void Duplicate_numbers_and_size_limits_are_not_silently_truncated()
		{
			HydrantImportParser.Parse("number,latitude,longitude\nH,1,2\nh,3,4", "csv").Result.Rejected.Should().ContainSingle().Which.Error.Should().Contain("more than once");
			Action tooMany = () => HydrantImportParser.Parse("number,latitude,longitude\n" + string.Join("\n", Enumerable.Range(1, 20001).Select(i => $"H{i},1,2")), "csv");
			tooMany.Should().Throw<ArgumentException>().WithMessage("*20,000*");
			Action tooLarge = () => HydrantImportParser.Parse(new string('x', HydrantImportParser.MaximumBytes + 1), "csv");
			tooLarge.Should().Throw<ArgumentException>().WithMessage("*10 MB*");
		}

		[Test]
		public async Task Imports_enforce_permission_and_module_gate()
		{
			var h = new RmsPreventionHarness();
			Func<Task> denied = () => h.HydrantsService.ImportAsync(Dept, Member, HydrantImportParser.JsonExample, "json");
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionHydrants);
			Func<Task> disabled = () => h.HydrantsService.ImportAsync(Dept, Admin, HydrantImportParser.JsonExample, "json");
			await disabled.Should().ThrowAsync<RecordsModuleDisabledException>();
			h.Hydrants.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Save_failure_returns_partial_counts_and_stops_without_leaking_exception_details()
		{
			var h = new RmsPreventionHarness();
			var repository = new Mock<IRmsHydrantsRepository>();
			repository.Setup(x => x.GetAllLiveAsync(Dept)).ReturnsAsync(new List<RmsHydrant>());
			repository.Setup(x => x.InsertAsync(It.IsAny<RmsHydrant>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((RmsHydrant entity, CancellationToken token, bool first) => entity);
			repository.Setup(x => x.InsertAsync(It.Is<RmsHydrant>(x => x.HydrantNumber == "H2"), It.IsAny<CancellationToken>(), true)).ThrowsAsync(new Exception("secret database details"));
			var service = new RecordsHydrantsService(h.Gate, repository.Object, h.FlowTests, h.Maintenance, h.Attachments, h.UnitOfWork.Object);
			var result = await service.ImportCsvAsync(Dept, Admin, "number,latitude,longitude\nH1,1,2\nH2,2,3\nH3,3,4");
			result.Created.Should().Be(1);
			result.FailureMessage.Should().Contain("row 3").And.Contain("retry").And.NotContain("secret");
			repository.Verify(x => x.InsertAsync(It.Is<RmsHydrant>(x => x.HydrantNumber == "H3"), It.IsAny<CancellationToken>(), true), Times.Never);
		}

		[Test]
		public async Task Mixed_create_and_update_batch_uses_one_lookup_and_one_transaction()
		{
			var h = new RmsPreventionHarness();
			var commits = 0;
			h.UnitOfWork.Setup(u => u.CommitChanges()).Callback(() => commits++);
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H-1", Latitude = 45.5m, Longitude = -122.6m });
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H-2", Latitude = 45.6m, Longitude = -122.7m });
			var singleLookups = h.Hydrants.NumberLookups;

			// "h-2" differs from the stored "H-2" only by case: whether it updates is the store's equality (here case-insensitive), not the service's.
			var result = await h.HydrantsService.ImportCsvAsync(Dept, Admin, "number,latitude,longitude\nH-1,45.51,-122.61\nh-2,45.62,-122.72\nH-3,45.7,-122.8\nH-4,45.8,-122.9\n");

			result.Created.Should().Be(2);
			result.Updated.Should().Be(2);
			result.FailureMessage.Should().BeNull();
			h.Hydrants.Rows.Should().HaveCount(4);
			h.Hydrants.Rows.Single(x => x.HydrantNumber == "H-1").Latitude.Should().Be(45.51m);
			h.Hydrants.Rows.Single(x => x.HydrantNumber == "h-2").Latitude.Should().Be(45.62m, "the matched row is updated in place with the imported number");
			h.Hydrants.Rows.Where(x => x.HydrantNumber is "H-3" or "H-4").Should().OnlyContain(x => x.Source == "csv-import");
			h.Hydrants.BatchNumberLookups.Should().Be(1);
			h.Hydrants.NumberLookups.Should().Be(singleLookups, "the import resolves numbers through the batch lookup, not one query per row");
			commits.Should().Be(1);
		}

		[Test]
		public async Task Large_imports_are_written_in_batches()
		{
			var h = new RmsPreventionHarness();
			var commits = 0;
			h.UnitOfWork.Setup(u => u.CommitChanges()).Callback(() => commits++);
			await h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H1500", Latitude = 1m, Longitude = 2m });
			var rows = RecordsHydrantsService.ImportBatchSize + 500;

			var result = await h.HydrantsService.ImportCsvAsync(Dept, Admin, "number,latitude,longitude\n" + string.Join("\n", Enumerable.Range(1, rows).Select(i => $"H{i},1,2")));

			result.Created.Should().Be(rows - 1);
			result.Updated.Should().Be(1);
			h.Hydrants.Rows.Should().HaveCount(rows);
			h.Hydrants.BatchNumberLookups.Should().Be(2);
			commits.Should().Be(2);
		}

		[Test]
		public async Task A_failed_batch_rolls_back_and_replays_to_the_same_stop_row_and_counts()
		{
			var h = new RmsPreventionHarness();
			var store = new List<RmsHydrant> { new RmsHydrant { RmsHydrantId = "existing", DepartmentId = Dept, HydrantNumber = "H1", Latitude = 1m, Longitude = 2m, RowVersion = 4 } };
			List<RmsHydrant> snapshot = null;
			var commits = 0; var discards = 0;
			h.UnitOfWork.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).Callback(() => snapshot = store.Select(x => x.CloneJson()).ToList()).ReturnsAsync((System.Data.Common.DbConnection)null);
			h.UnitOfWork.Setup(u => u.CommitChanges()).Callback(() => { commits++; snapshot = null; });
			h.UnitOfWork.Setup(u => u.DiscardChanges()).Callback(() => { discards++; store.Clear(); store.AddRange(snapshot); snapshot = null; });
			var repository = new Mock<IRmsHydrantsRepository>();
			repository.Setup(x => x.GetAllLiveAsync(Dept)).ReturnsAsync(() => store.ToList());
			repository.Setup(x => x.GetByNumberAsync(Dept, It.IsAny<string>())).ReturnsAsync((int _, string n) => store.FirstOrDefault(x => x.HydrantNumber == n));
			repository.Setup(x => x.GetByNumbersAsync(Dept, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync((int _, IEnumerable<string> numbers) => (IReadOnlyDictionary<string, RmsHydrant>)numbers.Select(n => store.FirstOrDefault(x => x.HydrantNumber == n)).Where(x => x != null).ToDictionary(x => x.HydrantNumber));
			repository.Setup(x => x.InsertAsync(It.IsAny<RmsHydrant>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((RmsHydrant e, CancellationToken _, bool __) => { store.Add(e); return e; });
			repository.Setup(x => x.InsertAsync(It.Is<RmsHydrant>(x => x.HydrantNumber == "H3"), It.IsAny<CancellationToken>(), true)).ThrowsAsync(new Exception("secret database details"));
			repository.Setup(x => x.UpdateAsync(It.IsAny<RmsHydrant>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((RmsHydrant e, CancellationToken _, bool __) => { store.RemoveAll(x => x.RmsHydrantId == e.RmsHydrantId); store.Add(e); return e; });
			var service = new RecordsHydrantsService(h.Gate, repository.Object, h.FlowTests, h.Maintenance, h.Attachments, h.UnitOfWork.Object);

			var result = await service.ImportCsvAsync(Dept, Admin, "number,latitude,longitude\nH1,5,6\nH2,2,3\nH3,3,4\nH4,4,5");

			// Exactly what a row-by-row import reports: H1 updated, H2 created, stopped at H3 (file line 4), H4 never written.
			result.Updated.Should().Be(1);
			result.Created.Should().Be(1);
			result.FailureMessage.Should().Contain("row 4").And.Contain("1 created and 1 updated").And.NotContain("secret");
			store.Select(x => x.HydrantNumber).Should().BeEquivalentTo(new[] { "H1", "H2" });
			store.Single(x => x.HydrantNumber == "H1").RowVersion.Should().Be(5, "the rolled-back batch attempt does not leak its in-memory increment into the replay");
			repository.Verify(x => x.InsertAsync(It.Is<RmsHydrant>(x => x.HydrantNumber == "H4"), It.IsAny<CancellationToken>(), true), Times.Never);
			commits.Should().Be(0);
			discards.Should().Be(1);
		}

		[TestCase(RmsHydrantFlowClass.AA, "#23c6c8")]
		[TestCase(RmsHydrantFlowClass.A, "#1ab394")]
		[TestCase(RmsHydrantFlowClass.B, "#f8ac59")]
		[TestCase(RmsHydrantFlowClass.C, "#ed5565")]
		public void Map_projection_has_distinct_identity_correct_color_and_escaped_popup(RmsHydrantFlowClass flow, string color)
		{
			var point = new HydrantMapPoint { HydrantId = "id", HydrantNumber = "<script>alert(1)</script>", InService = true, FlowClass = (int)flow, FlowGpm = 1200 };
			var marker = Resgrid.Web.Services.Controllers.v4.MappingController.ConvertHydrantMapMarker(point);
			marker.Type.Should().Be(5);
			marker.LayerId.Should().Be("hydrants");
			marker.Id.Should().Be("hydrant-id");
			marker.Color.Should().Be(color);
			marker.InfoWindowContent.Should().Contain("&lt;script&gt;").And.NotContain("<script>");
		}
	}
}
