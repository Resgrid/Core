using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ProtocolsController : SecureBaseController
	{
		private readonly IProtocolsService _protocolsService;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;

		public ProtocolsController(IProtocolsService protocolsService, ICallsService callsService, IAuthorizationService authorizationService, IDepartmentsService departmentsService)
		{
			_protocolsService = protocolsService;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Index()
		{
			var model = new ProtocolIndexModel();
			model.Protocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New()
		{
			var model = new NewProtocolModel();
			model.Protocol = new DispatchProtocol();

			var priorites = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);
			model.CallPriorities = new SelectList(priorites, "DepartmentCallPriorityId", "Name", priorites.FirstOrDefault(x => x.IsDefault));

			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(await _callsService.GetCallTypesForDepartmentAsync(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			model.TriggerTypes = model.TriggerTypesEnum.ToSelectList();
			model.Protocol.CreatedByUserId = UserId;
			model.Protocol.UpdatedByUserId = UserId;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New(NewProtocolModel model, IFormCollection form, ICollection<IFormFile> attachments)
		{
			if (attachments != null)
			{
				model.Protocol.Attachments = new Collection<DispatchProtocolAttachment>();
				foreach (var file in attachments)
				{
					if (file != null && file.Length > 0)
					{
						var extenion = file.FileName.Substring(file.FileName.IndexOf(char.Parse(".")) + 1,
							file.FileName.Length - file.FileName.IndexOf(char.Parse(".")) - 1);

						if (!String.IsNullOrWhiteSpace(extenion))
							extenion = extenion.ToLower();

						if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" &&
							extenion != "pdf" && extenion != "doc"
							&& extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" &&
							extenion != "odt"
							&& extenion != "xls" && extenion != "xlsx" && extenion != "txt" && extenion != "mpg" && extenion != "avi" &&
							extenion != "mpeg")
							ModelState.AddModelError("fileToUpload", string.Format("File type ({0}) is not importable.", extenion));

						if (file.Length > 30000000)
							ModelState.AddModelError("fileToUpload", "Attachment is too large, must be smaller then 30MB.");

						var attachment = new DispatchProtocolAttachment();
						attachment.FileType = file.ContentType;
						attachment.FileName = file.FileName;

						var uploadedFile = new byte[file.OpenReadStream().Length];
						file.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

						attachment.Data = uploadedFile;
						model.Protocol.Attachments.Add(attachment);
					}
				}
			}

			model.Protocol.CreatedByUserId = UserId;
			model.Protocol.UpdatedByUserId = UserId;

			if (ModelState.IsValid)
			{
				List<int> triggers = (from object key in form.Keys
									  where key.ToString().StartsWith("triggerType_")
									  select int.Parse(key.ToString().Replace("triggerType_", ""))).ToList();

				if (triggers.Count > 0)
					model.Protocol.Triggers = new Collection<DispatchProtocolTrigger>();

				model.Protocol.DepartmentId = DepartmentId;
				model.Protocol.CreatedOn = DateTime.UtcNow;
				model.Protocol.CreatedByUserId = UserId;
				model.Protocol.UpdatedOn = DateTime.UtcNow;
				model.Protocol.UpdatedByUserId = UserId;
				model.Protocol.Code = model.Protocol.Code.ToUpper();

				foreach (var i in triggers)
				{
					if (form.ContainsKey("triggerType_" + i))
					{
						var triggerType = int.Parse(form["triggerType_" + i]);
						var triggerStartsOn = form["triggerStartsOn_" + i];
						var triggerEndsOn = form["triggerEndsOn_" + i];
						var triggerCallPriotity = int.Parse(form["triggerCallPriority_" + i]);
						var triggerCallType = form["triggerCallType_" + i];

						var trigger = new DispatchProtocolTrigger();
						trigger.Type = triggerType;

						if (!String.IsNullOrWhiteSpace(triggerStartsOn))
							trigger.StartsOn = DateTime.Parse(triggerStartsOn);

						if (!String.IsNullOrWhiteSpace(triggerEndsOn))
							trigger.EndsOn = DateTime.Parse(triggerEndsOn);

						trigger.Priority = triggerType;
						trigger.CallType = triggerCallType;

						model.Protocol.Triggers.Add(trigger);
					}
				}

				List<int> questions = (from object key in form.Keys where key.ToString().StartsWith("question_") select int.Parse(key.ToString().Replace("question_", ""))).ToList();

				if (questions.Count > 0)
					model.Protocol.Questions = new Collection<DispatchProtocolQuestion>();

				foreach (var i in questions)
				{
					if (form.ContainsKey("question_" + i))
					{
						var questionText = form["question_" + i];
						var question = new DispatchProtocolQuestion();
						question.Question = questionText;

						List<int> answers = (from object key in form.Keys where key.ToString().StartsWith("answerForQuestion_" + i + "_") select int.Parse(key.ToString().Replace("answerForQuestion_" + i + "_", ""))).ToList();

						if (answers.Count > 0)
							question.Answers = new Collection<DispatchProtocolQuestionAnswer>();

						foreach (var answer in answers)
						{
							var trainingQuestionAnswer = new DispatchProtocolQuestionAnswer();
							var answerForQuestion = form["answerForQuestion_" + i + "_" + answer];

							var weight = form["weightForAnswer_" + i + "_" + answer];
							trainingQuestionAnswer.Answer = answerForQuestion;

							if (!string.IsNullOrWhiteSpace(weight))
							{
								trainingQuestionAnswer.Weight = int.Parse(weight);
							}

							question.Answers.Add(trainingQuestionAnswer);
						}

						model.Protocol.Questions.Add(question);
					}
				}


				await _protocolsService.SaveProtocolAsync(model.Protocol);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Delete)]
		public async Task<IActionResult> Delete(int id)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				Unauthorized();

			await _protocolsService.DeleteProtocol(id);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocol(int id)
		{
			var template = await _protocolsService.GetProtocolByIdAsync(id);

			return Json(template);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> View(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				Unauthorized();

			var model = new ViewProtocolModel();
			model.Protocol = await _protocolsService.GetProtocolByIdAsync(id);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocolsForPrioType(int priority, string type)
		{
			var protocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId);

			var call = new Call();
			call.Type = type;
			call.Priority = priority;

			var activeProtocols = _protocolsService.ProcessTriggers(protocols, call);

			return Json(Convert(activeProtocols));
		}


		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<FileResult> GetProtocolAttachment(int protocolAttachmentId)
		{
			var attachment = await _protocolsService.GetAttachmentByIdAsync(protocolAttachmentId);

			if (attachment != null)
			{
				if (!await _authorizationService.CanUserViewProtocolAsync(UserId, attachment.DispatchProtocolId))
					Unauthorized();

				return new FileContentResult(attachment.Data, attachment.FileType)
				{
					FileDownloadName = attachment.FileName
				};
			}

			return null;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetTextForProtocol(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				Unauthorized();


			var protocol = await _protocolsService.GetProtocolByIdAsync(id);

			var protocolText = "No Protocol Text Present";

			if (!String.IsNullOrWhiteSpace(protocol.ProtocolText))
				protocolText = protocol.ProtocolText;

			dynamic protocolJson = new
			{
				Name = protocol.Name,
				Text = protocolText
			};

			return Json(protocolJson);
		}

		private List<ProtocolJson> Convert(List<DispatchProtocol> dispatchProtocols)
		{
			var protocols = new List<ProtocolJson>();

			foreach (var dp in dispatchProtocols)
			{
				var protocol = new ProtocolJson();
				protocol.Id = dp.DispatchProtocolId;
				protocol.DepartmentId = dp.DepartmentId;
				protocol.Name = dp.Name;
				protocol.Code = dp.Code;
				protocol.IsDisabled = dp.IsDisabled;
				protocol.Description = dp.Description;
				protocol.ProtocolText = dp.ProtocolText;
				protocol.CreatedOn = dp.CreatedOn;
				protocol.CreatedByUserId = dp.CreatedByUserId;
				protocol.UpdatedOn = dp.UpdatedOn;
				protocol.MinimumWeight = dp.MinimumWeight;
				protocol.UpdatedByUserId = dp.UpdatedByUserId;
				protocol.State = (int)dp.State;
				protocol.Triggers = new List<ProtocolTriggerJson>();
				protocol.Attachments = new List<ProtocolTriggerAttachmentJson>();
				protocol.Questions = new List<ProtocolTriggerQuestionJson>();

				if (dp.Triggers != null && dp.Triggers.Any())
				{
					foreach (var t in dp.Triggers)
					{
						var trigger = new ProtocolTriggerJson();
						trigger.Id = t.DispatchProtocolTriggerId;
						trigger.Type = t.Type;
						trigger.StartsOn = t.StartsOn;
						trigger.EndsOn = t.EndsOn;
						trigger.Priority = t.Priority;
						trigger.CallType = t.CallType;
						trigger.Geofence = t.Geofence;

						protocol.Triggers.Add(trigger);
					}
				}

				if (dp.Attachments != null && dp.Attachments.Any())
				{
					foreach (var a in dp.Attachments)
					{
						var attachment = new ProtocolTriggerAttachmentJson();
						attachment.Id = a.DispatchProtocolAttachmentId;
						attachment.FileName = a.FileName;
						attachment.FileType = a.FileType;

						protocol.Attachments.Add(attachment);
					}
				}

				if (dp.Questions != null && dp.Questions.Any())
				{
					foreach (var q in dp.Questions)
					{
						var question = new ProtocolTriggerQuestionJson();
						question.Id = q.DispatchProtocolQuestionId;
						question.Question = q.Question;
						question.Answers = new List<ProtocolQuestionAnswerJson>();

						foreach (var a in q.Answers)
						{
							var answer = new ProtocolQuestionAnswerJson();
							answer.Id = a.DispatchProtocolQuestionAnswerId;
							answer.Answer = a.Answer;
							answer.Weight = a.Weight;

							question.Answers.Add(answer);
						}

						protocol.Questions.Add(question);
					}
				}

				protocols.Add(protocol);
			}

			return protocols;
		}
	}
}
