using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Records (RMS) module shell: work queue, locked Logs-parity authoring, lifecycle actions, revision
	/// history/diff, attachments, activation and the Records Settings screen (RMS plan sections 4.1,
	/// 4.8, 4.9). Every action gates on the Records.System flag first; per-Record visibility is checked
	/// through IRecordsAuthorizationService on every read, never inferred from a list.
	/// </summary>
	[Area("User")]
	public class RecordsController : SecureBaseController
	{
		private readonly IRecordsService _recordsService;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;
		private readonly ICompositeViewEngine _viewEngine;
		private readonly IPdfProvider _pdfProvider;
		private readonly IRecordsSearchService _recordsSearch;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDepartmentProfileMediaService _branding;
		private readonly IRecordsPrintLayoutService _printLayouts;
		private readonly IRecordsAccountabilityService _accountability;

		public RecordsController(IRecordsService recordsService, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService, ICallsService callsService,
			IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer,
			ICompositeViewEngine viewEngine, IPdfProvider pdfProvider, IRecordsSearchService recordsSearch, IDepartmentDataProtectionService dataProtection,
			IDepartmentProfileMediaService branding, IRecordsPrintLayoutService printLayouts, IRecordsAccountabilityService accountability)
		{
			_accountability = accountability;
			_recordsService = recordsService;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_callsService = callsService;
			_departmentSettingsService = departmentSettingsService;
			_eventAggregator = eventAggregator;
			_localizer = localizer;
			_viewEngine = viewEngine;
			_pdfProvider = pdfProvider;
			_recordsSearch = recordsSearch;
			_dataProtection = dataProtection;
			_branding = branding;
			_printLayouts = printLayouts;
		}

		#region Queue

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Index(int? year, string definitionKey, string state, string q = null, string owner = null, int? group = null, int page = 1)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var model = new RecordsIndexView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				Year = year,
				DefinitionKey = definitionKey,
				StateFilter = state,
				OwnerFilter = string.IsNullOrWhiteSpace(owner) ? null : owner,
				GroupFilter = group,
				Page = Math.Max(1, page)
			};
			if (TempData["RecordsError"] is string recordsError)
				model.ErrorMessage = recordsError;

			model.Definitions = DefinitionList();
			model.States = Enum.GetValues(typeof(RmsRecordState)).Cast<RmsRecordState>()
				.Select(s => new SelectListItem { Value = ((int)s).ToString(), Text = s.ToString() }).ToList();

			if (!moduleState.RecordsUsable)
				return View(model);

			model.Years = (await _recordsService.GetYearsAsync(DepartmentId)).Select(y => new SelectListItem { Value = y.ToString(), Text = y.ToString() }).ToList();

			var visibleGroups = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
			var states = int.TryParse(state, out var stateValue) ? new List<int> { stateValue } : null;

			model.Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
			model.SearchAvailable = _recordsSearch.IsAvailable;
			if (model.SearchAvailable)
				model.NarrativeSearchAvailable = await NarrativeSearchAvailableAsync();

			if (model.Query != null)
			{
				if (model.SearchAvailable && await TrySearchAsync(model, visibleGroups, states))
				{
					// Drill-through filters narrow the authorized hits in memory; the index has no owner/group term.
					if (model.OwnerFilter != null)
						model.Records = model.Records.Where(r => string.Equals(r.OwnerUserId, model.OwnerFilter, StringComparison.OrdinalIgnoreCase)).ToList();
					if (model.GroupFilter.HasValue)
						model.Records = model.Records.Where(r => r.StationGroupId == model.GroupFilter).ToList();
					return View(model);
				}

				// Host disabled or offline: the filtered queue still renders and the text is reported as not applied
				// (plan section 5.10). Search is never quietly reimplemented as LIKE.
				model.SearchDegraded = true;
			}

			var query = new RmsRecordQuery
			{
				Year = year,
				DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey,
				States = states,
				OwnerUserId = model.OwnerFilter,
				StationGroupId = model.GroupFilter,
				VisibleGroupIds = visibleGroups,
				ViewerUserId = UserId,
				Skip = (model.Page - 1) * model.PageSize,
				Take = model.PageSize
			};

			model.Records = await _recordsService.QueryAsync(DepartmentId, query);
			model.Total = await _recordsService.CountAsync(DepartmentId, query);
			model.PersonnelNames = await PersonnelNamesAsync();

			return View(model);
		}

		#endregion

		#region Activation

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Activate()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var model = new RecordsActivateView
			{
				Preview = await _cutoverService.GetActivationPreviewAsync(DepartmentId),
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false)
			};
			model.ViewGroupRecordsLockToGroup = model.Preview.SuggestedViewGroupRecordsLockToGroup;

			if (!model.Preview.FlagEnabled)
				return NotFound();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Activate(RecordsActivateView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			model.Preview = await _cutoverService.GetActivationPreviewAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (!model.Acknowledged)
			{
				model.ErrorMessage = _localizer["ActivationAcknowledge"];
				return View(model);
			}

			var result = await _cutoverService.ActivateAsync(DepartmentId, UserId, model.Reason, model.ViewGroupRecordsLockToGroup, IpAddressHelper.GetRequestIP(Request, true), cancellationToken);
			if (!result.Success)
			{
				model.ErrorMessage = result.Error;
				return View(model);
			}

			SendAudit(AuditLogTypes.DepartmentSettingsChanged, null, result.Cutover.CloneJsonToString());
			return RedirectToAction("Index");
		}

		#endregion

		#region Authoring

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> New(string definitionKey, int? callId)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return moduleState.FlagEnabled ? RedirectToAction("Index") : NotFound();

			if (string.IsNullOrWhiteSpace(definitionKey) || !RmsDefinitionKeys.LockedTypes.ContainsKey(definitionKey))
				definitionKey = callId.HasValue ? RmsDefinitionKeys.Run : RmsDefinitionKeys.Training;

			var model = new RecordEditView { DefinitionKey = definitionKey, RecordType = RmsDefinitionKeys.LockedTypes[definitionKey], CallId = callId };
			await PopulateListsAsync(model);
			model.StartedOn = DateTime.UtcNow.TimeConverter(model.Department);
			if (callId.HasValue)
				model.DuplicateCandidates = await _recordsService.GetDuplicateCandidatesAsync(DepartmentId, definitionKey, callId.Value);

			return View("Edit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Create(RecordEditView model, ICollection<IFormFile> files, CancellationToken cancellationToken)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return moduleState.FlagEnabled ? RedirectToAction("Index") : NotFound();

			await PopulateListsAsync(model);
			if (!RmsDefinitionKeys.LockedTypes.TryGetValue(model.DefinitionKey ?? string.Empty, out var recordType))
				return BadRequest();
			model.RecordType = recordType;

			try
			{
				var aggregate = await _recordsService.CreateDraftAsync(DepartmentId, UserId, BuildInput(model), cancellationToken);
				await SaveUploadsAsync(aggregate.Record.RmsOperationalRecordId, files, cancellationToken);

				if (model.FinalizeAfterSave && model.CanFinalize)
				{
					if (!model.Attested)
					{
						model.RecordId = aggregate.Record.RmsOperationalRecordId;
						model.RowVersion = aggregate.Record.RowVersion;
						model.ErrorMessage = _localizer["Attestation"];
						return View("Edit", model);
					}

					var fresh = await _recordsService.GetAsync(DepartmentId, aggregate.Record.RmsOperationalRecordId);
					await _recordsService.FinalizeAsync(DepartmentId, UserId, fresh.Record.RmsOperationalRecordId, fresh.Record.RowVersion, "1", null, null, cancellationToken);
				}

				return RedirectToAction("Details", new { id = aggregate.Record.RmsOperationalRecordId });
			}
			catch (ArgumentException ex)
			{
				model.ErrorMessage = ex.Message;
				return View("Edit", model);
			}
			catch (RecordTransitionException ex)
			{
				model.ErrorMessage = ex.Message;
				return View("Edit", model);
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Edit(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();

			var record = aggregate.Record;
			if (!(RmsLifecycle.IsEditable((RmsRecordState)record.State) || record.AmendsRevisionId != null))
				return RedirectToAction("Details", new { id });
			if (!CanEditRecord(record))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var model = new RecordEditView
			{
				RecordId = record.RmsOperationalRecordId,
				RowVersion = record.RowVersion,
				DefinitionKey = record.DefinitionKey,
				RecordType = (RmsOperationalRecordType)record.RecordType.GetValueOrDefault(),
				DraftReference = record.DraftReference,
				RecordNumber = record.RecordNumber,
				IsAmendment = record.AmendsRevisionId != null,
				CallId = record.CallId,
				StationGroupId = record.StationGroupId,
				ExternalId = record.ExternalId,
				StartedOn = record.StartedOn?.TimeConverter(department),
				EndedOn = record.EndedOn?.TimeConverter(department),
				Details = aggregate.Details ?? new RmsOperationalRecordDetail(),
				ParticipantUserIds = aggregate.Participants.Select(p => p.UserId).ToList(),
				Units = aggregate.Units.Select(u => new RecordUnitResponseInput
				{
					UnitId = u.UnitId,
					Dispatched = u.Dispatched?.TimeConverter(department),
					Enroute = u.Enroute?.TimeConverter(department),
					OnScene = u.OnScene?.TimeConverter(department),
					Released = u.Released?.TimeConverter(department),
					InQuarters = u.InQuarters?.TimeConverter(department)
				}).ToList()
			};
			if (model.Details.ActivityOn.HasValue)
				model.Details.ActivityOn = model.Details.ActivityOn.Value.TimeConverter(department);

			await PopulateListsAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Edit(RecordEditView model, ICollection<IFormFile> files, CancellationToken cancellationToken)
		{
			var aggregate = await LoadAuthorizedAsync(model.RecordId);
			if (aggregate == null)
				return NotFound();
			if (!CanEditRecord(aggregate.Record))
				return Unauthorized();

			await PopulateListsAsync(model);
			model.RecordType = (RmsOperationalRecordType)aggregate.Record.RecordType.GetValueOrDefault();
			model.DefinitionKey = aggregate.Record.DefinitionKey;

			try
			{
				var saved = await _recordsService.SaveDraftAsync(DepartmentId, UserId, model.RecordId, model.RowVersion, BuildInput(model), cancellationToken);
				await SaveUploadsAsync(model.RecordId, files, cancellationToken);

				if (model.FinalizeAfterSave && model.CanFinalize)
				{
					if (!model.Attested)
					{
						model.RowVersion = saved.Record.RowVersion;
						model.ErrorMessage = _localizer["Attestation"];
						return View(model);
					}

					var fresh = await _recordsService.GetAsync(DepartmentId, model.RecordId);
					await _recordsService.FinalizeAsync(DepartmentId, UserId, model.RecordId, fresh.Record.RowVersion, "1", model.ReasonCode, model.ReasonText, cancellationToken);
				}

				return RedirectToAction("Details", new { id = model.RecordId });
			}
			catch (RecordConcurrencyException)
			{
				model.ErrorMessage = _localizer["ConcurrencyError"];
				return View(model);
			}
			catch (ArgumentException ex)
			{
				model.ErrorMessage = ex.Message;
				return View(model);
			}
			catch (RecordTransitionException ex)
			{
				model.ErrorMessage = ex.Message;
				return View(model);
			}
		}

		#endregion

		#region Lifecycle actions

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Finalize)]
		public async Task<IActionResult> Finalize(string id, long rowVersion, bool attested, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();
			if (!attested)
				return await DetailsWithErrorAsync(id, _localizer["Attestation"]);

			try
			{
				await _recordsService.FinalizeAsync(DepartmentId, UserId, id, rowVersion, "1", reasonCode, reasonText, cancellationToken);
				return RedirectToAction("Details", new { id });
			}
			catch (RecordConcurrencyException)
			{
				return await DetailsWithErrorAsync(id, _localizer["ConcurrencyError"]);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public async Task<IActionResult> Amend(string id, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				await _recordsService.OpenAmendmentAsync(DepartmentId, UserId, id, cancellationToken);
				return RedirectToAction("Edit", new { id });
			}
			catch (RecordTransitionException ex)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public async Task<IActionResult> AbandonAmendment(string id, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			await _recordsService.AbandonAmendmentAsync(DepartmentId, UserId, id, cancellationToken);
			return RedirectToAction("Details", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public async Task<IActionResult> Void(string id, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				await _recordsService.VoidAsync(DepartmentId, UserId, id, reasonCode, reasonText, cancellationToken);
				return RedirectToAction("Details", new { id });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		/// <summary>Draft ownership transfer (plan section 4.7): audited, keeps the author and creation provenance.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Reassign)]
		public async Task<IActionResult> Reassign(string id, string newOwnerUserId, string reason, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				await _recordsService.ReassignDraftAsync(DepartmentId, UserId, id, newOwnerUserId, reason, cancellationToken);
				return RedirectToAction("Details", new { id });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public async Task<IActionResult> CancelDraft(string id, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				await _recordsService.CancelAsync(DepartmentId, UserId, id, cancellationToken);
				return RedirectToAction("Index");
			}
			catch (RecordTransitionException ex)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		#endregion

		#region Reads

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Details(string id)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, null, IpAddressHelper.GetRequestIP(Request, true));
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Revision(string id, string revisionId)
		{
			var model = await BuildRevisionViewAsync(id, revisionId);
			if (model == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, revisionId, RmsAccessAuditAction.Read, "Revision view", IpAddressHelper.GetRequestIP(Request, true));
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Diff(string id, string from, string to)
		{
			var model = await BuildDiffViewAsync(id, from, to);
			if (model == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, to, RmsAccessAuditAction.Read, "Revision diff", IpAddressHelper.GetRequestIP(Request, true));
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Attachment(string id, string attachmentId)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			var attachment = await _recordsService.GetAttachmentAsync(DepartmentId, attachmentId);
			if (attachment == null || !string.Equals(attachment.RecordId, id, StringComparison.Ordinal) || attachment.Data == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, "Attachment " + attachmentId, IpAddressHelper.GetRequestIP(Request, true));
			return File(attachment.Data, string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType, attachment.FileName ?? "attachment");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> AddAttachment(string id, ICollection<IFormFile> files, CancellationToken cancellationToken)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();
			if (!CanEditRecord(aggregate.Record))
				return Unauthorized();

			try
			{
				var rejected = await SaveUploadsAsync(id, files, cancellationToken);
				if (rejected.Count > 0)
					return await DetailsWithErrorAsync(id, string.Join(" ", rejected));
				return RedirectToAction("Details", new { id });
			}
			catch (RecordTransitionException ex)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		/// <summary>Per-record machine-readable export (RMS plan section 4.10): typed values keyed by stable field keys; never a NERIS payload.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> Export(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id, includeRevisions: true);
			if (aggregate == null)
				return NotFound();

			var snapshot = RecordSnapshotSerializer.Build(aggregate);
			if (!ClaimsAuthorizationHelper.CanViewRestrictedRecords() && snapshot.Details != null)
			{
				foreach (var field in RecordSnapshotSerializer.RestrictedDetailFields)
					typeof(RmsOperationalRecordDetail).GetProperty(field)?.SetValue(snapshot.Details, null);
			}

			var payload = new
			{
				format = "resgrid.record.v1",
				exportedOn = DateTime.UtcNow,
				exportedByUserId = UserId,
				record = snapshot,
				revisions = aggregate.Revisions.Select(r => new { r.RmsRevisionId, r.RevisionNumber, transition = ((RmsRevisionTransition)r.Transition).ToString(), r.PriorRevisionId, r.Checksum, r.ActorUserId, r.ReasonCode, r.CreatedOn })
			};

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, aggregate.Record.CurrentRevisionId, RmsAccessAuditAction.Export, "JSON export", IpAddressHelper.GetRequestIP(Request, true));
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload, Newtonsoft.Json.Formatting.Indented);
			return File(Encoding.UTF8.GetBytes(json), "application/json", (aggregate.Record.RecordNumber ?? aggregate.Record.DraftReference) + ".json");
		}

		/// <summary>
		/// Per-record print / save-as-PDF through IPdfProvider (RMS plan section 4.10): the record against its pinned
		/// definition, rendered in-process from the Print view with the fixed provenance footer.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> Print(string id)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();

			var record = model.Aggregate.Record;
			model.Provenance = await BuildProvenanceAsync(model.Department, model.PersonnelNames, record.RecordNumber ?? record.DraftReference, record.DefinitionKey, record.DefinitionVersion, record.RevisionCount > 0 ? record.RevisionCount : (int?)null);
			var pdf = await RenderPdfAsync("Print", model);

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, record.CurrentRevisionId, RmsAccessAuditAction.Export, "PDF print", IpAddressHelper.GetRequestIP(Request, true));
			return File(pdf, "application/pdf", SafeFileName(model.Provenance.RecordNumber) + ".pdf");
		}

		/// <summary>Single-revision print/PDF: the revision exactly as it stood (RMS plan sections 4.8 and 4.10).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> PrintRevision(string id, string revisionId)
		{
			var model = await BuildRevisionViewAsync(id, revisionId);
			if (model == null)
				return NotFound();

			model.Provenance = await BuildProvenanceAsync(model.Department, model.PersonnelNames, model.Snapshot.RecordNumber ?? model.Snapshot.DraftReference, model.Snapshot.DefinitionKey, model.Snapshot.DefinitionVersion, model.Revision.RevisionNumber);
			var pdf = await RenderPdfAsync("PrintRevision", model);

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, revisionId, RmsAccessAuditAction.Export, "PDF print (revision)", IpAddressHelper.GetRequestIP(Request, true));
			return File(pdf, "application/pdf", SafeFileName(model.Provenance.RecordNumber) + "-r" + model.Revision.RevisionNumber + ".pdf");
		}

		/// <summary>Two-revision diff print/PDF (RMS plan sections 4.8 and 4.10); withheld fields stay withheld.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> PrintDiff(string id, string from, string to)
		{
			var model = await BuildDiffViewAsync(id, from, to);
			if (model == null)
				return NotFound();

			var pdf = await RenderPdfAsync("PrintDiff", model);

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, to, RmsAccessAuditAction.Export, "PDF print (diff)", IpAddressHelper.GetRequestIP(Request, true));
			return File(pdf, "application/pdf", SafeFileName(model.Provenance.RecordNumber) + "-r" + model.From.RevisionNumber + "-r" + model.To.RevisionNumber + ".pdf");
		}

		/// <summary>
		/// List/search tabular export (RMS plan section 4.10): CSV, or the same rows as JSON, of the authorized filtered
		/// queue. Columns are the safe projection fields only, so the export carries no narrative or restricted data.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public async Task<IActionResult> ExportList(int? year, string definitionKey, string state, string q = null, string format = "csv")
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled || !moduleState.RecordsUsable)
				return NotFound();

			var visibleGroups = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
			var states = int.TryParse(state, out var stateValue) ? new List<int> { stateValue } : null;
			var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

			List<RmsRecordSearchProjection> projections;
			if (text != null)
			{
				// Free-text hits export exactly what the queue showed: the same search, the same per-record re-check.
				// With the host unavailable the export is refused rather than silently widened to the filtered list.
				var search = _recordsSearch.IsAvailable ? await SearchProjectionsAsync(text, visibleGroups, states, definitionKey, year, 0, RecordsListExport.MaxRows) : null;
				if (search == null || !search.Available)
				{
					TempData["RecordsError"] = _localizer["ExportSearchUnavailable"].Value;
					return RedirectToAction("Index", new { year, definitionKey, state, q = text });
				}

				projections = search.Records;
			}
			else
			{
				projections = await _recordsService.QueryAsync(DepartmentId, new RmsRecordQuery
				{
					Year = year,
					DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey,
					States = states,
					VisibleGroupIds = visibleGroups,
					ViewerUserId = UserId,
					Skip = 0,
					Take = RecordsListExport.MaxRows
				});
			}

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			var rows = RecordsListExport.BuildRows(projections, await PersonnelNamesAsync(), groups.ToDictionary(g => g.DepartmentGroupId, g => g.Name),
				await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false));

			var asJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
			var purpose = text == null ? $"List export {(asJson ? "JSON" : "CSV")} ({rows.Count} rows)" : $"Search export {(asJson ? "JSON" : "CSV")} ({rows.Count} rows, query length {text.Length})";
			await _recordsService.RecordAccessAsync(DepartmentId, UserId, null, null, RmsAccessAuditAction.Export, purpose, IpAddressHelper.GetRequestIP(Request, true));

			var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
			if (asJson)
				return File(Encoding.UTF8.GetBytes(RecordsListExport.ToJson(rows, UserId)), "application/json", $"records-{stamp}.json");

			return File(RecordsListExport.ToCsvBytes(rows), "text/csv", $"records-{stamp}.csv");
		}

		#endregion

		#region Accountability

		/// <summary>Who owes a report (plan section 4.7): reviewers, approvers, report managers and administrators.</summary>
		private bool CanViewAccountability()
		{
			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanReviewRecords()
				|| ClaimsAuthorizationHelper.CanApproveRecords() || ClaimsAuthorizationHelper.CanManageRecordReports();
		}

		private static RecordsAccountabilityPivot ParsePivot(string pivot)
		{
			switch ((pivot ?? string.Empty).Trim().ToLowerInvariant())
			{
				case "group": return RecordsAccountabilityPivot.Group;
				case "unit": return RecordsAccountabilityPivot.Unit;
				default: return RecordsAccountabilityPivot.Person;
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Accountability(string pivot = "person", int days = 30)
		{
			if (!CanViewAccountability())
				return Unauthorized();

			var model = await BuildAccountabilityAsync(ParsePivot(pivot), days);
			if (model == null)
				return NotFound();

			if (TempData["RecordsMessage"] is string message)
				model.Message = message;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Remind(string recordId, string pivot, int days, CancellationToken cancellationToken)
		{
			if (!CanViewAccountability())
				return Unauthorized();

			if (await LoadAuthorizedAsync(recordId) == null)
				return NotFound();

			var result = await _accountability.SendReminderAsync(DepartmentId, UserId, recordId, cancellationToken);
			TempData["RecordsMessage"] = result.Sent ? _localizer["ReminderSent"].Value : string.Format(_localizer["ReminderNotSent"].Value, result.Reason);
			return RedirectToAction("Accountability", new { pivot, days });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> RemindAll(string key, string pivot, int days, CancellationToken cancellationToken)
		{
			if (!CanViewAccountability())
				return Unauthorized();

			var model = await BuildAccountabilityAsync(ParsePivot(pivot), days);
			if (model == null)
				return NotFound();

			// The row is rebuilt from the viewer's own report, so only records the viewer can see are reminded.
			var row = model.Report.Rows.FirstOrDefault(r => string.Equals(r.Key, key ?? string.Empty, StringComparison.OrdinalIgnoreCase));
			var results = row == null
				? new List<RecordsReminderResult>()
				: await _accountability.SendRemindersAsync(DepartmentId, UserId, row.OpenRecords.Select(r => r.RecordId), cancellationToken);

			TempData["RecordsMessage"] = string.Format(_localizer["RemindersResult"].Value, results.Count(r => r.Sent), results.Count(r => !r.Sent));
			return RedirectToAction("Accountability", new { pivot, days });
		}

		private async Task<RecordsAccountabilityView> BuildAccountabilityAsync(RecordsAccountabilityPivot pivot, int days)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled || !moduleState.RecordsUsable)
				return null;

			var model = new RecordsAccountabilityView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				Pivot = pivot,
				Days = Math.Clamp(days, 1, 365),
				CanRemind = CanViewAccountability(),
				Report = await _accountability.BuildAsync(DepartmentId, UserId, pivot, Math.Clamp(days, 1, 365))
			};

			// Owner names are needed on every pivot (each open record shows its owner); group/unit names per pivot.
			model.Names = await PersonnelNamesAsync();
			if (pivot == RecordsAccountabilityPivot.Group)
			{
				foreach (var group in await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
					model.Names[group.DepartmentGroupId.ToString()] = group.Name;
			}
			else if (pivot == RecordsAccountabilityPivot.Unit)
			{
				foreach (var unit in await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>())
					model.Names[unit.UnitId.ToString()] = unit.Name;
			}

			return model;
		}

		#endregion

		#region Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Settings()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			return View(await BuildSettingsAsync(moduleState));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Settings(RecordsSettingsView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var before = await BuildSettingsAsync(moduleState);

			await _departmentSettingsService.SetRecordsDefaultLifecyclePresetAsync(DepartmentId, model.DefaultLifecyclePreset, cancellationToken);
			await _departmentSettingsService.SetRecordsReviewDueHoursAsync(DepartmentId, model.ReviewDueHours, cancellationToken);
			await _departmentSettingsService.SetRecordsNumberingConfigAsync(DepartmentId, new RecordsNumberingConfig
			{
				IncludeYear = model.IncludeYear,
				SequenceWidth = model.SequenceWidth,
				PerGroupSequence = model.PerGroupSequence,
				NumberAssignment = (int)RmsNumberAssignment.OnFinalize,
				ResetYearly = model.IncludeYear
			}, cancellationToken);
			// Turning group scoping on is a deliberate action with a preview (plan 5.7.1): the switch needs the
			// explicit confirmation; every other change on the form still saves.
			var groupScopingBlocked = model.GroupVisibilityMode == RecordsGroupVisibilityMode.GroupScoped
				&& before.GroupVisibilityMode != RecordsGroupVisibilityMode.GroupScoped && !model.ConfirmGroupScoping;
			if (!groupScopingBlocked)
				await _departmentSettingsService.SetRecordsGroupVisibilityModeAsync(DepartmentId, model.GroupVisibilityMode, cancellationToken);

			// DepartmentDefault print layout (plan section 4.10.1): a new version only when something changed.
			var submittedLayout = RecordsPrintLayoutService.Normalize(model.PrintLayout ?? RecordsPrintLayoutConfig.Default());
			var currentLayout = await _printLayouts.GetDepartmentDefaultAsync(DepartmentId);
			if (Newtonsoft.Json.JsonConvert.SerializeObject(submittedLayout) != Newtonsoft.Json.JsonConvert.SerializeObject(RecordsPrintLayoutService.Normalize(currentLayout.Config ?? RecordsPrintLayoutConfig.Default())))
				await _printLayouts.SaveDepartmentDefaultAsync(DepartmentId, UserId, submittedLayout, cancellationToken);

			var retention = await _departmentSettingsService.GetRecordsRetentionPolicyAsync(DepartmentId, true);
			retention.DepartmentDefaultYears = model.DepartmentDefaultYears;
			var overrides = new List<RecordsRetentionOverride>();
			var skippedRestricted = new List<string>();
			foreach (var row in model.RetentionOverrides ?? new List<RecordsRetentionOverrideRow>())
			{
				if (!row.RetentionYears.HasValue)
					continue;

				var isRestricted = RmsDefinitionKeys.RestrictedClass.Contains(row.DefinitionKey ?? string.Empty);
				var existing = retention.Overrides.FirstOrDefault(o => o.DefinitionKey == row.DefinitionKey);
				var changed = existing == null || existing.RetentionYears != row.RetentionYears.Value;
				if (isRestricted && changed && !row.ConfirmRestricted)
				{
					// Restricted-class overrides require explicit confirmation naming the definition (plan section 4.9).
					skippedRestricted.Add(row.DefinitionKey);
					if (existing != null)
						overrides.Add(existing);
					continue;
				}

				overrides.Add(new RecordsRetentionOverride
				{
					DefinitionKey = row.DefinitionKey,
					RetentionYears = Math.Max(0, row.RetentionYears.Value),
					AppliesFrom = changed || existing == null ? DateTime.UtcNow : existing.AppliesFrom
				});
			}
			retention.Overrides = overrides;
			retention.LastChangedByUserId = UserId;
			retention.LastChangedOn = DateTime.UtcNow;
			await _departmentSettingsService.SetRecordsRetentionPolicyAsync(DepartmentId, retention, cancellationToken);

			var searchConfig = await _departmentSettingsService.GetRecordsSearchConfigAsync(DepartmentId, true);
			searchConfig.IndexNarrative = model.IndexNarrative;
			await _departmentSettingsService.SetRecordsSearchConfigAsync(DepartmentId, searchConfig, cancellationToken);

			var after = await BuildSettingsAsync(moduleState);
			SendAudit(AuditLogTypes.DepartmentSettingsChanged, before.CloneJsonToString(), after.CloneJsonToString());

			after.Message = _localizer["SettingsSaved"];
			if (skippedRestricted.Count > 0)
				after.ErrorMessage = _localizer["RestrictedOverrideConfirm"] + " (" + string.Join(", ", skippedRestricted) + ")";
			if (groupScopingBlocked)
				after.ErrorMessage = string.IsNullOrEmpty(after.ErrorMessage) ? _localizer["GroupScopeConfirmRequired"].Value : after.ErrorMessage + " " + _localizer["GroupScopeConfirmRequired"];

			return View(after);
		}

		#endregion

		#region Helpers

		private async Task<RecordsSettingsView> BuildSettingsAsync(RecordsModuleState moduleState)
		{
			var numbering = await _departmentSettingsService.GetRecordsNumberingConfigAsync(DepartmentId, true);
			var retention = await _departmentSettingsService.GetRecordsRetentionPolicyAsync(DepartmentId, true);
			var search = await _departmentSettingsService.GetRecordsSearchConfigAsync(DepartmentId, true);

			var model = new RecordsSettingsView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				DefaultLifecyclePreset = await _departmentSettingsService.GetRecordsDefaultLifecyclePresetAsync(DepartmentId, true),
				ReviewDueHours = await _departmentSettingsService.GetRecordsReviewDueHoursAsync(DepartmentId, true),
				IncludeYear = numbering.IncludeYear,
				SequenceWidth = numbering.SequenceWidth,
				PerGroupSequence = numbering.PerGroupSequence,
				DepartmentDefaultYears = retention.DepartmentDefaultYears,
				GroupVisibilityMode = await _departmentSettingsService.GetRecordsGroupVisibilityModeAsync(DepartmentId, true),
				GroupScopePreview = await _recordsAuthorizationService.PreviewGroupScopingAsync(DepartmentId),
				IndexNarrative = search.IndexNarrative,
				Presets = Enum.GetValues(typeof(RmsLifecyclePreset)).Cast<RmsLifecyclePreset>().Select(p => new SelectListItem { Value = ((int)p).ToString(), Text = p.ToString() }).ToList(),
				VisibilityModes = new List<SelectListItem>
				{
					new SelectListItem { Value = ((int)RecordsGroupVisibilityMode.DepartmentWide).ToString(), Text = _localizer["GroupVisibilityDepartmentWide"] },
					new SelectListItem { Value = ((int)RecordsGroupVisibilityMode.GroupScoped).ToString(), Text = _localizer["GroupVisibilityGroupScoped"] }
				}
			};

			foreach (var kv in RmsDefinitionKeys.LockedTypes)
			{
				var existing = retention.Overrides.FirstOrDefault(o => o.DefinitionKey == kv.Key);
				model.RetentionOverrides.Add(new RecordsRetentionOverrideRow
				{
					DefinitionKey = kv.Key,
					Label = kv.Value.ToString(),
					Restricted = RmsDefinitionKeys.RestrictedClass.Contains(kv.Key),
					RetentionYears = existing?.RetentionYears
				});
			}

			model.SearchHealth = await _recordsSearch.GetHealthAsync();
			model.NarrativeSearchAvailable = model.SearchHealth.Online && await NarrativeSearchAvailableAsync();

			var layout = await _printLayouts.GetDepartmentDefaultAsync(DepartmentId);
			model.PrintLayout = layout.Config ?? RecordsPrintLayoutConfig.Default();
			model.PrintLayoutVersion = layout.LayoutVersion;
			model.HasLogo = (await _branding.GetBrandingAsync(DepartmentId)).HasLogo;

			return model;
		}

		/// <summary>
		/// Free-text path (RMS plan section 5.10): index hits are loaded from the projection table and every hit is
		/// re-checked against per-record visibility before it is shown. Totals come from authorized results or are
		/// suppressed, so a count never discloses a record the viewer cannot open.
		/// </summary>
		private sealed class AuthorizedSearchPage
		{
			public bool Available { get; set; }
			public List<RmsRecordSearchProjection> Records { get; set; } = new List<RmsRecordSearchProjection>();
			public int Total { get; set; }
			public int Dropped { get; set; }
			public bool Truncated { get; set; }
		}

		/// <summary>
		/// One search path for the queue and the export: the Lucene hits are loaded by id and re-checked with the
		/// per-record visibility rule, so neither surface can show or export a hit the viewer cannot open.
		/// </summary>
		private async Task<AuthorizedSearchPage> SearchProjectionsAsync(string text, List<int> visibleGroups, List<int> states, string definitionKey, int? year, int skip, int take)
		{
			RecordsSearchResult result;
			try
			{
				result = await _recordsSearch.SearchAsync(DepartmentId, new RecordsSearchRequest
				{
					Text = text,
					VisibleGroupIds = visibleGroups,
					ViewerUserId = UserId,
					States = states,
					DefinitionKey = string.IsNullOrWhiteSpace(definitionKey) ? null : definitionKey,
					Year = year,
					Skip = skip,
					Take = take
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Records search failed; falling back to the filtered queue.");
				return new AuthorizedSearchPage { Available = false };
			}

			if (!result.Available)
				return new AuthorizedSearchPage { Available = false };

			var recordSource = ((int)RmsSearchSourceType.Record).ToString();
			var ids = result.Hits.Where(h => h.SourceType == recordSource && !string.IsNullOrWhiteSpace(h.SourceId)).Select(h => h.SourceId).Distinct().ToList();
			var loaded = (await _recordsService.GetProjectionsByIdsAsync(DepartmentId, ids)).ToDictionary(p => p.RmsRecordSearchProjectionId, StringComparer.OrdinalIgnoreCase);

			var page = new AuthorizedSearchPage { Available = true, Total = result.Total, Truncated = result.Truncated };
			foreach (var id in ids)
			{
				if (!loaded.TryGetValue(id, out var projection) || !await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, id, DepartmentId))
				{
					page.Dropped++;
					continue;
				}
				page.Records.Add(projection);
			}

			return page;
		}

		private async Task<bool> TrySearchAsync(RecordsIndexView model, List<int> visibleGroups, List<int> states)
		{
			var page = await SearchProjectionsAsync(model.Query, visibleGroups, states, model.DefinitionKey, model.Year, (model.Page - 1) * model.PageSize, model.PageSize);
			if (!page.Available)
				return false;

			model.Records = page.Records;
			model.Total = page.Dropped == 0 ? page.Total : (model.Page - 1) * model.PageSize + page.Records.Count;
			model.SearchTruncated = page.Truncated;
			model.PersonnelNames = await PersonnelNamesAsync();
			return true;
		}

		/// <summary>Narrative search is available to unprotected departments that opted in and withdrawn on enrollment (plan section 5.10).</summary>
		private async Task<bool> NarrativeSearchAvailableAsync()
		{
			try
			{
				if (await _dataProtection.IsProtectionEnforcedAsync(DepartmentId))
					return false;

				var config = await _departmentSettingsService.GetRecordsSearchConfigAsync(DepartmentId);
				return config != null && config.IndexNarrative;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		private async Task<RecordDetailView> BuildDetailAsync(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id, includeRevisions: true);
			if (aggregate == null)
				return null;

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			return new RecordDetailView
			{
				Aggregate = aggregate,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				PersonnelNames = await PersonnelNamesAsync(),
				GroupNames = groups.ToDictionary(g => g.DepartmentGroupId, g => g.Name),
				CanEdit = CanEditRecord(aggregate.Record),
				CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords(),
				CanAmend = ClaimsAuthorizationHelper.CanAmendRecords(),
				CanVoid = ClaimsAuthorizationHelper.CanVoidRecords(),
				CanExport = ClaimsAuthorizationHelper.CanExportRecords(),
				CanViewRestricted = ClaimsAuthorizationHelper.CanViewRestrictedRecords(),
				CanReassign = ClaimsAuthorizationHelper.CanReassignRecordDrafts()
			};
		}

		private async Task<IActionResult> DetailsWithErrorAsync(string id, string error)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();
			model.ErrorMessage = error;
			return View("Details", model);
		}

		/// <summary>Loads a Record only when the flag is on and the viewer passes the per-Record visibility check.</summary>
		private async Task<RecordAggregate> LoadAuthorizedAsync(string id, bool includeRevisions = false)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return null;

			if (!await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, id, DepartmentId))
			{
				await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Denied, null, IpAddressHelper.GetRequestIP(Request, true));
				return null;
			}

			return await _recordsService.GetAsync(DepartmentId, id, includeRevisions);
		}

		private bool CanEditRecord(RmsOperationalRecord record)
		{
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return false;

			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				|| string.Equals(record.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(record.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| (record.AmendsRevisionId != null && ClaimsAuthorizationHelper.CanAmendRecords());
		}

		private RecordDraftInput BuildInput(RecordEditView model)
		{
			var department = model.Department;
			return new RecordDraftInput
			{
				DefinitionKey = model.DefinitionKey,
				CallId = model.CallId,
				StationGroupId = model.StationGroupId,
				ExternalId = model.ExternalId,
				StartedOn = ToUtc(model.StartedOn, department),
				EndedOn = ToUtc(model.EndedOn, department),
				Details = new RmsOperationalRecordDetail
				{
					Narrative = model.Details?.Narrative,
					InitialReport = model.Details?.InitialReport,
					Type = model.Details?.Type,
					Course = model.Details?.Course,
					CourseCode = model.Details?.CourseCode,
					Instructors = model.Details?.Instructors,
					Cause = model.Details?.Cause,
					InvestigatedByUserId = model.Details?.InvestigatedByUserId,
					ContactName = model.Details?.ContactName,
					ContactNumber = model.Details?.ContactNumber,
					OtherPersonnel = model.Details?.OtherPersonnel,
					Location = model.Details?.Location,
					OtherAgencies = model.Details?.OtherAgencies,
					OtherUnits = model.Details?.OtherUnits,
					BodyLocation = model.Details?.BodyLocation,
					PronouncedDeceasedBy = model.Details?.PronouncedDeceasedBy,
					CaseNumber = model.Details?.CaseNumber,
					Destination = model.Details?.Destination,
					Facilitator = model.Details?.Facilitator,
					UnitId = model.Details?.UnitId,
					ActivityOn = ToUtc(model.Details?.ActivityOn, department)
				},
				Participants = (model.ParticipantUserIds ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => new RecordParticipantInput { UserId = u }).ToList(),
				Units = (model.Units ?? new List<RecordUnitResponseInput>()).Where(u => u.UnitId > 0).Select(u => new RecordUnitResponseInput
				{
					UnitId = u.UnitId,
					Dispatched = ToUtc(u.Dispatched, department),
					Enroute = ToUtc(u.Enroute, department),
					OnScene = ToUtc(u.OnScene, department),
					Released = ToUtc(u.Released, department),
					InQuarters = ToUtc(u.InQuarters, department)
				}).ToList(),
				DuplicateContinueReason = model.DuplicateContinueReason,
				OriginClient = RmsOriginClient.Web
			};
		}

		private static DateTime? ToUtc(DateTime? local, Department department)
		{
			if (!local.HasValue || local.Value == DateTime.MinValue)
				return null;
			if (department == null || string.IsNullOrWhiteSpace(department.TimeZone))
				return DateTime.SpecifyKind(local.Value, DateTimeKind.Utc);

			return DateTimeHelpers.ConvertToUtc(local.Value, department.TimeZone, true);
		}

		/// <summary>Stores each upload; files that fail media hygiene or the scanner are skipped and their reasons returned.</summary>
		private async Task<List<string>> SaveUploadsAsync(string recordId, ICollection<IFormFile> files, CancellationToken cancellationToken)
		{
			var rejected = new List<string>();
			if (files == null)
				return rejected;

			foreach (var file in files.Where(f => f != null && f.Length > 0))
			{
				using var stream = new MemoryStream();
				await file.CopyToAsync(stream, cancellationToken);
				try
				{
					await _recordsService.AddAttachmentAsync(DepartmentId, UserId, recordId, Path.GetFileName(file.FileName), file.ContentType, stream.ToArray(), null, cancellationToken);
				}
				catch (ArgumentException ex)
				{
					rejected.Add(ex.Message);
				}
			}

			return rejected;
		}

		private async Task PopulateListsAsync(RecordEditView model)
		{
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.Definitions = DefinitionList();
			model.CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			model.Stations = groups.OrderBy(g => g.Name).Select(g => new SelectListItem { Value = g.DepartmentGroupId.ToString(), Text = g.Name }).ToList();

			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			model.Personnel = names.OrderBy(n => n.Name).Select(n => new SelectListItem { Value = n.UserId, Text = n.Name }).ToList();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>();
			model.AvailableUnits = units.OrderBy(u => u.Name).Select(u => new SelectListItem { Value = u.UnitId.ToString(), Text = u.Name }).ToList();

			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId) ?? new List<Call>();
			model.Calls = calls.OrderByDescending(c => c.LoggedOn).Select(c => new SelectListItem { Value = c.CallId.ToString(), Text = $"{c.Number} {c.Name}" }).ToList();
			if (model.CallId.HasValue && model.Calls.All(c => c.Value != model.CallId.Value.ToString()))
			{
				var call = await _callsService.GetCallByIdAsync(model.CallId.Value);
				if (call != null && call.DepartmentId == DepartmentId)
					model.Calls.Insert(0, new SelectListItem { Value = call.CallId.ToString(), Text = $"{call.Number} {call.Name}" });
			}
		}

		private static List<SelectListItem> DefinitionList()
		{
			return RmsDefinitionKeys.LockedTypes.Select(kv => new SelectListItem { Value = kv.Key, Text = kv.Value.ToString() }).ToList();
		}

		private async Task<Dictionary<string, string>> PersonnelNamesAsync()
		{
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var name in names)
			{
				if (!string.IsNullOrWhiteSpace(name.UserId))
					map[name.UserId] = name.Name;
			}
			return map;
		}

		private async Task<RecordRevisionView> BuildRevisionViewAsync(string id, string revisionId)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return null;

			var revisions = await _recordsService.GetRevisionsAsync(DepartmentId, id);
			var revision = revisions.FirstOrDefault(r => r.RmsRevisionId == revisionId);
			if (revision == null)
				return null;

			var model = new RecordRevisionView
			{
				Revision = revision,
				Snapshot = await _recordsService.GetRevisionSnapshotAsync(DepartmentId, revisionId),
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				PersonnelNames = await PersonnelNamesAsync(),
				CanViewRestricted = ClaimsAuthorizationHelper.CanViewRestrictedRecords()
			};

			return model.Snapshot == null ? null : model;
		}

		private async Task<RecordDiffView> BuildDiffViewAsync(string id, string from, string to)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return null;

			var revisions = await _recordsService.GetRevisionsAsync(DepartmentId, id);
			var fromRevision = revisions.FirstOrDefault(r => r.RmsRevisionId == from);
			var toRevision = revisions.FirstOrDefault(r => r.RmsRevisionId == to);
			if (fromRevision == null || toRevision == null)
				return null;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			return new RecordDiffView
			{
				RecordId = id,
				From = fromRevision,
				To = toRevision,
				Department = department,
				Diffs = await _recordsService.DiffRevisionsAsync(DepartmentId, from, to, ClaimsAuthorizationHelper.CanViewRestrictedRecords()),
				Provenance = await BuildProvenanceAsync(department, await PersonnelNamesAsync(), aggregate.Record.RecordNumber ?? aggregate.Record.DraftReference, aggregate.Record.DefinitionKey, aggregate.Record.DefinitionVersion, toRevision.RevisionNumber)
			};
		}

		/// <summary>
		/// The provenance footer plus the letterhead block (RMS plan section 4.10.1): identity and logo from the Department
		/// Profile rendered per the DepartmentDefault print layout. Branding resolves at print time; record content never does.
		/// </summary>
		private async Task<RecordPrintProvenance> BuildProvenanceAsync(Department department, IDictionary<string, string> names, string recordNumber, string definitionKey, int definitionVersion, int? revisionNumber)
		{
			var now = DateTime.UtcNow;
			var branding = await _branding.GetBrandingAsync(DepartmentId);
			var layout = await _printLayouts.GetDepartmentDefaultAsync(DepartmentId);
			var config = layout.Config ?? RecordsPrintLayoutConfig.Default();

			string logo = null;
			if (config.ShowLogo && branding.HasLogo)
			{
				var media = await _branding.GetMediaAsync(DepartmentId, DepartmentProfileMediaKind.PrintHeader);
				if (media?.Data != null && media.Data.Length > 0)
					logo = $"data:{media.ContentType ?? "image/png"};base64,{Convert.ToBase64String(media.Data)}";
			}

			var printedOnText = now.TimeConverterToString(department);
			if (!string.IsNullOrWhiteSpace(config.DateTimeFormat))
			{
				try { printedOnText = now.TimeConverter(department).ToString(config.DateTimeFormat); }
				catch (FormatException) { /* an invalid custom format falls back to the department default */ }
			}

			var phone = config.ShowPhone ? (branding.PhoneNumber ?? await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId)) : null;
			return new RecordPrintProvenance
			{
				RecordNumber = recordNumber,
				DefinitionKey = definitionKey,
				DefinitionVersion = definitionVersion,
				RevisionNumber = revisionNumber,
				PrintedByName = names != null && names.TryGetValue(UserId, out var printedBy) ? printedBy : UserId,
				PrintedOn = now,
				PrintedOnText = printedOnText,
				LayoutVersion = layout.LayoutVersion,
				DepartmentName = config.UseShortName ? branding.ShortName : branding.DisplayName,
				DepartmentAddress = config.ShowAddress ? branding.AddressText : null,
				DepartmentPhone = phone,
				Website = config.ShowWebsite ? branding.Website : null,
				LogoDataUri = logo,
				LetterheadLine1 = config.LetterheadLine1,
				LetterheadLine2 = config.LetterheadLine2,
				FooterText = config.FooterText,
				WatermarkLabel = config.WatermarkLabel,
				PageSize = RecordsPrintLayoutConfig.NormalizePageSize(config.PageSize)
			};
		}

		private async Task<byte[]> RenderPdfAsync(string viewName, object model)
		{
			var html = await RenderViewToStringAsync(viewName, model);
			return _pdfProvider.ConvertHtmlToPdf(html);
		}

		/// <summary>Renders a layout-less Records view in-process, so the PDF path never round-trips through HTTP.</summary>
		private async Task<string> RenderViewToStringAsync(string viewName, object model)
		{
			var viewResult = _viewEngine.FindView(ControllerContext, viewName, isMainPage: true);
			if (!viewResult.Success)
				throw new InvalidOperationException($"Records view '{viewName}' was not found.");

			var viewData = new ViewDataDictionary(ViewData) { Model = model };
			using var writer = new StringWriter();
			var viewContext = new ViewContext(ControllerContext, viewResult.View, viewData, TempData, writer, new HtmlHelperOptions());
			await viewResult.View.RenderAsync(viewContext);
			return writer.ToString();
		}

		private static string SafeFileName(string value)
		{
			var name = string.IsNullOrWhiteSpace(value) ? "record" : value;
			foreach (var invalid in Path.GetInvalidFileNameChars())
				name = name.Replace(invalid, '_');
			return name;
		}

		private void SendAudit(AuditLogTypes type, string before, string after)
		{
			try
			{
				_eventAggregator.SendMessage(new AuditEvent
				{
					DepartmentId = DepartmentId,
					UserId = UserId,
					Type = type,
					Before = before,
					After = after,
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		#endregion
	}
}
