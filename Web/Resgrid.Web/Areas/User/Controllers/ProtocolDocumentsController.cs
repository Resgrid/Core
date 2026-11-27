using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.WebCore.Areas.User.Models.Protocols;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Document-backed protocols (P1 R-P5): a new protocol from one PDF or DOCX, a new upload as the next version, the bulk upload of a
	/// binder (many files or a zip, folders as categories) with a per-file result list, and the one endpoint the primary file leaves
	/// through (inline PDF for the reading view, download otherwise), behind the per-version read rule (R-P4).
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolDocumentsController : SecureBaseController
	{
		/// <summary>The request a bulk upload may send: the per-file cap times a binder, bounded.</summary>
		public const long BulkRequestLimit = 300L * 1024 * 1024;

		private readonly IProtocolAccessService _access;
		private readonly IProtocolDocumentService _documents;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolsService _protocolsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;

		public ProtocolDocumentsController(IProtocolAccessService access, IProtocolDocumentService documents, IProtocolLibraryService library, IProtocolsService protocolsService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer)
		{
			_access = access;
			_documents = documents;
			_library = library;
			_protocolsService = protocolsService;
			_localizer = localizer;
		}

		private string Text(string key) => _localizer?[key]?.Value ?? key;

		private Task<ProtocolReader> ReaderAsync() => _access.GetReaderAsync(DepartmentId, UserId);

		private void Shell(ProtocolReader reader, string section)
		{
			ViewData["Section"] = section;
			ViewData["CanCreate"] = reader.MayAuthor;
			ViewData["IsManager"] = reader.IsManager;
			ViewData["IsAdmin"] = reader.IsDepartmentAdmin;
			ViewData["ManagesAcknowledgments"] = reader.IsDepartmentAdmin || reader.CanManageAcknowledgments || reader.CanApprove;
			ViewData["CanIssueBulletins"] = reader.MayApprove;
		}

		private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
		{
			if (file == null || file.Length == 0 || file.Length > ProtocolDocumentRules.MaxFileBytes)
				return null;
			using var stream = new MemoryStream();
			await file.CopyToAsync(stream, cancellationToken);
			return stream.ToArray();
		}

		#region One file

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New(int? kind = null)
		{
			var reader = await ReaderAsync();
			if (!reader.MayAuthor)
				return Unauthorized();

			Shell(reader, "BulkUpload");
			return View(new ProtocolDocumentUploadModel
			{
				Kind = kind.HasValue && ProtocolKindRules.IsAuthorable(kind.Value) ? kind.Value : (int)ProtocolKinds.Policy,
				Categories = await _library.GetCategoriesAsync(DepartmentId)
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		[RequestSizeLimit(ProtocolDocumentRules.MaxFileBytes + 1024 * 1024)]
		public async Task<IActionResult> New(ProtocolDocumentUploadModel model, IFormFile file, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			var data = await ReadAsync(file, cancellationToken);
			var result = await _documents.CreateFromFileAsync(reader, new ProtocolDocumentCreateRequest
			{
				Kind = model.Kind,
				CategoryId = model.CategoryId,
				Title = model.Title,
				FileName = file?.FileName,
				Data = data,
				RequiresAcknowledgment = model.RequiresAcknowledgment
			}, cancellationToken);

			if (result.Succeeded)
			{
				TempData["ProtocolMessage"] = Text("DocumentProtocolCreatedMessage");
				return RedirectToAction("View", "Protocols", new { area = "User", id = result.Protocol.DispatchProtocolId });
			}

			if (result.Error == ProtocolLifecycleErrors.NotAuthorized)
				return Unauthorized();

			Shell(reader, "BulkUpload");
			model.Categories = await _library.GetCategoriesAsync(DepartmentId);
			model.Error = Text("LifecycleError" + result.Error);
			return View(model);
		}

		/// <summary>A new upload is a new version: the file goes into the protocol's draft (started from the version in force).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Upload(int id)
		{
			var reader = await ReaderAsync();
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			if (protocol == null || protocol.DepartmentId != DepartmentId || protocol.IsDeleted || protocol.IsBulletin)
				return NotFound();
			if (!reader.MayAuthor)
				return Unauthorized();

			Shell(reader, "Library");
			var current = protocol.CurrentVersionId.HasValue ? await _library.GetVersionAsync(DepartmentId, protocol.CurrentVersionId.Value) : null;
			return View(new ProtocolDocumentVersionModel
			{
				Protocol = protocol,
				Current = current,
				Draft = await _library.GetOpenDraftAsync(DepartmentId, id),
				CurrentFile = current != null && current.IsDocument ? (await _documents.GetDocumentViewAsync(DepartmentId, current.ProtocolVersionId)).File : null
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		[RequestSizeLimit(ProtocolDocumentRules.MaxFileBytes + 1024 * 1024)]
		public async Task<IActionResult> Upload(int id, IFormFile file, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			var data = await ReadAsync(file, cancellationToken);
			var result = await _documents.UploadToDraftAsync(reader, id, file?.FileName, data, cancellationToken);
			if (result.Succeeded)
			{
				TempData["ProtocolMessage"] = Text("DocumentVersionUploadedMessage");
				return RedirectToAction("Edit", "Protocols", new { area = "User", id });
			}

			if (result.Error == ProtocolLifecycleErrors.NotAuthorized)
				return Unauthorized();
			if (result.Error == ProtocolLifecycleErrors.NotFound)
				return NotFound();

			TempData["ProtocolError"] = Text("LifecycleError" + result.Error);
			return RedirectToAction("Upload", new { id });
		}

		/// <summary>
		/// The primary file of a document-backed version: inline for a PDF (the reading view's browser viewer), as a download otherwise
		/// or when asked. Only for a reader who may read that version (R-P4); the bytes never leave through any other path.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Document(int versionId, bool download = false)
		{
			var file = await _documents.GetReadableFileAsync(await ReaderAsync(), versionId);
			if (file?.Data == null)
				return NotFound();

			Response.Headers["X-Content-Type-Options"] = "nosniff";
			var name = ProtocolDocumentRules.SafeFileName(file.FileName);
			var isPdf = string.Equals(file.ContentType, ProtocolDocumentRules.PdfContentType, StringComparison.OrdinalIgnoreCase);
			if (isPdf && !download)
			{
				// Inline in the browser's own viewer, framed only by this site.
				Response.Headers["Content-Disposition"] = new System.Net.Mime.ContentDisposition { Inline = true, FileName = name }.ToString();
				// Framed only by this site; no sandbox directive (it stops the browsers' built-in PDF viewers).
				Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
				Response.Headers["Cache-Control"] = "private, no-store";
				return File(file.Data, ProtocolDocumentRules.PdfContentType);
			}

			return File(file.Data, isPdf ? ProtocolDocumentRules.PdfContentType : ProtocolDocumentRules.DocxContentType, name);
		}

		#endregion One file

		#region Bulk upload

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> BulkUpload()
		{
			var reader = await ReaderAsync();
			if (!reader.MayAuthor)
				return Unauthorized();

			Shell(reader, "BulkUpload");
			return View(new ProtocolBulkUploadModel { Categories = await _library.GetCategoriesAsync(DepartmentId) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		[RequestSizeLimit(BulkRequestLimit)]
		[RequestFormLimits(MultipartBodyLengthLimit = BulkRequestLimit)]
		public async Task<IActionResult> BulkUpload(ProtocolBulkUploadModel model, List<IFormFile> files, CancellationToken cancellationToken = default)
		{
			var reader = await ReaderAsync();
			if (!reader.MayAuthor)
				return Unauthorized();

			var uploads = new List<ProtocolUploadFile>();
			foreach (var file in (files ?? new List<IFormFile>()).Where(f => f != null && f.Length > 0).Take(ProtocolDocumentRules.MaxBulkFiles))
			{
				var isZip = string.Equals(Path.GetExtension(file.FileName ?? string.Empty), ".zip", StringComparison.OrdinalIgnoreCase);
				if (isZip)
				{
					if (file.Length > BulkRequestLimit)
						continue;
					using var stream = new MemoryStream();
					await file.CopyToAsync(stream, cancellationToken);
					uploads.AddRange(_documents.ReadZip(stream.ToArray()));
					continue;
				}

				// A folder upload keeps the browser's relative path in the file name (webkitdirectory): it becomes the folder.
				var relative = (file.FileName ?? string.Empty).Replace('\\', '/');
				var folder = relative.Contains('/') ? relative.Substring(0, relative.LastIndexOf('/')) : null;
				uploads.Add(new ProtocolUploadFile { FileName = Path.GetFileName(relative), Folder = folder, Data = await ReadAsync(file, cancellationToken) });
			}

			Shell(reader, "BulkUpload");
			model.Categories = await _library.GetCategoriesAsync(DepartmentId);
			if (uploads.Count == 0)
			{
				model.Error = Text("BulkUploadNoFiles");
				return View(model);
			}

			var kind = ProtocolKindRules.IsAuthorable(model.Kind) ? model.Kind : (int)ProtocolKinds.Sog;
			model.Results = await _documents.BulkUploadAsync(reader, kind, model.CategoryId, model.FoldersAsCategories, uploads.Take(ProtocolDocumentRules.MaxBulkFiles).ToList(), cancellationToken);
			model.Categories = await _library.GetCategoriesAsync(DepartmentId);
			return View(model);
		}

		#endregion Bulk upload
	}
}
