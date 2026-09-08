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

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// RMS-5 occupancy master (RMS plan section 4.3): list, detail, editor, hazards, contact links, and the Contacts
	/// pre-plan crosswalk with the write-ownership switch. Reads need Record_View; writes need the prevention
	/// administrator permission (69), which the service enforces again.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordOccupanciesController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsOccupancyService _occupancies;
		private readonly IRecordsInspectionsService _inspections;
		private readonly IRecordsPermitsService _permits;
		private readonly IRecordsHydrantsService _hydrants;
		private readonly IRecordsPreventionAttachmentsService _attachments;
		private readonly IContactsService _contacts;

		public RecordOccupanciesController(IRecordsOccupancyService occupancies, IRecordsInspectionsService inspections, IRecordsPermitsService permits, IRecordsHydrantsService hydrants,
			IRecordsPreventionAttachmentsService attachments, IContactsService contacts, IRecordsCutoverService cutover, IFeatureToggleService featureToggles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer) : base(cutover, featureToggles, localizer)
		{
			_occupancies = occupancies;
			_inspections = inspections;
			_permits = permits;
			_hydrants = hydrants;
			_attachments = attachments;
			_contacts = contacts;
		}

		private const string Flag = FeatureFlagKeys.RecordsPreventionOccupancy;

		[HttpGet]
		public async Task<IActionResult> Index(string q = null, int? status = null, bool overdue = false, bool hazmat = false, int page = 1)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordOccupanciesIndexView { Search = q, Status = status, ReviewOverdue = overdue, HazmatOnly = hazmat, Page = Math.Max(1, page) });
				var query = new RmsOccupancyQuery { Search = q, Status = status, ReviewOverdue = overdue ? true : (bool?)null, HazmatOnSite = hazmat ? true : (bool?)null, Skip = (model.Page - 1) * model.PageSize, Take = model.PageSize };
				model.Occupancies = await _occupancies.ListAsync(DepartmentId, UserId, query);
				model.Total = await _occupancies.CountAsync(DepartmentId, UserId, query);
				model.Reconciliation = await _occupancies.GetReconciliationStatusAsync(DepartmentId);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		public async Task<IActionResult> Details(string id)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var aggregate = await _occupancies.GetAsync(DepartmentId, UserId, id);
				if (aggregate == null) return NotFound();
				var model = Prepare(new RecordOccupancyDetailsView { Aggregate = aggregate });
				model.InspectionsOn = await FlagAsync(FeatureFlagKeys.RecordsPreventionInspections);
				model.PermitsOn = await FlagAsync(FeatureFlagKeys.RecordsPreventionPermits);
				var contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>();
				model.ContactNames = NameMap(contacts, c => c.ContactId, c => ProtectedDataEnvelope.SafeDisplay(c.Name));
				model.Contacts = contacts.OrderBy(c => c.Name).Select(c => new SelectListItem { Value = c.ContactId, Text = ProtectedDataEnvelope.SafeDisplay(c.Name) }).ToList();
				if (model.InspectionsOn)
				{
					model.Inspections = await _inspections.ListAsync(DepartmentId, UserId, new RmsInspectionQuery { OccupancyId = id, Take = 25 });
					model.OpenViolations = await _inspections.GetViolationsForOccupancyAsync(DepartmentId, UserId, id, true);
					model.Programs = (await _inspections.GetProgramsAsync(DepartmentId, UserId, false)).Select(p => new SelectListItem { Value = p.RmsInspectionProgramId, Text = p.Name }).ToList();
				}
				if (model.PermitsOn) model.Permits = await _permits.ListAsync(DepartmentId, UserId, new RmsPermitQuery { OccupancyId = id, Take = 25 });
				model.Attachments = await _attachments.GetMetadataAsync(DepartmentId, UserId, RmsPreventionParentKind.Occupancy, id);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(string id = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordOccupancyEditView());
				if (!string.IsNullOrWhiteSpace(id))
				{
					var aggregate = await _occupancies.GetAsync(DepartmentId, UserId, id);
					if (aggregate == null) return NotFound();
					model.Occupancy = aggregate.Occupancy;
				}
				await PopulateEditAsync(model);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(RecordOccupancyEditView model, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var saved = await _occupancies.SaveAsync(DepartmentId, UserId, model.Occupancy, cancellationToken);
				Notify("OccupancySaved");
				return RedirectToAction(nameof(Details), new { id = saved.RmsOccupancyId });
			}
			catch (Exception ex)
			{
				var failure = Fail(ex);
				if (failure != null) return failure;
				Prepare(model);
				model.ErrorMessage = TempData["RecordsError"] as string;
				await PopulateEditAsync(model);
				return View(model);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.DeleteAsync(DepartmentId, UserId, id, cancellationToken); Notify("OccupancyDeleted"); return RedirectToAction(nameof(Index)); }
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Details), new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> MarkReviewed(string id, int nextReviewMonths, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.MarkReviewedAsync(DepartmentId, UserId, id, nextReviewMonths, cancellationToken); Notify("OccupancyReviewed"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SaveHazard(string id, RmsOccupancyHazard hazard, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { hazard.RmsOccupancyId = id; await _occupancies.SaveHazardAsync(DepartmentId, UserId, hazard, cancellationToken); Notify("HazardSaved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> DeleteHazard(string id, string hazardId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.DeleteHazardAsync(DepartmentId, UserId, hazardId, cancellationToken); Notify("HazardRemoved"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> LinkContact(string id, string contactId, int role, bool isPrimary, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.LinkContactAsync(DepartmentId, UserId, id, contactId, (RmsOccupancyContactRole)role, isPrimary, cancellationToken); Notify("ContactLinked"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> UnlinkContact(string id, string linkId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.UnlinkContactAsync(DepartmentId, UserId, linkId, cancellationToken); Notify("ContactUnlinked"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		// ---- Crosswalk and write ownership ---------------------------------------------------------------------

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Crosswalk()
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordOccupancyCrosswalkView { Status = await _occupancies.GetReconciliationStatusAsync(DepartmentId) });
				model.Candidates = await _occupancies.GetCandidatesAsync(DepartmentId, UserId, RmsOccupancyCrosswalkState.Candidate, 0, 500);
				model.Decided = (await _occupancies.GetCandidatesAsync(DepartmentId, UserId, RmsOccupancyCrosswalkState.Linked, 0, 100))
					.Concat(await _occupancies.GetCandidatesAsync(DepartmentId, UserId, RmsOccupancyCrosswalkState.Rejected, 0, 100))
					.OrderByDescending(c => c.DecidedOn).ToList();
				var occupancies = await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 });
				model.OccupancyNames = NameMap(occupancies, o => o.RmsOccupancyId, o => (o.OccupancyNumber + " " + o.Name).Trim());
				model.Occupancies = occupancies.OrderBy(o => o.Name).Select(o => new SelectListItem { Value = o.RmsOccupancyId, Text = (o.OccupancyNumber + " " + o.Name + " - " + o.AddressText).Trim() }).ToList();
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Inventory(CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var result = await _occupancies.InventoryCandidatesAsync(DepartmentId, UserId, cancellationToken);
				Notify("InventoryResult", result.SourcesScanned, result.CandidatesCreated, result.CandidatesUpdated, result.AlreadyDecided);
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Crosswalk));
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Link(string crosswalkId, string occupancyId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.LinkCandidateAsync(DepartmentId, UserId, crosswalkId, string.IsNullOrWhiteSpace(occupancyId) ? null : occupancyId, cancellationToken); Notify("CandidateLinked"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Crosswalk));
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Reject(string crosswalkId, string reason, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _occupancies.RejectCandidateAsync(DepartmentId, UserId, crosswalkId, reason, cancellationToken); Notify("CandidateRejected"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Crosswalk));
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Merge(string id, string targetOccupancyId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var target = await _occupancies.MergeAsync(DepartmentId, UserId, id, targetOccupancyId, cancellationToken);
				Notify("OccupancyMerged");
				return RedirectToAction(nameof(Details), new { id = target.RmsOccupancyId });
			}
			catch (Exception ex) { return Fail(ex) ?? RedirectToAction(nameof(Details), new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> SwitchOwnership(string reason, bool acknowledge, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			if (!acknowledge) { NotifyError(Localizer["SwitchOwnershipAcknowledge"].Value); return RedirectToAction(nameof(Crosswalk)); }
			try { await _occupancies.SwitchWriteOwnershipAsync(DepartmentId, UserId, reason, cancellationToken); Notify("OwnershipSwitched"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Crosswalk));
		}

		private async Task PopulateEditAsync(RecordOccupancyEditView model)
		{
			if (await FlagAsync(FeatureFlagKeys.RecordsPreventionHydrants))
			{
				try
				{
					model.Hydrants = new[] { new SelectListItem { Value = "", Text = "-" } }
						.Concat((await _hydrants.ListAsync(DepartmentId, UserId)).OrderBy(h => h.HydrantNumber).Select(h => new SelectListItem { Value = h.RmsHydrantId, Text = h.HydrantNumber + (string.IsNullOrWhiteSpace(h.AddressText) ? "" : " - " + h.AddressText), Selected = h.RmsHydrantId == model.Occupancy?.NearestHydrantId }))
						.ToList();
				}
				catch (RecordsModuleDisabledException) { }
			}
		}
	}
}
