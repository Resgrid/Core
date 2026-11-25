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
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols: the library, the detail page and the editor (New and Edit share one full-page editor), the disable toggle,
	/// and the JSON the call forms' protocol panel reads. Attachment bytes leave only through <see cref="GetProtocolAttachment"/>.
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolsController : SecureBaseController
	{
		private readonly IProtocolsService _protocolsService;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;

		public ProtocolsController(IProtocolsService protocolsService, ICallsService callsService, IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			IEventAggregator eventAggregator = null, IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer = null)
		{
			_protocolsService = protocolsService;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_eventAggregator = eventAggregator;
			_localizer = localizer;
		}

		private bool HasProtocolClaim(string action) => User?.HasClaim(ResgridClaimTypes.Resources.Protocols, action) ?? false;

		private string Text(string key) => _localizer?[key]?.Value ?? key;

		#region Library and detail

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Index(string q = null, string status = null)
		{
			var protocols = await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId);
			var usage = await _protocolsService.GetUsageCountsAsync(DepartmentId, DateTime.UtcNow.AddDays(-30));
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			var model = new ProtocolIndexModel
			{
				Query = q?.Trim(),
				Status = status == "enabled" || status == "disabled" ? status : "",
				Total = protocols.Count,
				DisabledCount = protocols.Count(x => x.IsDisabled),
				UsedOnCallsCount = protocols.Count(x => usage.TryGetValue(x.DispatchProtocolId, out var calls) && calls > 0),
				CanCreate = HasProtocolClaim(ResgridClaimTypes.Actions.Create),
				CanModify = HasProtocolClaim(ResgridClaimTypes.Actions.Update),
				CanDelete = HasProtocolClaim(ResgridClaimTypes.Actions.Delete),
				Message = TempData?["ProtocolMessage"] as string
			};

			IEnumerable<DispatchProtocol> shown = protocols;
			if (!string.IsNullOrWhiteSpace(model.Query))
				shown = shown.Where(x => (x.Name ?? "").Contains(model.Query, StringComparison.CurrentCultureIgnoreCase) || (x.Code ?? "").Contains(model.Query, StringComparison.OrdinalIgnoreCase));
			if (model.Status == "enabled")
				shown = shown.Where(x => !x.IsDisabled);
			else if (model.Status == "disabled")
				shown = shown.Where(x => x.IsDisabled);

			model.Rows = shown.Select(x => new ProtocolLibraryRow
			{
				Protocol = x,
				CallsLast30Days = usage.TryGetValue(x.DispatchProtocolId, out var calls) ? calls : 0,
				Triggers = (x.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>()).Select(t => ProtocolDisplay.Describe(t, priorities, types, _localizer)).ToList()
			}).ToList();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> View(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolByIdAsync(id);
			if (protocol == null)
				return NotFound();

			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			var usage = await _protocolsService.GetUsageCountsAsync(DepartmentId, DateTime.UtcNow.AddDays(-30));

			var model = new ViewProtocolModel
			{
				Protocol = protocol,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				TriggerLines = protocol.Triggers.Select(t => ProtocolDisplay.Describe(t, priorities, types, _localizer)).ToList(),
				CallsLast30Days = usage.TryGetValue(protocol.DispatchProtocolId, out var calls) ? calls : 0,
				CanModify = !protocol.IsDeleted && HasProtocolClaim(ResgridClaimTypes.Actions.Update) && await _authorizationService.CanUserModifyProtocolAsync(UserId, id),
				CanDelete = !protocol.IsDeleted && HasProtocolClaim(ResgridClaimTypes.Actions.Delete),
				Message = TempData?["ProtocolMessage"] as string
			};

			return View(model);
		}

		#endregion Library and detail

		#region Editor

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New()
		{
			var model = new ProtocolEditorModel { IsNew = true };
			await FillEditorAsync(model);

			return View("Edit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New(ProtocolEditorModel model, ICollection<IFormFile> attachments, CancellationToken cancellationToken = default)
		{
			model ??= new ProtocolEditorModel();
			model.IsNew = true;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var protocol = await BuildProtocolAsync(model, null, department);
			var uploads = await ReadUploadsAsync(attachments, 0, cancellationToken);

			// A new protocol is always a new row with new children: no id posted for it or its rows is used.
			protocol.DispatchProtocolId = 0;
			foreach (var trigger in protocol.Triggers) trigger.DispatchProtocolTriggerId = 0;
			foreach (var question in protocol.Questions)
			{
				question.DispatchProtocolQuestionId = 0;
				foreach (var answer in question.Answers) answer.DispatchProtocolQuestionAnswerId = 0;
			}

			protocol.Attachments = uploads;

			if (!ModelState.IsValid)
			{
				// The triggers and questions the author built are posted back into the page, not lost (D23).
				await FillEditorAsync(model);
				return View("Edit", model);
			}

			protocol.DepartmentId = DepartmentId;
			protocol.CreatedOn = DateTime.UtcNow;
			protocol.CreatedByUserId = UserId;
			protocol.UpdatedOn = DateTime.UtcNow;
			protocol.UpdatedByUserId = UserId;

			var saved = await _protocolsService.SaveProtocolAsync(protocol, cancellationToken);
			Audit(AuditLogTypes.ProtocolCreated, null, saved);

			return RedirectToAction("View", new { id = saved?.DispatchProtocolId ?? 0 });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Edit(int id)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolByIdAsync(id);
			if (protocol == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var time = new DepartmentTime(department);

			var model = new ProtocolEditorModel
			{
				ProtocolId = protocol.DispatchProtocolId,
				Name = protocol.Name,
				Code = protocol.Code,
				Description = protocol.Description,
				ProtocolText = protocol.ProtocolText,
				MinimumWeight = protocol.MinimumWeight,
				IsDisabled = protocol.IsDisabled,
				Triggers = protocol.Triggers.Select(t => new ProtocolTriggerInput
				{
					Key = "t" + t.DispatchProtocolTriggerId,
					Id = t.DispatchProtocolTriggerId,
					Type = t.Type,
					StartsOn = time.Input(t.StartsOn),
					EndsOn = time.Input(t.EndsOn),
					Priority = t.Priority,
					CallTypeId = t.CallTypeId,
					CallTypeName = t.CallTypeId.HasValue || ProtocolTriggerMatcher.IsNoTypeTrigger(t) ? null : t.CallType
				}).ToList(),
				Questions = protocol.Questions.Select(q => new ProtocolQuestionInput
				{
					Key = "q" + q.DispatchProtocolQuestionId,
					Id = q.DispatchProtocolQuestionId,
					Text = q.Question,
					Answers = (q.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).Select(a => new ProtocolAnswerInput
					{
						Key = "a" + a.DispatchProtocolQuestionAnswerId,
						Id = a.DispatchProtocolQuestionAnswerId,
						Text = a.Answer,
						Weight = a.Weight
					}).ToList()
				}).ToList()
			};

			await FillEditorAsync(model, protocol);
			return View("Edit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Edit(int id, ProtocolEditorModel model, ICollection<IFormFile> attachments, CancellationToken cancellationToken = default)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var current = await _protocolsService.GetProtocolByIdAsync(id);
			if (current == null || current.DepartmentId != DepartmentId)
				return NotFound();

			model ??= new ProtocolEditorModel();
			model.ProtocolId = id;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var edited = await BuildProtocolAsync(model, current, department);
			edited.DispatchProtocolId = id;
			edited.DepartmentId = DepartmentId;

			// Overposting: a child id must be one of this protocol's own rows. Anything else is refused outright.
			if (ProtocolRules.ForeignChildIds(current, edited).Count > 0)
				return BadRequest();

			var removeIds = (model.RemoveAttachmentIds ?? new List<int>()).Distinct().ToList();
			if (removeIds.Any(x => current.Attachments.All(a => a.DispatchProtocolAttachmentId != x)))
				return BadRequest();

			var remaining = current.Attachments.Count(a => !removeIds.Contains(a.DispatchProtocolAttachmentId));
			edited.Attachments = await ReadUploadsAsync(attachments, remaining, cancellationToken);

			if (!ModelState.IsValid)
			{
				await FillEditorAsync(model, current);
				return View("Edit", model);
			}

			var before = Snapshot(current);
			var saved = await _protocolsService.UpdateProtocolAsync(edited, removeIds, UserId, cancellationToken);
			Audit(AuditLogTypes.ProtocolUpdated, before, saved);

			if (current.IsDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, before, saved);

			TempData["ProtocolMessage"] = Text("ProtocolSavedMessage");
			return RedirectToAction("View", new { id });
		}

		/// <summary>Turns a posted editor into a protocol: trimmed fields, triggers in UTC, call types by id, questions and answers.</summary>
		private async Task<DispatchProtocol> BuildProtocolAsync(ProtocolEditorModel model, DispatchProtocol current, Department department)
		{
			var time = new DepartmentTime(department);
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			var protocol = new DispatchProtocol
			{
				Name = model.Name?.Trim(),
				Code = ProtocolRules.NormalizeCode(model.Code),
				Description = model.Description,
				ProtocolText = model.ProtocolText,
				MinimumWeight = model.MinimumWeight,
				IsDisabled = model.IsDisabled,
				Triggers = new List<DispatchProtocolTrigger>(),
				Questions = new List<DispatchProtocolQuestion>()
			};

			foreach (var input in model.Triggers ?? new List<ProtocolTriggerInput>())
			{
				if (input == null)
					continue;

				var trigger = new DispatchProtocolTrigger { DispatchProtocolTriggerId = input.Id, Type = input.Type };

				if (!string.IsNullOrWhiteSpace(input.StartsOn))
				{
					trigger.StartsOn = time.Parse(input.StartsOn);
					if (!trigger.StartsOn.HasValue) ModelState.AddModelError("Triggers", Text("ErrorTriggerDate"));
				}

				if (!string.IsNullOrWhiteSpace(input.EndsOn))
				{
					trigger.EndsOn = time.Parse(input.EndsOn);
					if (!trigger.EndsOn.HasValue) ModelState.AddModelError("Triggers", Text("ErrorTriggerDate"));
				}

				var needsPriority = input.Type == (int)ProtocolTriggerTypes.CallPriorty || input.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;
				var needsType = input.Type == (int)ProtocolTriggerTypes.CallType || input.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;

				if (needsPriority)
				{
					// The department's own priority, or the one the trigger already had (a priority removed since stays visible).
					var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == input.Id);
					if (input.Priority.HasValue && (priorities.Any(x => x.DepartmentCallPriorityId == input.Priority.Value && !x.IsDeleted) || existing?.Priority == input.Priority))
						trigger.Priority = input.Priority;
					else
						ModelState.AddModelError("Triggers", Text("ErrorTriggerPriority"));
				}

				if (needsType)
				{
					if (input.CallTypeId.HasValue)
					{
						var type = types.FirstOrDefault(x => x.CallTypeId == input.CallTypeId.Value);
						var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == input.Id);

						if (type != null)
						{
							trigger.CallTypeId = type.CallTypeId;
							trigger.CallType = type.Type;
						}
						else if (existing != null && existing.CallTypeId == input.CallTypeId)
						{
							// A type deleted since: kept as it was, so the author sees it and can change it.
							trigger.CallTypeId = existing.CallTypeId;
							trigger.CallType = existing.CallType;
						}
						else
						{
							ModelState.AddModelError("Triggers", Text("ErrorTriggerCallType"));
						}
					}
					else if (!string.IsNullOrWhiteSpace(input.CallTypeName) && current?.Triggers?.Any(x => x.DispatchProtocolTriggerId == input.Id && x.CallType == input.CallTypeName) == true)
					{
						// A trigger written before call type ids whose type no longer exists: its name, unchanged.
						trigger.CallType = input.CallTypeName;
					}
					// else: "no type" (null id, no name).
				}

				protocol.Triggers.Add(trigger);
			}

			foreach (var input in model.Questions ?? new List<ProtocolQuestionInput>())
			{
				if (input == null)
					continue;

				var answers = (input.Answers ?? new List<ProtocolAnswerInput>()).Where(a => a != null && (a.Id > 0 || !string.IsNullOrWhiteSpace(a.Text))).ToList();

				// An untouched blank row the author added and never filled in is not a question.
				if (input.Id <= 0 && string.IsNullOrWhiteSpace(input.Text) && answers.Count == 0)
					continue;

				protocol.Questions.Add(new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = input.Id,
					Question = input.Text?.Trim(),
					Answers = answers.Select(a => new DispatchProtocolQuestionAnswer
					{
						DispatchProtocolQuestionAnswerId = a.Id,
						Answer = a.Text?.Trim(),
						Weight = a.Weight
					}).ToList()
				});
			}

			foreach (var error in ProtocolRules.Validate(protocol))
				ModelState.AddModelError(error == "ErrorDescriptionTooLong" ? "Description" : error.StartsWith("ErrorCode") ? "Code" : "", Text(error));

			return protocol;
		}

		/// <summary>
		/// The uploaded files, each read whole (CopyTo a buffer, the stream opened once), within the per-file, per-save and
		/// per-protocol limits and the allowed types.
		/// </summary>
		private async Task<List<DispatchProtocolAttachment>> ReadUploadsAsync(ICollection<IFormFile> files, int existingCount, CancellationToken cancellationToken)
		{
			var uploads = new List<DispatchProtocolAttachment>();
			var posted = (files ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();

			if (posted.Count == 0)
				return uploads;

			if (posted.Count > ProtocolRules.MaxFilesPerSave)
				ModelState.AddModelError("attachments", string.Format(Text("ErrorTooManyFiles"), ProtocolRules.MaxFilesPerSave));

			if (existingCount + posted.Count > ProtocolRules.MaxAttachmentsPerProtocol)
				ModelState.AddModelError("attachments", string.Format(Text("ErrorTooManyAttachments"), ProtocolRules.MaxAttachmentsPerProtocol));

			if (posted.Sum(x => x.Length) > ProtocolRules.MaxBytesPerSave)
				ModelState.AddModelError("attachments", string.Format(Text("ErrorUploadTooLarge"), ProtocolRules.MaxBytesPerSave / 1_000_000));

			foreach (var file in posted)
			{
				if (!ProtocolRules.IsAllowedFile(file.FileName))
				{
					ModelState.AddModelError("attachments", string.Format(Text("ErrorFileType"), FileHelper.GetFileExtensionWithoutDot(file.FileName)));
					continue;
				}

				if (file.Length > ProtocolRules.MaxAttachmentBytes)
				{
					ModelState.AddModelError("attachments", string.Format(Text("ErrorFileTooLarge"), file.FileName, ProtocolRules.MaxAttachmentBytes / 1_000_000));
					continue;
				}

				if (!ModelState.IsValid)
					continue;

				using var buffer = new MemoryStream();
				await file.CopyToAsync(buffer, cancellationToken);

				uploads.Add(new DispatchProtocolAttachment
				{
					FileName = Path.GetFileName(file.FileName),
					FileType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
					Data = buffer.ToArray()
				});
			}

			return uploads;
		}

		private async Task FillEditorAsync(ProtocolEditorModel model, DispatchProtocol current = null)
		{
			model.CallPriorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			model.ExistingAttachments = current?.Attachments?.ToList() ?? new List<DispatchProtocolAttachment>();
			model.Triggers ??= new List<ProtocolTriggerInput>();
			model.Questions ??= new List<ProtocolQuestionInput>();
			model.RemoveAttachmentIds ??= new List<int>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.TimeZoneName = department?.TimeZone;

			// Rows posted back after a failed save keep the keys they had; rows built server side get one.
			var index = 0;
			foreach (var trigger in model.Triggers)
				trigger.Key = string.IsNullOrWhiteSpace(trigger.Key) ? "t" + (trigger.Id > 0 ? trigger.Id.ToString() : "n" + index++) : trigger.Key;
			foreach (var question in model.Questions)
			{
				question.Key = string.IsNullOrWhiteSpace(question.Key) ? "q" + (question.Id > 0 ? question.Id.ToString() : "n" + index++) : question.Key;
				question.Answers ??= new List<ProtocolAnswerInput>();
				foreach (var answer in question.Answers)
					answer.Key = string.IsNullOrWhiteSpace(answer.Key) ? "a" + (answer.Id > 0 ? answer.Id.ToString() : "n" + index++) : answer.Key;
			}
		}

		#endregion Editor

		#region Disable and delete

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> SetDisabled(int id, bool disabled, string returnTo = null, CancellationToken cancellationToken = default)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var before = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			var beforeSnapshot = Snapshot(before);
			var wasDisabled = before?.IsDisabled ?? false;

			var saved = await _protocolsService.SetProtocolDisabledAsync(id, disabled, UserId, cancellationToken);
			if (saved == null)
				return NotFound();

			if (wasDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, beforeSnapshot, saved);

			TempData["ProtocolMessage"] = Text(saved.IsDisabled ? "ProtocolDisabledMessage" : "ProtocolEnabledMessage");

			return returnTo == "view" ? RedirectToAction("View", new { id }) : RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Delete)]
		public async Task<IActionResult> Delete(int id)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var before = await _protocolsService.GetProtocolHeaderByIdAsync(id);

			if (await _protocolsService.DeleteProtocol(id))
				Audit(AuditLogTypes.ProtocolDeleted, Snapshot(before), null);

			return RedirectToAction("Index");
		}

		#endregion Disable and delete

		#region JSON for the call forms

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocol(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolByIdAsync(id);
			if (protocol == null)
				return NotFound();

			// A DTO, never the entity: the entity carried attachment bytes (D20).
			var json = new ProtocolDetailJson
			{
				IsDisabled = protocol.IsDisabled,
				ProtocolText = StringHelpers.SanitizeHtmlForDisplay(protocol.ProtocolText),
				CreatedOn = protocol.CreatedOn,
				UpdatedOn = protocol.UpdatedOn
			};
			Fill(json, protocol, null, null, null);

			return Json(json);
		}

		/// <summary>
		/// The department's enabled protocols evaluated for a call being written: the shared matcher (the v4
		/// Protocols/GetProtocolsForCall runs the same one), with the call type by id or name, and trigger windows compared with
		/// the call's logged time (department local, optional; now when blank).
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocolsForCall(int priority, string type = null, int? callTypeId = null, string loggedOn = null, int? callId = null)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var at = string.IsNullOrWhiteSpace(loggedOn) ? (DateTime?)null : new DepartmentTime(department).Parse(loggedOn);
			var onCall = new HashSet<int>();

			// Update Call: matched at the call's own logged time, and the protocols already on it (removed ones too) left out.
			if (callId.HasValue && callId.Value > 0 && await _authorizationService.CanUserViewCallAsync(UserId, callId.Value))
			{
				var call = await _callsService.GetCallByIdAsync(callId.Value);
				if (call != null && call.DepartmentId == DepartmentId)
				{
					at = call.LoggedOn;
					onCall = new HashSet<int>((await _callsService.GetCallProtocolsByCallIdAsync(call.CallId) ?? new List<CallProtocol>()).Select(x => x.DispatchProtocolId));
				}
			}

			var context = await _protocolsService.BuildCallContextAsync(DepartmentId, priority, callTypeId, type, at);
			var matches = (await _protocolsService.GetProtocolsForCallAsync(DepartmentId, context)).Where(x => !onCall.Contains(x.Protocol.DispatchProtocolId)).ToList();
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			return Json(matches.Select(m =>
			{
				var json = new ProtocolJson();
				Fill(json, m.Protocol, m, priorities, types);

				// Matching protocols only carry their questions (D20); the rest are names for the "add by hand" picker.
				if (m.State == ProtocolStates.Inactive)
					json.Questions = new List<ProtocolQuestionJson>();

				return json;
			}).ToList());
		}

		/// <summary>Kept for pages cached before P0; the same as <see cref="GetProtocolsForCall"/>.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public Task<IActionResult> GetProtocolsForPrioType(int priority, string type)
		{
			return GetProtocolsForCall(priority, type);
		}

		private void Fill(ProtocolJson json, DispatchProtocol protocol, ProtocolMatch match, List<DepartmentCallPriority> priorities, List<CallType> types)
		{
			json.Id = protocol.DispatchProtocolId;
			json.Name = protocol.Name;
			json.Code = protocol.Code;
			json.Description = StringHelpers.SanitizeHtmlForDisplay(protocol.Description);
			json.MinimumWeight = protocol.MinimumWeight;
			json.State = (int)(match?.State ?? ProtocolStates.Inactive);
			json.MatchedTriggerType = match?.MatchedTrigger?.Type;
			json.HasQuestions = protocol.Questions != null && protocol.Questions.Count > 0;
			json.Reason = match?.MatchedTrigger == null ? string.Empty : ProtocolDisplay.Reason(match.MatchedTrigger, priorities, types, _localizer);
			json.Questions = (protocol.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>()).OrderBy(q => q.DispatchProtocolQuestionId).Select(q => new ProtocolQuestionJson
			{
				Id = q.DispatchProtocolQuestionId,
				Question = q.Question,
				Answers = (q.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).OrderBy(a => a.DispatchProtocolQuestionAnswerId)
					.Select(a => new ProtocolAnswerJson { Id = a.DispatchProtocolQuestionAnswerId, Answer = a.Answer, Weight = a.Weight }).ToList()
			}).ToList();
			json.Attachments = (protocol.Attachments ?? Enumerable.Empty<DispatchProtocolAttachment>())
				.Select(a => new ProtocolAttachmentJson { Id = a.DispatchProtocolAttachmentId, FileName = a.FileName, FileType = a.FileType, Size = a.Size }).ToList();
		}

		/// <summary>The one web endpoint attachment bytes leave through: the protocol must be the caller's department's.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocolAttachment(int protocolAttachmentId)
		{
			var attachment = await _protocolsService.GetAttachmentForDepartmentAsync(DepartmentId, protocolAttachmentId);

			if (attachment == null || attachment.Data == null)
				return NotFound();

			return new FileContentResult(attachment.Data, string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType)
			{
				FileDownloadName = attachment.FileName
			};
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetTextForProtocol(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			if (protocol == null)
				return NotFound();

			var protocolText = Text("NoProtocolText");

			if (!String.IsNullOrWhiteSpace(protocol.ProtocolText))
				protocolText = StringHelpers.SanitizeHtmlForDisplay(protocol.ProtocolText);

			dynamic protocolJson = new
			{
				Name = protocol.Name,
				Text = protocolText
			};

			return Json(protocolJson);
		}

		#endregion JSON for the call forms

		#region Audit

		private static string Snapshot(DispatchProtocol protocol)
		{
			if (protocol == null)
				return null;

			return JsonConvert.SerializeObject(new
			{
				protocol.DispatchProtocolId,
				protocol.Name,
				protocol.Code,
				protocol.IsDisabled,
				protocol.IsDeleted,
				protocol.MinimumWeight,
				Triggers = protocol.Triggers?.Count,
				Questions = protocol.Questions?.Count,
				Attachments = protocol.Attachments?.Count
			});
		}

		private void Audit(AuditLogTypes type, string before, DispatchProtocol after)
		{
			if (_eventAggregator == null)
				return;

			var auditEvent = new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = type,
				Before = before,
				After = Snapshot(after),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request?.Headers["User-Agent"]} {Request?.Headers["Accept-Language"]}"
			};

			_eventAggregator.SendMessage<AuditEvent>(auditEvent);
		}

		#endregion Audit
	}
}
