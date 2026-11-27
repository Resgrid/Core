using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Services.Wellness
{
	/// <summary>The document a member exports: their own entries only, with the labels already localized by the caller.</summary>
	public class ExposureLogDocument
	{
		public string DepartmentName { get; set; }
		public string MemberName { get; set; }
		public string TimeZone { get; set; }
		public string Culture { get; set; }
		public bool RightToLeft { get; set; }
		public DateTime GeneratedOnUtc { get; set; }
		public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public List<RmsResponderExposure> Entries { get; set; } = new List<RmsResponderExposure>();
		public Dictionary<int, string> CallNames { get; set; } = new Dictionary<int, string>();
	}

	/// <summary>
	/// The member's exposure log as self-contained HTML (for the PDF converter: inline style, no script, every value HTML-encoded) and
	/// as formula-safe CSV; and the wellness officer's report CSV, which carries operational columns only. A withheld (sealed) value
	/// prints as the localized "withheld" label, never as an envelope.
	/// </summary>
	public static class WellnessDocuments
	{
		/// <summary>The Wellness resx keys the documents print; the caller localizes them into <see cref="ExposureLogDocument.Labels"/>.</summary>
		public static readonly IReadOnlyList<string> LabelKeys = new[]
		{
			"Doc.Title", "Doc.Member", "Doc.Date", "Doc.Category", "Doc.Status", "Doc.Call", "Doc.Duration", "Doc.Substance", "Doc.PpeWorn",
			"Doc.Decontamination", "Doc.Narrative", "Doc.HealthDetails", "Doc.Withheld", "Doc.GeneratedOn", "Doc.Retention", "Doc.CriticalIncidents",
			"Doc.ReportTitle", "Doc.Total", "Doc.PendingSuggestions", "Doc.LastExposure", "Doc.ScreeningFlagged", "Doc.OpenFollowUps", "Doc.Yes",
			"Category.Carcinogen", "Category.BloodbornePathogen", "Category.HazardousSubstance", "Category.CriticalIncident", "Category.Other",
			"Status.Suggested", "Status.Confirmed", "Status.Dismissed", "Status.Voided"
		};

		private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Doc.Title"] = "Exposure and critical-incident log",
			["Doc.Member"] = "Member",
			["Doc.Date"] = "Date",
			["Doc.Category"] = "Category",
			["Doc.Status"] = "Status",
			["Doc.Call"] = "Call",
			["Doc.Duration"] = "Duration (h:mm)",
			["Doc.Substance"] = "Substance or agent",
			["Doc.PpeWorn"] = "PPE worn",
			["Doc.Decontamination"] = "Decontamination",
			["Doc.Narrative"] = "What happened",
			["Doc.HealthDetails"] = "Health details",
			["Doc.Withheld"] = "Withheld (protected)",
			["Doc.GeneratedOn"] = "Generated {0}",
			["Doc.Retention"] = "Your department keeps these records for at least 30 years.",
			["Doc.CriticalIncidents"] = "Critical incidents",
			["Doc.ReportTitle"] = "Member exposure report",
			["Doc.Total"] = "Confirmed entries",
			["Doc.PendingSuggestions"] = "Awaiting review",
			["Doc.LastExposure"] = "Most recent",
			["Doc.ScreeningFlagged"] = "Increased screening",
			["Doc.OpenFollowUps"] = "Open follow-ups",
			["Doc.Yes"] = "Yes",
			["Category.Carcinogen"] = "Carcinogen",
			["Category.BloodbornePathogen"] = "Bloodborne pathogen",
			["Category.HazardousSubstance"] = "Hazardous substance",
			["Category.CriticalIncident"] = "Critical incident",
			["Category.Other"] = "Other",
			["Status.Suggested"] = "Awaiting review",
			["Status.Confirmed"] = "Confirmed",
			["Status.Dismissed"] = "Dismissed",
			["Status.Voided"] = "Recorded in error"
		};

		public static string ExposureLogCsv(ExposureLogDocument document)
		{
			document ??= new ExposureLogDocument();
			var csv = new StringBuilder();
			Row(csv, new[]
			{
				L(document.Labels, "Doc.Member"), L(document.Labels, "Doc.Date"), L(document.Labels, "Doc.Category"), L(document.Labels, "Doc.Status"),
				L(document.Labels, "Doc.Call"), L(document.Labels, "Doc.Duration"), L(document.Labels, "Doc.Substance"), L(document.Labels, "Doc.PpeWorn"),
				L(document.Labels, "Doc.Decontamination"), L(document.Labels, "Doc.Narrative"), L(document.Labels, "Doc.HealthDetails")
			});

			foreach (var entry in (document.Entries ?? new List<RmsResponderExposure>()).Where(e => e != null))
			{
				Row(csv, new[]
				{
					document.MemberName, LocalStamp(document.TimeZone, entry.OccurredOn), Category(document.Labels, entry.Category), Status(document.Labels, entry.Status),
					CallOf(document, entry.CallId), Duration(entry.DurationMinutes), Value(document.Labels, entry.Substance), Value(document.Labels, entry.PpeWorn),
					Value(document.Labels, entry.Decontamination), Value(document.Labels, entry.Narrative), Value(document.Labels, entry.HealthDetails)
				});
			}

			return csv.ToString();
		}

		public static string ExposureLogHtml(ExposureLogDocument document)
		{
			document ??= new ExposureLogDocument();
			var labels = document.Labels;
			var body = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(document.DepartmentName))
				body.Append("<div class=\"dept\">").Append(E(document.DepartmentName)).Append("</div>");
			body.Append("<h1>").Append(E(L(labels, "Doc.Title"))).Append("</h1>");
			body.Append("<table class=\"meta\"><tbody>");
			body.Append("<tr><th scope=\"row\">").Append(E(L(labels, "Doc.Member"))).Append("</th><td>").Append(E(document.MemberName)).Append("</td></tr>");
			var critical = (document.Entries ?? new List<RmsResponderExposure>()).Count(e => e != null && e.Counts && e.Category == (int)ResponderExposureCategories.CriticalIncident);
			body.Append("<tr><th scope=\"row\">").Append(E(L(labels, "Doc.CriticalIncidents"))).Append("</th><td>").Append(critical.ToString(CultureInfo.InvariantCulture)).Append("</td></tr>");
			body.Append("</tbody></table>");

			body.Append("<table class=\"grid\"><thead><tr>");
			foreach (var key in new[] { "Doc.Date", "Doc.Category", "Doc.Status", "Doc.Call", "Doc.Duration", "Doc.Substance", "Doc.PpeWorn", "Doc.Decontamination" })
				body.Append("<th>").Append(E(L(labels, key))).Append("</th>");
			body.Append("</tr></thead><tbody>");
			foreach (var entry in (document.Entries ?? new List<RmsResponderExposure>()).Where(e => e != null))
			{
				body.Append(entry.Counts ? "<tr>" : "<tr class=\"muted\">");
				Td(body, LocalStamp(document.TimeZone, entry.OccurredOn), "nowrap");
				Td(body, Category(labels, entry.Category));
				Td(body, Status(labels, entry.Status));
				Td(body, CallOf(document, entry.CallId));
				Td(body, Duration(entry.DurationMinutes), "num");
				Td(body, Value(labels, entry.Substance));
				Td(body, Value(labels, entry.PpeWorn));
				Td(body, Value(labels, entry.Decontamination));
				body.Append("</tr>");
				var narrative = Value(labels, entry.Narrative);
				var health = Value(labels, entry.HealthDetails);
				if (!string.IsNullOrWhiteSpace(narrative) || !string.IsNullOrWhiteSpace(health))
				{
					body.Append("<tr class=\"detail\"><td></td><td colspan=\"7\">");
					if (!string.IsNullOrWhiteSpace(narrative))
						body.Append("<div><strong>").Append(E(L(labels, "Doc.Narrative"))).Append(":</strong> ").Append(E(narrative)).Append("</div>");
					if (!string.IsNullOrWhiteSpace(health))
						body.Append("<div><strong>").Append(E(L(labels, "Doc.HealthDetails"))).Append(":</strong> ").Append(E(health)).Append("</div>");
					body.Append("</td></tr>");
				}
			}
			body.Append("</tbody></table>");
			body.Append("<div class=\"note\">").Append(E(L(labels, "Doc.Retention"))).Append("</div>");
			var on = document.GeneratedOnUtc == default ? DateTime.UtcNow : document.GeneratedOnUtc;
			body.Append("<div class=\"foot\">").Append(E(string.Format(CultureInfo.InvariantCulture, L(labels, "Doc.GeneratedOn"), LocalStamp(document.TimeZone, on)))).Append("</div>");

			var culture = string.IsNullOrWhiteSpace(document.Culture) ? "en" : document.Culture.Trim();
			var html = new StringBuilder();
			html.Append("<!DOCTYPE html>\n<html lang=\"").Append(E(culture)).Append("\" dir=\"").Append(document.RightToLeft ? "rtl" : "ltr").Append("\">\n");
			html.Append("<head>\n<meta charset=\"utf-8\"/>\n<title>").Append(E(L(labels, "Doc.Title"))).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n");
			html.Append("<body>\n").Append(body).Append("\n</body>\n</html>\n");
			return html.ToString();
		}

		/// <summary>The officer report: one row per member with operational counts. No protected value can reach it.</summary>
		public static string ReportCsv(IEnumerable<MemberExposureSummary> summaries, Func<string, string> nameOf, IReadOnlyDictionary<string, string> labels)
		{
			var csv = new StringBuilder();
			var header = new List<string> { L(labels, "Doc.Member"), L(labels, "Doc.Total"), L(labels, "Doc.CriticalIncidents") };
			var categories = new[] { ResponderExposureCategories.Carcinogen, ResponderExposureCategories.BloodbornePathogen, ResponderExposureCategories.HazardousSubstance, ResponderExposureCategories.Other };
			header.AddRange(categories.Select(c => Category(labels, (int)c)));
			header.AddRange(new[] { L(labels, "Doc.PendingSuggestions"), L(labels, "Doc.LastExposure"), L(labels, "Doc.ScreeningFlagged"), L(labels, "Doc.OpenFollowUps") });
			Row(csv, header);

			foreach (var summary in (summaries ?? Enumerable.Empty<MemberExposureSummary>()).Where(s => s != null))
			{
				var cells = new List<string>
				{
					nameOf?.Invoke(summary.UserId) ?? summary.UserId,
					summary.Total.ToString(CultureInfo.InvariantCulture),
					summary.CriticalIncidents.ToString(CultureInfo.InvariantCulture)
				};
				cells.AddRange(categories.Select(c => (summary.ByCategory != null && summary.ByCategory.TryGetValue((int)c, out var n) ? n : 0).ToString(CultureInfo.InvariantCulture)));
				cells.Add(summary.PendingSuggestions.ToString(CultureInfo.InvariantCulture));
				cells.Add(summary.LastOccurredOn.HasValue ? summary.LastOccurredOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
				cells.Add(summary.ScreeningFlagged ? L(labels, "Doc.Yes") : null);
				cells.Add(summary.OpenFollowUps.ToString(CultureInfo.InvariantCulture));
				Row(csv, cells);
			}

			return csv.ToString();
		}

		private static string Category(IReadOnlyDictionary<string, string> labels, int category) =>
			L(labels, "Category." + (Enum.IsDefined(typeof(ResponderExposureCategories), category) ? ((ResponderExposureCategories)category).ToString() : "Other"));

		private static string Status(IReadOnlyDictionary<string, string> labels, int status) =>
			L(labels, "Status." + (Enum.IsDefined(typeof(ResponderExposureStatuses), status) ? ((ResponderExposureStatuses)status).ToString() : "Confirmed"));

		/// <summary>A sealed or redacted value prints as the withheld label; never an envelope.</summary>
		private static string Value(IReadOnlyDictionary<string, string> labels, string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			if (value == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(value))
				return L(labels, "Doc.Withheld");
			return value;
		}

		private static string CallOf(ExposureLogDocument document, int? callId) =>
			callId.HasValue ? (document.CallNames != null && document.CallNames.TryGetValue(callId.Value, out var name) ? name : "#" + callId.Value.ToString(CultureInfo.InvariantCulture)) : null;

		private static string Duration(int? minutes)
		{
			if (!minutes.HasValue || minutes.Value <= 0)
				return null;
			return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes.Value / 60, minutes.Value % 60);
		}

		private static string LocalStamp(string timeZone, DateTime utc)
		{
			var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
			if (!string.IsNullOrWhiteSpace(timeZone))
				value = value.TimeConverter(new Department { TimeZone = timeZone.Trim() });
			return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		}

		private static string L(IReadOnlyDictionary<string, string> labels, string key)
		{
			if (labels != null && labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
				return value;
			return English.TryGetValue(key, out var english) ? english : key;
		}

		private static void Td(StringBuilder body, string text, string cssClass = null) =>
			body.Append(cssClass == null ? "<td>" : "<td class=\"" + cssClass + "\">").Append(E(text)).Append("</td>");

		private static void Row(StringBuilder csv, IEnumerable<string> cells) => csv.Append(CsvCell.Line(cells)).Append("\r\n");

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		private const string Style =
			"body{font-family:\"Segoe UI\",\"Noto Sans\",\"DejaVu Sans\",Arial,sans-serif;font-size:9.5pt;color:#222;margin:0;padding:8pt;}" +
			"h1{font-size:16pt;margin:2pt 0 8pt 0;}.dept{font-size:9pt;color:#555;letter-spacing:1px;}" +
			"table{border-collapse:collapse;}table.grid{width:100%;}" +
			"th,td{border-bottom:1px solid #ddd;padding:3pt 4pt;text-align:left;vertical-align:top;}" +
			"thead th{background:#f0f0f0;font-weight:bold;border-bottom:1px solid #bbb;}thead{display:table-header-group;}tr{page-break-inside:avoid;}" +
			".meta{margin-bottom:8pt;}.meta th,.meta td{border:none;padding:1pt 8pt 1pt 0;}.meta th{color:#555;font-weight:normal;}" +
			".num{text-align:right;white-space:nowrap;}.nowrap{white-space:nowrap;}.muted td{color:#888;}.detail td{border-bottom:1px solid #eee;font-size:9pt;color:#333;}" +
			"html[dir=rtl] th,html[dir=rtl] td{text-align:right;}html[dir=rtl] .num{text-align:left;}" +
			".note{margin-top:10pt;font-size:8.5pt;color:#555;}.foot{margin-top:12pt;font-size:8pt;color:#666;}";
	}
}
