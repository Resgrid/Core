using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Reporting;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class IncidentExportTests
	{
		private static Call SampleCall() => new Call
		{
			CallId = 7,
			Number = "26-1",
			Type = "Structure Fire",
			Priority = 1,
			State = 0,
			Name = "Test Call",
			NatureOfCall = "Fire, large",
			Address = "123 Main St",
			LoggedOn = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
			DispatchOn = new DateTime(2026, 6, 1, 12, 0, 45, DateTimeKind.Utc)
		};

		[Test]
		public void Generic_profile_has_no_required_gaps()
		{
			IncidentExport.GetUnmappedRequiredFields(ExportProfile.Generic).Should().BeEmpty();
		}

		[Test]
		public void Nfirs_profile_reports_known_gaps()
		{
			var gaps = IncidentExport.GetUnmappedRequiredFields(ExportProfile.Nfirs);

			gaps.Should().Contain("FDID");
			gaps.Should().Contain("IncidentTypeCode");
		}

		[Test]
		public void Nemsis_profile_reports_known_gaps()
		{
			var gaps = IncidentExport.GetUnmappedRequiredFields(ExportProfile.Nemsis);

			gaps.Should().Contain("PatientCareReportNumber");
			gaps.Should().Contain("EMSAgencyNumber");
		}

		[Test]
		public void BuildCsv_writes_header_and_escapes_values()
		{
			var bytes = IncidentExport.BuildCsv(ExportProfile.Generic, new[] { SampleCall() });
			var csv = Encoding.UTF8.GetString(bytes);

			csv.Should().StartWith("CallId,Number,IncidentNumber,Type");
			// A value containing a comma must be quoted.
			csv.Should().Contain("\"Fire, large\"");
			// Mapped UTC timestamp present.
			csv.Should().Contain("2026-06-01T12:00:00Z");
		}

		[Test]
		public void BuildCsv_neutralizes_leading_formula_characters()
		{
			var call = SampleCall();
			call.Name = "=HYPERLINK(\"http://evil.example\",\"click\")";
			call.NatureOfCall = "@cmd|' /C calc'!A0";
			call.Address = "-122.5";

			var bytes = IncidentExport.BuildCsv(ExportProfile.Generic, new[] { call });
			var csv = Encoding.UTF8.GetString(bytes);

			// Formula-leading cells are neutralized with a single-quote prefix.
			csv.Should().Contain("'=HYPERLINK");
			csv.Should().Contain("'@cmd");
			csv.Should().NotContain(",=HYPERLINK");
			// Plain negative numbers are exempt from neutralization.
			csv.Should().Contain(",-122.5,");
		}

		[Test]
		public void BuildCsv_nfirs_emits_full_schema_header_with_empty_gap_cells()
		{
			var bytes = IncidentExport.BuildCsv(ExportProfile.Nfirs, new[] { SampleCall() });
			var csv = Encoding.UTF8.GetString(bytes);

			// Gap columns are present in the schema (header) even though Resgrid can't fill them.
			csv.Should().Contain("FDID");
			csv.Should().Contain("IncidentTypeCode");
		}

		private static string TimelineCsvWithDescription(string description)
		{
			var commandService = new Mock<IIncidentCommandService>();
			commandService.Setup(x => x.GetTimelineForCallAsync(10, 7)).ReturnsAsync(new List<CommandLogEntry>
			{
				new CommandLogEntry
				{
					OccurredOn = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
					EntryType = (int)CommandLogEntryType.Note,
					Description = description,
					UserId = "user1"
				}
			});
			var service = new IncidentReportingService(commandService.Object, new Mock<ICallsService>().Object, new Mock<IIncidentResourcesService>().Object);
			return service.ExportTimelineCsvAsync(10, 7).GetAwaiter().GetResult();
		}

		[Test]
		public void ExportTimelineCsv_neutralizes_leading_formula_characters()
		{
			var csv = TimelineCsvWithDescription("@SUM(A1:A9)");

			// The formula-leading description is neutralized with a single-quote prefix.
			csv.Should().Contain("'@SUM(A1:A9)");
			csv.Should().NotContain(",@SUM");
		}

		[Test]
		public void ExportTimelineCsv_exempts_plain_negative_numbers()
		{
			var csv = TimelineCsvWithDescription("-42.5");

			// Plain negative numbers are NOT neutralized (coordinates/numbers survive intact).
			csv.Should().Contain(",-42.5");
			csv.Should().NotContain("'-42.5");
		}
	}
}
