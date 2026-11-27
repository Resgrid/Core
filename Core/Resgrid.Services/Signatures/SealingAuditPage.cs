using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// The Resgrid audit page appended to every document Resgrid seals from HTML (plan §21.1 L3, §22.3): subject, revision hash,
	/// the signer list with levels and methods, times and the one-time-code evidence from the Resgrid gate. It is part of the
	/// sealed bytes, so it cannot be changed without breaking the seal.
	/// </summary>
	public static class SealingAuditPage
	{
		public const string Marker = "rg-sealing-audit-page";

		/// <summary>The audit page markup (a page of its own).</summary>
		public static string Build(SealHtmlRequest request, string culture)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			string L(string key) => E(SignatureTexts.Get(key, culture));
			string T(DateTime? utc) => utc.HasValue ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : "—";

			var sb = new StringBuilder();
			sb.Append("<div class=\"").Append(Marker).Append("\" style=\"page-break-before:always;font-family:Arial,Helvetica,sans-serif;font-size:11px;color:#222\">");
			sb.Append("<h2 style=\"font-size:15px;margin:0 0 8px\">").Append(L("AuditPageTitle")).Append("</h2>");
			sb.Append("<table style=\"border-collapse:collapse;width:100%;margin-bottom:10px\">");
			void Row(string label, string value) => sb.Append("<tr><th style=\"text-align:left;border:1px solid #bbb;padding:3px 6px;background:#f2f2f2;width:30%\">").Append(label)
				.Append("</th><td style=\"border:1px solid #bbb;padding:3px 6px;word-break:break-all\">").Append(value).Append("</td></tr>");
			Row(L("AuditDepartment"), E(request.DepartmentName));
			Row(L("AuditDocument"), E(request.Title));
			Row(L("AuditSubject"), E(request.SubjectKind.ToString()) + " · " + E(request.SubjectId) + (string.IsNullOrWhiteSpace(request.RevisionId) ? string.Empty : " · " + E(request.RevisionId)));
			Row(L("AuditRevisionHash"), "<span style=\"font-family:monospace\">" + E(request.RevisionSha256) + "</span> (" + E(SignaturePolicy.ShortCode(request.RevisionSha256)) + ")");
			Row(L("AuditPreparedOn"), E(T(DateTime.UtcNow)));
			sb.Append("</table>");

			sb.Append("<h3 style=\"font-size:13px;margin:8px 0 4px\">").Append(L("AuditSigners")).Append("</h3>");
			var signers = request.Signers ?? new List<SealAuditSigner>();
			if (signers.Count == 0)
			{
				sb.Append("<p>").Append(L("AuditNoSigners")).Append("</p>");
			}
			else
			{
				sb.Append("<table style=\"border-collapse:collapse;width:100%\"><tr>");
				foreach (var head in new[] { "SignerColumn", "AuditRole", "LevelColumn", "AuditMethod", "SignedOnColumn", "AuditEvidence" })
					sb.Append("<th style=\"text-align:left;border:1px solid #bbb;padding:3px 6px;background:#f2f2f2\">").Append(L(head)).Append("</th>");
				sb.Append("</tr>");
				foreach (var s in signers)
				{
					sb.Append("<tr>");
					foreach (var cell in new[] { E(s.Name), E(s.Role), "L" + s.Level.ToString(CultureInfo.InvariantCulture), E(s.Method), E(T(s.SignedOnUtc)),
						E(s.Evidence) + (SignaturePolicy.IsSha256(s.ArtifactSha256) && !string.Equals(s.ArtifactSha256, request.RevisionSha256, StringComparison.OrdinalIgnoreCase) ? "<br/><span style=\"font-family:monospace\">" + E(s.ArtifactSha256) + "</span>" : string.Empty) })
						sb.Append("<td style=\"border:1px solid #bbb;padding:3px 6px;vertical-align:top\">").Append(cell).Append("</td>");
					sb.Append("</tr>");
				}
				sb.Append("</table>");
			}

			sb.Append("<p style=\"margin-top:10px;color:#555\">").Append(L("AuditSealNote")).Append("</p>");
			sb.Append("</div>");
			return sb.ToString();
		}

		/// <summary>The HTML with the audit page appended before &lt;/body&gt; (or at the end).</summary>
		public static string Append(string html, SealHtmlRequest request, string culture)
		{
			var page = Build(request, culture);
			if (string.IsNullOrEmpty(html))
				return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" + page + "</body></html>";

			var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
			return index < 0 ? html + page : html.Substring(0, index) + page + html.Substring(index);
		}
	}

	/// <summary>
	/// Canonical content hashes for subjects that have no stored hash: the subject serialized with sorted properties, minus the
	/// properties that change without changing what was signed (status, approvals, the signature fields themselves).
	/// </summary>
	public static class CanonicalContent
	{
		private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
		{
			DateFormatString = "yyyy-MM-ddTHH:mm:ss",
			DateTimeZoneHandling = DateTimeZoneHandling.Unspecified,
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
			Culture = CultureInfo.InvariantCulture
		});

		/// <summary>Always ignored, at every depth: bookkeeping that never changes the content.</summary>
		private static readonly string[] AlwaysExcluded = { "EditedOn", "EditedByUserId", "ModifiedOn", "UpdatedOn", "UpdatedByUserId", "RowVersion", "IdValue", "IdType", "TableName", "IdName", "IgnoredProperties" };

		public static string Hash(string kind, object subject, params string[] excluded)
		{
			var token = subject == null ? JValue.CreateNull() : JToken.FromObject(subject, Serializer);
			var skip = new HashSet<string>(AlwaysExcluded.Concat(excluded ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);
			var canonical = new JObject { ["kind"] = kind, ["content"] = Normalize(token, skip) };
			return SignaturePolicy.HashArtifact(canonical.ToString(Formatting.None));
		}

		private static JToken Normalize(JToken token, HashSet<string> skip)
		{
			switch (token)
			{
				case JObject obj:
					var sorted = new JObject();
					foreach (var property in obj.Properties().Where(p => !skip.Contains(p.Name)).OrderBy(p => p.Name, StringComparer.Ordinal))
						sorted[property.Name] = Normalize(property.Value, skip);
					return sorted;
				case JArray array:
					return new JArray(array.Select(t => Normalize(t, skip)));
				default:
					return token;
			}
		}
	}
}
