using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>List/search tabular export (RMS plan section 4.10): safe projection columns, RFC 4180 CSV, formula guard.</summary>
	[TestFixture]
	public class RecordsListExportTests
	{
		private static readonly Department Dept = new Department { DepartmentId = 1, Name = "Test", TimeZone = "UTC", Use24HourTime = true };

		[Test]
		public void BuildRows_maps_names_types_states_and_dates_from_the_projection()
		{
			var rows = RecordsListExport.BuildRows(new[]
			{
				new RmsRecordSearchProjection
				{
					RmsRecordSearchProjectionId = "proj-1", SourceId = "rec-1", RecordNumber = "TRN-2026-0001", DraftReference = "D-1",
					RecordType = (int)RmsOperationalRecordType.Training, DefinitionKey = RmsDefinitionKeys.Training, State = (int)RmsRecordState.Finalized,
					DisplaySummary = "Pump ops", OccurredOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), RecordCreatedOn = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
					AuthorUserId = "u1", StationGroupId = 12, CallNumber = "C-7"
				},
				null
			}, new Dictionary<string, string> { ["u1"] = "Jane Doe" }, new Dictionary<int, string> { [12] = "Station 1" }, Dept);

			rows.Should().HaveCount(1, "null projections are skipped");
			var row = rows[0];
			row.RecordNumber.Should().Be("TRN-2026-0001");
			row.Type.Should().Be("Training");
			row.State.Should().Be("Finalized");
			row.Author.Should().Be("Jane Doe");
			row.Station.Should().Be("Station 1");
			row.CallNumber.Should().Be("C-7");
			row.RecordId.Should().Be("rec-1");
			row.OccurredOn.Should().NotBeNullOrWhiteSpace();
			row.FinalizedOn.Should().BeEmpty();
		}

		[Test]
		public void BuildRows_falls_back_to_ids_when_names_are_unknown()
		{
			var rows = RecordsListExport.BuildRows(new[] { new RmsRecordSearchProjection { AuthorUserId = "ghost", StationGroupId = 99, State = 1 } }, null, null, Dept);
			rows.Single().Author.Should().Be("ghost");
			rows.Single().Station.Should().BeEmpty();
			rows.Single().Type.Should().BeEmpty();
		}

		[Test]
		public void ToCsv_writes_the_header_and_quotes_per_rfc_4180()
		{
			var csv = RecordsListExport.ToCsv(new[]
			{
				new RecordsListExportRow { RecordNumber = "RUN-1", Summary = "Smoke, \"kitchen\"\nno injuries", State = "Finalized" }
			});

			var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
			lines[0].Should().Be(string.Join(",", RecordsListExport.Columns));
			csv.Should().Contain("\"Smoke, \"\"kitchen\"\"\nno injuries\"");
			csv.Should().EndWith("\r\n");
			RecordsListExport.Columns.Should().HaveCount(13).And.NotContain(c => c.ToLowerInvariant().Contains("narrative"));
		}

		[Test]
		public void Escape_neutralizes_spreadsheet_formula_injection()
		{
			RecordsListExport.Escape("=HYPERLINK(\"x\")").Should().StartWith("\"'=HYPERLINK");
			RecordsListExport.Escape("+1").Should().Be("'+1");
			RecordsListExport.Escape("@cmd").Should().Be("'@cmd");
			RecordsListExport.Escape("plain").Should().Be("plain");
			RecordsListExport.Escape(null).Should().BeEmpty();
		}

		[Test]
		public void ToCsvBytes_starts_with_the_utf8_byte_order_mark()
		{
			var bytes = RecordsListExport.ToCsvBytes(new List<RecordsListExportRow>());
			bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
		}

		[Test]
		public void ToJson_carries_the_format_tag_columns_and_rows()
		{
			var json = JObject.Parse(RecordsListExport.ToJson(new List<RecordsListExportRow> { new RecordsListExportRow { RecordNumber = "RUN-1" } }, "user-1"));
			((string)json["format"]).Should().Be(RecordsListExport.JsonFormat);
			((int)json["rowCount"]).Should().Be(1);
			((string)json["exportedByUserId"]).Should().Be("user-1");
			json["columns"].Select(c => (string)c).Should().Equal(RecordsListExport.Columns);
			((string)json["rows"][0]["RecordNumber"]).Should().Be("RUN-1");
		}
	}
}
