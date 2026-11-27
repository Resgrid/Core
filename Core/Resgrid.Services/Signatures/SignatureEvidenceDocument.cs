using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// The HTML Resgrid renders for external signatures: the sealed-input document the e-sign platform shows (the subject's
	/// document and a signature page with the statement), and the signature evidence that is sealed when a request completes
	/// (the subject's document when it is still at the signed revision, each signer's statement, drawn signature, printed name and
	/// witness, and the hash of the platform's completed PDF).
	/// </summary>
	public static class SignatureEvidenceDocument
	{
		private const string Style = "<style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#222;margin:24px}h1{font-size:18px;margin:0 0 6px}" +
			"h2{font-size:14px;margin:14px 0 6px;border-bottom:1px solid #999;padding-bottom:2px}table{border-collapse:collapse;width:100%}" +
			"th,td{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}th{background:#eee}.mono{font-family:monospace;word-break:break-all}" +
			".sig{max-height:110px;max-width:320px}.muted{color:#666}</style>";

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		/// <summary>The subject's document followed by a signature page (the platform places the signature fields on that last page).</summary>
		public static string SigningInput(string documentHtml, SignatureRequestRecord request, string culture)
		{
			var signaturePage = new StringBuilder();
			signaturePage.Append("<div style=\"page-break-before:always;font-family:Arial,Helvetica,sans-serif;font-size:12px\">");
			signaturePage.Append("<h2>").Append(E(SignatureTexts.Get("SignaturePageTitle", culture))).Append("</h2>");
			signaturePage.Append("<p>").Append(E(request.StatementText)).Append("</p>");
			signaturePage.Append("<p class=\"mono\" style=\"font-family:monospace\">").Append(E(SignatureTexts.Get("ContentCodeLabel", culture))).Append(' ')
				.Append(E(SignaturePolicy.ShortCode(request.ArtifactSha256))).Append(" · ").Append(E(request.ArtifactSha256)).Append("</p>");
			signaturePage.Append("</div>");

			var body = Body(documentHtml);
			return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + E(request.Title) + "</title>" + Style + "</head><body>" + body + signaturePage + "</body></html>";
		}

		public static string Build(SignatureRequestRecord request, IList<SignatureRequestSigner> signers, IList<SignatureRecord> records,
			IDictionary<int, string> signatureImages, IDictionary<int, string> witnesses, string documentHtml, string providerCompletedSha256, string departmentName)
		{
			var sb = new StringBuilder();
			sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E(request.Title)).Append("</title>").Append(Style).Append("</head><body>");
			sb.Append("<h1>").Append(E(departmentName)).Append(" — ").Append(E(request.Title)).Append("</h1>");
			sb.Append("<p class=\"muted\">").Append(E(((SignatureSubjectKinds)request.SubjectKind).ToString())).Append(" · ").Append(E(request.SubjectId))
				.Append(" · <span class=\"mono\">").Append(E(request.ArtifactSha256)).Append("</span></p>");

			if (!string.IsNullOrWhiteSpace(providerCompletedSha256))
				sb.Append("<p>Completed document SHA-256: <span class=\"mono\">").Append(E(providerCompletedSha256)).Append("</span></p>");

			sb.Append("<h2>Signatures</h2><table><tr><th>Signer</th><th>Statement</th><th>Signature</th><th>Signed (UTC)</th></tr>");
			foreach (var signer in signers ?? new List<SignatureRequestSigner>())
			{
				var record = records?.FirstOrDefault(r => r.SignatureRecordId == signer.SignatureRecordId);
				// S3: a member step is named by its signature's snapshot (or the step label while unsigned) and shows its level.
				var name = signer.IsMember ? record?.SignerNameSnapshot ?? signer.StepLabel : signer.ExternalName;
				var level = record?.Level ?? (signer.IsMember ? (int)SignatureOrdering.StepLevel(signer) : (int)SignatureLevels.External);
				sb.Append("<tr><td>").Append(E(name)).Append("<br/><span class=\"muted\">").Append(E(((SignatureSignerRoles)signer.Role).ToString()))
					.Append(" · L").Append(level.ToString(CultureInfo.InvariantCulture))
					.Append(" · ").Append(E(((SignatureSignerStatuses)signer.Status).ToString())).Append("</span>");
				if (witnesses != null && witnesses.TryGetValue(signer.SignatureRequestSignerId, out var witness) && !string.IsNullOrWhiteSpace(witness))
					sb.Append("<br/><span class=\"muted\">Witness: ").Append(E(witness)).Append("</span>");
				sb.Append("</td><td>").Append(E(record?.StatementText ?? request.StatementText)).Append("</td><td>");
				if (signatureImages != null && signatureImages.TryGetValue(signer.SignatureRequestSignerId, out var image) && image != null &&
					image.StartsWith("data:image/png;base64,", StringComparison.Ordinal) && image.Skip(22).All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '='))
					sb.Append("<img class=\"sig\" alt=\"signature\" src=\"").Append(image).Append("\"/>");
				else if (record?.Method == (int)SignatureMethods.ExternalLink)
					sb.Append("<span class=\"muted\">Signed on the e-sign platform (see the completed document)</span>");
				sb.Append("</td><td>").Append(signer.SignedOn.HasValue ? signer.SignedOn.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "—").Append("</td></tr>");
			}
			sb.Append("</table>");

			if (!string.IsNullOrWhiteSpace(documentHtml))
				sb.Append("<div style=\"page-break-before:always\">").Append(Body(documentHtml)).Append("</div>");

			sb.Append("</body></html>");
			return sb.ToString();
		}

		/// <summary>The inside of a document's &lt;body&gt; (styles in its head are kept inline at the top).</summary>
		private static string Body(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return string.Empty;
			var styles = new StringBuilder();
			var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
			if (headEnd > 0)
			{
				var head = html.Substring(0, headEnd);
				var index = 0;
				while ((index = head.IndexOf("<style", index, StringComparison.OrdinalIgnoreCase)) >= 0)
				{
					var end = head.IndexOf("</style>", index, StringComparison.OrdinalIgnoreCase);
					if (end < 0)
						break;
					styles.Append(head, index, end + 8 - index);
					index = end + 8;
				}
			}

			var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
			var bodyOpenEnd = bodyStart < 0 ? -1 : html.IndexOf('>', bodyStart);
			var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
			var inner = bodyOpenEnd > 0 && bodyEnd > bodyOpenEnd ? html.Substring(bodyOpenEnd + 1, bodyEnd - bodyOpenEnd - 1) : html;
			return styles + inner;
		}
	}

	/// <summary>Records signatures (RmsSignatures, decision 21) in the shared list's shape. Read-only.</summary>
	public static class RmsSignatureProjection
	{
		public static SignatureVerification ToVerification(RmsSignature row)
		{
			var record = new SignatureRecord
			{
				DepartmentId = row.DepartmentId,
				SubjectKind = (int)SignatureSubjectKinds.RecordsDocument,
				SubjectId = row.RecordId,
				RevisionId = row.RevisionId,
				SignerUserId = row.SignerUserId,
				SignerNameSnapshot = row.SignerNameSnapshot,
				SignerRoleSnapshot = row.SignerRoleSnapshot,
				Intent = row.Intent switch
				{
					(int)RmsSignatureIntent.Approval => (int)SignatureIntents.Approve,
					(int)RmsSignatureIntent.Review => (int)SignatureIntents.Review,
					_ => (int)SignatureIntents.Attest
				},
				StatementVersion = row.StatementVersion,
				// An ADP-protected statement is ciphertext here; the projection never reveals it.
				StatementText = row.IsProtected ? null : row.StatementText,
				Level = (int)SignatureLevels.Attest,
				Method = (int)SignatureMethods.Session,
				ArtifactSha256 = row.ArtifactChecksum,
				SignedOn = row.SignedOn,
				IpAddress = row.IpAddress,
				CreatedOn = row.CreatedOn
			};

			// A Records revision is immutable (an amendment is a new revision), so a signature on it stays valid for that revision.
			return new SignatureVerification
			{
				Record = record,
				CurrentSha256 = row.ArtifactChecksum,
				State = string.IsNullOrWhiteSpace(row.ArtifactChecksum) ? SignatureVerificationStates.SubjectMissing : SignatureVerificationStates.Valid
			};
		}
	}
}
