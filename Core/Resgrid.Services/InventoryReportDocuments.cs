using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Resources;
using System.Text;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	/// <summary>Printable documents from attended, authorized snapshots. No scripts, external resources or user HTML.</summary>
	public static class InventoryReportDocuments
	{
		private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.Inventory.Inventory", typeof(Resgrid.Localization.SupportedLocales).Assembly);
		public static string Text(string key, CultureInfo culture = null)
		{
			culture = ReportCulture(culture);
			return Strings.GetResourceSet(CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName), true, false)?.GetString(key)
				?? Strings.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)?.GetString(key) ?? key;
		}
		private static CultureInfo ReportCulture(CultureInfo culture)
		{
			culture ??= CultureInfo.CurrentUICulture;
			return Resgrid.Localization.SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName) ? culture : CultureInfo.GetCultureInfo("en");
		}
		public static string Title(InventoryReportKind kind, CultureInfo culture = null) => Text("M5Report" + kind, culture);
		private static string H(object value) => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture));
		private static string Page(InventoryReportKind kind, string body, CultureInfo culture) => "<!doctype html><html lang=\"" + H(culture.Name) + "\" dir=\"" + (culture.TextInfo.IsRightToLeft ? "rtl" : "ltr")
			+ "\"><head><meta charset=\"utf-8\"><title>" + H(Title(kind, culture)) + "</title><style>@page{size:landscape;margin:12mm}body{font-family:Arial,sans-serif;font-size:10px}table{border-collapse:collapse;width:100%;margin-bottom:16px}td,th{border:1px solid #888;padding:4px;text-align:start;overflow-wrap:anywhere}thead{display:table-header-group}tr{page-break-inside:avoid}.signatures td{height:28px}h1{font-size:19px}</style></head><body><h1>" + H(Title(kind, culture)) + "</h1>" + body + "</body></html>";
		public static string Locked(InventoryReportKind kind, CultureInfo culture = null)
		{
			culture = ReportCulture(culture);
			return Page(kind, "<p>" + H(Text("M5ScheduledReportProtected", culture)) + "</p>", culture);
		}
		public static string Build(InventoryReport report, CultureInfo culture = null)
		{
			if (report == null || !Enum.IsDefined(report.Kind) || report.Columns.Count == 0 || report.Columns.Count > 40 || report.Rows.Count > 5000)
				throw new InventoryException(400, "InvalidReport");
			culture = ReportCulture(culture);
			var body = new StringBuilder("<p>" + H(Text("M4AsOfUtc", culture)) + ": " + H(report.GeneratedOn.ToString("u", CultureInfo.InvariantCulture)) + "</p><p>" + H(Text("M5ReportScope", culture)) + "</p>");
			if (report.FromUtc.HasValue || report.UntilUtc.HasValue)
				body.Append("<p>").Append(H(Text("M5ReportRange", culture))).Append(": ").Append(H(report.FromUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "—")).Append(" / ").Append(H(report.UntilUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "—")).Append("</p>");
			if (report.Kind is InventoryReportKind.Usage or InventoryReportKind.LowStock or InventoryReportKind.Issuance or InventoryReportKind.Expiration)
				body.Append("<p>").Append(H(Text("M5ReportMethod" + report.Kind, culture))).Append("</p>");
			if (report.Kind == InventoryReportKind.Valuation)
			{
				body.Append("<p>").Append(H(Text("M4ValuationScope", culture))).Append("</p>");
				if (report.Rows.Any(row => row.TryGetValue("Amount", out var quantity) && quantity is decimal number && number < 0)) body.Append("<p>").Append(H(Text("M4NegativeStock", culture))).Append("</p>");
			}
			body.Append("<table><thead><tr>");
			foreach (var column in report.Columns) body.Append("<th>").Append(H(Text(column, culture))).Append("</th>");
			body.Append("</tr></thead><tbody>");
			if (report.Rows.Count == 0) body.Append("<tr><td colspan=\"").Append(report.Columns.Count).Append("\">").Append(H(Text("NoRows", culture))).Append("</td></tr>");
			foreach (var row in report.Rows)
			{
				body.Append("<tr>");
				foreach (var column in report.Columns) body.Append("<td>").Append(H(Format(row.TryGetValue(column, out var value) ? value : null, column, culture))).Append("</td>");
				body.Append("</tr>");
				if (report.Kind == InventoryReportKind.ControlledSubstanceLog)
					body.Append("<tr class=\"signatures\"><td colspan=\"").Append(report.Columns.Count / 2).Append("\">").Append(H(Text("M5PerformerSignature", culture))).Append(": ____________________</td><td colspan=\"")
						.Append(report.Columns.Count - report.Columns.Count / 2).Append("\">").Append(H(Text("M5WitnessSignature", culture))).Append(": ____________________</td></tr>");
			}
			body.Append("</tbody></table>");
			if (report.Totals.Count > 0)
			{
				body.Append("<table><thead><tr>");
				foreach (var column in new[] { "M4Currency", "M4KnownValue", "M4UncostedRows" }) body.Append("<th>").Append(H(Text(column, culture))).Append("</th>");
				body.Append("</tr></thead><tbody>");
				foreach (var total in report.Totals) body.Append("<tr><td>").Append(H(total.CurrencyCode ?? Text("M4UnspecifiedCurrency", culture))).Append("</td><td>").Append(H(total.KnownValue.ToString("0.######", culture))).Append("</td><td>").Append(total.UncostedRows.ToString(culture)).Append("</td></tr>");
				body.Append("</tbody></table>");
			}
			return Page(report.Kind, body.ToString(), culture);
		}
		private static string Format(object value, string column, CultureInfo culture)
		{
			if (value == null) return Text(column == "M4Currency" ? "M4UnspecifiedCurrency" : column is "UnitCost" or "M4KnownValue" ? "M4UnknownCost" : "M5NotRecorded", culture);
			return value switch
			{
				InventoryTrackingMode mode => Text("Tracking" + (int)mode, culture),
				InventoryAssetStatus status => Text("Status" + (int)status, culture),
				InventoryTransactionType movement => Text("Movement" + (int)movement, culture),
				InventoryUsageType usage => Text("RecordUsageType" + (int)usage, culture),
				InventoryIssuanceStatus issuance => Text("IssuanceStatus" + (int)issuance, culture),
				InventoryReferenceType reference => Text("M5ReferenceType" + (int)reference, culture),
				DateTime date => date.ToString("u", CultureInfo.InvariantCulture),
				decimal number => number.ToString("0.######", culture),
				IFormattable formattable => formattable.ToString(null, culture),
				_ => Convert.ToString(value, culture)
			};
		}
	}
}
