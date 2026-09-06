using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordDocumentsController : SecureBaseController
	{
		private readonly IRecordsDocumentService _documents;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsService _records;
		public RecordDocumentsController(IRecordsDocumentService documents, IRecordsCutoverService cutover, IRecordsService records)
		{ _documents = documents; _cutover = cutover; _records = records; }

		[HttpGet]
		public async Task<IActionResult> Revision(string id, RmsRecordKind kind, string revisionId = null)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try
			{
				var document = await _documents.GetAsync(DepartmentId, UserId, id, kind, revisionId);
				if (document == null) return NotFound();
				var html = await _documents.RenderHtmlAsync(DepartmentId, UserId, document);
				if (kind == RmsRecordKind.IncidentReport)
				{
					var links = new StringBuilder("<nav aria-label=\"Report files\"><h2>Revision attachments</h2>");
					foreach (var attachment in (JObject.Parse(document.ContentJson)["Attachments"] as JArray ?? new JArray()).OfType<JObject>())
					{
						var attachmentId = (string)attachment["RmsRecordAttachmentId"]; if (attachmentId == null) continue;
						links.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(Url.Action("Attachment", "IncidentReports", new { area = "User", id, attachmentId, revisionId = document.RevisionId }))).Append("\">")
							.Append(WebUtility.HtmlEncode((string)attachment["FileName"])).Append("</a></p>");
					}
					html = html.Replace("</body>", links.Append("</nav></body>").ToString());
				}
				await Audit(document, RmsAccessAuditAction.Read, "Departmental revision viewed");
				await RequireCurrentAsync(document);
				Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'; base-uri 'none'";
				Response.Headers["Cache-Control"] = "no-store";
				return Content(html, "text/html", Encoding.UTF8);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: 409); }
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> Export(string id, RmsRecordKind kind, string revisionId = null, string format = "pdf")
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			if (format != "pdf" && format != "json" && format != "csv") return BadRequest("Choose PDF, JSON, or CSV.");
			try
			{
				var document = await _documents.GetAsync(DepartmentId, UserId, id, kind, revisionId, true);
				if (document == null) return NotFound();
				byte[] bytes; string contentType;
				if (format == "pdf") { bytes = await _documents.RenderPdfAsync(DepartmentId, UserId, document); contentType = "application/pdf"; }
				else if (format == "json") { bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { document.Format, document.RecordId, document.RecordKind, document.RecordNumber, document.RevisionId, document.RevisionNumber, document.OriginalChecksum, document.ContentChecksum, document.FinalizedOn, document.AttestedBy, document.AttestationVersion, document.WithheldFields, Content = JObject.Parse(document.ContentJson) }, Formatting.Indented)); contentType = "application/json"; }
				else { bytes = Encoding.UTF8.GetBytes(Csv(document)); contentType = "text/csv"; }
				await Audit(document, RmsAccessAuditAction.Export, "Departmental " + format + " export");
				await RequireCurrentAsync(document, true);
				Response.Headers["Cache-Control"] = "no-store";
				var name = document.RecordNumber ?? "record"; foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
				return File(bytes, contentType, name + "-revision-" + document.RevisionNumber + "." + format);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: 409); }
		}

		[HttpGet]
		public async Task<IActionResult> Diff(string id, RmsRecordKind kind, string from, string to)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try
			{
				var left = await _documents.GetAsync(DepartmentId, UserId, id, kind, from);
				var right = await _documents.GetAsync(DepartmentId, UserId, id, kind, to);
				if (left == null || right == null) return NotFound();
				await Audit(right, RmsAccessAuditAction.Read, "Compare departmental revisions " + left.RevisionNumber + " and " + right.RevisionNumber);
				ViewData["From"] = left.RevisionNumber; ViewData["To"] = right.RevisionNumber;
				ViewData["RecordId"] = id; ViewData["Kind"] = kind; ViewData["FromId"] = from; ViewData["ToId"] = to;
				ViewData["Withheld"] = left.WithheldFields.Count > 0 || right.WithheldFields.Count > 0;
				Response.Headers["Cache-Control"] = "no-store";
				return View(await _documents.DiffAsync(DepartmentId, UserId, left, right));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: 409); }
		}
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> PrintDiff(string id, RmsRecordKind kind, string from, string to)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try
			{
				var left = await _documents.GetAsync(DepartmentId, UserId, id, kind, from, true);
				var right = await _documents.GetAsync(DepartmentId, UserId, id, kind, to, true);
				if (left == null || right == null) return NotFound();
				var pdf = await _documents.RenderDiffPdfAsync(DepartmentId, UserId, id, kind, from, to);
				await Audit(right, RmsAccessAuditAction.Export, "Departmental PDF revision comparison");
				await RequireCurrentAsync(left, true); await RequireCurrentAsync(right, true);
				Response.Headers["Cache-Control"] = "no-store";
				return File(pdf, "application/pdf", "record-changes-r" + left.RevisionNumber + "-r" + right.RevisionNumber + ".pdf");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { return Problem(ex.Message, statusCode: 409); }
		}
		private async Task RequireCurrentAsync(RecordDocument document, bool exporting = false)
		{
			var current = await _documents.GetAsync(DepartmentId, UserId, document.RecordId, document.RecordKind, document.RevisionId, exporting);
			if (current == null || current.ContentChecksum != document.ContentChecksum) throw new UnauthorizedAccessException("Record access changed; reload the revision.");
		}
		private Task Audit(RecordDocument document, RmsAccessAuditAction action, string purpose) => _records.RecordAccessAsync(DepartmentId, UserId, document.RecordId, document.RevisionId, action, purpose, IpAddressHelper.GetRequestIP(Request, true));
		public static string Csv(RecordDocument document)
		{
			string Cell(string value) { value ??= ""; if (value.TrimStart().StartsWith("=") || value.TrimStart().StartsWith("+") || value.TrimStart().StartsWith("-") || value.TrimStart().StartsWith("@") || value.StartsWith("\t") || value.StartsWith("\r")) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
			var csv = new StringBuilder("Record,Revision,Field,Value\r\n");
			foreach (var value in JObject.Parse(document.ContentJson).Descendants()) if (value is JValue scalar)
				csv.Append(Cell(document.RecordNumber)).Append(',').Append(document.RevisionNumber).Append(',').Append(Cell(value.Path)).Append(',').Append(Cell(scalar.ToString())).Append("\r\n");
			return csv.ToString();
		}
	}
}
