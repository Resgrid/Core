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
using Newtonsoft.Json;
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
		private readonly IRecordsBulkPacketService _bulk;
		private readonly IRecordsFieldRolloutService _fieldRollout;
		private readonly IFeatureToggleService _featureToggles;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IRecordsUdfService _udf;
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
		private readonly IRecordsDashboardService _dashboard;
		private readonly IRecordsProtectionService _protection;
		private readonly IProtectedGrantContext _grantContext;
		private readonly IRecordsRevealService _reveal;
		private readonly IRecordDefinitionsService _definitions;
		private readonly IRecordTypedValuesService _typedValues;
		private readonly IContactsService _contacts;

		public RecordsController(IRecordsService recordsService, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService, ICallsService callsService,
			IDepartmentSettingsService departmentSettingsService, IEventAggregator eventAggregator,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer,
			ICompositeViewEngine viewEngine, IPdfProvider pdfProvider, IRecordsSearchService recordsSearch, IDepartmentDataProtectionService dataProtection,
			IDepartmentProfileMediaService branding, IRecordsPrintLayoutService printLayouts, IRecordsAccountabilityService accountability, IRecordsDashboardService dashboard, IRecordsUdfService udf,
			IRecordsProtectionService protection, IProtectedGrantContext grantContext, IRecordsRevealService reveal, IRecordDefinitionsService definitions, IRecordTypedValuesService typedValues, IContactsService contacts,
			IRecordsBulkPacketService bulk, IRecordsFieldRolloutService fieldRollout, IFeatureToggleService featureToggles)
		{
			_fieldRollout = fieldRollout;
			_bulk = bulk;
			_contacts = contacts;
			_reveal = reveal;
			_definitions = definitions;
			_typedValues = typedValues;
			_accountability = accountability;
			_protection = protection;
			_grantContext = grantContext;
			_recordsService = recordsService;
			_cutoverService = cutoverService;
			_featureToggles = featureToggles;
			_recordsAuthorizationService = recordsAuthorizationService;
			_udf = udf;
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
			_dashboard = dashboard;
		}

		#region Dashboard

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> NewRunCall(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null) return NotFound();
			if (!CanEditRecord(aggregate.Record) || !await _recordsAuthorizationService.CanCreateSourceCallAsync(UserId, DepartmentId)) return Forbid();
			if (aggregate.Record.DefinitionKey != RmsDefinitionKeys.Run || aggregate.Record.CallId.HasValue) return BadRequest();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			return View(new RecordNewCallView { RecordId = id, RowVersion = aggregate.Record.RowVersion, OccurredOn = aggregate.Record.StartedOn?.TimeConverter(department) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> NewRunCall(RecordNewCallView model, CancellationToken cancellationToken)
		{
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				await _recordsService.CreateRunCallAsync(DepartmentId, UserId, model.RecordId, model.RowVersion,
					new RecordNewCallInput { Name = model.Name, Address = model.Address, Nature = model.Nature, OccurredOnUtc = ToUtc(model.OccurredOn, department) ?? default }, cancellationToken);
				return RedirectToAction("Edit", new { id = model.RecordId });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { model.ErrorMessage = _localizer["ConcurrencyError"]; Response.StatusCode = 409; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { model.ErrorMessage = ex.Message; }
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Autosave(RecordEditView model, CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl = "no-store";
			var module = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!module.RecordsUsable) return NotFound();
			var aggregate = await LoadAuthorizedAsync(model.RecordId);
			if (aggregate == null) return NotFound();
			if (!CanEditRecord(aggregate.Record)) return Forbid();
			model.DefinitionKey = aggregate.Record.DefinitionKey;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			try
			{
				var saved = await _recordsService.SaveDraftAsync(DepartmentId, UserId, model.RecordId, model.RowVersion, BuildInput(model), cancellationToken);
				return Json(new { rowVersion = saved.Record.RowVersion });
			}
			catch (RecordConcurrencyException) { return Conflict(new { error = "This draft changed elsewhere. Reload it before saving again; your unsaved text remains in this form." }); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException) { return BadRequest(new { error = ex.Message }); }
		}

		/// <summary>
		/// The Records work queues an officer opens the module to look at (RMS-3): incomplete, awaiting review,
		/// rejected, accepted, overdue, the disclosure clock, and the NERIS crosswalk gaps that decide whether a
		/// filing will map cleanly at all. Counts are group-scope aware and degrade into warnings rather than
		/// failing the page.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
		{
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var model = new RecordsDashboardView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				CanManageDisclosures = ClaimsAuthorizationHelper.CanManageRecordDisclosures(),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
			};

			if (moduleState.RecordsUsable)
			{
				model.Dashboard = await _dashboard.GetAsync(DepartmentId, UserId, cancellationToken);
				model.Coverage = await _dashboard.GetCrosswalkCoverageAsync(DepartmentId, cancellationToken);
			}

			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			return View(model);
		}

		#endregion

		#region Queue

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Index(int? year, string definitionKey, string state, string q = null, string owner = null, int? group = null, int page = 1)
		{
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var model = new RecordsIndexView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				QualityReviewOn = await _featureToggles.IsEnabledAsync(FeatureFlagKeys.RecordsQualityReview, DepartmentId),
				AnalyticsOn = await _featureToggles.IsEnabledAsync(FeatureFlagKeys.RecordsAnalytics, DepartmentId),
				Year = year,
				DefinitionKey = definitionKey,
				StateFilter = state,
				OwnerFilter = string.IsNullOrWhiteSpace(owner) ? null : owner,
				GroupFilter = group,
				Page = Math.Max(1, page)
			};
			if (TempData["RecordsError"] is string recordsError)
				model.ErrorMessage = recordsError;

			model.Definitions = await DefinitionListAsync();
			model.States = Enum.GetValues(typeof(RmsRecordState)).Cast<RmsRecordState>()
				.Select(s => new SelectListItem { Value = ((int)s).ToString(), Text = s.ToString() }).ToList();

			if (!moduleState.RecordsUsable)
				return View(model);

			model.Years = (await _recordsService.GetYearsAsync(DepartmentId)).Select(y => new SelectListItem { Value = y.ToString(), Text = y.ToString() }).ToList();
			await PopulateBulkAsync(model);

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
			if (callId.HasValue && !ClaimsAuthorizationHelper.CanViewCalls()) return Forbid();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return moduleState.FlagEnabled ? RedirectToAction("Index") : NotFound();

			if (!string.IsNullOrWhiteSpace(definitionKey) && !RmsDefinitionKeys.LockedTypes.ContainsKey(definitionKey))
			{
				// Department definitions (RMS-1B) render through the definition-driven form, pinned to the published version.
				var published = await _definitions.GetCurrentPublishedAsync(DepartmentId, definitionKey);
				if (published != null)
				{
					var form = await BuildDefinitionFormAsync(null, published, callId, null);
					form.ProtectionEnforced = await _protection.IsEnforcedAsync(DepartmentId);
					ApplyTempDataError(form);
					return View("EditDefinition", form);
				}
			}
			if (string.IsNullOrWhiteSpace(definitionKey) || !RmsDefinitionKeys.LockedTypes.ContainsKey(definitionKey))
				definitionKey = callId.HasValue ? RmsDefinitionKeys.Run : RmsDefinitionKeys.Training;

			var model = new RecordEditView { DefinitionKey = definitionKey, RecordType = RmsDefinitionKeys.LockedTypes[definitionKey], CallId = callId };
			await PopulateListsAsync(model);
			model.StartedOn = DateTime.UtcNow.TimeConverter(model.Department);
			if (callId.HasValue)
				model.DuplicateCandidates = await _recordsService.GetDuplicateCandidatesAsync(DepartmentId, definitionKey, callId.Value);
			// A new record has nothing to reveal, but its first save seals the cataloged columns and needs the grant.
			model.ProtectionEnforced = await _protection.IsEnforcedAsync(DepartmentId);
			ApplyTempDataError(model);

			return View("Edit", model);
		}

		/// <summary>
		/// Reveal-and-edit for a new record under Protected Data enforcement (RMS plan section 5.9.3): the reveal module
		/// posts the grant here after step-up, and the authoring form renders carrying it so the first save can seal.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> NewRevealed(string definitionKey, int? callId)
		{
			var result = await New(definitionKey, callId);
			if (result is ViewResult view && view.Model is RecordsBaseView model)
				CarryGrant(model);
			return result;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Create(RecordEditView model, ICollection<IFormFile> files, CancellationToken cancellationToken)
		{
			if (model.CallId.HasValue && !ClaimsAuthorizationHelper.CanViewCalls()) return Forbid();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return moduleState.FlagEnabled ? RedirectToAction("Index") : NotFound();

			await PopulateListsAsync(model);
			RmsRecordDefinitionVersion definitionVersion = null;
			if (!RmsDefinitionKeys.LockedTypes.TryGetValue(model.DefinitionKey ?? string.Empty, out var recordType))
			{
				definitionVersion = await _definitions.GetCurrentPublishedAsync(DepartmentId, model.DefinitionKey ?? string.Empty);
				if (definitionVersion == null)
					return BadRequest();
			}
			model.RecordType = recordType;
			CarryGrant(model);
			model.ProtectionEnforced = await _protection.IsEnforcedAsync(DepartmentId);

			try
			{
				var aggregate = await _recordsService.CreateDraftAsync(DepartmentId, UserId, BuildInput(model), cancellationToken);
				await SaveUploadsAsync(aggregate.Record.RmsOperationalRecordId, files, cancellationToken, model.AttachmentClassification);

				if (model.FinalizeAfterSave && model.CanFinalize)
				{
					if (!model.Attested)
					{
						model.RecordId = aggregate.Record.RmsOperationalRecordId;
						model.RowVersion = aggregate.Record.RowVersion;
						return await EditErrorAsync(model, aggregate, definitionVersion, _localizer["Attestation"]);
					}

					var fresh = await _recordsService.GetAsync(DepartmentId, aggregate.Record.RmsOperationalRecordId);
					await _recordsService.FinalizeAsync(DepartmentId, UserId, fresh.Record.RmsOperationalRecordId, fresh.Record.RowVersion, "1", null, null, cancellationToken);
				}

				return RedirectToAction("Details", new { id = aggregate.Record.RmsOperationalRecordId });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (ArgumentException ex)
			{
				return await EditErrorAsync(model, null, definitionVersion, ex.Message);
			}
			catch (RecordTransitionException ex)
			{
				return await EditErrorAsync(model, null, definitionVersion, ex.Message);
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

			if (record.RecordType == null)
			{
				var version = aggregate.DefinitionVersionRow ?? await _definitions.GetVersionAsync(DepartmentId, record.DefinitionKey, record.DefinitionVersion);
				if (version == null)
					return NotFound();
				var form = await BuildDefinitionFormAsync(aggregate, version, record.CallId, null);
				ApplyTempDataError(form);
				return View("EditDefinition", form);
			}

			var model = await BuildEditAsync(aggregate);
			ApplyTempDataError(model);
			return View(model);
		}

		/// <summary>
		/// Reveal-and-edit (RMS plan section 5.9.3): the reveal module posts the grant here after step-up. The draft is
		/// hydrated through the value seam with that grant, so the form renders plaintext, and the grant is carried in
		/// the form's hidden field so the save and every autosave present it again.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> EditRevealed(string id)
		{
			var result = await Edit(id);
			if (result is ViewResult view && view.Model is RecordsBaseView model)
				CarryGrant(model);
			return result;
		}

		private async Task<RecordEditView> BuildEditAsync(RecordAggregate aggregate)
		{
			var record = aggregate.Record;
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
				ParticipantRows = aggregate.Participants.Select(p => new RecordParticipantEditRow { UserId = p.UserId, Selected = true, UnitId = p.UnitId, Role = p.Role }).ToList(),
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
			model.Details = JsonConvert.DeserializeObject<RmsOperationalRecordDetail>(JsonConvert.SerializeObject(model.Details));
			if (!await CanViewRestrictedAsync() || !await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords))
			{
				if (RmsDefinitionKeys.RestrictedClass.Contains(model.DefinitionKey ?? string.Empty))
					foreach (var name in RecordSnapshotSerializer.RestrictedDetailFields)
						typeof(RmsOperationalRecordDetail).GetProperty(name)?.SetValue(model.Details, null);
			}
			if (model.Details.ActivityOn.HasValue)
				model.Details.ActivityOn = model.Details.ActivityOn.Value.TimeConverter(department);

			await PopulateListsAsync(model);
			model.ApplyProtection(aggregate.Protection);
			return model;
		}

		/// <summary>The grant the current request presented (header or form field) travels with the re-rendered form.</summary>
		private void CarryGrant(RecordsBaseView model)
		{
			model.ProtectedGrant = _grantContext.GrantToken;
			model.ProtectedGrantExpiresOnUtc = model.ProtectedGrant == null ? null : HttpProtectedGrantContext.ReadExpiry(Request);
		}

		private void ApplyTempDataError(RecordsBaseView model)
		{
			if (TempData["RecordsError"] is string error && string.IsNullOrEmpty(model.ErrorMessage))
				model.ErrorMessage = error;
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
			if (model.CallId.HasValue && model.CallId != aggregate.Record.CallId && !ClaimsAuthorizationHelper.CanViewCalls()) return Forbid();
			if (model.ParticipantRows == null)
				model.ParticipantRows = (model.ParticipantUserIds ?? new List<string>()).Select(id => { var previous = aggregate.Participants.FirstOrDefault(p => p.UserId == id); return new RecordParticipantEditRow { UserId = id, Selected = true, UnitId = previous?.UnitId, Role = previous?.Role }; }).ToList();

			await PopulateListsAsync(model);
			model.RecordType = (RmsOperationalRecordType)aggregate.Record.RecordType.GetValueOrDefault();
			model.DefinitionKey = aggregate.Record.DefinitionKey;
			var definitionVersion = aggregate.Record.RecordType == null ? aggregate.DefinitionVersionRow ?? await _definitions.GetVersionAsync(DepartmentId, aggregate.Record.DefinitionKey, aggregate.Record.DefinitionVersion) : null;
			CarryGrant(model);
			model.ApplyProtection(aggregate.Protection);

			try
			{
				var saved = await _recordsService.SaveDraftAsync(DepartmentId, UserId, model.RecordId, model.RowVersion, BuildInput(model), cancellationToken);
				await SaveUploadsAsync(model.RecordId, files, cancellationToken, model.AttachmentClassification);

				if (model.FinalizeAfterSave && model.CanFinalize)
				{
					if (!model.Attested)
					{
						model.RowVersion = saved.Record.RowVersion;
						return await EditErrorAsync(model, saved, definitionVersion, _localizer["Attestation"]);
					}

					var fresh = await _recordsService.GetAsync(DepartmentId, model.RecordId);
					await _recordsService.FinalizeAsync(DepartmentId, UserId, model.RecordId, fresh.Record.RowVersion, "1", model.ReasonCode, model.ReasonText, cancellationToken);
				}

				return RedirectToAction("Details", new { id = model.RecordId });
			}
			catch (RecordConcurrencyException)
			{
				return await EditErrorAsync(model, aggregate, definitionVersion, _localizer["ConcurrencyError"]);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (ArgumentException ex)
			{
				return await EditErrorAsync(model, aggregate, definitionVersion, ex.Message);
			}
			catch (RecordTransitionException ex)
			{
				return await EditErrorAsync(model, aggregate, definitionVersion, ex.Message);
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

		/// <summary>
		/// ADP client-side reveal (plan 7.2; RMS plan 5.9.3). The grant rides the request header, so the ordinary
		/// authorized load already resolves the aggregate for it; this returns the resolved values keyed the way the
		/// Details page marks its cells. A grant proves the caller stepped up, never that they may read this record,
		/// so the record is authorized exactly as the page is.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> RevealRecord([FromForm] string id)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();

			// Shared with the v4 Reveal endpoint (IRecordsRevealService): same keys, same withholding, same audit.
			var outcome = await _reveal.RevealRecordAsync(DepartmentId, UserId, aggregate, await CanViewRestrictedAsync(), IpAddressHelper.GetRequestIP(Request, true));
			if (!outcome.Success)
				return Json(new { success = false, error = outcome.Error });
			return Json(new { success = true, fields = outcome.Fields });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public IActionResult Revision(string id, string revisionId) => RedirectToAction("Revision", "RecordDocuments", new { id, kind=RmsRecordKind.Operational, revisionId });

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public IActionResult Diff(string id, string from, string to) => RedirectToAction("Diff", "RecordDocuments", new { id, kind=RmsRecordKind.Operational, from, to });

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Attachment(string id, string attachmentId)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			var attachment = await _recordsService.GetAttachmentAsync(DepartmentId, UserId, attachmentId);
			if (attachment == null || !string.Equals(attachment.RecordId, id, StringComparison.Ordinal) || attachment.Data == null)
				return NotFound();

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, "Attachment " + attachmentId, IpAddressHelper.GetRequestIP(Request, true));
			return File(attachment.Data, string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType, attachment.FileName ?? "attachment");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> AddAttachment(string id, ICollection<IFormFile> files, CancellationToken cancellationToken, int classification = 1)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();
			if (!CanEditRecord(aggregate.Record))
				return Unauthorized();

			try
			{
				var rejected = await SaveUploadsAsync(id, files, cancellationToken, classification);
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
			if (aggregate.Record.CurrentRevisionId != null)
				return RedirectToAction("Export", "RecordDocuments", new { id, kind = RmsRecordKind.Operational, revisionId = aggregate.Record.CurrentRevisionId, format = "json" });

			var snapshot = RecordSnapshotSerializer.Build(aggregate);
			snapshot.CustomFields = await _udf.ProjectAsync(DepartmentId, UserId, snapshot.CustomFields);
			if (!await CanViewRestrictedAsync() && snapshot.Details != null)
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
			if (record.CurrentRevisionId != null)
				return RedirectToAction("Export", "RecordDocuments", new { id, kind = RmsRecordKind.Operational, revisionId = record.CurrentRevisionId, format = "pdf" });
			model.Provenance = await BuildProvenanceAsync(model.Department, model.PersonnelNames, record.RecordNumber ?? record.DraftReference, record.DefinitionKey, record.DefinitionVersion, record.RevisionCount > 0 ? record.RevisionCount : (int?)null);
			var pdf = await RenderPdfAsync("Print", model);

			await _recordsService.RecordAccessAsync(DepartmentId, UserId, id, record.CurrentRevisionId, RmsAccessAuditAction.Export, "PDF print", IpAddressHelper.GetRequestIP(Request, true));
			return File(pdf, "application/pdf", SafeFileName(model.Provenance.RecordNumber) + ".pdf");
		}

		/// <summary>Single-revision print/PDF: the revision exactly as it stood (RMS plan sections 4.8 and 4.10).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public IActionResult PrintRevision(string id, string revisionId) => RedirectToAction("Export", "RecordDocuments", new { id, kind=RmsRecordKind.Operational, revisionId, format="pdf" });

		/// <summary>Two-revision diff print/PDF (RMS plan sections 4.8 and 4.10); withheld fields stay withheld.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Export)]
		public IActionResult PrintDiff(string id, string from, string to) => RedirectToAction("PrintDiff", "RecordDocuments", new { id, kind = RmsRecordKind.Operational, from, to });

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

			// Setting 77 (plan section 4.9): the statutory clock is bounded, the profile must be one the disclosure
			// workflow knows, and the release approver must be a current member so a departed user is never the gate.
			var disclosure = await _departmentSettingsService.GetRecordsDisclosureConfigAsync(DepartmentId, true) ?? new RecordsDisclosureConfig();
			disclosure.StatutoryClockDays = Math.Max(1, Math.Min(365, model.DisclosureStatutoryClockDays));
			disclosure.DefaultRedactionProfile = RmsRedactionProfiles.IsKnown(model.DisclosureDefaultRedactionProfile) ? model.DisclosureDefaultRedactionProfile : RmsRedactionProfiles.Standard;
			var approver = string.IsNullOrWhiteSpace(model.DisclosureReleaseApproverUserId) ? null : model.DisclosureReleaseApproverUserId.Trim();
			if (approver != null && !await _recordsAuthorizationService.IsActiveMemberAsync(approver, DepartmentId))
				approver = null;
			disclosure.ReleaseApproverUserId = approver;
			await _departmentSettingsService.SetRecordsDisclosureConfigAsync(DepartmentId, disclosure, cancellationToken);

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

			var disclosure = await _departmentSettingsService.GetRecordsDisclosureConfigAsync(DepartmentId, true) ?? new RecordsDisclosureConfig();
			model.DisclosureStatutoryClockDays = disclosure.StatutoryClockDays;
			model.DisclosureDefaultRedactionProfile = RmsRedactionProfiles.IsKnown(disclosure.DefaultRedactionProfile) ? disclosure.DefaultRedactionProfile : RmsRedactionProfiles.Standard;
			model.DisclosureReleaseApproverUserId = disclosure.ReleaseApproverUserId;
			model.RedactionProfiles = RmsRedactionProfiles.All.Select(p => new SelectListItem { Value = p, Text = _localizer["RedactionProfile" + p] }).ToList();
			model.ReleaseApprovers.Add(new SelectListItem { Value = string.Empty, Text = _localizer["DisclosureApproverAnyAdmin"] });
			foreach (var person in (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>()).OrderBy(p => p.Name))
				model.ReleaseApprovers.Add(new SelectListItem { Value = person.UserId, Text = person.Name });

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
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) throw new UnauthorizedAccessException("Records access is not authorized.");
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
				CanViewRestricted = await CanViewRestrictedAsync(),
				CanReassign = ClaimsAuthorizationHelper.CanReassignRecordDrafts(),
				DefinitionName = await DefinitionNameAsync(aggregate.Record),
				DefinitionLayout = aggregate.Record.RecordType == null ? (await _printLayouts.ResolveForDefinitionAsync(DepartmentId, aggregate.Record.DefinitionKey, aggregate.Record.DefinitionVersion)).Definition : null
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

			var aggregate = await _recordsService.GetAsync(DepartmentId, id, includeRevisions);
			if (aggregate != null && !await CanViewRestrictedAsync()) aggregate.Attachments = aggregate.Attachments.Where(a => !a.RequiresRestrictedAccess).ToList();
			return aggregate;
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
				CustomFields = model.CustomFields,
				Values = model.Values ?? new List<RecordValueInput>(),
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
				Participants = BuildParticipantInput(model),
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

		public static List<RecordParticipantInput> BuildParticipantInput(RecordEditView model) => model.ParticipantRows != null
			? model.ParticipantRows.Where(p => p.Selected && !string.IsNullOrWhiteSpace(p.UserId)).Select(p => new RecordParticipantInput { UserId = p.UserId, UnitId = p.UnitId, Role = p.Role }).ToList()
			: (model.ParticipantUserIds ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => new RecordParticipantInput { UserId = u }).ToList();

		/// <summary>Stores each upload; files that fail media hygiene or the scanner are skipped and their reasons returned.</summary>
		private async Task<List<string>> SaveUploadsAsync(string recordId, ICollection<IFormFile> files, CancellationToken cancellationToken, int classification = 1)
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
					await _recordsService.AddAttachmentAsync(DepartmentId, UserId, recordId, Path.GetFileName(file.FileName), file.ContentType, stream.ToArray(), null, cancellationToken, classification);
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
			if (model.IsNew) model.CustomFieldForm = await _udf.GetNewFormAsync(DepartmentId, UserId, model.DefinitionKey, RmsDefinitionKeys.LockedDefinitionVersion);
			if (!string.IsNullOrWhiteSpace(model.RecordId) && await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, model.RecordId, DepartmentId))
			{
				var current = await _recordsService.GetAsync(DepartmentId, model.RecordId);
				model.CustomFieldForm = await _udf.ProjectAsync(DepartmentId, UserId, current?.CustomFields);
			}
			if (model.CustomFieldForm != null && model.CustomFields?.DefinitionId == model.CustomFieldForm.DefinitionId)
				foreach (var field in model.CustomFieldForm.Fields.Where(f=>!f.Field.IsReadOnly))
					if (model.CustomFields.Values?.TryGetValue(field.Field.UdfFieldId,out var submitted)==true) field.Value=submitted;
			model.CanViewRestricted = await CanViewRestrictedAsync()
				&& await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.Definitions = await DefinitionListAsync();
			model.CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			model.Stations = groups.OrderBy(g => g.Name).Select(g => new SelectListItem { Value = g.DepartmentGroupId.ToString(), Text = g.Name }).ToList();

			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			model.Personnel = names.OrderBy(n => n.Name).Select(n => new SelectListItem { Value = n.UserId, Text = n.Name }).ToList();
			model.ParticipantRows ??= model.ParticipantUserIds.Select(id => new RecordParticipantEditRow { UserId = id, Selected = true }).ToList();
			if (model.ParticipantRows.All(p => !string.IsNullOrWhiteSpace(p.UserId))) model.ParticipantRows.Add(new RecordParticipantEditRow { Selected = true });

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>();
			model.AvailableUnits = units.OrderBy(u => u.Name).Select(u => new SelectListItem { Value = u.UnitId.ToString(), Text = u.Name }).ToList();

			var calls = ClaimsAuthorizationHelper.CanViewCalls() ? await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId) ?? new List<Call>() : new List<Call>();
			model.Calls = new List<SelectListItem>();
			foreach (var call in calls.OrderByDescending(c => c.LoggedOn))
				if (await _recordsAuthorizationService.CanReadSourceCallAsync(UserId, DepartmentId, call))
					model.Calls.Add(new SelectListItem { Value = call.CallId.ToString(), Text = $"{call.Number} {call.Name}" });
			if (model.CallId.HasValue && model.Calls.All(c => c.Value != model.CallId.Value.ToString()))
			{
				// Retain the existing binding without reopening its source. Otherwise a browser posts the blank option
				// after access is revoked and an unrelated edit would erase the authorized snapshot.
				model.Calls.Insert(0, new SelectListItem { Value = model.CallId.Value.ToString(), Text = $"{model.Details?.CallNumber ?? model.CallId.Value.ToString()} {model.Details?.CallName}".Trim() });
			}
		}

		/// <summary>Locked Logs-parity definitions plus the department's published definitions (RMS-1B).</summary>
		private async Task<List<SelectListItem>> DefinitionListAsync()
		{
			var list = RmsDefinitionKeys.LockedTypes.Select(kv => new SelectListItem { Value = kv.Key, Text = kv.Value.ToString() }).ToList();
			try
			{
				foreach (var definition in (await _definitions.ListAsync(DepartmentId)).Where(d => !d.Locked && d.PublishedVersion.HasValue && !d.Retired).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
					list.Add(new SelectListItem { Value = definition.Key, Text = definition.Name });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Department definitions could not be listed for the Records chooser.");
			}
			return list;
		}

		private async Task<string> DefinitionNameAsync(RmsOperationalRecord record)
		{
			if (record?.RecordType != null) return null;
			return (await _definitions.ListAsync(DepartmentId, true)).FirstOrDefault(d => string.Equals(d.Key, record?.DefinitionKey, StringComparison.OrdinalIgnoreCase))?.Name ?? record?.DefinitionKey;
		}

		/// <summary>The definition-driven authoring form (RMS-1B): pinned schema, stored or posted values, rule evaluation, reference lists.</summary>
		private async Task<RecordDefinitionFormView> BuildDefinitionFormAsync(RecordAggregate aggregate, RmsRecordDefinitionVersion version, int? callId, RecordEditView posted)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var record = aggregate?.Record;
			var definition = await _definitions.GetAsync(DepartmentId, version.DefinitionKey);
			var form = new RecordDefinitionFormView
			{
				RecordId = record?.RmsOperationalRecordId, RowVersion = posted?.RowVersion ?? record?.RowVersion ?? 0, DefinitionKey = version.DefinitionKey, DefinitionName = definition?.Definition.Name ?? version.DefinitionKey, DefinitionVersion = version.Version,
				DraftReference = record?.DraftReference, RecordNumber = record?.RecordNumber, IsAmendment = record?.AmendsRevisionId != null,
				CallId = posted?.CallId ?? record?.CallId ?? callId, StationGroupId = posted?.StationGroupId ?? record?.StationGroupId, ExternalId = posted?.ExternalId ?? record?.ExternalId,
				StartedOn = posted?.StartedOn ?? record?.StartedOn?.TimeConverter(department) ?? (record == null ? DateTime.UtcNow.TimeConverter(department) : (DateTime?)null), EndedOn = posted?.EndedOn ?? record?.EndedOn?.TimeConverter(department),
				Schema = version.Schema, Values = aggregate?.Values, PostedValues = posted?.Values, LifecyclePreset = (RmsLifecyclePreset)version.LifecyclePreset, MinimumClientCapability = version.MinimumClientCapability,
				CanViewRestricted = await CanViewRestrictedAsync(), CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords(), Department = department,
				FinalizeAfterSave = posted?.FinalizeAfterSave ?? false, Attested = posted?.Attested ?? false, ReasonCode = posted?.ReasonCode, ReasonText = posted?.ReasonText, AttachmentClassification = posted?.AttachmentClassification ?? 1
			};
			form.ApplyProtection(aggregate?.Protection);
			form.Evaluation = _typedValues.EvaluateRules(version.Schema, aggregate?.Values ?? new RecordValueSet());
			if (definition?.Definition.TemplateKey != null)
			{
				var template = RecordTemplateCatalog.Find(definition.Definition.TemplateKey);
				var profile = RecordTemplateCatalog.FindProfile(definition.Definition.JurisdictionProfileKey ?? "generic") ?? RecordTemplateCatalog.FindProfile("generic");
				if (template != null && profile != null)
				{
					var rendering = RecordTemplatePacksService.Render(template, profile, profile.ProfileKey, profile.DefaultLocale);
					form.ProvenanceStatement = rendering.ProvenanceStatement;
					form.IsPreview = RecordTemplateCatalog.PackOf(template.Key)?.IsPreview ?? false;
				}
			}
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			form.Stations = groups.OrderBy(g => g.Name).Select(g => new SelectListItem { Value = g.DepartmentGroupId.ToString(), Text = g.Name }).ToList();
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			form.Personnel = names.OrderBy(n => n.Name).Select(n => new SelectListItem { Value = n.UserId, Text = n.Name }).ToList();
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>();
			form.AvailableUnits = units.OrderBy(u => u.Name).Select(u => new SelectListItem { Value = u.UnitId.ToString(), Text = u.Name }).ToList();
			var calls = ClaimsAuthorizationHelper.CanViewCalls() ? await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId) ?? new List<Call>() : new List<Call>();
			form.Calls = calls.OrderByDescending(c => c.LoggedOn).Select(c => new SelectListItem { Value = c.CallId.ToString(), Text = $"{c.Number} - {c.Name}" }).ToList();
			try
			{
				var contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>();
				form.Contacts = contacts.Select(c => new SelectListItem { Value = c.ContactId, Text = string.Join(" ", new[] { c.FirstName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) is var n && !string.IsNullOrWhiteSpace(n) ? n : c.CompanyName ?? c.ContactId }).OrderBy(i => i.Text).ToList();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Contacts could not be listed for the definition form.");
			}
			form.Attachments = (aggregate?.Attachments ?? new List<RmsRecordAttachment>()).Select(a => new SelectListItem { Value = a.RmsRecordAttachmentId, Text = a.FileName }).ToList();
			if (record == null && callId.HasValue)
				form.DuplicateCandidates = await _recordsService.GetDuplicateCandidatesAsync(DepartmentId, version.DefinitionKey, callId.Value);
			return form;
		}

		/// <summary>Re-renders the right editor after a failed post: the definition form (with the posted values) or the locked-type Edit view.</summary>
		private async Task<IActionResult> EditErrorAsync(RecordEditView model, RecordAggregate aggregate, RmsRecordDefinitionVersion definitionVersion, string error)
		{
			if (definitionVersion == null)
			{
				model.ErrorMessage = error;
				return View("Edit", model);
			}
			var form = await BuildDefinitionFormAsync(aggregate, definitionVersion, model.CallId, model);
			form.ErrorMessage = error;
			CarryGrant(form);
			form.ProtectionEnforced = aggregate?.Protection?.IsProtected ?? await _protection.IsEnforcedAsync(DepartmentId);
			return View("EditDefinition", form);
		}

		/// <summary>
		/// Per-app Field Records rollout (RMS plan RMS-1D): who is on a compatible version, what the apps were
		/// refused, and where authoring stopped. Counts only — this page never shows what anybody wrote.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> FieldRollout(int windowDays = 30, CancellationToken cancellationToken = default)
		{
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled) return NotFound();

			var model = new RecordsFieldRolloutView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				WindowDays = windowDays
			};

			try
			{
				model.Rollout = await _fieldRollout.GetAsync(DepartmentId, UserId, windowDays, cancellationToken);
				// The service clamps the window; the label has to say the window the numbers actually cover.
				model.WindowDays = model.Rollout.WindowDays;
			}
			catch (UnauthorizedAccessException)
			{
				return Forbid();
			}

			return View(model);
		}

		private async Task PopulateBulkAsync(RecordsIndexView model)
		{
			model.CanBulkAssign = await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ReviewRecords);
			model.CanBulkPacket = await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ExportRecords);
			if (model.CanBulkAssign)
			{
				var names = await PersonnelNamesAsync();
				model.Reviewers = names.OrderBy(n => n.Value, StringComparer.CurrentCultureIgnoreCase).Select(n => new SelectListItem { Value = n.Key, Text = n.Value }).ToList();
			}
		}

		/// <summary>Bulk assign-for-review and bulk packets over the checked rows (RMS plan section 4.7). No bulk void, no bulk delete.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Bulk(string bulkAction, List<string> ids, string reviewerUserId, string reason, string title, string purpose, string deliverTo, CancellationToken cancellationToken)
		{
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			ids = (ids ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
			if (ids.Count == 0) { TempData["RecordsError"] = _localizer["BulkNothingSelected"].Value; return RedirectToAction("Index"); }
			try
			{
				switch ((bulkAction ?? string.Empty).ToLowerInvariant())
				{
					case "assign":
					{
						var result = await _bulk.AssignForReviewAsync(DepartmentId, UserId, new RecordsBulkAssignRequest { RecordIds = ids, ReviewerUserId = reviewerUserId, Reason = reason }, cancellationToken);
						TempData["RecordsMessage"] = string.Format(_localizer["BulkAssigned"].Value, result.Processed, result.Skipped);
						return RedirectToAction("Index");
					}
					case "packet":
					case "bundle":
					{
						var result = await _bulk.BuildPacketAsync(DepartmentId, UserId, new RecordsBulkPacketRequest
						{
							RecordIds = ids, Mode = bulkAction.ToLowerInvariant() == "bundle" ? RecordsBulkPacketMode.Bundle : RecordsBulkPacketMode.CompiledPdf,
							Title = title, Purpose = purpose, DeliverToEmail = deliverTo, OriginClient = RmsOriginClient.Web
						}, cancellationToken);
						TempData["RecordsMessage"] = string.Format(_localizer["BulkPacketCreated"].Value, result.Processed, result.Skipped) + (result.Delivered ? " " + _localizer["BulkDelivered"].Value : string.Empty);
						return RedirectToAction("BulkDownload", new { id = result.Run.RmsExportRunId });
					}
					default:
						return BadRequest();
				}
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; return RedirectToAction("Index"); }
		}

		[HttpGet]
		public async Task<IActionResult> BulkDownload(string id)
		{
			if (!await _recordsAuthorizationService.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			RmsExportRun run;
			try { run = await _bulk.GetPacketAsync(DepartmentId, UserId, id); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			if (run?.Data == null) return NotFound();
			return File(run.Data, run.ContentType ?? "application/octet-stream", run.FileName ?? "packet");
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
				CanViewRestricted = await CanViewRestrictedAsync()
			};

			if (model.Snapshot != null) model.Snapshot.CustomFields = await _udf.ProjectAsync(DepartmentId, UserId, model.Snapshot.CustomFields);
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
				Diffs = await _recordsService.DiffRevisionsAsync(DepartmentId, from, to, await CanViewRestrictedAsync()),
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

        private async Task<bool> CanViewRestrictedAsync() => ClaimsAuthorizationHelper.CanViewRestrictedRecords()
            && await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
	}
}
