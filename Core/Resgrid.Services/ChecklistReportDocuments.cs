using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	/// <summary>In-memory exports from already authorized snapshots. No remote images, scripts or user HTML.</summary>
	public static class ChecklistReportDocuments
	{
		private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.Checklists.Checklists", typeof(Resgrid.Localization.SupportedLocales).Assembly);
		public static string Text(string key) => Strings.GetString(key, CultureInfo.CurrentUICulture) ?? key;
		private static string H(object value) => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.CurrentCulture));
		private static string Cell(object value) => "<td>" + H(value) + "</td>";
		private static string Head(params string[] keys) => "<tr>" + string.Concat(keys.Select(k => "<th>" + H(Text(k)) + "</th>")) + "</tr>";
		private static string Page(string title, string body) => "<!doctype html><html lang=\"" + H(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) + "\" dir=\"" + (CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "rtl" : "ltr") + "\"><head><meta charset=\"utf-8\"><title>" + H(Text(title)) + "</title><style>body{font-family:Arial,sans-serif;font-size:12px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #aaa;padding:6px;text-align:start}tr{page-break-inside:avoid}</style></head><body><h1>" + H(Text(title)) + "</h1>" + body + "</body></html>";
		public static string Locked() => Page("ChecklistComplianceReport", "<p>" + H(Text("ScheduledReportProtected")) + "</p>");
		public static string Compliance(ChecklistComplianceSummary report, bool missedOnly = false)
		{
			var body = new StringBuilder("<p>" + H(report.FromUtc.ToString("u")) + " — " + H(report.UntilUtc.ToString("u")) + "</p><p>" + H(Text("ComplianceMethod")) + "</p>");
			body.Append("<p>").Append(H(Text("AuthorizedScope"))).Append("</p><table>").Append(Head("Target", "ExpectedChecks", "CompletedChecks", "OnTimeChecks", "MissedChecks", "ExcusedChecks", "CompletionRate"));
			foreach (var group in report.Groups.Where(g => !missedOnly || g.Missed > 0)) body.Append("<tr>").Append(Cell(group.Target.Name)).Append(Cell(group.Expected)).Append(Cell(group.Completed)).Append(Cell(group.OnTime)).Append(Cell(group.Missed)).Append(Cell(group.Skipped)).Append(Cell(group.CompletionRate?.ToString("0.##") ?? "—")).Append("</tr>");
			body.Append("</table><h2>").Append(H(Text("MissedTrend"))).Append("</h2><table>").Append(Head("Date", "ExpectedChecks", "MissedChecks"));
			foreach (var day in report.Trend) body.Append("<tr>").Append(Cell(day.DayUtc.ToString("yyyy-MM-dd"))).Append(Cell(day.Expected)).Append(Cell(day.Missed)).Append("</tr>");
			body.Append("</table>").Append(Entries(report.Entries.Where(e => !missedOnly || e.Missed))).Append(Unavailable(report.UnavailableSources));
			return Page(missedOnly ? "ChecklistMissedReport" : "ChecklistComplianceReport", body.ToString());
		}
		public static string ComplianceBody(ChecklistComplianceSummary report, bool missedOnly = false)
		{
			var html = Compliance(report, missedOnly);
			return html.Substring(html.IndexOf("<body>", StringComparison.Ordinal) + 6).Replace("</body></html>", "");
		}
		private static string Entries(IEnumerable<ChecklistReportEntry> entries)
		{
			var body = new StringBuilder("<table>" + Head("Name", "Target", "ScheduledFor", "WindowEnds", "CompletedChecks", "MissedChecks", "Result", "Score"));
			foreach (var entry in entries) body.Append("<tr>").Append(Cell(entry.Name)).Append(Cell(entry.Target.Name)).Append(Cell(entry.StartUtc.ToString("u"))).Append(Cell(entry.DueUtc?.ToString("u"))).Append(Cell(Text(entry.Completed ? "Yes" : "No"))).Append(Cell(Text(entry.Missed ? "Yes" : "No"))).Append(Cell(entry.Passed.HasValue ? Text(entry.Passed.Value ? "Passed" : "Failed") : "—")).Append(Cell(entry.Score)).Append("</tr>");
			return body.Append("</table>").ToString();
		}
		private static string Unavailable(IEnumerable<string> sources) => "<ul>" + string.Concat(sources.Select(s => "<li>" + H(Text(s)) + "</li>")) + "</ul>";
		public static string Packet(ReadinessEvidenceManifestV1 packet) => Page("ReadinessPacketReport", "<p>" + H(Text("CallId")) + ": " + H(packet.CallId) + "</p><p>" + H(packet.CoverageStartUtc.ToString("u")) + " — " + H(packet.CoverageEndUtc.ToString("u")) + "</p><p>" + H(Text("PacketMethod")) + "</p><h2>" + H(Text("DispatchedUnits")) + "</h2><ul>" + string.Concat(packet.Units.Select(u => "<li>" + H(u.Name) + " (#" + H(u.UnitId) + "; " + H(u.DispatchedUtc.ToString("u")) + ")</li>")) + "</ul><h2>" + H(Text("IssuedEquipment")) + "</h2><ul>" + string.Concat(packet.Assets.Select(a => "<li>" + H(a.Name) + " (" + H(a.AssetId) + ")</li>")) + "</ul>" + Entries(packet.Checklists) + Unavailable(packet.UnavailableSources));
		public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		public static void EnsureStillAuthorized(ReadinessEvidenceManifestV1 captured, ReadinessEvidenceManifestV1 current)
		{
			if (captured.DepartmentId != current.DepartmentId || captured.CallId != current.CallId
				|| captured.Checklists.Any(e => !current.Checklists.Any(c => c.OccurrenceId == e.OccurrenceId))
				|| captured.Units.Any(u => !current.Units.Any(c => c.UnitId == u.UnitId))
				|| captured.Assets.Any(a => !current.Assets.Any(c => c.AssetId == a.AssetId && c.SourceId == a.SourceId)))
				throw new ChecklistException(403, "The request could not be completed.");
		}
		public static ReadinessEvidencePackage Package(ReadinessEvidenceManifestV1 manifest, IPdfProvider pdf)
		{
			var json = JsonConvert.SerializeObject(manifest, Formatting.None, new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Utc, Culture = CultureInfo.InvariantCulture });
			var bytes = pdf.ConvertHtmlToPdf(Packet(manifest));
			if (bytes == null || bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") throw new InvalidOperationException("Checklist PDF generation failed.");
			if (bytes.Length > 16 * 1024 * 1024) throw new ChecklistException(400, "ReportTooLarge");
			return new ReadinessEvidencePackage { ManifestJson = json, ManifestSha256 = Sha256(Encoding.UTF8.GetBytes(json)), Pdf = bytes, PdfSha256 = Sha256(bytes) };
		}
		private static string CsvCell(object value)
		{
			var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
			// CSV quoting alone does not stop spreadsheet formula execution, including leading whitespace.
			if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n')) text = "'" + text;
			return "\"" + text.Replace("\"", "\"\"") + "\"";
		}
		public static byte[] Csv(ChecklistComplianceSummary report)
		{
			var csv = new StringBuilder(string.Join(",", new[] { "Target", "TargetType", "ExpectedChecks", "CompletedChecks", "OnTimeChecks", "MissedChecks", "ExcusedChecks", "CompletionRate" }.Select(k => CsvCell(Text(k)))) + "\r\n");
			foreach (var g in report.Groups) csv.AppendLine(string.Join(",", new object[] { g.Target.Name, Text(g.Target.Type.ToString()), g.Expected, g.Completed, g.OnTime, g.Missed, g.Skipped, g.CompletionRate }.Select(CsvCell)));
			return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
		}
	}
}
