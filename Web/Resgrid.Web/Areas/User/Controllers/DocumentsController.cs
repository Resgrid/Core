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
				Unauthorized();

			NewDocumentView model = new NewDocumentView();
			model.Categories = new SelectList(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId), "Name", "Name");

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_View)]
		public async Task<IActionResult> GetDepartmentDocumentCategories()
		{
			return Json(await _documentsService.GetDistinctCategoriesByDepartmentIdAsync(DepartmentId));
		}

		[Authorize(Policy = ResgridResources.Documents_View)]
		public async Task<FileResult> GetDocument(int documentId)
		{
			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document.DepartmentId != DepartmentId)
				Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			return new FileContentResult(document.Data, document.Type)
			{
				FileDownloadName = document.Filename
			};
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_Delete)]
		public async Task<IActionResult> DeleteDocument(int documentId, CancellationToken cancellationToken)
		{
			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document.DepartmentId != DepartmentId)
				Unauthorized();

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || document.UserId != UserId)
				Unauthorized();

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
				Unauthorized();

			if (fileToUpload == null || fileToUpload.Length <= 0)
				ModelState.AddModelError("fileToUpload", "You must select a document to add.");
			else
			{
				var extenion = fileToUpload.FileName.Substring(fileToUpload.FileName.IndexOf(char.Parse(".")) + 1,
					fileToUpload.FileName.Length - fileToUpload.FileName.IndexOf(char.Parse(".")) - 1);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

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
				doc.Description = model.Description;

				if (!String.IsNullOrWhiteSpace(model.Category) && model.Category != "None")
					doc.Category = model.Category;
				else
					doc.Category = null;

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					doc.AdminsOnly = bool.Parse(model.AdminOnly);
				else
					doc.AdminsOnly = false;

				doc.Type = fileToUpload.ContentType;
				doc.Filename = fileToUpload.FileName;

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

				byte[] uploadedFile = new byte[fileToUpload.OpenReadStream().Length];
				await fileToUpload.OpenReadStream().ReadAsync(uploadedFile, 0, uploadedFile.Length);

				doc.Data = uploadedFile;

				await _documentsService.SaveDocumentAsync(doc, cancellationToken);

				_eventAggregator.SendMessage<DocumentAddedEvent>(new DocumentAddedEvent() { DepartmentId = DepartmentId, Document = doc });

				return RedirectToAction("Index", "Documents", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_Update)]
		public async Task<IActionResult> EditDocument(int documentId)
		{
			EditDocumentView model = new EditDocumentView();

			var document = await _documentsService.GetDocumentByIdAsync(documentId);

			if (document.DepartmentId != DepartmentId)
				Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			if (!ClaimsAuthorizationHelper.CanCreateDocument())
				Unauthorized();

			List<DocumentCategory> noteCategories = new List<DocumentCategory>();
			noteCategories.Add(new DocumentCategory { Name = "None" });
			noteCategories.AddRange(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

			model.Name = document.Name;
			model.Description = document.Description;
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

			if (document.DepartmentId != DepartmentId)
				Unauthorized();

			if (document.AdminsOnly && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				Unauthorized();

			if (!ClaimsAuthorizationHelper.CanCreateDocument())
				Unauthorized();

			if (fileToUpload != null && fileToUpload.Length > 0)
			{
				var extenion = fileToUpload.FileName.Substring(fileToUpload.FileName.IndexOf(char.Parse(".")) + 1,
					fileToUpload.FileName.Length - fileToUpload.FileName.IndexOf(char.Parse(".")) - 1);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

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
				document.Description = model.Description;

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
					document.Type = fileToUpload.ContentType;
					document.Filename = fileToUpload.FileName;

					byte[] uploadedFile = new byte[fileToUpload.OpenReadStream().Length];
					await fileToUpload.OpenReadStream().ReadAsync(uploadedFile, 0, uploadedFile.Length);

					document.Data = uploadedFile;
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

				return RedirectToAction("Index", "Documents", new { Area = "User" });
			}

			List<DocumentCategory> noteCategories = new List<DocumentCategory>();
			noteCategories.Add(new DocumentCategory { Name = "None" });
			noteCategories.AddRange(await _documentsService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

			return View(model);
		}
	}
}
