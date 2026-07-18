using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Documents;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Providers;
using Resgrid.Framework;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Services;
using System.Collections.Generic;
using System.Linq;
using NuGet.Packaging;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class DocumentsController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IDocumentsService _documentsService;
		private readonly IEventAggregator _eventAggregator;

		public DocumentsController(IDepartmentsService departmentsService, IDocumentsService documentsService, IEventAggregator eventAggregator)
		{
			_departmentsService = departmentsService;
			_documentsService = documentsService;
			_eventAggregator = eventAggregator;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Documents_View)]
		public async Task<IActionResult> Index(string type, string category)
		{
			var model = new IndexView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.Documents = await _documentsService.GetFilteredDocumentsByDepartmentIdAsync(DepartmentId, type, category);

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				model.Documents = model.Documents.Where(x => !x.AdminsOnly).ToList();

			model.Categories = await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId);

			model.SelectedCategory = category;
			model.SelectedType = type;

			model.UserId = UserId;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_Create)]
		public async Task<IActionResult> NewDocument()
		{
			if (!ClaimsAuthorizationHelper.CanCreateDocument())
				return Unauthorized();

			NewDocumentView model = new NewDocumentView();
			model.Categories = new SelectList(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId), "Name", "Name");

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_View)]
		public async Task<IActionResult> ViewDocument(int documentId)
		{
			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document == null)
				return NotFound();

			if (document.DepartmentId != DepartmentId)
				return Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var canManageDocument = ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || document.UserId == UserId;
			var model = new ViewDocumentView
			{
				Document = document,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				UploadedByName = await UserHelper.GetFullNameForUser(document.UserId),
				DescriptionHtml = StringHelpers.SanitizeHtmlInString(document.Description),
				CanEdit = canManageDocument && ClaimsAuthorizationHelper.CanUpdateDocument(),
				CanDelete = canManageDocument && ClaimsAuthorizationHelper.CanDeleteDocument()
			};

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_View)]
		public async Task<IActionResult> GetDepartmentDocumentCategories()
		{
			return Json(await _documentsService.GetDistinctCategoriesByDepartmentIdAsync(DepartmentId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_View)]
		public async Task<IActionResult> GetDocument(int documentId)
		{
			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document == null)
				return NotFound();

			if (document.DepartmentId != DepartmentId)
				return Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			return new FileContentResult(document.Data, String.IsNullOrWhiteSpace(document.Type) ? "application/octet-stream" : document.Type)
			{
				FileDownloadName = String.IsNullOrWhiteSpace(document.Filename) ? document.Name : document.Filename
			};
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Documents_Delete)]
		public async Task<IActionResult> DeleteDocument(int documentId, CancellationToken cancellationToken)
		{
			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document == null)
				return NotFound();

			if (document.DepartmentId != DepartmentId)
				return Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && document.UserId != UserId)
				return Unauthorized();

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = $"{document.DocumentId} - {document.Name} - {document.Description} - {document.Category} - {document.AdminsOnly}";
			auditEvent.Type = AuditLogTypes.DocumentRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _documentsService.DeleteDocumentAsync(document, cancellationToken);

			return RedirectToAction("Index", "Documents", new { Area = "User" });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Documents_Create)]
		public async Task<IActionResult> NewDocument(NewDocumentView model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.CanCreateDocument())
				return Unauthorized();

			if (fileToUpload == null || fileToUpload.Length <= 0)
				ModelState.AddModelError("fileToUpload", "You must select a document to add.");
			else
			{
				var extenion = FileHelper.GetFileExtensionWithoutDot(fileToUpload.FileName);

				if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" && extenion != "pdf" && extenion != "doc"
					&& extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" && extenion != "odt"
					&& extenion != "xls" && extenion != "xlsx" && extenion != "mp3" && extenion != "m4a" && extenion != "ogg" && extenion != "wav"
					&& extenion != "mp4" && extenion != "m4v" && extenion != "mov" && extenion != "wmv" && extenion != "avi" && extenion != "mpg" && extenion != "txt")
					ModelState.AddModelError("fileToUpload", string.Format("Document type ({0}) is not importable.", extenion));

				if (fileToUpload.Length > 10000000)
					ModelState.AddModelError("fileToUpload", "Document is too large, must be smaller then 10MB.");
			}

			if (ModelState.IsValid)
			{
				Document doc = new Document();
				doc.DepartmentId = DepartmentId;
				doc.UserId = UserId;
				doc.AddedOn = DateTime.UtcNow;
				doc.Name = model.Name;
				doc.Description = StringHelpers.SanitizeHtmlInString(model.Description);

				if (!String.IsNullOrWhiteSpace(model.Category) && model.Category != "None")
					doc.Category = model.Category;
				else
					doc.Category = null;

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					doc.AdminsOnly = bool.Parse(model.AdminOnly);
				else
					doc.AdminsOnly = false;

				doc.Type = String.IsNullOrWhiteSpace(fileToUpload.ContentType) ? "application/octet-stream" : fileToUpload.ContentType;
				doc.Filename = FileHelper.GetSafeFileName(fileToUpload.FileName);

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = doc.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.DocumentAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				using (var uploadStream = fileToUpload.OpenReadStream())
				{
					doc.Data = await FileHelper.ReadAllBytesAsync(uploadStream, cancellationToken);
				}

				doc = await _documentsService.SaveDocumentAsync(doc, cancellationToken);

				_eventAggregator.SendMessage<DocumentAddedEvent>(new DocumentAddedEvent() { DepartmentId = DepartmentId, Document = doc });

				return RedirectToAction("ViewDocument", "Documents", new { Area = "User", documentId = doc.DocumentId });
			}

			model.Categories = new SelectList(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId), "Name", "Name");
			model.Description = StringHelpers.SanitizeHtmlInString(model.Description);
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_Update)]
		public async Task<IActionResult> EditDocument(int documentId)
		{
			EditDocumentView model = new EditDocumentView();

			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document == null)
				return NotFound();

			if (document.DepartmentId != DepartmentId)
				return Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!ClaimsAuthorizationHelper.CanUpdateDocument())
				return Unauthorized();

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && document.UserId != UserId)
				return Unauthorized();

			List<DocumentCategory> noteCategories = new List<DocumentCategory>();
			noteCategories.Add(new DocumentCategory { Name = "None" });
			noteCategories.AddRange(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

			model.Name = document.Name;
			model.Description = StringHelpers.SanitizeHtmlInString(document.Description);
			model.Category = document.Category;
			model.AdminOnly = document.AdminsOnly.ToString();
			model.Document = document;
			model.UserId = UserId;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Documents_Update)]
		public async Task<IActionResult> EditDocument(EditDocumentView model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			var document = await _documentsService.GetDocumentByIdAsync(model.DocumentId);

			if (document == null)
				return NotFound();

			if (document.DepartmentId != DepartmentId)
				return Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (!ClaimsAuthorizationHelper.CanUpdateDocument())
				return Unauthorized();

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && document.UserId != UserId)
				return Unauthorized();

			if (fileToUpload != null && fileToUpload.Length > 0)
			{
				var extenion = FileHelper.GetFileExtensionWithoutDot(fileToUpload.FileName);

				if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" && extenion != "pdf" && extenion != "doc"
					&& extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" && extenion != "odt"
					&& extenion != "xls" && extenion != "xlsx" && extenion != "mp3" && extenion != "m4a" && extenion != "ogg" && extenion != "wav"
					&& extenion != "mp4" && extenion != "m4v" && extenion != "mov" && extenion != "wmv" && extenion != "avi" && extenion != "mpg" && extenion != "txt")
					ModelState.AddModelError("fileToUpload", "Document type (extension) is not importable.");

				if (fileToUpload.Length > 10000000)
					ModelState.AddModelError("fileToUpload", "Document is too large, must be smaller then 10MB.");
			}

			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				auditEvent.Before = $"{document.DocumentId} - {document.Name} - {document.Description} - {document.Category} - {document.AdminsOnly}";

				document.Name = model.Name;
				document.Description = StringHelpers.SanitizeHtmlInString(model.Description);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					document.AdminsOnly = bool.Parse(model.AdminOnly);
				else
					document.AdminsOnly = false;

				if (!String.IsNullOrWhiteSpace(model.Category) && model.Category != "None")
					document.Category = model.Category;
				else
					document.Category = null;

				if (fileToUpload != null && fileToUpload.Length > 0)
				{
					document.Type = String.IsNullOrWhiteSpace(fileToUpload.ContentType) ? "application/octet-stream" : fileToUpload.ContentType;
					document.Filename = FileHelper.GetSafeFileName(fileToUpload.FileName);

					using (var uploadStream = fileToUpload.OpenReadStream())
					{
						document.Data = await FileHelper.ReadAllBytesAsync(uploadStream, cancellationToken);
					}
				}

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = $"{document.DocumentId} - {document.Name} - {document.Description} - {document.Category} - {document.AdminsOnly}";
				auditEvent.Type = AuditLogTypes.DocumentEdited;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _documentsService.SaveDocumentAsync(document, cancellationToken);

				return RedirectToAction("ViewDocument", "Documents", new { Area = "User", documentId = document.DocumentId });
			}

			List<DocumentCategory> noteCategories = new List<DocumentCategory>();
			noteCategories.Add(new DocumentCategory { Name = "None" });
			noteCategories.AddRange(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");
			model.Document = document;
			model.UserId = UserId;
			model.Description = StringHelpers.SanitizeHtmlInString(model.Description);

			return View(model);
		}
	}
}
