using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Workforce;
using Resgrid.Services.Workforce;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase E (E5 / E7 acceptance): the CRD math and the reviewed schema
	/// profile. Two employees at $25 and $24 per hour aggregate to a $24.50 mean and median; odd and even medians;
	/// zero hours block a rate; Box 1 fallback is a remarked warning; remote counts reconcile; MENA and multiracial
	/// codes; the RY2025 column order; the second Wednesday of May.
	/// </summary>
	[TestFixture]
	public class PayDataAggregatorTests
	{
		private static readonly CaPayDataSchemaProfile Profile = CaPayDataSchemaProfile.Current;

		private static PayDataReportEmployeeSnapshot Snapshot(string id, decimal earnings, decimal hours, string demographic = "B20", string category = "10", int workMode = (int)WorkModes.NonRemote, string establishment = "hq") => new PayDataReportEmployeeSnapshot
		{
			PayDataReportEmployeeSnapshotId = id, PayDataReportRunId = "run", DepartmentId = 1, WorkforceEstablishmentId = establishment, JobCategoryCode = category, DemographicCode = demographic,
			PayBandCode = Profile.PayBandFor(earnings)?.Code, AnnualEarningsValue = earnings, AnnualHours = hours, AnnualWeeks = 52, HourlyRateValue = PayDataAggregator.HourlyRate(earnings, hours), WorkMode = workMode, IsIncluded = true
		};

		[Test]
		public void Two_employees_at_25_and_24_per_hour_aggregate_to_a_24_50_mean_and_median()
		{
			var rows = PayDataAggregator.Aggregate(new[] { Snapshot("a", 52000m, 2080m), Snapshot("b", 49920m, 2080m) }, Profile);
			rows.Should().HaveCount(1, "same establishment, job category, demographic code and pay band");
			var row = rows[0];
			row.EmployeeCount.Should().Be(2);
			row.AnnualHours.Should().Be(4160m);
			row.MeanHourlyRateValue.Should().Be(24.50m);
			row.MedianHourlyRateValue.Should().Be(24.50m);
			row.NonRemoteCount.Should().Be(2);
			row.PayBandCode.Should().Be("6", "$49,920–$62,919");
		}

		[Test]
		public void Median_handles_odd_and_even_counts_and_zero_hours_yields_no_rate()
		{
			PayDataAggregator.Median(new[] { 10m, 30m, 20m }).Should().Be(20m);
			PayDataAggregator.Median(new[] { 10m, 20m, 30m, 40m }).Should().Be(25m);
			PayDataAggregator.Mean(Array.Empty<decimal>()).Should().Be(0m);
			PayDataAggregator.HourlyRate(50000m, 0m).Should().BeNull("zero hours cannot produce a rate");
			PayDataAggregator.HourlyRate(null, 2080m).Should().BeNull();
		}

		[Test]
		public void Snapshots_split_rows_on_job_category_demographic_and_pay_band_and_reconcile_remote_counts()
		{
			var snapshots = new[]
			{
				Snapshot("a", 52000m, 2080m), Snapshot("b", 52000m, 2080m, workMode: (int)WorkModes.RemoteWithinCalifornia),
				Snapshot("c", 52000m, 2080m, demographic: "H10"), Snapshot("d", 52000m, 2080m, category: "3"), Snapshot("e", 208000m, 2080m)
			};
			var rows = PayDataAggregator.Aggregate(snapshots, Profile);
			rows.Should().HaveCount(4);
			var first = rows.Single(r => r.DemographicCode == "B20" && r.JobCategoryCode == "10" && r.PayBandCode == "6");
			first.EmployeeCount.Should().Be(2); first.NonRemoteCount.Should().Be(1); first.RemoteWithinCaliforniaCount.Should().Be(1);
			rows.Single(r => r.DemographicCode == "H10").EmployeeCount.Should().Be(1, "MENA is its own code (H) in RY2025");
			rows.Single(r => r.PayBandCode == "12").EmployeeCount.Should().Be(1, "$208,000 and over");
			var validation = new PayDataValidationResult();
			PayDataAggregator.ValidateRows(rows, snapshots, Profile, validation);
			validation.Errors.Should().BeEmpty();

			rows[0].NonRemoteCount = 0;
			var broken = new PayDataValidationResult();
			PayDataAggregator.ValidateRows(rows, snapshots, Profile, broken);
			broken.Errors.Should().Contain(e => e.Code == PayDataValidationCodes.RemoteCountsMismatch);
		}

		[Test]
		public void Demographic_codes_follow_the_profile_precedence()
		{
			Profile.DemographicCode("Yes", new[] { "B", "E" }, "20").Should().Be("A20", "Hispanic or Latino takes precedence");
			Profile.DemographicCode("No", new[] { "B", "E" }, "10").Should().Be("G10", "two or more races");
			Profile.DemographicCode("No", new[] { "H" }, "30").Should().Be("H30", "Middle Eastern or North African");
			Profile.DemographicCode("No", new[] { "C" }, "20").Should().Be("C20");
			Profile.DemographicCode("No", Array.Empty<string>(), "20").Should().BeNull("no race answered");
			Profile.DemographicCode("No", new[] { "C" }, null).Should().BeNull("no sex answered");
		}

		[Test]
		public void Reviewed_profile_has_the_RY2025_shape_and_the_second_Wednesday_of_May()
		{
			Profile.Code.Should().Be("CRD-RY2025");
			Profile.ReportingYear.Should().Be(2025);
			Profile.JobCategories.Select(j => j.Code).Should().Equal("1", "2", "3", "4", "5", "6", "7", "8", "9", "10");
			Profile.PayBands.Should().HaveCount(12);
			Profile.PayBands.Last().Maximum.Should().BeNull();
			Profile.RaceEthnicities.Select(r => r.Code).Should().Equal("A", "B", "C", "D", "E", "F", "G", "H");
			Profile.Sexes.Select(s => s.Code).Should().Equal("10", "20", "30");
			Profile.PayrollColumns.Select(c => c.Header).Should().StartWith(new[] { "Establishment Name", "Establishment Address", "Establishment City", "Establishment State", "Establishment Zip", "NAICS Code", "Major Activity" });
			Profile.PayrollColumns.Select(c => c.Header).Should().EndWith(new[] { "Non-Remote Employees", "Remote Employees Located in California", "Remote Employees Located Outside California", "Row-Level Clarifying Remarks" });
			Profile.LaborContractorColumns.Take(2).Select(c => c.Header).Should().Equal("Labor Contractor Name", "Labor Contractor FEIN");
			Profile.LaborContractorColumns.Count.Should().Be(Profile.PayrollColumns.Count + 2);
			Profile.DueDate.Should().Be(new DateTime(2026, 5, 13));
			CaPayDataSchemaProfile.SecondWednesdayOfMay(2025).Should().Be(new DateTime(2025, 5, 14));
			Profile.IsSnapshotInWindow(new DateTime(2025, 10, 1), new DateTime(2025, 10, 31)).Should().BeTrue();
			Profile.IsSnapshotInWindow(new DateTime(2025, 9, 15), new DateTime(2025, 10, 15)).Should().BeFalse();
			Profile.IsSnapshotInWindow(new DateTime(2025, 10, 1), new DateTime(2025, 12, 1)).Should().BeFalse("longer than one pay period");
			Profile.SourceChecksum.Should().BeNull("the reviewer records the handbook checksum before the profile is used in production");
			CaPayDataReportingService.ProfileHash(Profile).Should().HaveLength(64).And.Be(CaPayDataReportingService.ProfileHash(CaPayDataSchemaProfile.ForYear(2025)));
		}

		[Test]
		public void Cells_render_in_template_order_and_exports_are_checksum_stable()
		{
			var rows = PayDataAggregator.Aggregate(new[] { Snapshot("a", 52000m, 2080m), Snapshot("b", 49920m, 2080m) }, Profile);
			var establishment = new PayDataAggregator.ExportEstablishment { Id = "hq", Name = "Station 1", Address = "1 Main St", City = "Sacramento", State = "CA", Zip = "95814", Naics = "922160", MajorActivity = "Fire protection", TotalEmployees = 2, FiledPriorYear = true, IsHeadquarters = true };
			var cells = PayDataAggregator.Cells(rows[0], Profile, (int)PayDataReportTypes.PayrollEmployee, establishment, null);
			cells.Should().HaveCount(Profile.PayrollColumns.Count);
			cells.Take(10).Should().Equal("Station 1", "1 Main St", "Sacramento", "CA", "95814", "922160", "Fire protection", "2", "Yes", "Yes");
			cells.Skip(10).Take(7).Should().Equal("10", "B20", "6", "2", "4160", "24.50", "24.50");
			var validation = new PayDataValidationResult();
			PayDataAggregator.ValidateCells(Profile.PayrollColumns, cells, "row", validation);
			validation.Errors.Should().BeEmpty();

			var csv1 = PayDataAggregator.RenderCsv(Profile.PayrollColumns, new[] { (IReadOnlyList<string>)cells });
			var csv2 = PayDataAggregator.RenderCsv(Profile.PayrollColumns, new[] { (IReadOnlyList<string>)cells });
			PayDataAggregator.Sha256(csv1).Should().Be(PayDataAggregator.Sha256(csv2));
			Encoding.UTF8.GetString(csv1).Split('\n')[0].Should().StartWith("Establishment Name,Establishment Address");
			var xlsx = PayDataAggregator.RenderXlsx(Profile.PayrollColumns, new[] { (IReadOnlyList<string>)cells });
			using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
			archive.Entries.Select(e => e.FullName).Should().Contain("xl/worksheets/sheet1.xml").And.Contain("[Content_Types].xml");

			var contractorCells = PayDataAggregator.Cells(rows[0], Profile, (int)PayDataReportTypes.LaborContractorEmployee, establishment, new PayDataAggregator.ExportContractor { Name = "Staffing Co", Fein = "12-3456789" });
			contractorCells.Take(2).Should().Equal("Staffing Co", "12-3456789");
			contractorCells.Should().HaveCount(Profile.LaborContractorColumns.Count);
		}

		[Test]
		public void Annual_fact_normalisation_prefers_box_5_and_derives_reportable_hours()
		{
			var fact = new WorkforceAnnualPayFact { ReportType = (int)PayDataReportTypes.PayrollEmployee, W2Box5Value = 52000m, W2Box1Value = 50000m, ActualWorkedHours = 2000m, PaidLeaveHours = 80m };
			WorkforceService.Normalize(fact);
			fact.EarningsUsedValue.Should().Be(52000m);
			fact.EarningsSource.Should().Be((int)EarningsSources.W2Box5);
			fact.ReportableHours.Should().Be(2080m);

			var fallback = new WorkforceAnnualPayFact { ReportType = (int)PayDataReportTypes.PayrollEmployee, W2Box1Value = 50000m, ExemptProxyMethod = (int)ExemptProxyMethods.DaysTimesAverageHours, DaysWorked = 250, ProxyAverageHoursPerDay = 8m };
			WorkforceService.Normalize(fallback);
			fallback.EarningsSource.Should().Be((int)EarningsSources.W2Box1Fallback, "Box 1 only when Box 5 is absent, and it is remarked");
			fallback.ReportableHours.Should().Be(2000m);

			var contractor = new WorkforceAnnualPayFact { ReportType = (int)PayDataReportTypes.LaborContractorEmployee, ClientAllocatedEarningsValue = 30000m, ClientAllocatedHours = 1200m, ClientAllocatedWeeks = 30m };
			WorkforceService.Normalize(contractor);
			contractor.EarningsUsedValue.Should().Be(30000m);
			contractor.EarningsSource.Should().Be((int)EarningsSources.ClientAllocated);
			contractor.ReportableHours.Should().Be(1200m);
			contractor.WeeksWorked.Should().Be(30m);
		}

		[Test]
		public void Csv_import_parser_handles_quotes_and_blocking_codes_are_the_documented_set()
		{
			WorkforceService.ParseCsvLine("a,\"b,c\",\"d\"\"e\",").Should().Equal("a", "b,c", "d\"e", "");
			CaPayDataReportingService.IsBlocking(PayDataValidationCodes.DemographicMissing).Should().BeTrue();
			CaPayDataReportingService.IsBlocking(PayDataValidationCodes.EarningsBox1Fallback).Should().BeFalse("a warning, not a block");
			CaPayDataReportingService.IsBlocking(PayDataValidationCodes.ObserverPerceptionUsed).Should().BeFalse();
		}
	}
}
