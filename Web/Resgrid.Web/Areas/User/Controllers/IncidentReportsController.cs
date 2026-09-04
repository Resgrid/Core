using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
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
	/// NERIS incident reports (RMS-2, plan sections 4.2, 5.3, 5.5): queue, start-from-call, sectioned draft editing
	/// with provenance, local/destination validation, finalize with attestation, submission history and the
	/// department NERIS profile/crosswalk settings. Every read passes the per-record visibility rule.
	/// </summary>
	[Area("User")]
	public class IncidentReportsController : SecureBaseController
	{
		private const string IncidentTypeSet = "incident_type";

		private readonly IIncidentReportsService _incidentReports;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly INerisProfileService _neris;
		private readonly IRmsSubmissionsRepository _submissions;
		private readonly IIncidentAnalysisService _analysis;
		private readonly IRecordsEvidenceService _evidence;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public IncidentReportsController(IIncidentReportsService incidentReports, IRecordsCutoverService cutoverService, IRecordsAuthorizationService recordsAuthorizationService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService, ICallsService callsService,
			INerisProfileService neris, IRmsSubmissionsRepository submissions, IIncidentAnalysisService analysis, IRecordsEvidenceService evidence,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			_incidentReports = incidentReports;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_callsService = callsService;
			_neris = neris;
			_submissions = submissions;
			_analysis = analysis;
			_evidence = evidence;
			_localizer = localizer;
		}

		#region Queue / start

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Index(int? year, string state, int page = 1)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var profile = await _neris.GetProfileAsync(DepartmentId);
			var model = new IncidentReportsIndexView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				SystemEnabled = Config.NerisConfig.Enabled,
				ProfileConfigured = profile != null && !string.IsNullOrWhiteSpace(profile.NerisEntityId),
				SubmissionEnabled = await _neris.IsSubmissionEnabledAsync(DepartmentId),
				Year = year,
				StateFilter = state,
				Page = Math.Max(1, page),
				States = Enum.GetValues(typeof(RmsRecordState)).Cast<RmsRecordState>().Select(s => new SelectListItem { Value = ((int)s).ToString(), Text = s.ToString() }).ToList()
			};
			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			if (TempData["RecordsError"] is string error)
				model.ErrorMessage = error;

			if (!moduleState.RecordsUsable)
				return View(model);

			model.Years = (await _incidentReports.GetYearsAsync(DepartmentId)).Select(y => new SelectListItem { Value = y.ToString(), Text = y.ToString() }).ToList();
			// Per-record visibility (plan 5.7.1) is applied by the query, not by filtering the page afterwards: a page
			// filtered after paging reported its own length as the total, so a member with scoped visibility saw a
			// single page of links and could never reach anything older.
			var visibleGroups = await _recordsAuthorizationService.GetVisibleGroupIdsAsync(UserId, DepartmentId);
			var query = new RmsIncidentReportQuery
			{
				Year = year,
				States = int.TryParse(state, out var stateValue) ? new List<int> { stateValue } : null,
				VisibleGroupIds = visibleGroups,
				ViewerUserId = UserId,
				Skip = (model.Page - 1) * model.PageSize,
				Take = model.PageSize
			};
			model.Reports = (await _incidentReports.QueryAsync(DepartmentId, query)) ?? new List<RmsIncidentReport>();
			model.Total = await _incidentReports.CountAsync(DepartmentId, query);
			model.PersonnelNames = await PersonnelNamesAsync();

			if (ClaimsAuthorizationHelper.CanCreateRecord())
			{
				var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId) ?? new List<Call>();
				model.ActiveCalls = calls.OrderByDescending(c => c.LoggedOn)
					.Select(c => new SelectListItem { Value = c.CallId.ToString(), Text = $"{c.Number} - {c.Name}" }).ToList();
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Start(int callId, CancellationToken cancellationToken)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return NotFound();

			try
			{
				var existing = await _incidentReports.GetForCallAsync(DepartmentId, callId);
				var aggregate = await _incidentReports.StartFromCallAsync(DepartmentId, UserId, callId, RmsOriginClient.Web, cancellationToken);
				TempData["RecordsMessage"] = existing != null ? _localizer["ExistingReportForCall"].Value : _localizer["IncidentReportStarted"].Value;
				return RedirectToAction(existing != null ? "Details" : "Edit", new { id = aggregate.Report.RmsIncidentReportId });
			}
			catch (ArgumentException ex)
			{
				TempData["RecordsError"] = ex.Message;
				return RedirectToAction("Index");
			}
		}

		/// <summary>Entry from a Call page: opens the existing report or starts one.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> ForCall(int callId)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return NotFound();

			var existing = await _incidentReports.GetForCallAsync(DepartmentId, callId);
			if (existing != null)
				return RedirectToAction("Details", new { id = existing.Report.RmsIncidentReportId });

			return RedirectToAction("Index");
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

			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Read, null, IpAddressHelper.GetRequestIP(Request, true));
			return View(model);
		}

		/// <summary>The immutable submission artifact (the exact payload sent), for administrators auditing a delivery.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> Payload(string id, string submissionId)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return NotFound();

			var submission = aggregate.Submissions.FirstOrDefault(s => s.RmsSubmissionId == submissionId);
			if (submission == null || string.IsNullOrWhiteSpace(submission.PayloadJson))
				return NotFound();

			await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, submission.RevisionId, RmsAccessAuditAction.Export, $"Submission payload {submission.RmsSubmissionId}", IpAddressHelper.GetRequestIP(Request, true));
			return File(Encoding.UTF8.GetBytes(submission.PayloadJson), "application/json", $"neris-{aggregate.Report.RecordNumber ?? aggregate.Report.DraftReference}-{submission.RmsSubmissionId.Substring(0, 8)}.json");
		}

		#endregion

		#region Authoring

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Edit(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();
			if (!CanEditReport(aggregate.Report))
				return Unauthorized();

			var model = await BuildEditAsync(aggregate);
			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Edit(IncidentReportEditView model, CancellationToken cancellationToken)
		{
			var aggregate = await LoadAuthorizedAsync(model.ReportId);
			if (aggregate == null)
				return NotFound();
			if (!CanEditReport(aggregate.Report))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			try
			{
				await _incidentReports.SaveDraftAsync(DepartmentId, UserId, model.ReportId, model.RowVersion, BuildInput(model, department), ClaimsAuthorizationHelper.CanViewRestrictedRecords(), cancellationToken);
				if (model.ValidateAfterSave)
				{
					var issues = await _incidentReports.ValidateAsync(DepartmentId, model.ReportId, true, cancellationToken);
					TempData["RecordsMessage"] = issues.Count == 0 ? _localizer["NoValidationIssues"].Value : string.Format(_localizer["ValidationRun"].Value, issues.Count);
					return RedirectToAction(issues.Any(i => i.Severity == (int)RmsValidationSeverity.Error) ? "Edit" : "Details", new { id = model.ReportId });
				}

				TempData["RecordsMessage"] = _localizer["RecordSaved"].Value;
				return RedirectToAction("Details", new { id = model.ReportId });
			}
			catch (RecordConcurrencyException)
			{
				return await EditWithErrorAsync(aggregate, _localizer["ConcurrencyError"]);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException)
			{
				return await EditWithErrorAsync(aggregate, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Validate(string id, CancellationToken cancellationToken)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();

			try
			{
				var issues = await _incidentReports.ValidateAsync(DepartmentId, id, true, cancellationToken);
				TempData["RecordsMessage"] = issues.Count == 0 ? _localizer["NoValidationIssues"].Value : string.Format(_localizer["ValidationRun"].Value, issues.Count);
				return RedirectToAction("Details", new { id });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		#endregion

		#region Lifecycle

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> SubmitForReview(string id, long rowVersion, CancellationToken cancellationToken)
		{
			return await TransitionAsync(id, () => _incidentReports.SubmitForReviewAsync(DepartmentId, UserId, id, rowVersion, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Review)]
		public async Task<IActionResult> ReturnForCorrection(string id, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			return await TransitionAsync(id, () => _incidentReports.ReturnForCorrectionAsync(DepartmentId, UserId, id, reasonCode, reasonText, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Finalize)]
		public async Task<IActionResult> Finalize(string id, long rowVersion, bool attested, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			if (!attested)
				return await DetailsWithErrorAsync(id, _localizer["Attestation"]);

			return await TransitionAsync(id, () => _incidentReports.FinalizeAsync(DepartmentId, UserId, id, rowVersion, IncidentReportsService.AttestationStatementVersion, IpAddressHelper.GetRequestIP(Request, true), reasonCode, reasonText, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> CorrectAndResubmit(string id, long rowVersion, bool attested, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			if (!attested)
				return await DetailsWithErrorAsync(id, _localizer["Attestation"]);

			return await TransitionAsync(id, () => _incidentReports.CorrectAndResubmitAsync(DepartmentId, UserId, id, rowVersion, IncidentReportsService.AttestationStatementVersion, IpAddressHelper.GetRequestIP(Request, true), reasonCode, reasonText, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> Submit(string id, CancellationToken cancellationToken)
		{
			var result = await TransitionAsync(id, () => _incidentReports.QueueSubmissionAsync(DepartmentId, UserId, id, cancellationToken));
			if (result is RedirectToActionResult)
				TempData["RecordsMessage"] = _localizer["SubmissionQueued"].Value;
			return result;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public async Task<IActionResult> Amend(string id, CancellationToken cancellationToken)
		{
			return await TransitionAsync(id, () => _incidentReports.OpenAmendmentAsync(DepartmentId, UserId, id, cancellationToken), "Edit");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Amend)]
		public async Task<IActionResult> AbandonAmendment(string id, CancellationToken cancellationToken)
		{
			return await TransitionAsync(id, () => _incidentReports.AbandonAmendmentAsync(DepartmentId, UserId, id, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public async Task<IActionResult> Void(string id, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			return await TransitionAsync(id, () => _incidentReports.VoidAsync(DepartmentId, UserId, id, reasonCode, reasonText, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public async Task<IActionResult> CancelDraft(string id, CancellationToken cancellationToken)
		{
			var result = await TransitionAsync(id, () => _incidentReports.CancelAsync(DepartmentId, UserId, id, cancellationToken));
			return result is RedirectToActionResult ? RedirectToAction("Index") : result;
		}

		private async Task<IActionResult> TransitionAsync(string id, Func<Task<IncidentReportAggregate>> action, string successAction = "Details")
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				await action();
				return RedirectToAction(successAction, new { id });
			}
			catch (RecordConcurrencyException)
			{
				return await DetailsWithErrorAsync(id, _localizer["ConcurrencyError"]);
			}
			catch (IncidentReportValidationException ex)
			{
				return await DetailsWithErrorAsync(id, _localizer["ValidationBlocked"] + " " + ex.Message);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		#endregion

		#region Evidence

		/// <summary>
		/// Captures one evidence artifact against the report (RMS plan section 4.5). The reason is required and is
		/// written to the access audit: putting another subsystem's data into an official record is an act somebody
		/// has to own.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> CaptureEvidence(string id, int kind, string captureReason, CancellationToken cancellationToken)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();
			if (!CanEditReport(aggregate.Report))
				return Unauthorized();
			if (string.IsNullOrWhiteSpace(captureReason))
				return await DetailsWithErrorAsync(id, _localizer["EvidenceReasonRequired"]);

			try
			{
				var artifact = await _evidence.CaptureAsync(new RecordEvidenceCaptureRequest
				{
					DepartmentId = DepartmentId,
					RecordId = id,
					RecordKind = RmsRecordKind.IncidentReport,
					Kind = (RmsEvidenceKind)kind,
					CaptureReason = captureReason,
					CallId = aggregate.Report.CallId,
					CoverageStart = aggregate.Report.CallCreatedOn,
					CoverageEnd = aggregate.Report.IncidentClearedOn,
					UnitIds = aggregate.Units.Where(u => u.UnitId.HasValue).Select(u => u.UnitId.Value).ToList(),
					CapturedByUserId = UserId,
					OriginClient = RmsOriginClient.Web
				}, ClaimsAuthorizationHelper.CanViewRestrictedRecords(), cancellationToken);

				TempData["RecordsMessage"] = artifact == null ? _localizer["EvidenceUnavailable"].Value : _localizer["EvidenceCaptured"].Value;
				return RedirectToAction("Details", new { id });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		/// <summary>The stored manifest of one artifact, for an author checking what was actually captured.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> EvidenceManifest(string id, string artifactId)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			var artifact = await _evidence.GetAsync(DepartmentId, artifactId);
			if (artifact == null || !string.Equals(artifact.RecordId, id, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(artifact.ManifestJson))
				return NotFound();

			// Classification is decided at capture and never widened; a restricted manifest needs the restricted grant.
			if (artifact.Classification != (int)RmsEvidenceClassification.Unrestricted && !ClaimsAuthorizationHelper.CanViewRestrictedRecords())
				return Unauthorized();

			await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, artifact.RevisionId, RmsAccessAuditAction.Export, $"Evidence {artifact.RmsEvidenceArtifactId}", IpAddressHelper.GetRequestIP(Request, true));
			return File(Encoding.UTF8.GetBytes(artifact.ManifestJson), "application/json", $"evidence-{artifact.RmsEvidenceArtifactId.Substring(0, 8)}.json");
		}

		#endregion

		#region Settings (NERIS profile + crosswalk)

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> Settings()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var model = await BuildSettingsAsync(moduleState);
			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> Settings(NerisSettingsView model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			try
			{
				var profile = await _neris.GetProfileAsync(DepartmentId) ?? new RmsNerisProfile { DepartmentId = DepartmentId };
				var previousGrantType = profile.GrantType;
				profile.NerisEntityId = string.IsNullOrWhiteSpace(model.NerisEntityId) ? null : model.NerisEntityId.Trim().ToUpperInvariant();
				profile.EntityName = string.IsNullOrWhiteSpace(model.EntityName) ? null : model.EntityName.Trim();
				profile.Environment = string.IsNullOrWhiteSpace(model.Environment) ? NerisEnvironments.Production : model.Environment;
				profile.BaseUrlOverride = string.IsNullOrWhiteSpace(model.BaseUrlOverride) ? null : model.BaseUrlOverride.Trim();
				profile.GrantType = string.IsNullOrWhiteSpace(model.GrantType) ? NerisGrantTypes.Password : model.GrantType;
				profile.AutoSubmitOnFinalize = model.AutoSubmitOnFinalize;
				profile.IsEnabled = model.IsEnabled;

				// The credential is write-only: any filled field replaces the stored one; all blank keeps it.
				NerisCredential credential = null;
				if (!string.IsNullOrWhiteSpace(model.Password) || !string.IsNullOrWhiteSpace(model.ClientSecret) || !string.IsNullOrWhiteSpace(model.Username) || !string.IsNullOrWhiteSpace(model.ClientId))
					credential = new NerisCredential { Username = model.Username?.Trim(), Password = model.Password, ClientId = model.ClientId?.Trim(), ClientSecret = model.ClientSecret };

				// The two grant types fill different slots of the stored credential, so keeping the old one across a
				// change would send empty credentials on the next token request and read as a rejected credential.
				if (credential == null && !string.IsNullOrWhiteSpace(previousGrantType)
					&& !string.Equals(previousGrantType, profile.GrantType, StringComparison.OrdinalIgnoreCase))
					throw new ArgumentException(_localizer["CredentialRequiredForGrantTypeChange"]);

				await _neris.SaveProfileAsync(profile, credential, UserId, cancellationToken);

				var existing = (await _neris.GetCrosswalksAsync(DepartmentId)).Where(c => c.SetKey == IncidentTypeSet && c.LocalSource == NerisCrosswalkSources.CallType).ToList();
				foreach (var row in model.Crosswalk ?? new List<NerisCrosswalkRow>())
				{
					if (string.IsNullOrWhiteSpace(row.CallType))
						continue;
					var current = existing.FirstOrDefault(c => string.Equals(c.LocalCode, row.CallType, StringComparison.OrdinalIgnoreCase));
					if (string.IsNullOrWhiteSpace(row.NerisCode))
					{
						if (current != null)
							await _neris.RemoveCrosswalkAsync(DepartmentId, IncidentTypeSet, NerisCrosswalkSources.CallType, row.CallType, cancellationToken);
					}
					else if (current == null || !string.Equals(current.NerisCode, row.NerisCode, StringComparison.Ordinal))
					{
						await _neris.SaveCrosswalkAsync(DepartmentId, UserId, IncidentTypeSet, NerisCrosswalkSources.CallType, row.CallType, row.NerisCode, cancellationToken);
					}
				}

				TempData["RecordsMessage"] = _localizer["NerisSettingsSaved"].Value;
				return RedirectToAction("Settings");
			}
			catch (ArgumentException ex)
			{
				var rebuilt = await BuildSettingsAsync(moduleState);
				rebuilt.ErrorMessage = ex.Message;
				return View(rebuilt);
			}
		}

		private async Task<NerisSettingsView> BuildSettingsAsync(RecordsModuleState moduleState)
		{
			var profile = await _neris.GetProfileAsync(DepartmentId);
			var model = new NerisSettingsView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				SystemEnabled = Config.NerisConfig.Enabled,
				ContractVersion = _neris.ContractVersion,
				NerisEntityId = profile?.NerisEntityId,
				EntityName = profile?.EntityName,
				Environment = profile?.Environment ?? NerisEnvironments.Production,
				BaseUrlOverride = profile?.BaseUrlOverride,
				GrantType = profile?.GrantType ?? NerisGrantTypes.Password,
				AutoSubmitOnFinalize = profile?.AutoSubmitOnFinalize ?? false,
				IsEnabled = profile?.IsEnabled ?? false,
				HasCredential = !string.IsNullOrWhiteSpace(profile?.EncryptedCredentialJson),
				LastTokenIssuedOn = profile?.LastTokenIssuedOn,
				LastSuccessfulCallOn = profile?.LastSuccessfulCallOn,
				LastError = profile?.LastError,
				SubmissionEnabled = await _neris.IsSubmissionEnabledAsync(DepartmentId),
				Environments = new List<SelectListItem>
				{
					new SelectListItem { Value = NerisEnvironments.Production, Text = NerisEnvironments.Production },
					new SelectListItem { Value = NerisEnvironments.Sandbox, Text = NerisEnvironments.Sandbox }
				},
				GrantTypes = new List<SelectListItem>
				{
					new SelectListItem { Value = NerisGrantTypes.Password, Text = NerisGrantTypes.Password },
					new SelectListItem { Value = NerisGrantTypes.ClientCredentials, Text = NerisGrantTypes.ClientCredentials }
				},
				IncidentTypeCodes = Codes(IncidentTypeSet)
			};

			var crosswalks = (await _neris.GetCrosswalksAsync(DepartmentId)).Where(c => c.SetKey == IncidentTypeSet && c.LocalSource == NerisCrosswalkSources.CallType).ToList();
			var callTypes = (await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>()).Select(t => t.Type).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
			foreach (var mapped in crosswalks.Select(c => c.LocalCode).Where(c => !callTypes.Contains(c, StringComparer.OrdinalIgnoreCase)))
				callTypes.Add(mapped);
			model.Crosswalk = callTypes.Select(t => new NerisCrosswalkRow { CallType = t, NerisCode = crosswalks.FirstOrDefault(c => string.Equals(c.LocalCode, t, StringComparison.OrdinalIgnoreCase))?.NerisCode }).ToList();

			foreach (RmsSubmissionState state in Enum.GetValues(typeof(RmsSubmissionState)))
				model.QueueCounts[state] = await _submissions.CountByStateAsync(DepartmentId, (int)state);

			return model;
		}

		#endregion

		#region Helpers

		private async Task<IncidentReportDetailView> BuildDetailAsync(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return null;

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			var analysis = await _analysis.GetForReportAsync(DepartmentId, id);
			return new IncidentReportDetailView
			{
				SectionRequirements = await _incidentReports.GetSectionRequirementsAsync(DepartmentId, id),
				Analysis = analysis?.Analysis,
				Evidence = await _evidence.GetForRecordAsync(DepartmentId, id),
				EvidenceSources = await _evidence.GetSourceStatesAsync(DepartmentId),
				CanViewRestricted = ClaimsAuthorizationHelper.CanViewRestrictedRecords(),
				Aggregate = aggregate,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				Profile = await _neris.GetProfileAsync(DepartmentId),
				SubmissionEnabled = await _neris.IsSubmissionEnabledAsync(DepartmentId),
				PersonnelNames = await PersonnelNamesAsync(),
				GroupNames = groups.ToDictionary(g => g.DepartmentGroupId, g => g.Name),
				CanEdit = CanEditReport(aggregate.Report),
				CanReview = ClaimsAuthorizationHelper.CanReviewRecords(),
				CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords(),
				CanSubmit = ClaimsAuthorizationHelper.CanSubmitRecords(),
				CanAmend = ClaimsAuthorizationHelper.CanAmendRecords(),
				CanVoid = ClaimsAuthorizationHelper.CanVoidRecords(),
				CanExport = ClaimsAuthorizationHelper.CanExportRecords(),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
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

		private async Task<IActionResult> EditWithErrorAsync(IncidentReportAggregate aggregate, string error)
		{
			var model = await BuildEditAsync(aggregate);
			model.ErrorMessage = error;
			return View("Edit", model);
		}

		/// <summary>Loads a report only when the flag is on and the viewer passes the per-record visibility check.</summary>
		private async Task<IncidentReportAggregate> LoadAuthorizedAsync(string id, bool includeHistory = false)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return null;

			if (!await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, id, DepartmentId))
			{
				await _incidentReports.RecordAccessAsync(DepartmentId, UserId, id, null, RmsAccessAuditAction.Denied, null, IpAddressHelper.GetRequestIP(Request, true));
				return null;
			}

			return await _incidentReports.GetAsync(DepartmentId, id, includeHistory);
		}

		private bool CanEditReport(RmsIncidentReport report)
		{
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return false;

			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				|| string.Equals(report.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(report.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| (report.AmendsRevisionId != null && ClaimsAuthorizationHelper.CanAmendRecords());
		}

		private async Task<IncidentReportEditView> BuildEditAsync(IncidentReportAggregate aggregate)
		{
			var r = aggregate.Report;
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var call = await _callsService.GetCallByIdAsync(r.CallId);
			var model = new IncidentReportEditView
			{
				ReportId = r.RmsIncidentReportId,
				RowVersion = r.RowVersion,
				DraftReference = r.DraftReference,
				RecordNumber = r.RecordNumber,
				CallId = r.CallId,
				CallLabel = call == null ? r.CallId.ToString() : $"{call.Number} - {call.Name}",
				IsAmendment = r.AmendsRevisionId != null,
				IsRejected = r.State == (int)RmsRecordState.Rejected,
				RejectionSummary = r.RejectionSummary,
				State = (RmsRecordState)r.State,
				IncidentNumber = r.IncidentNumber,
				CallCreatedOn = r.CallCreatedOn?.TimeConverter(department),
				CallAnsweredOn = r.CallAnsweredOn?.TimeConverter(department),
				CallArrivalOn = r.CallArrivalOn?.TimeConverter(department),
				IncidentClearedOn = r.IncidentClearedOn?.TimeConverter(department),
				DispatchCenterId = r.DispatchCenterId,
				DeterminantCode = r.DeterminantCode,
				DispatchIncidentCode = r.DispatchIncidentCode,
				Disposition = r.Disposition,
				PeoplePresent = r.PeoplePresent,
				DisplacementCount = r.DisplacementCount,
				AnimalsRescued = r.AnimalsRescued,
				SpecialModifiers = string.IsNullOrWhiteSpace(r.SpecialModifiersCsv) ? new List<string>() : r.SpecialModifiersCsv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
				StationGroupId = r.StationGroupId,
				Narrative = aggregate.Narrative?.Narrative,
				ImpedimentNarrative = aggregate.Narrative?.ImpedimentNarrative,
				OutcomeNarrative = aggregate.Narrative?.OutcomeNarrative,
				Department = department,
				Facts = aggregate.Facts.Where(f => !string.IsNullOrWhiteSpace(f.FactKey)).GroupBy(f => f.FactKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
				Issues = aggregate.Issues
			};

			if (aggregate.Location != null)
			{
				var l = aggregate.Location;
				model.AddressText = l.AddressText; model.Number = l.Number; model.NumberPrefix = l.NumberPrefix; model.NumberSuffix = l.NumberSuffix; model.Street = l.Street; model.UnitValue = l.UnitValue;
				model.Municipality = l.Municipality; model.County = l.County; model.LocationState = l.State; model.PostalCode = l.PostalCode; model.Country = l.Country;
				model.PlaceType = l.PlaceType; model.LocationUse = l.LocationUse; model.CrossStreet1 = l.CrossStreet1; model.CrossStreet2 = l.CrossStreet2; model.Latitude = l.Latitude; model.Longitude = l.Longitude;
			}

			model.Types = aggregate.Types.OrderBy(t => t.Ordinal).Select(t => new IncidentTypeRow { TypeCode = t.TypeCode, IsPrimary = t.IsPrimary }).ToList();
			model.Units = aggregate.Units.Where(u => u.UnitId.HasValue).Select(u => new IncidentUnitRow
			{
				UnitId = u.UnitId.Value, Selected = true, UnitNerisId = u.UnitNerisId, Staffing = u.Staffing, UnableToDispatch = u.UnableToDispatch,
				DispatchedOn = u.DispatchedOn?.TimeConverter(department), EnrouteOn = u.EnrouteOn?.TimeConverter(department), OnSceneOn = u.OnSceneOn?.TimeConverter(department),
				StagingOn = u.StagingOn?.TimeConverter(department), CanceledEnrouteOn = u.CanceledEnrouteOn?.TimeConverter(department), ClearedOn = u.ClearedOn?.TimeConverter(department),
				ResponseMode = u.ResponseMode
			}).ToList();
			model.Aids = aggregate.Aids.OrderBy(a => a.Ordinal).Select(a => new IncidentAidRow { Direction = a.Direction, AidType = a.AidType, CounterpartNerisId = a.CounterpartNerisId, CounterpartName = a.CounterpartName, IsNonFireDepartment = a.IsNonFireDepartment, NonFdType = a.NonFdType }).ToList();
			model.Tactics = aggregate.Tactics.OrderBy(t => t.Ordinal).Select(t => new IncidentTacticRow { TacticCode = t.TacticCode, ActorUnitId = t.ActorUnitId, OccurredOn = t.OccurredOn?.TimeConverter(department) }).ToList();

			model.CanEditRestricted = ClaimsAuthorizationHelper.CanViewRestrictedRecords();
			await BuildSectionsAsync(model, aggregate);

			model.IncidentTypeCodes = Codes(IncidentTypeSet);
			model.TacticCodes = Codes("action_tactic");
			model.AidTypes = Codes("aid_type");
			model.AidDirections = Codes("aid_direction");
			model.NonFdTypes = Codes("aid_nonfd");
			model.SpecialModifierCodes = Codes("special_modifier");
			model.PlaceTypes = Codes("location_place");
			model.LocationUses = Codes("location_use");
			model.ResponseModes = Codes("response_mode");
			model.Stations = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>()).OrderBy(g => g.Name).Select(g => new SelectListItem { Value = g.DepartmentGroupId.ToString(), Text = g.Name }).ToList();
			model.AvailableUnits = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>()).OrderBy(u => u.Name).Select(u => new SelectListItem { Value = u.UnitId.ToString(), Text = u.Name }).ToList();

			// A unit that has since been removed from the department is still on this report. The form posts the rows
			// it renders and the save replaces the set wholesale, so leaving it out of the list would delete the
			// response the night it happened.
			foreach (var missing in model.Units.Where(u => model.AvailableUnits.All(a => a.Value != u.UnitId.ToString())))
			{
				var name = aggregate.Units.FirstOrDefault(x => x.UnitId == missing.UnitId)?.UnitNameSnapshot;
				model.AvailableUnits.Add(new SelectListItem { Value = missing.UnitId.ToString(), Text = string.IsNullOrWhiteSpace(name) ? $"#{missing.UnitId}" : name });
			}

			model.CasualtyPersonTypes = new List<SelectListItem>
			{
				new SelectListItem { Value = RmsCasualtyPersonTypes.Firefighter, Text = _localizer["Firefighter"].Value },
				new SelectListItem { Value = RmsCasualtyPersonTypes.Civilian, Text = _localizer["Civilian"].Value }
			};
			model.CasualtyCauses = Codes("casualty_cause");
			model.CasualtyActions = Codes("casualty_action");
			model.CasualtyTimelines = Codes("casualty_timeline");
			model.DutyTypes = Codes("duty");
			model.PpeCodes = Codes("casualty_ppe");
			model.RescueActionCodes = Codes("rescue_action");
			model.RescueImpedimentCodes = Codes("rescue_impediment");
			model.RescueModes = Codes("rescue_mode");
			model.RescuePaths = Codes("rescue_path");
			model.RescueElevations = Codes("rescue_elevation");
			model.PresenceKnownCodes = Codes("rescue_presence_known");
			model.ExposureItemTypes = Codes("exposure_item");
			model.ExposureDamageTypes = Codes("exposure_damage");
			model.DisplacementCauseCodes = Codes("displace_cause");
			model.Personnel = (await PersonnelNamesAsync()).OrderBy(kvp => kvp.Value).Select(kvp => new SelectListItem { Value = kvp.Key, Text = kvp.Value }).ToList();
			return model;
		}

		/// <summary>
		/// The conditional sections this report must or may carry, in rule order, each with its existing rows and
		/// one spare. Sections already on the record but no longer demanded by the selected types are still shown,
		/// so an author who changed the incident type can take the stale section back off rather than being stuck
		/// with a section validation will reject.
		/// </summary>
		private async Task BuildSectionsAsync(IncidentReportEditView model, IncidentReportAggregate aggregate)
		{
			var requirements = await _incidentReports.GetSectionRequirementsAsync(DepartmentId, aggregate.Report.RmsIncidentReportId);
			var kinds = requirements.Select(r => r.Kind).ToList();
			foreach (var kind in aggregate.Modules.Select(m => (RmsIncidentModuleKind)m.ModuleKind).Distinct())
			{
				if (!kinds.Contains(kind))
					kinds.Add(kind);
			}

			foreach (var kind in kinds)
			{
				var requirement = requirements.FirstOrDefault(r => r.Kind == kind);
				var descriptor = RmsIncidentModuleCatalog.Get(kind);
				var rows = aggregate.Modules.Where(m => m.ModuleKind == (int)kind).OrderBy(m => m.Ordinal).ToList();

				model.Sections.Add(new IncidentSectionView
				{
					Kind = kind,
					Required = requirement?.Required ?? false,
					Reason = requirement?.Reason ?? _localizer["SectionNoLongerApplies"].Value,
					Present = rows.Count > 0,
					IsCollection = descriptor?.IsCollection ?? false,
					PayloadPath = descriptor?.PayloadPath,
					SchemaName = descriptor?.SchemaName,
					PrimaryCodes = Codes(requirement?.PrimaryCodeSet),
					SecondaryCodes = Codes(requirement?.SecondaryCodeSet)
				});

				foreach (var row in rows)
				{
					model.Modules.Add(new IncidentModuleRow
					{
						Kind = (int)kind, Included = true, PrimaryCode = row.PrimaryCode, SecondaryCode = row.SecondaryCode, Quantity = row.Quantity,
						QuantityUnit = row.QuantityUnit, OccurredOn = row.OccurredOn?.TimeConverter(model.Department), DetailJson = row.DetailJson
					});
				}

				// A singleton section always gets its one row; a collection gets a spare to add the next entry.
				if (rows.Count == 0 || (descriptor?.IsCollection ?? false))
					model.Modules.Add(new IncidentModuleRow { Kind = (int)kind, Included = false });
			}

			model.Resources = aggregate.Resources.OrderBy(r => r.Ordinal)
				.Select(r => new IncidentResourceRow { ResourceCode = r.ResourceCode, Quantity = r.Quantity, Detail = r.Detail }).ToList();

			model.Casualties = aggregate.Casualties.OrderBy(c => c.Ordinal).Select(c => new IncidentCasualtyRow
			{
				Included = true, Kind = c.Kind, PersonType = c.PersonType, PersonnelUserId = c.PersonnelUserId, Rank = c.Rank, YearsOfService = c.YearsOfService,
				JobClassification = c.JobClassification, BirthMonthYear = c.BirthMonthYear, Gender = c.Gender, Race = c.Race, WasInjured = c.WasInjured, WasFatal = c.WasFatal,
				CasualtyCause = c.CasualtyCause, CasualtyAction = c.CasualtyAction, CasualtyTimeline = c.CasualtyTimeline, DutyType = c.DutyType, Ppe = Split(c.PpeCsv),
				InjuryDetailJson = c.InjuryDetailJson, RescueType = c.RescueType, RescueActions = Split(c.RescueActionsCsv), RescueImpediments = Split(c.RescueImpedimentsCsv),
				RescueMode = c.RescueMode, RescuePath = c.RescuePath, RescueElevation = c.RescueElevation, PresenceKnown = c.PresenceKnown,
				OccurredOn = c.OccurredOn?.TimeConverter(model.Department), DetailJson = c.DetailJson
			}).ToList();

			model.Exposures = aggregate.Exposures.OrderBy(e => e.Ordinal).Select(e => new IncidentExposureRow
			{
				Included = true, LocationKind = e.LocationKind, ItemType = e.ItemType, DamageType = e.DamageType, LocationUse = e.LocationUse, PeoplePresent = e.PeoplePresent,
				DisplacementCount = e.DisplacementCount, DisplacementCauses = Split(e.DisplacementCausesCsv), AddressText = e.AddressText, Street = e.Street,
				Municipality = e.Municipality, State = e.State, PostalCode = e.PostalCode, Latitude = e.Latitude, Longitude = e.Longitude,
				EstimatedValue = e.EstimatedValue, EstimatedLoss = e.EstimatedLoss, DetailJson = e.DetailJson
			}).ToList();
		}

		private IncidentReportDraftInput BuildInput(IncidentReportEditView model, Department department)
		{
			var hasLocation = new[] { model.AddressText, model.Number, model.Street, model.Municipality, model.PostalCode, model.PlaceType, model.LocationUse, model.CrossStreet1 }.Any(s => !string.IsNullOrWhiteSpace(s)) || model.Latitude.HasValue;
			return new IncidentReportDraftInput
			{
				IncidentNumber = model.IncidentNumber,
				CallCreatedOn = ToUtc(model.CallCreatedOn, department),
				CallAnsweredOn = ToUtc(model.CallAnsweredOn, department),
				CallArrivalOn = ToUtc(model.CallArrivalOn, department),
				IncidentClearedOn = ToUtc(model.IncidentClearedOn, department),
				DispatchCenterId = model.DispatchCenterId,
				DeterminantCode = model.DeterminantCode,
				DispatchIncidentCode = model.DispatchIncidentCode,
				Disposition = model.Disposition,
				PeoplePresent = model.PeoplePresent,
				DisplacementCount = model.DisplacementCount,
				AnimalsRescued = model.AnimalsRescued,
				SpecialModifiers = model.SpecialModifiers ?? new List<string>(),
				StationGroupId = model.StationGroupId,
				Location = !hasLocation ? null : new IncidentLocationInput
				{
					AddressText = model.AddressText, Number = model.Number, NumberPrefix = model.NumberPrefix, NumberSuffix = model.NumberSuffix, Street = model.Street, UnitValue = model.UnitValue,
					Municipality = model.Municipality, County = model.County, State = model.LocationState, PostalCode = model.PostalCode, Country = model.Country,
					PlaceType = model.PlaceType, LocationUse = model.LocationUse, CrossStreet1 = model.CrossStreet1, CrossStreet2 = model.CrossStreet2, Latitude = model.Latitude, Longitude = model.Longitude
				},
				Types = (model.Types ?? new List<IncidentTypeRow>()).Where(t => !string.IsNullOrWhiteSpace(t.TypeCode)).Select(t => new IncidentTypeInput { TypeCode = t.TypeCode, IsPrimary = t.IsPrimary }).ToList(),
				Units = (model.Units ?? new List<IncidentUnitRow>()).Where(u => u.Selected && u.UnitId > 0).Select(u => new IncidentUnitResponseInput
				{
					UnitId = u.UnitId, UnitNerisId = u.UnitNerisId, Staffing = u.Staffing, UnableToDispatch = u.UnableToDispatch,
					DispatchedOn = ToUtc(u.DispatchedOn, department), EnrouteOn = ToUtc(u.EnrouteOn, department), OnSceneOn = ToUtc(u.OnSceneOn, department),
					StagingOn = ToUtc(u.StagingOn, department), CanceledEnrouteOn = ToUtc(u.CanceledEnrouteOn, department), ClearedOn = ToUtc(u.ClearedOn, department),
					ResponseMode = u.ResponseMode
				}).ToList(),
				Aids = (model.Aids ?? new List<IncidentAidRow>()).Select(a => new IncidentAidInput { Direction = a.Direction, AidType = a.AidType, CounterpartNerisId = a.CounterpartNerisId, CounterpartName = a.CounterpartName, IsNonFireDepartment = a.IsNonFireDepartment, NonFdType = a.NonFdType }).ToList(),
				Tactics = (model.Tactics ?? new List<IncidentTacticRow>()).Where(t => !string.IsNullOrWhiteSpace(t.TacticCode)).Select(t => new IncidentTacticInput { TacticCode = t.TacticCode, ActorUnitId = t.ActorUnitId, OccurredOn = ToUtc(t.OccurredOn, department) }).ToList(),
				Narrative = model.Narrative,
				ImpedimentNarrative = model.ImpedimentNarrative,
				OutcomeNarrative = model.OutcomeNarrative,
				// The Web form always renders every section, so it always posts every section: an empty list here
				// means the author removed the rows, not that the client could not show them.
				Modules = (model.Modules ?? new List<IncidentModuleRow>()).Where(m => m.Included && m.Kind > 0).Select(m => new IncidentModuleInput
				{
					Kind = (RmsIncidentModuleKind)m.Kind, PrimaryCode = m.PrimaryCode, SecondaryCode = m.SecondaryCode, Quantity = m.Quantity,
					QuantityUnit = m.QuantityUnit, OccurredOn = ToUtc(m.OccurredOn, department), DetailJson = m.DetailJson
				}).ToList(),
				Resources = (model.Resources ?? new List<IncidentResourceRow>()).Where(r => !string.IsNullOrWhiteSpace(r.ResourceCode))
					.Select(r => new IncidentResourceInput { ResourceCode = r.ResourceCode, Quantity = r.Quantity, Detail = r.Detail }).ToList(),
				Casualties = (model.Casualties ?? new List<IncidentCasualtyRow>()).Where(c => c.Included).Select(c => new IncidentCasualtyRescueInput
				{
					Kind = (RmsCasualtyRescueKind)c.Kind, PersonType = c.PersonType, PersonnelUserId = c.PersonnelUserId, Rank = c.Rank, YearsOfService = c.YearsOfService,
					JobClassification = c.JobClassification, BirthMonthYear = c.BirthMonthYear, Gender = c.Gender, Race = c.Race, WasInjured = c.WasInjured, WasFatal = c.WasFatal,
					CasualtyCause = c.CasualtyCause, CasualtyAction = c.CasualtyAction, CasualtyTimeline = c.CasualtyTimeline, DutyType = c.DutyType,
					Ppe = c.Ppe ?? new List<string>(), InjuryDetailJson = c.InjuryDetailJson, RescueType = c.RescueType,
					RescueActions = c.RescueActions ?? new List<string>(), RescueImpediments = c.RescueImpediments ?? new List<string>(),
					RescueMode = c.RescueMode, RescuePath = c.RescuePath, RescueElevation = c.RescueElevation, PresenceKnown = c.PresenceKnown,
					OccurredOn = ToUtc(c.OccurredOn, department), DetailJson = c.DetailJson
				}).ToList(),
				Exposures = (model.Exposures ?? new List<IncidentExposureRow>()).Where(e => e.Included).Select(e => new IncidentExposureInput
				{
					LocationKind = e.LocationKind, ItemType = e.ItemType, DamageType = e.DamageType, LocationUse = e.LocationUse, PeoplePresent = e.PeoplePresent,
					DisplacementCount = e.DisplacementCount, DisplacementCauses = e.DisplacementCauses ?? new List<string>(), AddressText = e.AddressText, Street = e.Street,
					Municipality = e.Municipality, State = e.State, PostalCode = e.PostalCode, Latitude = e.Latitude, Longitude = e.Longitude,
					EstimatedValue = e.EstimatedValue, EstimatedLoss = e.EstimatedLoss, DetailJson = e.DetailJson
				}).ToList(),
				OriginClient = RmsOriginClient.Web
			};
		}

		private static List<string> Split(string csv)
		{
			return string.IsNullOrWhiteSpace(csv)
				? new List<string>()
				: csv.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
		}

		private List<SelectListItem> Codes(string setKey)
		{
			if (string.IsNullOrWhiteSpace(setKey))
				return new List<SelectListItem>();

			var set = _neris.GetValueSet(setKey);
			return (set?.Codes ?? new List<string>()).Select(c => new SelectListItem { Value = c, Text = c.Replace("||", " / ").Replace('_', ' ') }).ToList();
		}

		private static DateTime? ToUtc(DateTime? local, Department department)
		{
			if (!local.HasValue || local.Value == DateTime.MinValue)
				return null;
			if (department == null || string.IsNullOrWhiteSpace(department.TimeZone))
				return DateTime.SpecifyKind(local.Value, DateTimeKind.Utc);

			return DateTimeHelpers.ConvertToUtc(local.Value, department.TimeZone, true);
		}

		private async Task<Dictionary<string, string>> PersonnelNamesAsync()
		{
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var name in names ?? new List<PersonName>())
			{
				if (!string.IsNullOrWhiteSpace(name.UserId))
					map[name.UserId] = name.Name;
			}
			return map;
		}

		#endregion
	}
}
