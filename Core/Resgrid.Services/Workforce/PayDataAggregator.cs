using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Resgrid.Model.Workforce;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// Pure CRD reporting math (Workforce &amp; Business Operations plan, E1/E3): the individual hourly rate, the
	/// pay band, the exact group key, arithmetic mean and true ordered median inside a group, the three remote
	/// counts, the reconciliation checks, and the CSV / XLSX rendering of a profile's columns. Employee facts stay
	/// at full precision; only the exported aggregate is rounded.
	/// </summary>
	public static class PayDataAggregator
	{
		public const string HispanicYes = "Yes";

		/// <summary>Earnings ÷ reportable hours. Zero / missing hours is a blocking exception, never a $0 rate.</summary>
		public static decimal? HourlyRate(decimal? earnings, decimal hours) => earnings.HasValue && hours > 0 ? earnings.Value / hours : null;

		public static string GroupKey(PayDataReportEmployeeSnapshot s) => string.Join("|", s.WorkforceEstablishmentId ?? string.Empty, s.WorkforceLaborContractorId ?? string.Empty, s.JobCategoryCode ?? string.Empty, s.DemographicCode ?? string.Empty, s.PayBandCode ?? string.Empty, s.ExemptionCode ?? string.Empty, s.EmploymentTypeCode ?? string.Empty);

		public static decimal Mean(IReadOnlyCollection<decimal> values) => values.Count == 0 ? 0m : values.Sum() / values.Count;

		public static decimal Median(IReadOnlyCollection<decimal> values)
		{
			if (values.Count == 0) return 0m;
			var sorted = values.OrderBy(v => v).ToList();
			var mid = sorted.Count / 2;
			return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
		}

		/// <summary>Groups included snapshots on the exact profile key and computes the aggregate row (mean / median from individual rates).</summary>
		public static List<PayDataReportRow> Aggregate(IReadOnlyList<PayDataReportEmployeeSnapshot> snapshots, CaPayDataSchemaProfile profile)
		{
			var rows = new List<PayDataReportRow>();
			var sort = 0;
			foreach (var group in snapshots.Where(s => s.IsIncluded).GroupBy(GroupKey).OrderBy(g => g.Key, StringComparer.Ordinal))
			{
				var first = group.First();
				var rates = group.Select(s => s.HourlyRateValue).Where(r => r.HasValue).Select(r => r.Value).ToList();
				rows.Add(new PayDataReportRow
				{
					DepartmentId = first.DepartmentId, PayDataReportRunId = first.PayDataReportRunId, WorkforceEstablishmentId = first.WorkforceEstablishmentId, WorkforceLaborContractorId = first.WorkforceLaborContractorId,
					JobCategoryCode = first.JobCategoryCode, DemographicCode = first.DemographicCode, PayBandCode = first.PayBandCode, ExemptionCode = first.ExemptionCode, EmploymentTypeCode = first.EmploymentTypeCode,
					EmployeeCount = group.Count(), AnnualHours = group.Sum(s => s.AnnualHours), AnnualWeeks = group.Sum(s => s.AnnualWeeks),
					MeanHourlyRateValue = Math.Round(Mean(rates), profile.RateDecimals, MidpointRounding.AwayFromZero), MedianHourlyRateValue = Math.Round(Median(rates), profile.RateDecimals, MidpointRounding.AwayFromZero),
					NonRemoteCount = group.Count(s => s.WorkMode == (int)WorkModes.NonRemote), RemoteWithinCaliforniaCount = group.Count(s => s.WorkMode == (int)WorkModes.RemoteWithinCalifornia),
					RemoteOutsideCaliforniaCount = group.Count(s => s.WorkMode == (int)WorkModes.RemoteOutsideCaliforniaAssignedToCaliforniaEstablishment),
					ContributingSnapshotIdsCsv = string.Join(",", group.Select(s => s.PayDataReportEmployeeSnapshotId)), SortOrder = sort++, AddedOn = DateTime.UtcNow
				});
			}
			return rows;
		}

		/// <summary>Profile field / length / code / reconciliation checks on the aggregate rows.</summary>
		public static void ValidateRows(IReadOnlyList<PayDataReportRow> rows, IReadOnlyList<PayDataReportEmployeeSnapshot> snapshots, CaPayDataSchemaProfile profile, PayDataValidationResult result)
		{
			var included = snapshots.Where(s => s.IsIncluded).ToList();
			if (rows.Sum(r => r.EmployeeCount) != included.Count) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EmployeeCountMismatch, Scope = "run", IsBlocking = true, Detail = $"{rows.Sum(r => r.EmployeeCount)} vs {included.Count}" });
			foreach (var row in rows)
			{
				if (row.NonRemoteCount + row.RemoteWithinCaliforniaCount + row.RemoteOutsideCaliforniaCount != row.EmployeeCount)
					result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.RemoteCountsMismatch, Scope = "row", SubjectId = row.PayDataReportRowId, IsBlocking = true, Detail = GroupLabel(row) });
				if (profile.JobCategories.All(j => j.Code != row.JobCategoryCode)) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.JobCategoryUnknown, Scope = "row", SubjectId = row.PayDataReportRowId, IsBlocking = true, Detail = row.JobCategoryCode });
				if (!string.IsNullOrWhiteSpace(row.RowRemarks) && row.RowRemarks.Length > 500) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.FieldTooLong, Scope = "row", SubjectId = row.PayDataReportRowId, IsBlocking = true, Detail = "Row-Level Clarifying Remarks" });
			}
			foreach (var establishment in included.GroupBy(s => s.WorkforceEstablishmentId ?? string.Empty))
			{
				var rowTotal = rows.Where(r => (r.WorkforceEstablishmentId ?? string.Empty) == establishment.Key).Sum(r => r.EmployeeCount);
				if (rowTotal != establishment.Count()) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EmployeeCountMismatch, Scope = "establishment", SubjectId = establishment.Key, IsBlocking = true, Detail = $"{rowTotal} vs {establishment.Count()}" });
			}
		}

		public static string GroupLabel(PayDataReportRow row) => $"{row.JobCategoryCode}/{row.DemographicCode}/{row.PayBandCode}";

		#region Export

		public sealed class ExportEstablishment
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public string Address { get; set; }
			public string City { get; set; }
			public string State { get; set; }
			public string Zip { get; set; }
			public string Naics { get; set; }
			public string MajorActivity { get; set; }
			public int TotalEmployees { get; set; }
			public bool? FiledPriorYear { get; set; }
			public bool IsHeadquarters { get; set; }
		}

		public sealed class ExportContractor
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public string Fein { get; set; }
		}

		/// <summary>The exact upload cells for one row in the profile's column order.</summary>
		public static List<string> Cells(PayDataReportRow row, CaPayDataSchemaProfile profile, int reportType, ExportEstablishment establishment, ExportContractor contractor)
		{
			var cells = new List<string>();
			if (reportType == (int)PayDataReportTypes.LaborContractorEmployee) { cells.Add(contractor?.Name); cells.Add(contractor?.Fein); }
			cells.Add(establishment?.Name); cells.Add(establishment?.Address); cells.Add(establishment?.City); cells.Add(establishment?.State); cells.Add(establishment?.Zip); cells.Add(establishment?.Naics); cells.Add(establishment?.MajorActivity);
			cells.Add(establishment == null ? null : establishment.TotalEmployees.ToString(CultureInfo.InvariantCulture)); cells.Add(establishment?.FiledPriorYear == true ? "Yes" : "No"); cells.Add(establishment?.IsHeadquarters == true ? "Yes" : "No");
			cells.Add(row.JobCategoryCode); cells.Add(row.DemographicCode); cells.Add(row.PayBandCode);
			cells.Add(row.EmployeeCount.ToString(CultureInfo.InvariantCulture)); cells.Add(Math.Round(row.AnnualHours, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture));
			cells.Add((row.MeanHourlyRateValue ?? 0).ToString("0.00", CultureInfo.InvariantCulture)); cells.Add((row.MedianHourlyRateValue ?? 0).ToString("0.00", CultureInfo.InvariantCulture));
			cells.Add(row.NonRemoteCount.ToString(CultureInfo.InvariantCulture)); cells.Add(row.RemoteWithinCaliforniaCount.ToString(CultureInfo.InvariantCulture)); cells.Add(row.RemoteOutsideCaliforniaCount.ToString(CultureInfo.InvariantCulture));
			cells.Add(row.RowRemarks);
			return cells;
		}

		public static IReadOnlyList<CaPayDataColumn> Columns(CaPayDataSchemaProfile profile, int reportType) => reportType == (int)PayDataReportTypes.LaborContractorEmployee ? profile.LaborContractorColumns : profile.PayrollColumns;

		public static void ValidateCells(IReadOnlyList<CaPayDataColumn> columns, IReadOnlyList<string> cells, string rowId, PayDataValidationResult result)
		{
			for (var i = 0; i < columns.Count && i < cells.Count; i++)
			{
				var column = columns[i]; var value = cells[i];
				if (column.Required && string.IsNullOrWhiteSpace(value)) result.Errors.Add(new PayDataValidationIssue { Code = "required_" + Slug(column.Header), Scope = "row", SubjectId = rowId, IsBlocking = true, Detail = column.Header });
				else if (!string.IsNullOrEmpty(value) && value.Length > column.MaxLength) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.FieldTooLong, Scope = "row", SubjectId = rowId, IsBlocking = true, Detail = column.Header });
			}
		}

		private static string Slug(string header) => new string(header.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

		public static byte[] RenderCsv(IReadOnlyList<CaPayDataColumn> columns, IEnumerable<IReadOnlyList<string>> rows)
		{
			var sb = new StringBuilder();
			sb.AppendLine(string.Join(",", columns.Select(c => Csv(c.Header))));
			foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(Csv)));
			return new UTF8Encoding(false).GetBytes(sb.ToString());
		}

		private static string Csv(string value)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;
			return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
		}

		/// <summary>A minimal SpreadsheetML workbook (one sheet, inline strings) — no third-party dependency.</summary>
		public static byte[] RenderXlsx(IReadOnlyList<CaPayDataColumn> columns, IEnumerable<IReadOnlyList<string>> rows, string sheetName = "PayData")
		{
			var sheet = new StringBuilder();
			sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
			var r = 1;
			sheet.Append(XlsxRow(r++, columns.Select(c => c.Header).ToList(), null));
			foreach (var row in rows) sheet.Append(XlsxRow(r++, row, columns));
			sheet.Append("</sheetData></worksheet>");
			using var stream = new MemoryStream();
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
			{
				Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
				Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
				Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"" + Xml(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
				Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
				Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
			}
			return stream.ToArray();
		}

		/// <summary>Cell type follows the schema, not the text: only integer / decimal columns become numeric cells, so zero-padded ZIP, NAICS and code values keep their leading zeros and a rate such as 0.75 stays a number. The header row (no schema) is all text.</summary>
		private static string XlsxRow(int index, IReadOnlyList<string> cells, IReadOnlyList<CaPayDataColumn> columns)
		{
			var sb = new StringBuilder("<row r=\"" + index + "\">");
			for (var c = 0; c < cells.Count; c++)
			{
				var value = cells[c] ?? string.Empty;
				var reference = ColumnName(c) + index;
				var type = columns != null && c < columns.Count ? columns[c].Type : null;
				var numeric = (type == "integer" || type == "decimal") && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) && value == value.Trim() && value.Length > 0;
				if (numeric)
					sb.Append("<c r=\"").Append(reference).Append("\"><v>").Append(value).Append("</v></c>");
				else
					sb.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t>").Append(Xml(value)).Append("</t></is></c>");
			}
			return sb.Append("</row>").ToString();
		}

		private static string ColumnName(int index)
		{
			var name = string.Empty;
			index++;
			while (index > 0) { var rem = (index - 1) % 26; name = (char)('A' + rem) + name; index = (index - 1) / 26; }
			return name;
		}

		private static string Xml(string value) => System.Security.SecurityElement.Escape(value ?? string.Empty);

		private static void Add(ZipArchive zip, string path, string content)
		{
			var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
			using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
			writer.Write(content);
		}

		public static string Sha256(byte[] data)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(data ?? Array.Empty<byte>())).ToLowerInvariant();
		}

		#endregion
	}
}
