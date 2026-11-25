using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Services
{
	/// <summary>
	/// The printable Training T1 documents: transcript, certificate of completion, hours report and the LMS formal reports, as
	/// self-contained HTML5 (inline style, no script, no external asset: the PDF converter runs without JavaScript or local file
	/// access) and as formula-safe CSV. Every value is HTML-encoded; labels come localized in the context, with English as the
	/// fallback. Line and certificate dates arrive in department time and are printed as given; the UTC instants of a period
	/// and the generated stamp are shown in the context's time zone.
	/// </summary>
	public static class TrainingDocuments
	{
		/// <summary>The resource keys (Trainings resx) the documents print; the caller localizes them into the context.</summary>
		public static readonly IReadOnlyList<string> LabelKeys = new[]
		{
			"Doc.Transcript", "Doc.Member", "Doc.Period", "Doc.Date", "Doc.Course", "Doc.Code", "Doc.Category", "Doc.Source", "Doc.Status", "Doc.Hours",
			"Doc.CeHours", "Doc.CeProvider", "Doc.CeCourseNumber", "Doc.Method", "Doc.Instructors", "Doc.TotalHours", "Doc.HoursByCategory", "Doc.Uncategorized",
			"Doc.GeneratedOn", "Doc.NotCounted", "Doc.CertificateTitle", "Doc.CertifiesThat", "Doc.HasCompleted", "Doc.CompletedOn", "Doc.IssuedBy",
			"Doc.VerificationCode", "Doc.VerifyAt", "Doc.HoursReport", "Doc.Records", "Doc.Members", "Doc.Target", "Doc.UnresolvedDuplicates",
			"Doc.PendingRecords", "Doc.NotAccreditor", "Doc.FormerMember", "Doc.IsoReport", "Doc.NccpReport", "Doc.National", "Doc.Local", "Doc.Individual",
			"Doc.Cycle", "Doc.Level"
		};

		private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Doc.Transcript"] = "Training transcript",
			["Doc.Member"] = "Member",
			["Doc.Period"] = "Period",
			["Doc.Date"] = "Date",
			["Doc.Course"] = "Course",
			["Doc.Code"] = "Code",
			["Doc.Category"] = "Category",
			["Doc.Source"] = "Source",
			["Doc.Status"] = "Status",
			["Doc.Hours"] = "Hours",
			["Doc.CeHours"] = "CE hours",
			["Doc.CeProvider"] = "CE provider",
			["Doc.CeCourseNumber"] = "CE course number",
			["Doc.Method"] = "Method",
			["Doc.Instructors"] = "Instructors",
			["Doc.TotalHours"] = "Total hours",
			["Doc.HoursByCategory"] = "Hours by category",
			["Doc.Uncategorized"] = "Uncategorized",
			["Doc.GeneratedOn"] = "Generated {0} by {1}",
			["Doc.NotCounted"] = "Not counted",
			["Doc.CertificateTitle"] = "Certificate of completion",
			["Doc.CertifiesThat"] = "This certifies that",
			["Doc.HasCompleted"] = "has completed",
			["Doc.CompletedOn"] = "Completed on",
			["Doc.IssuedBy"] = "Issued by",
			["Doc.VerificationCode"] = "Verification code",
			["Doc.VerifyAt"] = "Verify at",
			["Doc.HoursReport"] = "Training hours report",
			["Doc.Records"] = "Records",
			["Doc.Members"] = "Members",
			["Doc.Target"] = "Target",
			["Doc.UnresolvedDuplicates"] = "Unresolved possible duplicates: {0}",
			["Doc.PendingRecords"] = "Pending records: {0}",
			["Doc.NotAccreditor"] = "Issued by the department named above. Resgrid is not a continuing-education accreditor.",
			["Doc.FormerMember"] = "Former member",
			["Doc.IsoReport"] = "ISO FSRS training report",
			["Doc.NccpReport"] = "NREMT NCCP progress",
			["Doc.National"] = "National",
			["Doc.Local"] = "Local",
			["Doc.Individual"] = "Individual",
			["Doc.Cycle"] = "Cycle",
			["Doc.Level"] = "Level"
		};

		private const string QrPrefix = "data:image/png;base64,";

		private static readonly Regex Base64Body = new Regex("^[A-Za-z0-9+/]+={0,2}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private static readonly Regex Placeholder = new Regex(@"\{\{\s*([a-zA-Z]+)\s*\}\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

		#region Transcript

		public static string TranscriptHtml(TranscriptDocument document)
		{
			document ??= new TranscriptDocument();
			var context = document.Context ?? new TrainingDocumentContext();
			var lines = (document.Lines ?? new List<TranscriptLine>()).Where(l => l != null).ToList();
			var (byCategory, total) = TranscriptTotals(document, lines, context);

			var body = new StringBuilder();
			Heading(body, context, L(context, "Doc.Transcript"));

			body.Append("<table class=\"meta\">");
			MetaRow(body, L(context, "Doc.Member"), string.IsNullOrWhiteSpace(document.MemberIdentifier)
				? document.MemberName
				: document.MemberName + " (" + document.MemberIdentifier + ")");
			var period = Period(context, document.FromUtc, document.ToUtc);
			if (period != null)
				MetaRow(body, L(context, "Doc.Period"), period);
			body.Append("</table>");

			body.Append("<table class=\"grid\"><thead><tr>");
			Th(body, L(context, "Doc.Date"));
			Th(body, L(context, "Doc.Course"));
			Th(body, L(context, "Doc.Code"));
			Th(body, L(context, "Doc.Category"));
			Th(body, L(context, "Doc.Source"));
			Th(body, L(context, "Doc.Status"));
			Th(body, L(context, "Doc.Hours"), "num");
			Th(body, L(context, "Doc.CeHours"), "num");
			Th(body, L(context, "Doc.Method"));
			Th(body, L(context, "Doc.Instructors"));
			body.Append("</tr></thead><tbody>");

			foreach (var line in lines)
			{
				body.Append(line.Counts ? "<tr>" : "<tr class=\"muted\">");
				Td(body, Date(line.CompletedOn), "nowrap");

				body.Append("<td>").Append(E(line.Title));
				var ce = new List<string>();
				if (!string.IsNullOrWhiteSpace(line.CeProvider))
					ce.Add(L(context, "Doc.CeProvider") + ": " + line.CeProvider);
				if (!string.IsNullOrWhiteSpace(line.CeCourseNumber))
					ce.Add(L(context, "Doc.CeCourseNumber") + ": " + line.CeCourseNumber);
				if (ce.Count > 0)
					body.Append("<div class=\"small\">").Append(E(string.Join(" · ", ce))).Append("</div>");
				body.Append("</td>");

				Td(body, line.CourseCode);
				Td(body, string.IsNullOrWhiteSpace(line.Category) ? L(context, "Doc.Uncategorized") : line.Category);
				Td(body, line.Source);

				body.Append("<td>").Append(E(line.Status));
				if (!line.Counts)
					body.Append(" <span class=\"tag\">").Append(E(L(context, "Doc.NotCounted"))).Append("</span>");
				body.Append("</td>");

				Td(body, Number(line.Hours), "num");
				Td(body, Number(line.CeHours), "num");
				Td(body, line.Method);
				Td(body, line.Instructors);
				body.Append("</tr>");
			}

			body.Append("</tbody></table>");

			body.Append("<h2>").Append(E(L(context, "Doc.HoursByCategory"))).Append("</h2>");
			body.Append("<table class=\"grid narrow\"><thead><tr>");
			Th(body, L(context, "Doc.Category"));
			Th(body, L(context, "Doc.Hours"), "num");
			body.Append("</tr></thead><tbody>");
			foreach (var pair in byCategory)
			{
				body.Append("<tr>");
				Td(body, pair.Key);
				Td(body, Number(pair.Value), "num");
				body.Append("</tr>");
			}
			body.Append("<tr class=\"total\">");
			Td(body, L(context, "Doc.TotalHours"));
			Td(body, Number(total), "num");
			body.Append("</tr></tbody></table>");

			Generated(body, context);
			return Page(context, L(context, "Doc.Transcript"), body.ToString(), ReportStyle);
		}

		public static string TranscriptCsv(TranscriptDocument document)
		{
			document ??= new TranscriptDocument();
			var context = document.Context ?? new TrainingDocumentContext();
			var csv = new StringBuilder();
			Row(csv, new[]
			{
				L(context, "Doc.Member"), L(context, "Doc.Date"), L(context, "Doc.Course"), L(context, "Doc.Code"), L(context, "Doc.Category"), L(context, "Doc.Source"),
				L(context, "Doc.Status"), L(context, "Doc.Hours"), L(context, "Doc.CeHours"), L(context, "Doc.CeProvider"), L(context, "Doc.CeCourseNumber"),
				L(context, "Doc.Method"), L(context, "Doc.Instructors"), L(context, "Doc.NotCounted")
			});

			foreach (var line in (document.Lines ?? new List<TranscriptLine>()).Where(l => l != null))
			{
				Row(csv, new[]
				{
					document.MemberName, Date(line.CompletedOn), line.Title, line.CourseCode,
					string.IsNullOrWhiteSpace(line.Category) ? L(context, "Doc.Uncategorized") : line.Category,
					line.Source, line.Status, Number(line.Hours), Number(line.CeHours), line.CeProvider, line.CeCourseNumber, line.Method, line.Instructors,
					line.Counts ? null : L(context, "Doc.NotCounted")
				});
			}

			return csv.ToString();
		}

		/// <summary>Hours per category and the total, from the counted lines (a line that does not count never adds hours).</summary>
		private static (List<KeyValuePair<string, decimal>> ByCategory, decimal Total) TranscriptTotals(TranscriptDocument document, List<TranscriptLine> lines,
			TrainingDocumentContext context)
		{
			if (lines.Count == 0)
			{
				var given = (document.HoursByCategory ?? new Dictionary<string, decimal>())
					.Select(p => new KeyValuePair<string, decimal>(string.IsNullOrWhiteSpace(p.Key) ? L(context, "Doc.Uncategorized") : p.Key, p.Value))
					.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
				return (given, document.TotalHours);
			}

			var counted = lines.Where(l => l.Counts).ToList();
			var byCategory = counted
				.GroupBy(l => string.IsNullOrWhiteSpace(l.Category) ? L(context, "Doc.Uncategorized") : l.Category.Trim(), StringComparer.CurrentCultureIgnoreCase)
				.Select(g => new KeyValuePair<string, decimal>(g.Key, g.Sum(l => l.Hours)))
				.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
			return (byCategory, counted.Sum(l => l.Hours));
		}

		#endregion Transcript

		#region Certificate

		public static string CertificateHtml(CertificateDocument document)
		{
			document ??= new CertificateDocument();
			var context = document.Context ?? new TrainingDocumentContext();
			var body = new StringBuilder();

			body.Append("<div class=\"frame\">");
			if (!string.IsNullOrWhiteSpace(context.DepartmentName))
				body.Append("<div class=\"dept\">").Append(E(context.DepartmentName)).Append("</div>");
			body.Append("<h1>").Append(E(L(context, "Doc.CertificateTitle"))).Append("</h1>");
			body.Append("<div class=\"lead\">").Append(E(L(context, "Doc.CertifiesThat"))).Append("</div>");
			body.Append("<div class=\"name\">").Append(E(document.MemberName)).Append("</div>");
			body.Append("<div class=\"lead\">").Append(E(L(context, "Doc.HasCompleted"))).Append("</div>");
			body.Append("<div class=\"course\">").Append(E(document.Course)).Append("</div>");
			if (!string.IsNullOrWhiteSpace(document.CourseCode))
				body.Append("<div class=\"code\">").Append(E(document.CourseCode)).Append("</div>");

			body.Append("<table class=\"details\">");
			MetaRow(body, L(context, "Doc.CompletedOn"), Date(document.CompletedOn));
			MetaRow(body, L(context, "Doc.Hours"), Number(document.Hours));
			if (document.CeHours.HasValue)
				MetaRow(body, L(context, "Doc.CeHours"), Number(document.CeHours));
			if (!string.IsNullOrWhiteSpace(document.CeProvider))
				MetaRow(body, L(context, "Doc.CeProvider"), document.CeProvider);
			if (!string.IsNullOrWhiteSpace(document.CeCourseNumber))
				MetaRow(body, L(context, "Doc.CeCourseNumber"), document.CeCourseNumber);
			if (!string.IsNullOrWhiteSpace(document.Category))
				MetaRow(body, L(context, "Doc.Category"), document.Category);
			if (!string.IsNullOrWhiteSpace(document.Instructors))
				MetaRow(body, L(context, "Doc.Instructors"), document.Instructors);
			body.Append("</table>");

			body.Append("<div class=\"issued\">").Append(E(L(context, "Doc.IssuedBy"))).Append(": <strong>")
				.Append(E(IssuedBy(document))).Append("</strong></div>");

			if (!string.IsNullOrWhiteSpace(document.VerificationCode))
			{
				body.Append("<table class=\"verify\"><tr>");
				var qr = QrImage(document.QrImageDataUrl);
				if (qr != null)
					body.Append("<td>").Append(qr).Append("</td>");
				body.Append("<td>");
				body.Append("<div>").Append(E(L(context, "Doc.VerificationCode"))).Append(": <strong>").Append(E(document.VerificationCode.Trim())).Append("</strong></div>");
				if (!string.IsNullOrWhiteSpace(document.VerificationUrl))
					body.Append("<div>").Append(E(L(context, "Doc.VerifyAt"))).Append(": ").Append(E(document.VerificationUrl.Trim())).Append("</div>");
				body.Append("</td></tr></table>");
			}

			body.Append("<div class=\"note\">").Append(E(L(context, "Doc.NotAccreditor"))).Append("</div>");
			body.Append("</div>");

			return Page(context, L(context, "Doc.CertificateTitle"), body.ToString(), CertificateStyle);
		}

		/// <summary>
		/// The LMS department template (already sanitized by the caller): placeholders become HTML-encoded values in one pass (a
		/// value that itself reads like a placeholder stays text). The not-accreditor line is always printed under the template.
		/// </summary>
		public static string CustomCertificateHtml(string sanitizedTemplateBody, CertificateDocument document)
		{
			document ??= new CertificateDocument();
			var context = document.Context ?? new TrainingDocumentContext();
			var hasCode = !string.IsNullOrWhiteSpace(document.VerificationCode);

			string Value(string name)
			{
				switch (name.ToLowerInvariant())
				{
					case "member": return E(document.MemberName);
					case "course": return E(document.Course);
					case "code": return E(document.CourseCode);
					case "date": return E(Date(document.CompletedOn));
					case "hours": return E(Number(document.Hours));
					case "cehours": return E(Number(document.CeHours));
					case "department": return E(context.DepartmentName);
					case "issuedby": return E(IssuedBy(document));
					case "verificationcode": return hasCode ? E(document.VerificationCode.Trim()) : string.Empty;
					case "verifyurl": return hasCode ? E(document.VerificationUrl?.Trim()) : string.Empty;
					case "qr": return hasCode ? QrImage(document.QrImageDataUrl) ?? string.Empty : string.Empty;
					default: return null;
				}
			}

			var template = sanitizedTemplateBody ?? string.Empty;
			string filled;
			try
			{
				filled = Placeholder.Replace(template, m => Value(m.Groups[1].Value) ?? m.Value);
			}
			catch (RegexMatchTimeoutException)
			{
				return CertificateHtml(document);
			}

			var body = new StringBuilder();
			body.Append("<div class=\"custom\">").Append(filled).Append("</div>");
			body.Append("<div class=\"note\">").Append(E(L(context, "Doc.NotAccreditor"))).Append("</div>");
			return Page(context, L(context, "Doc.CertificateTitle"), body.ToString(), CustomCertificateStyle);
		}

		private static string IssuedBy(CertificateDocument document)
		{
			var issuer = document.IssuedBy?.Trim();
			var department = document.Context?.DepartmentName?.Trim();
			if (string.IsNullOrEmpty(issuer))
				return department ?? string.Empty;
			if (!string.IsNullOrEmpty(department) && issuer.IndexOf(department, StringComparison.OrdinalIgnoreCase) < 0)
				return department + " · " + issuer;
			return issuer;
		}

		/// <summary>An &lt;img&gt; for a PNG data URL with a clean base64 body; null for anything else (never an external source).</summary>
		private static string QrImage(string dataUrl)
		{
			var value = dataUrl?.Trim();
			if (string.IsNullOrEmpty(value) || !value.StartsWith(QrPrefix, StringComparison.Ordinal) || value.Length > 2 * 1024 * 1024)
				return null;
			var payload = value.Substring(QrPrefix.Length);
			bool valid;
			try { valid = payload.Length > 0 && payload.Length % 4 == 0 && Base64Body.IsMatch(payload); }
			catch (RegexMatchTimeoutException) { valid = false; }
			return valid ? "<img class=\"qr\" alt=\"QR\" src=\"" + E(value) + "\"/>" : null;
		}

		#endregion Certificate

		#region Hours report

		public static string HoursReportHtml(HoursReportDocument document)
		{
			document ??= new HoursReportDocument();
			var context = document.Context ?? new TrainingDocumentContext();
			var report = document.Report ?? new TrainingHoursReport();
			var body = new StringBuilder();
			Heading(body, context, L(context, "Doc.HoursReport"));

			var period = report.Query == null ? null : Period(context, report.Query.From, report.Query.To);
			if (period != null)
			{
				body.Append("<table class=\"meta\">");
				MetaRow(body, L(context, "Doc.Period"), period);
				body.Append("</table>");
			}

			body.Append("<table class=\"grid\"><thead><tr>");
			Th(body, string.IsNullOrWhiteSpace(document.GroupTitle) ? L(context, "Doc.Category") : document.GroupTitle);
			Th(body, L(context, "Doc.Records"), "num");
			Th(body, L(context, "Doc.Members"), "num");
			Th(body, L(context, "Doc.Hours"), "num");
			Th(body, L(context, "Doc.CeHours"), "num");
			Th(body, L(context, "Doc.Target"), "num");
			body.Append("</tr></thead><tbody>");

			foreach (var row in (report.Rows ?? new List<TrainingHoursReportRow>()).Where(r => r != null))
			{
				body.Append("<tr>");
				body.Append("<td>").Append(E(RowLabel(row, context)));
				if (row.IsFormerMember)
					body.Append(" <span class=\"tag\">").Append(E(L(context, "Doc.FormerMember"))).Append("</span>");
				body.Append("</td>");
				Td(body, Number(row.Records), "num");
				Td(body, Number(row.Members), "num");
				Td(body, Number(row.Hours), "num");
				Td(body, Number(row.CeHours), "num");
				Td(body, Number(row.TargetHours), "num");
				body.Append("</tr>");
			}

			body.Append("<tr class=\"total\">");
			Td(body, L(context, "Doc.TotalHours"));
			Td(body, Number(report.TotalRecords), "num");
			Td(body, Number(report.Members), "num");
			Td(body, Number(report.TotalHours), "num");
			Td(body, null, "num");
			Td(body, null, "num");
			body.Append("</tr></tbody></table>");

			if (report.UnresolvedDuplicates > 0 || report.PendingRecords > 0)
			{
				body.Append("<div class=\"notes\">");
				if (report.UnresolvedDuplicates > 0)
					body.Append("<div>").Append(E(Format(context, "Doc.UnresolvedDuplicates", Number(report.UnresolvedDuplicates)))).Append("</div>");
				if (report.PendingRecords > 0)
					body.Append("<div>").Append(E(Format(context, "Doc.PendingRecords", Number(report.PendingRecords)))).Append("</div>");
				body.Append("</div>");
			}

			Generated(body, context);
			return Page(context, L(context, "Doc.HoursReport"), body.ToString(), ReportStyle);
		}

		public static string HoursReportCsv(HoursReportDocument document)
		{
			document ??= new HoursReportDocument();
			var context = document.Context ?? new TrainingDocumentContext();
			var report = document.Report ?? new TrainingHoursReport();
			var csv = new StringBuilder();
			Row(csv, new[]
			{
				string.IsNullOrWhiteSpace(document.GroupTitle) ? L(context, "Doc.Category") : document.GroupTitle, L(context, "Doc.Records"), L(context, "Doc.Members"),
				L(context, "Doc.Hours"), L(context, "Doc.CeHours"), L(context, "Doc.Target"), L(context, "Doc.FormerMember")
			});

			foreach (var row in (report.Rows ?? new List<TrainingHoursReportRow>()).Where(r => r != null))
			{
				Row(csv, new[]
				{
					RowLabel(row, context), Number(row.Records), Number(row.Members), Number(row.Hours), Number(row.CeHours), Number(row.TargetHours),
					row.IsFormerMember ? L(context, "Doc.FormerMember") : null
				});
			}

			Row(csv, new[] { L(context, "Doc.TotalHours"), Number(report.TotalRecords), Number(report.Members), Number(report.TotalHours), null, null, null });
			if (report.UnresolvedDuplicates > 0)
				Row(csv, new[] { Format(context, "Doc.UnresolvedDuplicates", Number(report.UnresolvedDuplicates)) });
			if (report.PendingRecords > 0)
				Row(csv, new[] { Format(context, "Doc.PendingRecords", Number(report.PendingRecords)) });
			return csv.ToString();
		}

		private static string RowLabel(TrainingHoursReportRow row, TrainingDocumentContext context)
		{
			if (!string.IsNullOrWhiteSpace(row.Label))
				return row.Label;
			return string.IsNullOrWhiteSpace(row.Key) ? L(context, "Doc.Uncategorized") : row.Key;
		}

		#endregion Hours report

		#region LMS reports

		public static string IsoFsrsReportHtml(IsoFsrsReport report, TrainingDocumentContext context)
		{
			report ??= new IsoFsrsReport();
			context ??= new TrainingDocumentContext();
			var categories = IsoCategories(report);
			var body = new StringBuilder();
			Heading(body, context, L(context, "Doc.IsoReport"));

			var period = report.From == default && report.To == default ? null : Period(context, report.From, report.To);
			if (period != null)
			{
				body.Append("<table class=\"meta\">");
				MetaRow(body, L(context, "Doc.Period"), period);
				body.Append("</table>");
			}

			body.Append("<table class=\"grid\"><thead><tr>");
			Th(body, L(context, "Doc.Member"));
			foreach (var category in categories)
			{
				body.Append("<th class=\"num\">").Append(E(category.Code));
				if (!string.IsNullOrWhiteSpace(category.Name))
					body.Append("<div class=\"small\">").Append(E(category.Name)).Append("</div>");
				if (category.TargetHours.HasValue)
					body.Append("<div class=\"small\">").Append(E(L(context, "Doc.Target") + ": " + Number(category.TargetHours))).Append("</div>");
				body.Append("</th>");
			}
			Th(body, L(context, "Doc.TotalHours"), "num");
			body.Append("</tr></thead><tbody>");

			foreach (var row in (report.Rows ?? new List<IsoFsrsReportRow>()).Where(r => r != null))
			{
				body.Append("<tr>");
				Td(body, string.IsNullOrWhiteSpace(row.Name) ? row.UserId : row.Name);
				decimal sum = 0;
				foreach (var category in categories)
				{
					var hours = IsoHours(row, category.Code);
					sum += hours;
					var met = category.TargetHours.HasValue && category.TargetHours.Value > 0 && hours >= category.TargetHours.Value;
					Td(body, Number(hours), met ? "num met" : "num");
				}
				Td(body, Number(sum), "num");
				body.Append("</tr>");
			}

			body.Append("</tbody></table>");
			Generated(body, context);
			return Page(context, L(context, "Doc.IsoReport"), body.ToString(), ReportStyle);
		}

		/// <summary>One column per category code with its target in the header ("ISO-CO (Target 192)").</summary>
		public static string IsoFsrsReportCsv(IsoFsrsReport report, TrainingDocumentContext context)
		{
			report ??= new IsoFsrsReport();
			context ??= new TrainingDocumentContext();
			var categories = IsoCategories(report);
			var csv = new StringBuilder();

			var header = new List<string> { L(context, "Doc.Member") };
			header.AddRange(categories.Select(c => c.TargetHours.HasValue ? c.Code + " (" + L(context, "Doc.Target") + " " + Number(c.TargetHours) + ")" : c.Code));
			header.Add(L(context, "Doc.TotalHours"));
			Row(csv, header);

			foreach (var row in (report.Rows ?? new List<IsoFsrsReportRow>()).Where(r => r != null))
			{
				var cells = new List<string> { string.IsNullOrWhiteSpace(row.Name) ? row.UserId : row.Name };
				var hours = categories.Select(c => IsoHours(row, c.Code)).ToList();
				cells.AddRange(hours.Select(h => Number(h)));
				cells.Add(Number(hours.Sum()));
				Row(csv, cells);
			}

			return csv.ToString();
		}

		private static List<TrainingCategory> IsoCategories(IsoFsrsReport report) =>
			(report.Categories ?? new List<TrainingCategory>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Code))
				.GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
				.OrderBy(c => c.SortOrder).ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase).ToList();

		private static decimal IsoHours(IsoFsrsReportRow row, string code)
		{
			if (row.HoursByCode == null || string.IsNullOrWhiteSpace(code))
				return 0m;
			if (row.HoursByCode.TryGetValue(code.Trim(), out var hours))
				return hours;
			return row.HoursByCode.Where(p => string.Equals(p.Key?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase)).Sum(p => p.Value);
		}

		public static string NccpProgressHtml(NccpProgressReport report, TrainingDocumentContext context)
		{
			report ??= new NccpProgressReport();
			context ??= new TrainingDocumentContext();
			var body = new StringBuilder();
			Heading(body, context, L(context, "Doc.NccpReport"));

			body.Append("<table class=\"grid\"><thead><tr>");
			Th(body, L(context, "Doc.Member"));
			Th(body, L(context, "Doc.Level"));
			Th(body, L(context, "Doc.Cycle"));
			Th(body, L(context, "Doc.National"), "num");
			Th(body, L(context, "Doc.Local"), "num");
			Th(body, L(context, "Doc.Individual"), "num");
			body.Append("</tr></thead><tbody>");

			foreach (var row in (report.Rows ?? new List<NccpProgressRow>()).Where(r => r != null))
			{
				body.Append("<tr>");
				Td(body, string.IsNullOrWhiteSpace(row.Name) ? row.UserId : row.Name);
				body.Append("<td>").Append(E(row.Target?.Level));
				if (!string.IsNullOrWhiteSpace(row.CertificationName))
					body.Append("<div class=\"small\">").Append(E(row.CertificationName)).Append("</div>");
				body.Append("</td>");
				Td(body, Cycle(row), "nowrap");
				// A member without an LMS seat is never judged against a target (§17.4 seat rules): hours only.
				var target = row.NotLicensed ? null : row.Target;
				NccpCell(body, row.National, target?.National);
				NccpCell(body, row.Local, target?.Local);
				NccpCell(body, row.Individual, target?.Individual);
				body.Append("</tr>");
			}

			body.Append("</tbody></table>");
			Generated(body, context);
			return Page(context, L(context, "Doc.NccpReport"), body.ToString(), ReportStyle);
		}

		/// <summary>National, local and individual hours, each beside the row's level target.</summary>
		public static string NccpProgressCsv(NccpProgressReport report, TrainingDocumentContext context)
		{
			report ??= new NccpProgressReport();
			context ??= new TrainingDocumentContext();
			var csv = new StringBuilder();
			string TargetOf(string key) => L(context, key) + " (" + L(context, "Doc.Target") + ")";
			Row(csv, new[]
			{
				L(context, "Doc.Member"), L(context, "Doc.Level"), L(context, "Doc.Cycle"),
				L(context, "Doc.National"), TargetOf("Doc.National"), L(context, "Doc.Local"), TargetOf("Doc.Local"),
				L(context, "Doc.Individual"), TargetOf("Doc.Individual")
			});

			foreach (var row in (report.Rows ?? new List<NccpProgressRow>()).Where(r => r != null))
			{
				var level = row.Target?.Level;
				if (!string.IsNullOrWhiteSpace(row.CertificationName))
					level = string.IsNullOrWhiteSpace(level) ? row.CertificationName : level + " · " + row.CertificationName;
				// A member without an LMS seat is never judged against a target (§17.4 seat rules): the target columns stay empty.
				var target = row.NotLicensed ? null : row.Target;
				Row(csv, new[]
				{
					string.IsNullOrWhiteSpace(row.Name) ? row.UserId : row.Name, level, Cycle(row),
					Number(row.National), Number(target?.National), Number(row.Local), Number(target?.Local),
					Number(row.Individual), Number(target?.Individual)
				});
			}

			return csv.ToString();
		}

		private static void NccpCell(StringBuilder body, decimal hours, decimal? target)
		{
			var met = target.HasValue && target.Value > 0 && hours >= target.Value;
			Td(body, target.HasValue ? Number(hours) + " / " + Number(target) : Number(hours), met ? "num met" : "num");
		}

		private static string Cycle(NccpProgressRow row)
		{
			if (!row.CycleStart.HasValue && !row.CycleEnd.HasValue)
				return null;
			return (row.CycleStart.HasValue ? Date(row.CycleStart.Value) : "…") + " – " + (row.CycleEnd.HasValue ? Date(row.CycleEnd.Value) : "…");
		}

		#endregion LMS reports

		#region Rendering helpers

		/// <summary>The localized label, or the English text when the caller has none (a missing resource prints its key).</summary>
		private static string L(TrainingDocumentContext context, string key)
		{
			if (context?.Labels != null && context.Labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
				return value;
			return English.TryGetValue(key, out var fallback) ? fallback : key;
		}

		/// <summary>A localized format string; a translation with a broken placeholder falls back to English.</summary>
		private static string Format(TrainingDocumentContext context, string key, params object[] args)
		{
			try
			{
				return string.Format(CultureInfo.InvariantCulture, L(context, key), args);
			}
			catch (FormatException)
			{
				return string.Format(CultureInfo.InvariantCulture, English.TryGetValue(key, out var fallback) ? fallback : key + " {0}", args);
			}
		}

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

		private static string Number(decimal? value) => value.HasValue ? Number(value.Value) : null;

		private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

		private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		/// <summary>A UTC instant in the context's time zone (UTC when it has none or the zone does not resolve).</summary>
		private static DateTime Local(TrainingDocumentContext context, DateTime utc)
		{
			var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
			if (string.IsNullOrWhiteSpace(context?.TimeZone))
				return value;
			return value.TimeConverter(new Department { TimeZone = context.TimeZone.Trim() });
		}

		/// <summary>The period as department dates: the start's local date and the day before the exclusive end.</summary>
		private static string Period(TrainingDocumentContext context, DateTime? fromUtc, DateTime? toUtc)
		{
			if (!fromUtc.HasValue && !toUtc.HasValue)
				return null;

			string from = null, to = null;
			if (fromUtc.HasValue && fromUtc.Value > DateTime.MinValue.AddDays(2))
				from = Date(Local(context, fromUtc.Value).Date);
			if (toUtc.HasValue && toUtc.Value < DateTime.MaxValue.AddDays(-2) && toUtc.Value > DateTime.MinValue.AddDays(2))
			{
				var end = Local(context, toUtc.Value).AddTicks(-1).Date;
				if (from == null || string.CompareOrdinal(Date(end), from) >= 0)
					to = Date(end);
			}

			if (from == null && to == null)
				return null;
			return (from ?? "…") + " – " + (to ?? "…");
		}

		private static void Heading(StringBuilder body, TrainingDocumentContext context, string title)
		{
			if (!string.IsNullOrWhiteSpace(context.DepartmentName))
				body.Append("<div class=\"dept\">").Append(E(context.DepartmentName)).Append("</div>");
			body.Append("<h1>").Append(E(title)).Append("</h1>");
		}

		private static void Generated(StringBuilder body, TrainingDocumentContext context)
		{
			var on = context.GeneratedOnUtc == default ? DateTime.UtcNow : context.GeneratedOnUtc;
			var stamp = Local(context, on).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
			body.Append("<div class=\"foot\">").Append(E(Format(context, "Doc.GeneratedOn", stamp, context.GeneratedBy ?? string.Empty))).Append("</div>");
		}

		private static void MetaRow(StringBuilder body, string label, string value)
		{
			body.Append("<tr><th scope=\"row\">").Append(E(label)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
		}

		private static void Th(StringBuilder body, string text, string cssClass = null)
		{
			body.Append(cssClass == null ? "<th>" : "<th class=\"" + cssClass + "\">").Append(E(text)).Append("</th>");
		}

		private static void Td(StringBuilder body, string text, string cssClass = null)
		{
			body.Append(cssClass == null ? "<td>" : "<td class=\"" + cssClass + "\">").Append(E(text)).Append("</td>");
		}

		private static void Row(StringBuilder csv, IEnumerable<string> cells) => csv.Append(CsvCell.Line(cells)).Append("\r\n");

		private static string Page(TrainingDocumentContext context, string title, string body, string style)
		{
			var culture = string.IsNullOrWhiteSpace(context?.Culture) ? "en" : context.Culture.Trim();
			var html = new StringBuilder();
			html.Append("<!DOCTYPE html>\n<html lang=\"").Append(E(culture)).Append("\" dir=\"").Append(context?.RightToLeft == true ? "rtl" : "ltr").Append("\">\n");
			html.Append("<head>\n<meta charset=\"utf-8\"/>\n<title>").Append(E(title)).Append("</title>\n<style>").Append(style).Append("</style>\n</head>\n");
			html.Append("<body>\n").Append(body).Append("\n</body>\n</html>\n");
			return html.ToString();
		}

		private const string ReportStyle =
			"body{font-family:\"Segoe UI\",\"Noto Sans\",\"DejaVu Sans\",Arial,sans-serif;font-size:9.5pt;color:#222;margin:0;padding:8pt;}" +
			"h1{font-size:16pt;margin:2pt 0 8pt 0;}h2{font-size:12pt;margin:14pt 0 4pt 0;}" +
			".dept{font-size:9pt;color:#555;letter-spacing:1px;}" +
			"table{border-collapse:collapse;}table.grid{width:100%;}table.narrow{width:auto;min-width:40%;}" +
			"th,td{border-bottom:1px solid #ddd;padding:3pt 4pt;text-align:left;vertical-align:top;}" +
			"thead th{background:#f0f0f0;font-weight:bold;border-bottom:1px solid #bbb;}thead{display:table-header-group;}tr{page-break-inside:avoid;}" +
			".meta{margin-bottom:8pt;}.meta th,.meta td{border:none;padding:1pt 8pt 1pt 0;}.meta th{color:#555;font-weight:normal;}" +
			".num{text-align:right;white-space:nowrap;}.nowrap{white-space:nowrap;}" +
			"html[dir=rtl] th,html[dir=rtl] td{text-align:right;}html[dir=rtl] .num{text-align:left;}html[dir=rtl] .meta th,html[dir=rtl] .meta td{padding:1pt 0 1pt 8pt;}" +
			".small{font-size:8pt;color:#666;font-weight:normal;}.muted td{color:#888;}" +
			".tag{font-size:7.5pt;border:1px solid #aaa;padding:0 2pt;color:#555;white-space:nowrap;}" +
			".total td{font-weight:bold;border-top:2px solid #999;}.met{font-weight:bold;color:#1a6b2f;}" +
			".notes{margin-top:8pt;color:#8a5300;}.foot{margin-top:12pt;font-size:8pt;color:#666;}";

		private const string CertificateStyle =
			"body{font-family:Georgia,\"Noto Serif\",\"DejaVu Serif\",\"Times New Roman\",serif;color:#1d2a3a;margin:0;padding:10pt;}" +
			".frame{border:6px double #1d2a3a;padding:22pt 30pt;text-align:center;page-break-inside:avoid;}" +
			".dept{font-size:12pt;letter-spacing:2px;color:#444;}" +
			"h1{font-size:28pt;font-weight:normal;margin:8pt 0 12pt 0;}" +
			".lead{font-size:12pt;color:#555;margin:4pt 0;}" +
			".name{font-size:24pt;font-weight:bold;margin:6pt 0;}" +
			".course{font-size:17pt;font-weight:bold;margin:6pt 0 2pt 0;}.code{font-size:10pt;color:#555;}" +
			"table{border-collapse:collapse;}.details{margin:12pt auto 0 auto;}" +
			".details th,.details td{padding:2pt 8pt;font-size:11pt;text-align:left;vertical-align:top;}.details th{font-weight:normal;color:#555;}" +
			"html[dir=rtl] .details th,html[dir=rtl] .details td{text-align:right;}" +
			".issued{margin-top:14pt;font-size:11pt;}" +
			".verify{margin:10pt auto 0 auto;font-size:9pt;}.verify td{padding:0 6pt;text-align:left;vertical-align:middle;}html[dir=rtl] .verify td{text-align:right;}" +
			".qr{width:80pt;height:80pt;}" +
			".note{margin-top:10pt;font-size:8pt;color:#666;}";

		private const string CustomCertificateStyle =
			"body{font-family:Georgia,\"Noto Serif\",\"DejaVu Serif\",\"Times New Roman\",serif;color:#1d2a3a;margin:0;padding:10pt;}" +
			".custom{page-break-inside:avoid;}.custom img{max-width:100%;}.qr{width:80pt;height:80pt;}" +
			".note{margin-top:10pt;font-size:8pt;color:#666;text-align:center;}";

		#endregion Rendering helpers
	}
}
