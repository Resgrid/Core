using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-1B saved reports: validation against the schema flags, visibility-filtered runs, grouping, aggregates, version mappings and CSV.</summary>
	[TestFixture]
	public class RecordSavedReportsServiceTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsDefinitionHarness();

		private static RecordDefinitionSchema ShiftLog() => Schema(
			Section("shift", "Shift",
				Field("site", RmsFieldType.ShortText, true, configure: f => { f.Searchable = true; f.Filterable = true; f.Sortable = true; }),
				Field("hours", RmsFieldType.Quantity, configure: f => { f.UnitFamily = "time"; f.DefaultUnit = "h"; f.Aggregatable = true; f.Filterable = true; f.Sortable = true; }),
				Select("status", "Open", "Closed"), Field("secret", RmsFieldType.ShortText, classification: RmsFieldClassification.Restricted), Field("notes", RmsFieldType.LongText)));

		private static RmsSavedReportDefinition Report(string name, RecordReportSpec spec, bool restricted = false) => new RmsSavedReportDefinition
		{
			Name = name, DefinitionKey = "shift-log", SpecJson = JsonConvert.SerializeObject(spec), IncludeRestricted = restricted
		};

		private async Task<string> RecordAsync(string site, string hours, string status, bool finalize = true)
		{
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput
			{
				DefinitionKey = "shift-log",
				Values = new List<RecordValueInput> { Value("shift", "site", site), new RecordValueInput { SectionKey = "shift", FieldKey = "hours", Value = hours, UnitCode = "h" }, Value("shift", "status", status), Value("shift", "secret", "S-" + site) }
			});
			if (finalize) await _h.Records.FinalizeAsync(Dept, Author, draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);
			return draft.Record.RmsOperationalRecordId;
		}

		[Test]
		public async Task Validation_checks_columns_filters_group_by_and_aggregates_against_the_schema_flags()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", ShiftLog());
			var bad = Report("Bad", new RecordReportSpec
			{
				Columns = new List<string> { "record.number", "notes", "secret", "missing" },
				Filters = new List<RecordReportFilter> { new RecordReportFilter { FieldKey = "notes", Operator = RmsRuleOperator.Equals, Value = "x" }, new RecordReportFilter { FieldKey = "site", Operator = RmsRuleOperator.And } },
				GroupByFieldKey = "hours", Aggregates = new List<RecordReportAggregateSpec> { new RecordReportAggregateSpec { Aggregate = RmsReportAggregate.Sum, FieldKey = "site" } }
			});
			var validation = await _h.Reports.ValidateAsync(Dept, bad);
			validation.IsValid.Should().BeFalse();
			validation.Issues.Select(i => i.Code).Should().Contain(new[] { "unknown_field", "restricted", "not_filterable", "bad_operator", "not_groupable", "not_aggregatable" });
			validation.Issues.Should().NotContain(i => i.Path == "columns" && i.Message.Contains("notes"), "long text is exportable even though it cannot filter");

			Func<Task> save = () => _h.Reports.SaveAsync(Dept, Admin, bad);
			await save.Should().ThrowAsync<ArgumentException>();
			var noDefinition = await _h.Reports.ValidateAsync(Dept, new RmsSavedReportDefinition { Name = "x", DefinitionKey = "nope", SpecJson = "{}" });
			noDefinition.Issues.Should().ContainSingle(i => i.Code == "unknown_definition");

			var good = Report("Good", new RecordReportSpec { Columns = new List<string> { "record.number", "site", "hours" }, GroupByFieldKey = "status", Aggregates = new List<RecordReportAggregateSpec> { new RecordReportAggregateSpec { Aggregate = RmsReportAggregate.Sum, FieldKey = "hours" } } });
			(await _h.Reports.ValidateAsync(Dept, good)).IsValid.Should().BeTrue();
			var saved = await _h.Reports.SaveAsync(Dept, Admin, good);
			saved.RmsSavedReportDefinitionId.Should().NotBeNullOrEmpty();
			saved.RowVersion.Should().Be(1);
			(await _h.Reports.GetForDepartmentAsync(Dept)).Should().ContainSingle(r => r.Name == "Good");

			_h.Authorization.Setup(a => a.HasPermissionAsync("analyst", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			Func<Task> restricted = () => _h.Reports.SaveAsync(Dept, "analyst", Report("Secrets", new RecordReportSpec { Columns = new List<string> { "secret" } }, restricted: true));
			await restricted.Should().ThrowAsync<UnauthorizedAccessException>();
			var staleCopy = new RmsSavedReportDefinition { RmsSavedReportDefinitionId = saved.RmsSavedReportDefinitionId, Name = "Renamed", DefinitionKey = saved.DefinitionKey, SpecJson = saved.SpecJson, RowVersion = 99 };
			Func<Task> stale = () => _h.Reports.SaveAsync(Dept, Admin, staleCopy);
			await stale.Should().ThrowAsync<RecordConcurrencyException>();
		}

		[Test]
		public async Task Runs_group_aggregate_sort_and_respect_row_limits_and_restricted_gates()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", ShiftLog());
			await RecordAsync("Depot 4", "2", "open");
			await RecordAsync("Depot 5", "1.5", "closed");
			await RecordAsync("Depot 6", "3", "open");
			await RecordAsync("Depot 7", "8", "open", finalize: false);

			var spec = new RecordReportSpec
			{
				Columns = new List<string> { "record.number", "site", "hours", "status" }, GroupByFieldKey = "status", SortFieldKey = "site", SortDescending = true,
				Aggregates = new List<RecordReportAggregateSpec> { new RecordReportAggregateSpec { Aggregate = RmsReportAggregate.Sum, FieldKey = "hours" }, new RecordReportAggregateSpec { Aggregate = RmsReportAggregate.Count } },
				Filters = new List<RecordReportFilter> { new RecordReportFilter { FieldKey = "site", Operator = RmsRuleOperator.NotEquals, Value = "Depot 5" } }
			};
			var report = await _h.Reports.SaveAsync(Dept, Admin, Report("Hours by status", spec));

			var result = await _h.Reports.RunAsync(Dept, Author, report.RmsSavedReportDefinitionId);
			result.ColumnLabels.Should().Equal("Record number", "Site", "Hours", "Status");
			result.TotalMatched.Should().Be(2, "drafts are excluded and the filter drops Depot 5");
			result.Rows.Select(r => r[1]).Should().Equal("Depot 6", "Depot 4");
			result.Rows[0][0].Should().StartWith("SHI");
			result.Groups.Should().ContainSingle(g => g.GroupKey == "Open" && g.Count == 2);
			result.Groups.Single().Aggregates["sum:hours"].Should().Be(300m, "aggregates sum the canonical (minutes) value");
			result.Groups.Single().Aggregates["count"].Should().Be(2);
			_h.Defs.Reports.Single().LastRunByUserId.Should().Be(Author);

			spec.IncludeDrafts = true; spec.Filters.Clear(); report.SpecJson = JsonConvert.SerializeObject(spec); report.MaxRowsPerRun = 2;
			await _h.Reports.SaveAsync(Dept, Admin, report);
			var limited = await _h.Reports.RunAsync(Dept, Author, report.RmsSavedReportDefinitionId);
			limited.Rows.Should().HaveCount(2);
			limited.Truncated.Should().BeTrue();
			limited.Warnings.Should().ContainSingle(w => w.Contains("stopped at 2"));

			_h.Authorization.Setup(a => a.HasPermissionAsync("viewer", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			var secrets = await _h.Reports.SaveAsync(Dept, Admin, Report("Secrets", new RecordReportSpec { Columns = new List<string> { "site", "secret" } }, restricted: true));
			Func<Task> denied = () => _h.Reports.RunAsync(Dept, "viewer", secrets.RmsSavedReportDefinitionId);
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			var revealed = await _h.Reports.RunAsync(Dept, Admin, secrets.RmsSavedReportDefinitionId);
			revealed.Rows.Should().OnlyContain(r => r[1].StartsWith("S-Depot"));
		}

		[Test]
		public async Task Version_mappings_carry_older_records_into_a_report_on_the_newer_version()
		{
			await _h.CreateAndPublishAsync("shift-log", "Shift log", ShiftLog());
			await RecordAsync("Depot 4", "2", "open");
			var v2 = await _h.Definitions.OpenDraftAsync(Dept, Admin, "shift-log");
			var input = Resgrid.Services.Records.RecordDefinitionsService.ToDraftInput(v2);
			var site = input.Schema.FindField("site"); site.Key = "location"; site.Label = "Location";
			await _h.Definitions.SaveDraftAsync(Dept, Admin, "shift-log", 2, v2.RowVersion, input);
			await _h.PublishAsync("shift-log");
			var v2Record = await _h.Records.CreateDraftAsync(Dept, Author, new RecordDraftInput { DefinitionKey = "shift-log", Values = new List<RecordValueInput> { Value("shift", "location", "Depot 9") } });
			await _h.Records.FinalizeAsync(Dept, Author, v2Record.Record.RmsOperationalRecordId, v2Record.Record.RowVersion, "1", null, null);

			var report = await _h.Reports.SaveAsync(Dept, Admin, Report("Locations", new RecordReportSpec { Columns = new List<string> { "location", "record.definition_version" } }));
			var unmapped = await _h.Reports.RunAsync(Dept, Author, report.RmsSavedReportDefinitionId);
			unmapped.Rows.Should().HaveCount(2);
			unmapped.UnmappedVersions.Should().Equal(1);
			unmapped.Rows.Single(r => r[1] == "1")[0].Should().BeEmpty();

			report.SpecJson = JsonConvert.SerializeObject(new RecordReportSpec { Columns = new List<string> { "location", "record.definition_version" }, VersionMappings = new Dictionary<int, Dictionary<string, string>> { [1] = new Dictionary<string, string> { ["location"] = "site" } } });
			await _h.Reports.SaveAsync(Dept, Admin, report);
			var mapped = await _h.Reports.RunAsync(Dept, Author, report.RmsSavedReportDefinitionId);
			mapped.UnmappedVersions.Should().BeEmpty();
			mapped.Rows.Select(r => r[0]).Should().BeEquivalentTo(new[] { "Depot 4", "Depot 9" });
			(await _h.Reports.DeleteAsync(Dept, Admin, report.RmsSavedReportDefinitionId)).Should().BeTrue();
			(await _h.Reports.GetForDepartmentAsync(Dept)).Should().BeEmpty();
		}
	}
}
