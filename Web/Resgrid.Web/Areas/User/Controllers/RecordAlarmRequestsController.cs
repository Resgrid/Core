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
	/// Smoke / CO alarm requests (M0284), part of the CRR module: the open queue, the request form (from an occupancy, a call
	/// or blank), completing a request into the CRR activity that counts the units left, declining and reopening. Reads need
	/// Record_View; changes need the prevention administrator permission, which the service enforces again.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordAlarmRequestsController : RecordsPreventionMvcControllerBase
	{
		private readonly IRecordsAlarmRequestsService _requests;
		private readonly IRecordsCrrService _crr;
		private readonly IRecordsOccupancyService _occupancies;
		private readonly IContactsService _contacts;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentsService _departments;
		private readonly ICallsService _calls;
		private readonly IRecordsAuthorizationService _authorization;

		public RecordAlarmRequestsController(IRecordsAlarmRequestsService requests, IRecordsCrrService crr, IRecordsOccupancyService occupancies, IContactsService contacts,
			IUserProfileService profiles, IDepartmentsService departments, ICallsService calls, IRecordsAuthorizationService authorization,
			IRecordsCutoverService cutover, IFeatureToggleService featureToggles, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
			: base(cutover, featureToggles, localizer)
		{
			_requests = requests;
			_crr = crr;
			_occupancies = occupancies;
			_contacts = contacts;
			_profiles = profiles;
			_departments = departments;
			_calls = calls;
			_authorization = authorization;
		}

		private const string Flag = FeatureFlagKeys.RecordsPreventionCrr;

		/// <summary>The queue; open requests oldest first by default, 0 for every status.</summary>
		[HttpGet]
		public async Task<IActionResult> Index(int status = (int)RmsAlarmRequestStatus.Open, bool mine = false, int page = 1)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordAlarmRequestsIndexView { Status = status == 0 ? (int?)null : status, MineOnly = mine, Page = Math.Max(1, page) });
				var query = new RmsAlarmRequestQuery
				{
					Statuses = model.Status.HasValue ? new List<int> { model.Status.Value } : null,
					AssignedToUserId = mine ? UserId : null,
					OldestFirst = model.Status == (int)RmsAlarmRequestStatus.Open,
					Skip = (model.Page - 1) * model.PageSize,
					Take = model.PageSize
				};
				model.Requests = await _requests.ListAsync(DepartmentId, UserId, query);
				model.Total = await _requests.CountAsync(DepartmentId, UserId, query);
				model.OpenCount = await _requests.CountAsync(DepartmentId, UserId, new RmsAlarmRequestQuery { Statuses = new List<int> { (int)RmsAlarmRequestStatus.Open } });
				await PopulateOccupancyNamesAsync(model.OccupancyNames, model.Requests.Select(r => r.RmsOccupancyId));
				model.UserNames = await UserNamesAsync();
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
				var request = await _requests.GetAsync(DepartmentId, UserId, id);
				if (request == null) return NotFound();
				var model = Prepare(new RecordAlarmRequestDetailsView { Request = request });
				if (!string.IsNullOrWhiteSpace(request.RmsCrrActivityId)) model.Activity = await _crr.GetAsync(DepartmentId, UserId, request.RmsCrrActivityId);
				if (!string.IsNullOrWhiteSpace(request.RmsOccupancyId))
				{
					try { model.Occupancy = (await _occupancies.GetAsync(DepartmentId, UserId, request.RmsOccupancyId))?.Occupancy; }
					catch (RecordsModuleDisabledException) { }
				}
				if (!string.IsNullOrWhiteSpace(request.RequesterContactId))
				{
					var contact = (await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>()).FirstOrDefault(c => c.ContactId == request.RequesterContactId);
					model.ContactName = contact == null ? request.RequesterContactId : ProtectedDataEnvelope.SafeDisplay(contact.Name);
				}
				model.UserNames = await UserNamesAsync();
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		/// <summary>New or open request. From an occupancy or a call the form opens filled in for that place.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(string id = null, string occupancyId = null, int? callId = null)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var model = Prepare(new RecordAlarmRequestEditView());
				if (!string.IsNullOrWhiteSpace(id))
				{
					var request = await _requests.GetAsync(DepartmentId, UserId, id);
					if (request == null) return NotFound();
					if (!request.IsOpen()) return RedirectToAction(nameof(Details), new { id });
					model.Request = request;
				}
				else
				{
					model.Request.RmsOccupancyId = occupancyId;
					if (callId.HasValue)
					{
						var call = await _calls.GetCallByIdAsync(callId.Value);
						if (call != null && !call.IsDeleted && await _authorization.CanReadSourceCallAsync(UserId, DepartmentId, call))
						{
							model.Request.CallId = call.CallId;
							model.Request.Source = (int)RmsAlarmRequestSource.DuringCall;
							model.Request.LocationText = call.Address;
						}
					}
				}
				await PopulateEditAsync(model);
				return View(model);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Edit(RecordAlarmRequestEditView model, string requestedOn, string scheduledFor, int? callId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				model.Request.RequestedOn = ParseEnteredUtc(requestedOn) ?? model.Request.RequestedOn;
				model.Request.ScheduledFor = ParseEnteredUtc(scheduledFor);
				model.Request.CallId = callId;
				var saved = await _requests.SaveAsync(DepartmentId, UserId, model.Request, cancellationToken);
				Notify("AlarmRequestSaved", saved.RequestNumber);
				return RedirectToAction(nameof(Details), new { id = saved.RmsCrrAlarmRequestId });
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
		public async Task<IActionResult> Complete(string id, string occurredOn, int smokeAlarmsInstalled, int coAlarmsInstalled, int comboAlarmsInstalled, int alarmReason,
			decimal hoursSpent, string outcome, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try
			{
				var done = await _requests.CompleteAsync(DepartmentId, UserId, new AlarmRequestCompletionInput
				{
					RequestId = id, OccurredOn = ParseEnteredUtc(occurredOn), SmokeAlarmsInstalled = smokeAlarmsInstalled, CoAlarmsInstalled = coAlarmsInstalled,
					ComboAlarmsInstalled = comboAlarmsInstalled, AlarmReason = alarmReason, HoursSpent = hoursSpent, Outcome = outcome
				}, cancellationToken);
				Notify("AlarmRequestCompleted", done.Request.RequestNumber, done.Activity.AlarmsInstalledTotal());
			}
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Decline(string id, int declineReason, string notes, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { var r = await _requests.DeclineAsync(DepartmentId, UserId, id, (RmsAlarmRequestDeclineReason)declineReason, notes, cancellationToken); Notify("AlarmRequestDeclined", r.RequestNumber); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Reopen(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { var r = await _requests.ReopenAsync(DepartmentId, UserId, id, cancellationToken); Notify("AlarmRequestReopened", r.RequestNumber); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; }
			return RedirectToAction(nameof(Details), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync(Flag)) return NotFound();
			try { await _requests.DeleteAsync(DepartmentId, UserId, id, cancellationToken); Notify("AlarmRequestDeleted"); }
			catch (Exception ex) { var f = Fail(ex); if (f != null) return f; return RedirectToAction(nameof(Details), new { id }); }
			return RedirectToAction(nameof(Index));
		}

		private async Task PopulateEditAsync(RecordAlarmRequestEditView model)
		{
			try
			{
				model.Occupancies = new[] { new SelectListItem { Value = "", Text = "-" } }
					.Concat((await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 })).OrderBy(o => o.Name).Select(o => new SelectListItem { Value = o.RmsOccupancyId, Text = (o.OccupancyNumber + " " + o.Name + " - " + o.AddressText).Trim(), Selected = o.RmsOccupancyId == model.Request?.RmsOccupancyId }))
					.ToList();
			}
			catch (RecordsModuleDisabledException) { }
			var contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>();
			model.Contacts = new[] { new SelectListItem { Value = "", Text = "-" } }
				.Concat(contacts.Where(c => !c.IsDeleted).Select(c => new SelectListItem { Value = c.ContactId, Text = ProtectedDataEnvelope.SafeDisplay(c.Name), Selected = c.ContactId == model.Request?.RequesterContactId }).OrderBy(i => i.Text, StringComparer.CurrentCultureIgnoreCase))
				.ToList();
			var names = await UserNamesAsync();
			var active = await _departments.GetActiveMemberUserIdsAsync(DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			model.Members = new[] { new SelectListItem { Value = "", Text = "-" } }
				.Concat(names.Where(n => active.Contains(n.Key) || n.Key == model.Request?.AssignedToUserId).OrderBy(n => n.Value, StringComparer.CurrentCultureIgnoreCase)
					.Select(n => new SelectListItem { Value = n.Key, Text = n.Value, Selected = n.Key == model.Request?.AssignedToUserId }))
				.ToList();
		}

		private async Task<Dictionary<string, string>> UserNamesAsync()
		{
			var profiles = await _profiles.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
			return profiles.ToDictionary(p => p.Key, p => (p.Value.FirstName + " " + p.Value.LastName).Trim(), StringComparer.Ordinal);
		}

		private async Task PopulateOccupancyNamesAsync(Dictionary<string, string> map, IEnumerable<string> ids)
		{
			var wanted = new HashSet<string>(ids.Where(i => !string.IsNullOrWhiteSpace(i)), StringComparer.Ordinal);
			if (wanted.Count == 0) return;
			try
			{
				foreach (var o in await _occupancies.ListAsync(DepartmentId, UserId, new RmsOccupancyQuery { Take = 2000 }))
					if (wanted.Contains(o.RmsOccupancyId)) map[o.RmsOccupancyId] = (o.OccupancyNumber + " " + o.Name).Trim();
			}
			catch (RecordsModuleDisabledException) { }
		}
	}
}
