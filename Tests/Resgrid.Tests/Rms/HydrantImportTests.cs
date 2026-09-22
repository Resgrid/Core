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

		[TestCase("number,latitude,longitude,latitude\nH,1,2,3")]
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
