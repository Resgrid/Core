using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.WebCore.Areas.User.Models.Files;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class FilesController : SecureBaseController
	{
		private readonly IAuthorizationService _authorizationService;
		private readonly ICallsService _callsService;

		public FilesController(IAuthorizationService authorizationService, ICallsService callsService)
		{
			_authorizationService = authorizationService;
			_callsService = callsService;
		}

		[HttpGet]
		public IActionResult Upload(int type, string resourceId)
		{
			UploadFileView model = new UploadFileView();
			model.Type = type;
			model.ResourceId = resourceId;

			return View(model);
		}

		[ValidateAntiForgeryToken]
		[HttpPost]
		public async Task<IActionResult> Upload(UploadFileView model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			string FileType = null;
			string FileName = null;
			byte[] Data = null;

			if (fileToUpload == null || fileToUpload.Length <= 0)
				ModelState.AddModelError("fileToUpload", "You must select a file to upload.");
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

				FileType = fileToUpload.ContentType;
				FileName = fileToUpload.FileName;

				var uploadedFile = new byte[fileToUpload.OpenReadStream().Length];
				fileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				Data = uploadedFile;
			}

			if (ModelState.IsValid)
			{

				switch ((FileUploadTypes)model.Type)
				{
					case FileUploadTypes.CallFile:
						CallAttachment callAttachment = new CallAttachment();
						callAttachment.CallId = int.Parse(model.ResourceId);
						callAttachment.CallAttachmentType = (int)CallAttachmentTypes.File;
						callAttachment.Name = model.Name;
						callAttachment.FileName = FileName;
						//callAttachment.FileType = FileType;
						callAttachment.Size = Data.Length;
						callAttachment.Data = Data;
						callAttachment.Timestamp = DateTime.UtcNow;

						if (!await _authorizationService.CanUserEditCallAsync(UserId, callAttachment.CallId))
							Unauthorized();

						await _callsService.SaveCallAttachmentAsync(callAttachment, cancellationToken);

						return RedirectToAction("ViewCall", "Dispatch", new { Area = "User", callId = callAttachment.CallId });
					case FileUploadTypes.CallImage:
						CallAttachment callAttachment2 = new CallAttachment();
						callAttachment2.CallId = int.Parse(model.ResourceId);
						callAttachment2.CallAttachmentType = (int)CallAttachmentTypes.Image;
						callAttachment2.Name = model.Name;
						callAttachment2.FileName = FileName;
						//callAttachment2. = FileType;
						callAttachment2.Size = Data.Length;
						callAttachment2.Data = Data;
						callAttachment2.Timestamp = DateTime.UtcNow;

						if (!await _authorizationService.CanUserEditCallAsync(UserId, callAttachment2.CallId))
							Unauthorized();

						await _callsService.SaveCallAttachmentAsync(callAttachment2, cancellationToken);

						return RedirectToAction("ViewCall", "Dispatch", new { Area = "User", callId = callAttachment2.CallId });

				}
			}

			return View(model);
		}
	}
}
