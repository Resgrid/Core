using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Documents;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Providers;

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
			model.Categories = await _documentsService.GetDistinctCategoriesByDepartmentIdAsync(DepartmentId);

			model.SelectedCategory = category;
			model.SelectedType = type;

			model.UserId = UserId;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Documents_Create)]
		public async Task<IActionResult> NewDocument()
		{
			NewDocumentView model = new NewDocumentView();

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

			await _documentsService.DeleteDocumentAsync(document, cancellationToken);

			return RedirectToAction("Index", "Documents", new { Area = "User" });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Documents_Create)]
		public async Task<IActionResult> NewDocument(NewDocumentView model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			//file = Request.Files["fileToUpload"];

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

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					doc.AdminsOnly = bool.Parse(model.AdminOnly);
				else
					doc.AdminsOnly = false;

				doc.Type = fileToUpload.ContentType;
				doc.Category = model.Category;
				doc.Filename = fileToUpload.FileName;

				byte[] uploadedFile = new byte[fileToUpload.OpenReadStream().Length];
				fileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

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
				document.Name = model.Name;
				document.Description = model.Description;

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					document.AdminsOnly = bool.Parse(model.AdminOnly);
				else
					document.AdminsOnly = false;

				document.Category = model.Category;

				if (fileToUpload != null && fileToUpload.Length > 0)
				{
					document.Type = fileToUpload.ContentType;
					document.Filename = fileToUpload.FileName;

					byte[] uploadedFile = new byte[fileToUpload.OpenReadStream().Length];
					fileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					document.Data = uploadedFile;
				}

				await _documentsService.SaveDocumentAsync(document, cancellationToken);

				return RedirectToAction("Index", "Documents", new { Area = "User" });
			}

			return View(model);
		}
	}
}
