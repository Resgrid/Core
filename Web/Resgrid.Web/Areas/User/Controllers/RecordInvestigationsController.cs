using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// RMS-5 investigation cases (RMS plan section 4.4). RecordRestricted_View is the entry policy; the service adds
	/// case-membership authorization and audits every read, so this controller only shapes pages.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.RecordRestricted_View)]
	public class RecordInvestigationsController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsInvestigationsService _investigations;
		private readonly IRecordsOccupancyService _occupancies;
		private readonly IUserProfileService _profiles;

		public RecordInvestigationsController(IRecordsInvestigationsService investigations, IRecordsOccupancyService occupancies, IUserProfileService profiles,
			IRecordsCutoverService cutover, IFeatureToggleService featureToggles, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_investigations = investigations;
			_occupancies = occupancies;
			_profiles = profiles;
		}

		private const string Flag = FeatureFlagKeys.RecordsInvestigations;
		private string Ip => IpAddressHelper.GetRequestIP(Request, true);

		[HttpGet]
		public async Task<IActionResult> Index(bool includeClosed = false)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { return View(Prepare(new RecordInvestigationsIndexView { IncludeClosed = includeClosed, Cases = await _investigations.ListMyCasesAsync(DepartmentId, UserId, includeClosed) })); }
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		public async Task<IActionResult> Details(string id)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var aggregate = await _investigations.GetAsync(DepartmentId, UserId, id, Ip);
				if (aggregate == null) return NotFound();
				var model = Prepare(new RecordInvestigationDetailsView { Aggregate = aggregate });
				var profiles = await _profiles.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
				model.UserNames = profiles.ToDictionary(p => p.Key, p => (p.Value.FirstName + " " + p.Value.LastName).Trim(), StringComparer.Ordinal);
				model.DepartmentUsers = profiles.OrderBy(p => p.Value.LastName).ThenBy(p => p.Value.FirstName).Select(p => new SelectListItem { Value = p.Key, Text = (p.Value.FirstName + " " + p.Value.LastName).Trim() }).ToList();
				model.Members = aggregate.Members.Where(m => m.IsActive).Select(m => new SelectListItem { Value = m.UserId, Text = model.UserName(m.UserId) }).ToList();
				if (model.IsLead) model.Audit = await _investigations.GetAccessAuditAsync(DepartmentId, UserId, id, 50);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		public async Task<IActionResult> Open(string occupancyId = null, int? callId = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			var model = Prepare(new RecordInvestigationOpenView { OccupancyId = occupancyId, CallId = callId });
			await PopulateOccupanciesAsync(model);
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Open(RecordInvestigationOpenView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var opened = await _investigations.OpenAsync(DepartmentId, UserId, model.Title, string.IsNullOrWhiteSpace(model.OccupancyId) ? null : model.OccupancyId, model.CallId, model.IncidentSummary, cancellationToken);
				Notify("CaseOpened", opened.CaseNumber);
				return RedirectToAction(nameof(Details), new { id = opened.RmsInvestigationCaseId });
			}
			catch (Exception ex)
			{
				var failure = Fail(ex);
				if (failure != null) return failure;
				Prepare(model);
				model.ErrorMessage = TempData["RecordsError"] as string;
				await PopulateOccupanciesAsync(model);
				return View(model);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Update(string id, long rowVersion, string title, string incidentSummary, string occupancyId, int? callId, string leadInvestigatorUserId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				await _investigations.UpdateAsync(DepartmentId, UserId, new RmsInvestigationCase { RmsInvestigationCaseId = id, RowVersion = rowVersion, Title = title, IncidentSummary = incidentSummary, RmsOccupancyId = string.IsNullOrWhiteSpace(occupancyId) ? null : occupancyId, CallId = callId, LeadInvestigatorUserId = leadInvestigatorUserId }, cancellationToken);
				Notify("CaseSaved");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddMember(string id, string userId, int role, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.AddMemberAsync(DepartmentId, UserId, id, userId, (RmsInvestigationRole)role, cancellationToken); Notify("MemberAdded"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveMember(string id, string memberId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.RemoveMemberAsync(DepartmentId, UserId, id, memberId, cancellationToken); Notify("MemberRemoved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> LinkIncident(string id, string recordId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.LinkIncidentAsync(DepartmentId, UserId, id, recordId, cancellationToken); Notify("IncidentLinked"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> UnlinkIncident(string id, string linkId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.UnlinkIncidentAsync(DepartmentId, UserId, id, linkId, cancellationToken); Notify("IncidentUnlinked"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveNote(string id, string noteId, int kind, string occurredOn, string subject, string body, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				if (string.IsNullOrWhiteSpace(noteId)) await _investigations.AddNoteAsync(DepartmentId, UserId, id, (RmsInvestigationNoteKind)kind, ParseUtc(occurredOn) ?? DateTime.UtcNow, subject, body, cancellationToken);
				else await _investigations.UpdateNoteAsync(DepartmentId, UserId, noteId, subject, body, cancellationToken);
				Notify("NoteSaved");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddEvidence(string id, int kind, string description, string collectedOn, string collectedFrom, string storageLocation, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				await _investigations.AddEvidenceAsync(DepartmentId, UserId, id, new RmsInvestigationEvidence { Kind = kind, Description = description, CollectedOn = ParseUtc(collectedOn) ?? DateTime.UtcNow, CollectedByUserId = UserId, CollectedFrom = collectedFrom, StorageLocation = storageLocation }, cancellationToken);
				Notify("EvidenceAdded");
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Custody(string id, string evidenceId)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var aggregate = await _investigations.GetAsync(DepartmentId, UserId, id, Ip);
				var evidence = aggregate?.Evidence.FirstOrDefault(e => e.RmsInvestigationEvidenceId == evidenceId);
				if (evidence == null) return NotFound();
				var model = Prepare(new RecordInvestigationCustodyView { CaseId = id, Evidence = evidence, Chain = await _investigations.GetCustodyChainAsync(DepartmentId, UserId, evidenceId) });
				var profiles = await _profiles.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
				model.UserNames = profiles.ToDictionary(p => p.Key, p => (p.Value.FirstName + " " + p.Value.LastName).Trim(), StringComparer.Ordinal);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> TransferCustody(string id, string evidenceId, string toUserId, string toExternal, string reason, int resultingState, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.TransferCustodyAsync(DepartmentId, UserId, evidenceId, string.IsNullOrWhiteSpace(toUserId) ? null : toUserId, toExternal, reason, (RmsEvidenceState)resultingState, cancellationToken); Notify("CustodyTransferred"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Custody), new { id, evidenceId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddReferral(string id, string agency, string reason, string referenceNumber, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.AddReferralAsync(DepartmentId, UserId, id, agency, reason, referenceNumber, cancellationToken); Notify("ReferralAdded"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReferralState(string id, string referralId, int state, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.UpdateReferralStateAsync(DepartmentId, UserId, referralId, (RmsReferralState)state, cancellationToken); Notify("ReferralUpdated"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Findings(string id, int classification, string causeDetail, string originDescription, string findings, bool recommendsAmendment, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.RecordFindingsAsync(DepartmentId, UserId, id, (RmsFireCauseClassification)classification, causeDetail, originDescription, findings, recommendsAmendment, cancellationToken); Notify("FindingsRecorded"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ApproveFindings(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.ApproveFindingsAsync(DepartmentId, UserId, id, cancellationToken); Notify("FindingsApproved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReturnFindings(string id, string reason, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.ReturnFindingsAsync(DepartmentId, UserId, id, reason, cancellationToken); Notify("FindingsReturned"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Close(string id, string reason, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.CloseAsync(DepartmentId, UserId, id, reason, cancellationToken); Notify("CaseClosed"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Reopen(string id, string reason, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _investigations.ReopenAsync(DepartmentId, UserId, id, reason, cancellationToken); Notify("CaseReopened"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Export(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var bytes = await _investigations.ExportAsync(DepartmentId, UserId, id, Ip, cancellationToken);
				return File(bytes, "application/json", $"investigation-{id}.json");
			}
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Details), new { id }); }
		}

		private async Task PopulateOccupanciesAsync(RecordInvestigationOpenView model)
		{
			try
			{
				model.Occupancies = new[] { new SelectListItem { Value = "", Text = "-" } }
					.Concat((await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 })).OrderBy(o => o.Name).Select(o => new SelectListItem { Value = o.RmsOccupancyId, Text = (o.OccupancyNumber + " " + o.Name + " - " + o.AddressText).Trim(), Selected = o.RmsOccupancyId == model.OccupancyId }))
					.ToList();
			}
			catch (Exception ex) when (ex is RecordsModuleDisabledException || ex is UnauthorizedAccessException) { }
		}
	}
}
