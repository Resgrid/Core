using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// The OSHA 300 log and 301 incident reports of a year (§13b WL1) as formula-safe CSV and as self-contained HTML for the PDF converter
	/// (inline style, no script, every value HTML-encoded). The 300 log already carries "Privacy case" in place of a privacy case's name;
	/// a withheld (sealed) value prints as the localized "withheld" label, never as an envelope. Labels are the MemberHealth resx keys the
	/// caller localizes; English is the fallback.
	/// </summary>
	public static class MemberHealthDocuments
	{
		public static readonly IReadOnlyList<string> LabelKeys = new[]
		{
			"Osha.LogTitle", "Osha.ReportTitle", "Osha.Establishment", "Osha.Year", "Osha.CaseNumber", "Osha.EmployeeName", "Osha.JobTitle", "Osha.DateOfInjury",
			"Osha.Where", "Osha.Description", "Osha.Death", "Osha.DaysAwayCase", "Osha.RestrictedCase", "Osha.OtherRecordable", "Osha.DaysAway", "Osha.DaysRestricted",
			"Osha.Type.Injury", "Osha.Type.SkinDisorder", "Osha.Type.RespiratoryCondition", "Osha.Type.Poisoning", "Osha.Type.HearingLoss", "Osha.Type.AllOtherIllnesses",
			"Osha.Totals", "Osha.TimeOfEvent", "Osha.WhatHappened", "Osha.InjuryDetails", "Osha.Treatment", "Osha.EmergencyRoom", "Osha.Hospitalized", "Osha.DateOfDeath",
			"Osha.PrivacyCase", "Osha.EmployeeDetailsNote", "Osha.Withheld", "Osha.Yes", "Osha.No", "Osha.PrivacyCaseName", "Osha.Retention", "Osha.Generated"
		};

		private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Osha.LogTitle"] = "Log of work-related injuries and illnesses (OSHA Form 300 shape)",
			["Osha.ReportTitle"] = "Injury and illness incident report (OSHA Form 301 shape)",
			["Osha.Establishment"] = "Establishment",
			["Osha.Year"] = "Year",
			["Osha.CaseNumber"] = "Case no.",
			["Osha.EmployeeName"] = "Employee's name",
			["Osha.JobTitle"] = "Job title",
			["Osha.DateOfInjury"] = "Date of injury or onset of illness",
			["Osha.Where"] = "Where the event occurred",
			["Osha.Description"] = "Injury or illness, parts of body affected, and object or substance",
			["Osha.Death"] = "Death",
			["Osha.DaysAwayCase"] = "Days away from work",
			["Osha.RestrictedCase"] = "Job transfer or restriction",
			["Osha.OtherRecordable"] = "Other recordable case",
			["Osha.DaysAway"] = "Away from work (days)",
			["Osha.DaysRestricted"] = "On job transfer or restriction (days)",
			["Osha.Type.Injury"] = "Injury",
			["Osha.Type.SkinDisorder"] = "Skin disorder",
			["Osha.Type.RespiratoryCondition"] = "Respiratory condition",
			["Osha.Type.Poisoning"] = "Poisoning",
			["Osha.Type.HearingLoss"] = "Hearing loss",
			["Osha.Type.AllOtherIllnesses"] = "All other illnesses",
			["Osha.Totals"] = "Totals",
			["Osha.TimeOfEvent"] = "Time of event",
			["Osha.WhatHappened"] = "What was the employee doing, and what happened?",
			["Osha.InjuryDetails"] = "What was the injury or illness, and what harmed the employee?",
			["Osha.Treatment"] = "Physician, facility and treatment",
			["Osha.EmergencyRoom"] = "Treated in an emergency room",
			["Osha.Hospitalized"] = "Hospitalized overnight as an in-patient",
			["Osha.DateOfDeath"] = "Date of death",
			["Osha.PrivacyCase"] = "Privacy case",
			["Osha.EmployeeDetailsNote"] = "Employee address, date of birth, date hired and gender are completed from the personnel file.",
			["Osha.Withheld"] = "Withheld (protected)",
			["Osha.Yes"] = "Yes",
			["Osha.No"] = "No",
			["Osha.PrivacyCaseName"] = "Privacy case",
			["Osha.Retention"] = "Keep these records for five years after the end of the year they cover (29 CFR 1904.33).",
			["Osha.Generated"] = "Generated {0}"
		};

		private static readonly OshaInjuryTypes[] Types = (OshaInjuryTypes[])Enum.GetValues(typeof(OshaInjuryTypes));

		/// <summary>The 300 log: one row per recordable case (columns A-M) and a totals row.</summary>
		public static string LogCsv(OshaLog log, IReadOnlyDictionary<string, string> labels)
		{
			log ??= new OshaLog();
			var csv = new StringBuilder();
			var header = new List<string>
			{
				L(labels, "Osha.CaseNumber"), L(labels, "Osha.EmployeeName"), L(labels, "Osha.JobTitle"), L(labels, "Osha.DateOfInjury"), L(labels, "Osha.Where"),
				L(labels, "Osha.Description"), L(labels, "Osha.Death"), L(labels, "Osha.DaysAwayCase"), L(labels, "Osha.RestrictedCase"), L(labels, "Osha.OtherRecordable"),
				L(labels, "Osha.DaysAway"), L(labels, "Osha.DaysRestricted")
			};
			header.AddRange(Types.Select(t => L(labels, "Osha.Type." + t)));
			Row(csv, header);

			foreach (var row in log.Rows ?? new List<OshaLogRow>())
			{
				var cells = new List<string>
				{
					row.CaseNumber, Name(labels, row), row.JobTitle, Date(row.DateOfInjury), row.Location, Value(labels, row.Description),
					Mark(row.Outcome == OshaCaseOutcomes.Death), Mark(row.Outcome == OshaCaseOutcomes.DaysAwayFromWork),
					Mark(row.Outcome == OshaCaseOutcomes.JobTransferOrRestriction), Mark(row.Outcome == OshaCaseOutcomes.OtherRecordable),
					Number(row.DaysAway), Number(row.DaysRestricted)
				};
				cells.AddRange(Types.Select(t => Mark(row.InjuryType == t)));
				Row(csv, cells);
			}

			var totals = new List<string>
			{
				L(labels, "Osha.Totals"), null, null, null, null, null,
				Number(log.Count(OshaCaseOutcomes.Death)), Number(log.Count(OshaCaseOutcomes.DaysAwayFromWork)),
				Number(log.Count(OshaCaseOutcomes.JobTransferOrRestriction)), Number(log.Count(OshaCaseOutcomes.OtherRecordable)),
				Number(log.TotalDaysAway), Number(log.TotalDaysRestricted)
			};
			totals.AddRange(Types.Select(t => Number(log.Count(t))));
			Row(csv, totals);
			return csv.ToString();
		}

		/// <summary>The 301 reports: one row per case with the incident facts Resgrid holds.</summary>
		public static string ReportsCsv(OshaLog log, IReadOnlyDictionary<string, string> labels)
		{
			log ??= new OshaLog();
			var csv = new StringBuilder();
			Row(csv, new[]
			{
				L(labels, "Osha.CaseNumber"), L(labels, "Osha.EmployeeName"), L(labels, "Osha.JobTitle"), L(labels, "Osha.DateOfInjury"), L(labels, "Osha.TimeOfEvent"),
				L(labels, "Osha.Where"), L(labels, "Osha.WhatHappened"), L(labels, "Osha.InjuryDetails"), L(labels, "Osha.Treatment"), L(labels, "Osha.EmergencyRoom"),
				L(labels, "Osha.Hospitalized"), L(labels, "Osha.DateOfDeath"), L(labels, "Osha.PrivacyCase")
			});
			foreach (var report in log.Reports ?? new List<Osha301Report>())
			{
				Row(csv, new[]
				{
					report.CaseNumber, report.EmployeeName, report.JobTitle, Date(report.DateOfInjury), report.TimeOfEvent, report.Location,
					Value(labels, report.WhatHappened), Value(labels, report.InjuryDetails), Value(labels, report.TreatmentDetails),
					YesNo(labels, report.TreatedInEmergencyRoom), YesNo(labels, report.HospitalizedOvernight), Date(report.DiedOn), YesNo(labels, report.IsPrivacyCase)
				});
			}
			return csv.ToString();
		}

		public static string LogHtml(OshaLog log, IReadOnlyDictionary<string, string> labels, string culture, bool rightToLeft, DateTime generatedLocal)
		{
			log ??= new OshaLog();
			var body = new StringBuilder();
			Heading(body, log, labels, "Osha.LogTitle");
			body.Append("<table class=\"grid\"><thead><tr>");
			foreach (var key in new[] { "Osha.CaseNumber", "Osha.EmployeeName", "Osha.JobTitle", "Osha.DateOfInjury", "Osha.Where", "Osha.Description", "Osha.Death",
				"Osha.DaysAwayCase", "Osha.RestrictedCase", "Osha.OtherRecordable", "Osha.DaysAway", "Osha.DaysRestricted" })
				body.Append("<th>").Append(E(L(labels, key))).Append("</th>");
			foreach (var type in Types)
				body.Append("<th class=\"narrow\">").Append(E(L(labels, "Osha.Type." + type))).Append("</th>");
			body.Append("</tr></thead><tbody>");
			foreach (var row in log.Rows ?? new List<OshaLogRow>())
			{
				body.Append("<tr>");
				Td(body, row.CaseNumber, "nowrap");
				Td(body, Name(labels, row));
				Td(body, row.JobTitle);
				Td(body, Date(row.DateOfInjury), "nowrap");
				Td(body, row.Location);
				Td(body, Value(labels, row.Description));
				Td(body, Mark(row.Outcome == OshaCaseOutcomes.Death), "mark");
				Td(body, Mark(row.Outcome == OshaCaseOutcomes.DaysAwayFromWork), "mark");
				Td(body, Mark(row.Outcome == OshaCaseOutcomes.JobTransferOrRestriction), "mark");
				Td(body, Mark(row.Outcome == OshaCaseOutcomes.OtherRecordable), "mark");
				Td(body, Number(row.DaysAway), "num");
				Td(body, Number(row.DaysRestricted), "num");
				foreach (var type in Types)
					Td(body, Mark(row.InjuryType == type), "mark");
				body.Append("</tr>");
			}
			body.Append("</tbody><tfoot><tr><th colspan=\"6\">").Append(E(L(labels, "Osha.Totals"))).Append("</th>");
			foreach (var outcome in new[] { OshaCaseOutcomes.Death, OshaCaseOutcomes.DaysAwayFromWork, OshaCaseOutcomes.JobTransferOrRestriction, OshaCaseOutcomes.OtherRecordable })
				Td(body, Number(log.Count(outcome)), "num");
			Td(body, Number(log.TotalDaysAway), "num");
			Td(body, Number(log.TotalDaysRestricted), "num");
			foreach (var type in Types)
				Td(body, Number(log.Count(type)), "num");
			body.Append("</tr></tfoot></table>");
			Footer(body, labels, generatedLocal);
			return Page(body, labels, "Osha.LogTitle", culture, rightToLeft);
		}

		public static string ReportsHtml(OshaLog log, IReadOnlyDictionary<string, string> labels, string culture, bool rightToLeft, DateTime generatedLocal)
		{
			log ??= new OshaLog();
			var body = new StringBuilder();
			var first = true;
			foreach (var report in log.Reports ?? new List<Osha301Report>())
			{
				body.Append(first ? "<section>" : "<section class=\"break\">");
				first = false;
				Heading(body, log, labels, "Osha.ReportTitle");
				body.Append("<table class=\"form\"><tbody>");
				Field(body, labels, "Osha.CaseNumber", report.CaseNumber);
				Field(body, labels, "Osha.EmployeeName", report.EmployeeName);
				Field(body, labels, "Osha.JobTitle", report.JobTitle);
				Field(body, labels, "Osha.DateOfInjury", Date(report.DateOfInjury));
				Field(body, labels, "Osha.TimeOfEvent", report.TimeOfEvent);
				Field(body, labels, "Osha.Where", report.Location);
				Field(body, labels, "Osha.WhatHappened", Value(labels, report.WhatHappened));
				Field(body, labels, "Osha.InjuryDetails", Value(labels, report.InjuryDetails));
				Field(body, labels, "Osha.Treatment", Value(labels, report.TreatmentDetails));
				Field(body, labels, "Osha.EmergencyRoom", YesNo(labels, report.TreatedInEmergencyRoom));
				Field(body, labels, "Osha.Hospitalized", YesNo(labels, report.HospitalizedOvernight));
				Field(body, labels, "Osha.DateOfDeath", Date(report.DiedOn));
				Field(body, labels, "Osha.PrivacyCase", YesNo(labels, report.IsPrivacyCase));
				body.Append("</tbody></table>");
				body.Append("<div class=\"note\">").Append(E(L(labels, "Osha.EmployeeDetailsNote"))).Append("</div>");
				body.Append("</section>");
			}
			Footer(body, labels, generatedLocal);
			return Page(body, labels, "Osha.ReportTitle", culture, rightToLeft);
		}

		private static void Heading(StringBuilder body, OshaLog log, IReadOnlyDictionary<string, string> labels, string titleKey)
		{
			body.Append("<h1>").Append(E(L(labels, titleKey))).Append("</h1>");
			body.Append("<table class=\"meta\"><tbody>");
			body.Append("<tr><th scope=\"row\">").Append(E(L(labels, "Osha.Establishment"))).Append("</th><td>").Append(E(log.EstablishmentName)).Append("</td></tr>");
			body.Append("<tr><th scope=\"row\">").Append(E(L(labels, "Osha.Year"))).Append("</th><td>").Append(log.Year.ToString(CultureInfo.InvariantCulture)).Append("</td></tr>");
			body.Append("</tbody></table>");
		}

		private static void Footer(StringBuilder body, IReadOnlyDictionary<string, string> labels, DateTime generatedLocal)
		{
			body.Append("<div class=\"note\">").Append(E(L(labels, "Osha.Retention"))).Append("</div>");
			body.Append("<div class=\"foot\">").Append(E(string.Format(CultureInfo.InvariantCulture, L(labels, "Osha.Generated"), generatedLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))).Append("</div>");
		}

		private static string Page(StringBuilder body, IReadOnlyDictionary<string, string> labels, string titleKey, string culture, bool rightToLeft)
		{
			var html = new StringBuilder();
			html.Append("<!DOCTYPE html>\n<html lang=\"").Append(E(string.IsNullOrWhiteSpace(culture) ? "en" : culture.Trim())).Append("\" dir=\"").Append(rightToLeft ? "rtl" : "ltr").Append("\">\n");
			html.Append("<head>\n<meta charset=\"utf-8\"/>\n<title>").Append(E(L(labels, titleKey))).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n");
			html.Append("<body>\n").Append(body).Append("\n</body>\n</html>\n");
			return html.ToString();
		}

		private static void Field(StringBuilder body, IReadOnlyDictionary<string, string> labels, string key, string value) =>
			body.Append("<tr><th scope=\"row\">").Append(E(L(labels, key))).Append("</th><td>").Append(E(value)).Append("</td></tr>");

		/// <summary>The log name: "Privacy case" (localized) for a privacy case, whatever the row carries.</summary>
		private static string Name(IReadOnlyDictionary<string, string> labels, OshaLogRow row) =>
			row.IsPrivacyCase ? L(labels, "Osha.PrivacyCaseName") : row.EmployeeName;

		/// <summary>A sealed or redacted value prints as the withheld label; never an envelope.</summary>
		private static string Value(IReadOnlyDictionary<string, string> labels, string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			if (value == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(value))
				return L(labels, "Osha.Withheld");
			return value;
		}

		private static string Mark(bool on) => on ? "X" : null;

		private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

		private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		private static string YesNo(IReadOnlyDictionary<string, string> labels, bool value) => L(labels, value ? "Osha.Yes" : "Osha.No");

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
			"@page{size:landscape;}body{font-family:\"Segoe UI\",\"Noto Sans\",\"DejaVu Sans\",Arial,sans-serif;font-size:8.5pt;color:#222;margin:0;padding:8pt;}" +
			"h1{font-size:14pt;margin:2pt 0 6pt 0;}table{border-collapse:collapse;}table.grid{width:100%;}" +
			"th,td{border:1px solid #ccc;padding:2pt 3pt;text-align:left;vertical-align:top;}thead th{background:#f0f0f0;font-weight:bold;}" +
			"thead{display:table-header-group;}tr{page-break-inside:avoid;}tfoot th,tfoot td{background:#f7f7f7;font-weight:bold;}" +
			".meta{margin-bottom:6pt;}.meta th,.meta td{border:none;padding:1pt 8pt 1pt 0;}.meta th{color:#555;font-weight:normal;}" +
			".form{width:100%;margin-bottom:6pt;}.form th{width:35%;color:#444;font-weight:normal;background:#fafafa;}" +
			".num,.mark{text-align:center;white-space:nowrap;}.nowrap{white-space:nowrap;}.narrow{font-size:7.5pt;}" +
			".break{page-break-before:always;}html[dir=rtl] th,html[dir=rtl] td{text-align:right;}" +
			".note{margin-top:8pt;font-size:8pt;color:#555;}.foot{margin-top:10pt;font-size:7.5pt;color:#666;}";
	}
}
