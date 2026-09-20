using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Workforce
{
	/// <summary>
	/// A reviewed, versioned description of one California CRD Pay Data Reporting year (plan E1): the official
	/// source, the job-category / pay-band / race-ethnicity-sex code lists, the upload columns in exact order with
	/// their types and limits, the snapshot window and the rounding rule. CRD rejects stale templates, so a new
	/// reporting year is a new profile; historical runs keep the profile they were frozen with. The first
	/// implementation profile is Reporting Year 2025 (filed in 2026). Its column headers, code lists and source
	/// checksum are data the reviewer confirms against the official template before the first filing.
	/// </summary>
	public sealed class CaPayDataSchemaProfile
	{
		public string Code { get; }
		public int ReportingYear { get; }
		public DateTime ReviewedOn { get; }
		public string SourceUrl { get; }
		public string SourceTitle { get; }
		/// <summary>SHA-256 of the official template, recorded by the reviewer; null until confirmed.</summary>
		public string SourceChecksum { get; }
		public IReadOnlyList<CaPayDataCode> JobCategories { get; }
		public IReadOnlyList<CaPayDataPayBand> PayBands { get; }
		public IReadOnlyList<CaPayDataCode> RaceEthnicities { get; }
		public IReadOnlyList<CaPayDataCode> Sexes { get; }
		public IReadOnlyList<CaPayDataColumn> PayrollColumns { get; }
		public IReadOnlyList<CaPayDataColumn> LaborContractorColumns { get; }
		public DateTime SnapshotWindowStart { get; }
		public DateTime SnapshotWindowEnd { get; }
		public int MaxFileBytes { get; }
		public int RateDecimals { get; }
		/// <summary>Statutory due date: the second Wednesday in May of the filing year.</summary>
		public DateTime DueDate { get; }

		private CaPayDataSchemaProfile(string code, int year, DateTime reviewedOn, string sourceUrl, string sourceTitle, string sourceChecksum, IReadOnlyList<CaPayDataCode> jobCategories,
			IReadOnlyList<CaPayDataPayBand> payBands, IReadOnlyList<CaPayDataCode> races, IReadOnlyList<CaPayDataCode> sexes, IReadOnlyList<CaPayDataColumn> payroll, IReadOnlyList<CaPayDataColumn> contractor)
		{
			Code = code; ReportingYear = year; ReviewedOn = reviewedOn; SourceUrl = sourceUrl; SourceTitle = sourceTitle; SourceChecksum = sourceChecksum;
			JobCategories = jobCategories; PayBands = payBands; RaceEthnicities = races; Sexes = sexes; PayrollColumns = payroll; LaborContractorColumns = contractor;
			SnapshotWindowStart = new DateTime(year, 10, 1); SnapshotWindowEnd = new DateTime(year, 12, 31); MaxFileBytes = 20 * 1024 * 1024; RateDecimals = 2;
			DueDate = SecondWednesdayOfMay(year + 1);
		}

		public const string CurrentCode = "CRD-RY2025";
		public static readonly IReadOnlyList<CaPayDataSchemaProfile> All = new[] { BuildReportingYear2025() };
		public static CaPayDataSchemaProfile Current => All.Last();
		public static CaPayDataSchemaProfile Get(string code) => All.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
		public static CaPayDataSchemaProfile ForYear(int reportingYear) => All.FirstOrDefault(p => p.ReportingYear == reportingYear);

		public static DateTime SecondWednesdayOfMay(int filingYear)
		{
			var first = new DateTime(filingYear, 5, 1);
			var offset = ((int)DayOfWeek.Wednesday - (int)first.DayOfWeek + 7) % 7;
			return first.AddDays(offset + 7);
		}

		public bool IsSnapshotInWindow(DateTime start, DateTime end) => start.Date >= SnapshotWindowStart && end.Date <= SnapshotWindowEnd && end.Date >= start.Date && (end.Date - start.Date).TotalDays <= 31;

		public CaPayDataPayBand PayBandFor(decimal annualEarnings) => PayBands.FirstOrDefault(b => annualEarnings >= b.Minimum && (!b.Maximum.HasValue || annualEarnings <= b.Maximum.Value));

		/// <summary>Combined race/ethnicity/sex upload code: race letter + sex digits (Hispanic/Latino takes precedence; two or more races → G; MENA → H).</summary>
		public string DemographicCode(string hispanicLatino, IReadOnlyCollection<string> raceCodes, string sexCode)
		{
			var sex = Sexes.FirstOrDefault(s => string.Equals(s.Code, sexCode, StringComparison.OrdinalIgnoreCase))?.Code;
			if (sex == null) return null;
			string race;
			if (string.Equals(hispanicLatino, "Yes", StringComparison.OrdinalIgnoreCase)) race = "A";
			else
			{
				var known = (raceCodes ?? Array.Empty<string>()).Where(c => RaceEthnicities.Any(r => r.Code == c && r.Code != "A")).Distinct().ToList();
				if (known.Count == 0) return null;
				race = known.Count > 1 ? "G" : known[0];
			}
			return race + sex;
		}

		private static CaPayDataSchemaProfile BuildReportingYear2025()
		{
			var jobs = new[]
			{
				new CaPayDataCode("1", "Executive or senior level officials and managers"), new CaPayDataCode("2", "First or mid-level officials and managers"), new CaPayDataCode("3", "Professionals"),
				new CaPayDataCode("4", "Technicians"), new CaPayDataCode("5", "Sales workers"), new CaPayDataCode("6", "Administrative support workers"), new CaPayDataCode("7", "Craft workers"),
				new CaPayDataCode("8", "Operatives"), new CaPayDataCode("9", "Laborers and helpers"), new CaPayDataCode("10", "Service workers")
			};
			var bands = new[]
			{
				new CaPayDataPayBand("1", 0m, 19239m), new CaPayDataPayBand("2", 19240m, 24439m), new CaPayDataPayBand("3", 24440m, 30679m), new CaPayDataPayBand("4", 30680m, 38999m),
				new CaPayDataPayBand("5", 39000m, 49919m), new CaPayDataPayBand("6", 49920m, 62919m), new CaPayDataPayBand("7", 62920m, 80079m), new CaPayDataPayBand("8", 80080m, 101919m),
				new CaPayDataPayBand("9", 101920m, 128959m), new CaPayDataPayBand("10", 128960m, 163799m), new CaPayDataPayBand("11", 163800m, 207999m), new CaPayDataPayBand("12", 208000m, null)
			};
			var races = new[]
			{
				new CaPayDataCode("A", "Hispanic or Latino"), new CaPayDataCode("B", "White (not Hispanic or Latino)"), new CaPayDataCode("C", "Black or African American"),
				new CaPayDataCode("D", "Native Hawaiian or Other Pacific Islander"), new CaPayDataCode("E", "Asian"), new CaPayDataCode("F", "American Indian or Alaska Native"),
				new CaPayDataCode("G", "Two or more races"), new CaPayDataCode("H", "Middle Eastern or North African")
			};
			var sexes = new[] { new CaPayDataCode("10", "Female"), new CaPayDataCode("20", "Male"), new CaPayDataCode("30", "Non-binary") };
			var establishment = new[]
			{
				new CaPayDataColumn("Establishment Name", "text", 100, true), new CaPayDataColumn("Establishment Address", "text", 100, true), new CaPayDataColumn("Establishment City", "text", 50, true),
				new CaPayDataColumn("Establishment State", "text", 2, true), new CaPayDataColumn("Establishment Zip", "text", 10, true), new CaPayDataColumn("NAICS Code", "text", 6, true),
				new CaPayDataColumn("Major Activity", "text", 100, true), new CaPayDataColumn("Total Number of Employees at Establishment", "integer", 10, true),
				new CaPayDataColumn("Was this establishment reported in prior year?", "yesno", 3, true), new CaPayDataColumn("Is this the Headquarters?", "yesno", 3, true)
			};
			var group = new[]
			{
				new CaPayDataColumn("Job Category", "code", 2, true), new CaPayDataColumn("Race/Ethnicity/Sex", "code", 3, true), new CaPayDataColumn("Pay Band", "code", 2, true),
				new CaPayDataColumn("Number of Employees", "integer", 10, true), new CaPayDataColumn("Total Hours", "integer", 10, true),
				new CaPayDataColumn("Mean Hourly Rate", "decimal", 12, true), new CaPayDataColumn("Median Hourly Rate", "decimal", 12, true),
				new CaPayDataColumn("Non-Remote Employees", "integer", 10, true), new CaPayDataColumn("Remote Employees Located in California", "integer", 10, true),
				new CaPayDataColumn("Remote Employees Located Outside California", "integer", 10, true), new CaPayDataColumn("Row-Level Clarifying Remarks", "text", 500, false)
			};
			var payroll = establishment.Concat(group).ToList();
			var contractor = new[] { new CaPayDataColumn("Labor Contractor Name", "text", 100, true), new CaPayDataColumn("Labor Contractor FEIN", "text", 10, true) }.Concat(establishment).Concat(group).ToList();
			return new CaPayDataSchemaProfile(CurrentCode, 2025, new DateTime(2026, 8, 21), "https://calcivilrights.ca.gov/paydatareporting/", "CRD Pay Data Reporting — Reporting Year 2025 handbook and templates", null,
				jobs, bands, races, sexes, payroll, contractor);
		}
	}

	public sealed class CaPayDataCode
	{
		public string Code { get; }
		public string Label { get; }
		public CaPayDataCode(string code, string label) { Code = code; Label = label; }
	}

	public sealed class CaPayDataPayBand
	{
		public string Code { get; }
		public decimal Minimum { get; }
		public decimal? Maximum { get; }
		public CaPayDataPayBand(string code, decimal minimum, decimal? maximum) { Code = code; Minimum = minimum; Maximum = maximum; }
	}

	public sealed class CaPayDataColumn
	{
		public string Header { get; }
		/// <summary>text, integer, decimal, code, yesno.</summary>
		public string Type { get; }
		public int MaxLength { get; }
		public bool Required { get; }
		public CaPayDataColumn(string header, string type, int maxLength, bool required) { Header = header; Type = type; MaxLength = maxLength; Required = required; }
	}
}
