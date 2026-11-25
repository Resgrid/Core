using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// L3 sealed documents (e-Signature S1): download a department's sealed document, "Verify a document" for officers (upload a
	/// PDF: the DSS validation result and whether Resgrid issued exactly these bytes), and the protocol version PDF (P1's
	/// version, sealed when sealing is configured; the version's readers only).
	/// </summary>
	[Area("User")]
	[Authorize]
	public class SealedDocumentsController : SecureBaseController
	{
		public const long MaxVerifyBytes = 25L * 1024 * 1024;

		private readonly IDocumentSealingService _sealing;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IProtocolLibraryService _library;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Signatures> _text;

		public SealedDocumentsController(IDocumentSealingService sealing, IProtocolAccessService protocolAccess, IProtocolLibraryService library,
			IDepartmentsService departments, IStringLocalizer<Signatures> text)
		{
			_sealing = sealing;
			_protocolAccess = protocolAccess;
			_library = library;
			_departments = departments;
			_text = text;
		}

		/// <summary>A sealed (or as-is stored) document of this department. Subject access is the page that links it.</summary>
		[HttpGet]
		public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
		{
			var document = await _sealing.GetByIdAsync(DepartmentId, id);
			if (document == null)
				return NotFound();
			if (!IsAdmin() && !string.Equals(document.CreatedByUserId, UserId, StringComparison.OrdinalIgnoreCase) && !await SubjectReadableAsync(document))
				return NotFound();

			var bytes = await _sealing.OpenAsync(DepartmentId, id, cancellationToken);
			if (bytes == null)
				return NotFound();

			Response.Headers["Cache-Control"] = "no-store";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			var pdf = SealingPolicy.LooksLikePdf(bytes);
			return File(bytes, pdf ? "application/pdf" : "application/json", document.FileName ?? (pdf ? "sealed.pdf" : "audit.json"));
		}

		[HttpGet]
		public IActionResult Verify()
		{
			return View(new VerifyDocumentView { Availability = _sealing.GetAvailability() });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequestSizeLimit(MaxVerifyBytes + 64 * 1024)]
		public async Task<IActionResult> Verify(IFormFile file, CancellationToken cancellationToken)
		{
			var view = new VerifyDocumentView { Availability = _sealing.GetAvailability(), Submitted = true, FileName = file?.FileName };
			if (file == null || file.Length == 0 || file.Length > MaxVerifyBytes)
			{
				view.Message = _text["VerifyFileRequired"].Value;
				return View(view);
			}

			byte[] bytes;
			using (var buffer = new MemoryStream())
			{
				await file.CopyToAsync(buffer, cancellationToken);
				bytes = buffer.ToArray();
			}

			if (!SealingPolicy.LooksLikePdf(bytes))
			{
				view.Message = _text["VerifyNotPdf"].Value;
				return View(view);
			}

			view.Result = await _sealing.VerifyAsync(DepartmentId, bytes, cancellationToken);
			return View(view);
		}

		/// <summary>A protocol version as a PDF (P1 had no PDF export): sealed at L3 when sealing is configured, else "sealing pending".</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ProtocolVersion(int protocolId, int versionId, CancellationToken cancellationToken)
		{
			var protocol = await _protocolAccess.GetReadableAsync(DepartmentId, UserId, protocolId);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return NotFound();

			var version = await _library.GetVersionAsync(DepartmentId, versionId);
			if (version == null || version.DepartmentId != DepartmentId || version.DispatchProtocolId != protocolId)
				return NotFound();

			var reader = await _protocolAccess.GetReaderAsync(DepartmentId, UserId);
			var audience = await _library.GetAudienceAsync(DepartmentId, protocolId);
			if (!ProtocolAccessRules.CanReadVersion(reader, protocol, audience, version))
				return NotFound();

			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			var settings = await _library.GetSettingsAsync(DepartmentId);
			var keepDays = settings?.KeepDaysFor(protocol.Kind);
			var hash = SignaturePolicy.IsSha256(version.ContentHash) ? version.ContentHash : ProtocolContentHash.Compute(version.ContentHtml);
			var label = version.Major.ToString(CultureInfo.InvariantCulture) + "." + version.Minor.ToString(CultureInfo.InvariantCulture);

			try
			{
				var outcome = await _sealing.SealHtmlAsync(new SealHtmlRequest
				{
					DepartmentId = DepartmentId,
					SubjectKind = SignatureSubjectKinds.ProtocolVersion,
					SubjectId = version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture),
					RevisionId = label,
					RevisionSha256 = hash,
					Title = (protocol.Code + " " + protocol.Name).Trim() + " v" + label,
					FileName = "protocol-" + protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture) + "-v" + label + ".pdf",
					Html = ProtocolVersionHtml(protocol, version, label, department?.Name),
					PageSize = PageSize(),
					RetentionYears = keepDays.HasValue ? (int)Math.Ceiling(keepDays.Value / 365.0) : null,
					DepartmentName = department?.Name,
					Culture = CultureInfo.CurrentUICulture.Name,
					RequestedByUserId = UserId
				}, cancellationToken);

				return SealedFile(outcome);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A protocol version PDF could not be produced.");
				return Problem(_text["PdfFailed"].Value, statusCode: 500);
			}
		}

		/// <summary>The sealed bytes, or the unsealed rendering with the "sealing pending" header.</summary>
		[NonAction]
		public IActionResult SealedFile(SealOutcome outcome)
		{
			Response.Headers["Cache-Control"] = "no-store";
			Response.Headers["X-Resgrid-Seal"] = outcome.Sealed ? "sealed" : "pending";
			return File(outcome.Bytes, "application/pdf", outcome.Document?.FileName ?? "document.pdf");
		}

		private static string ProtocolVersionHtml(DispatchProtocol protocol, ProtocolVersion version, string label, string departmentName)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			var sb = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#222;margin:24px}" +
				"h1{font-size:18px;margin:0 0 4px}.muted{color:#666}table{border-collapse:collapse}td,th{border:1px solid #bbb;padding:4px 6px}</style></head><body>");
			sb.Append("<h1>").Append(E(protocol.Code)).Append(' ').Append(E(protocol.Name)).Append("</h1>");
			sb.Append("<p class=\"muted\">").Append(E(departmentName)).Append(" · v").Append(E(label));
			if (version.EffectiveOn.HasValue)
				sb.Append(" · ").Append(version.EffectiveOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			sb.Append(" · <span style=\"font-family:monospace\">").Append(E(SignaturePolicy.ShortCode(version.ContentHash))).Append("</span></p>");
			// Stored protocol HTML is sanitized on save; it is sanitized again for output (stored-html-rendering), and the PDF
			// renderer runs with JavaScript and local file access disabled.
			// P3: a block version keeps its tables and block anchors (the structured allow-list), so a medication table is never dropped.
			sb.Append("<div>").Append(ProtocolContentDocument.DisplayHtml(version.ContentHtml ?? string.Empty)).Append("</div>");
			if (!string.IsNullOrWhiteSpace(version.ChangeSummary))
				sb.Append("<h2>Change summary</h2><p>").Append(E(version.ChangeSummary)).Append("</p>");
			sb.Append("</body></html>");
			return sb.ToString();
		}

		private async Task<bool> SubjectReadableAsync(SealedDocument document)
		{
			// Protocol versions: the version's readers. Other subjects are reached from their module pages, which hold the
			// member's own rule; outside those, a document is the requester's and the department admins'.
			if (document.SubjectKind != (int)SignatureSubjectKinds.ProtocolVersion || !int.TryParse(document.SubjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var versionId))
				return false;
			var version = await _library.GetVersionAsync(DepartmentId, versionId);
			if (version == null)
				return false;
			var protocol = await _protocolAccess.GetReadableAsync(DepartmentId, UserId, version.DispatchProtocolId);
			if (protocol == null)
				return false;
			return ProtocolAccessRules.CanReadVersion(await _protocolAccess.GetReaderAsync(DepartmentId, UserId), protocol, await _library.GetAudienceAsync(DepartmentId, protocol.DispatchProtocolId), version);
		}

		private static bool IsAdmin() => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		private static string PageSize() =>
			string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4";
	}
}
