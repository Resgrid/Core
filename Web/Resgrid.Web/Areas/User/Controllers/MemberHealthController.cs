using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Wellness;
using Resgrid.Web.Areas.User.Models.Wellness;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Personnel → Wellness → Health records (member wellness WL1, protocols-training-knowledge-plan §13b): BBP / infectious-disease exposure
	/// cases run by the designated officer, injuries with duty status and the return-to-duty clearance, NFPA 1582 medical clearances and the
	/// OSHA 300/301 export. Every member reports and reads their own; department administration alone opens nothing. Every rule lives in
	/// IMemberHealthService; this controller maps requests and localizes refusals. Workforce Pro (2026-11-26): 404 while the Workforce.Pro flag
	/// is off; read-only with the upsell banner while not licensed (enforced by the service).
	/// </summary>
	[Area("User")]
	[Authorize]
	public class MemberHealthController : SecureBaseController
	{
		private const string MessageKey = "MemberHealthMessage";
		private const string ErrorKey = "MemberHealthError";

		private readonly IMemberHealthService _health;
		private readonly IMemberWellnessService _wellness;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly IPdfProvider _pdfProvider;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Wellness.MemberHealth> _localizer;
		private readonly IWorkforceProAccessService _workforcePro;
		private WorkforceProAccess _workforceProAccess;

		private Department _department;
		private Dictionary<string, string> _names;
		private MemberHealthAccess _access;

		public MemberHealthController(IMemberHealthService health, IMemberWellnessService wellness, IDepartmentsService departmentsService, ICallsService callsService,
			IPdfProvider pdfProvider, IWorkforceProAccessService workforcePro, IStringLocalizer<Resgrid.Localization.Areas.User.Wellness.MemberHealth> localizer)
		{
			_workforcePro = workforcePro;
			_health = health;
			_wellness = wellness;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_pdfProvider = pdfProvider;
			_localizer = localizer;
		}

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			// Workforce Pro: invisible while the flag is off (data kept); read-only with the upsell banner while not licensed.
			_workforceProAccess = await Resgrid.Web.Helpers.WorkforceProRequestGate.GetAccessAsync(HttpContext, _workforcePro, DepartmentId);
			if (!_workforceProAccess.Offered)
			{
				context.Result = NotFound();
				return;
			}

			_access = await _health.GetAccessAsync(DepartmentId, UserId);
			ViewData["WellnessAccess"] = new WellnessAccessView
			{
				IsOfficer = _access.IsWellnessOfficer,
				IsAdmin = _access.IsAdmin,
				IsDesignatedOfficer = _access.IsDesignatedOfficer,
				IsHealthOfficer = _access.IsHealthOfficer,
				WorkforceProReadOnly = _workforceProAccess.IsReadOnly,
				WorkforceProLicensed = _workforceProAccess.Licensed,
				ShowWorkforceProLinks = _access.IsAdmin,
				WorkforceProGraceEndsUtc = _access.IsAdmin && _workforceProAccess.InDunningGrace ? _workforceProAccess.GraceEndsUtc : null
			};
			await next();
		}

		private MemberHealthAccess Access => _access ?? new MemberHealthAccess();

		/// <summary>A page about one member: when Workforce Pro is licensed but the member holds no seat, their records are read-only (the notice).</summary>
		private void MarkSeat(string memberUserId, string memberName)
		{
			if (_workforceProAccess == null || !_workforceProAccess.Licensed || _workforceProAccess.HoldsSeat(memberUserId))
				return;
			if (ViewData["WellnessAccess"] is WellnessAccessView view)
			{
				view.SeatMissingMemberName = memberName;
				view.SeatMissingIsSelf = string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase);
			}
		}

		private string L(string key) => _localizer[key].Value;

		private void Flash(string message, string error = null)
		{
			if (TempData == null)
				return;
			if (!string.IsNullOrWhiteSpace(message))
				TempData[MessageKey] = message;
			if (!string.IsNullOrWhiteSpace(error))
				TempData[ErrorKey] = error;
		}

		private string TempMessage() => TempData?[MessageKey] as string;
		private string TempError() => TempData?[ErrorKey] as string;

		/// <summary>A rule refusal's localized message ("Error.{code}"), or the generic one.</summary>
		private string ErrorText(Exception ex)
		{
			if (ex is MemberWellnessException rule)
			{
				var text = _localizer["Error." + rule.Code];
				if (!text.ResourceNotFound)
					return text.Value;
			}
			return L("Error.Generic");
		}

		private async Task<Department> DepartmentAsync() => _department ??= await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			if (_names != null)
				return _names;
			_names = (await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				.Where(n => !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name?.Trim(), StringComparer.OrdinalIgnoreCase);
			return _names;
		}

		private async Task<string> NameOfAsync(string userId) => MemberHealthPresentation.NameOf(await NamesAsync(), userId);

		private async Task<string> CallNameAsync(int? callId)
		{
			if (!callId.HasValue)
				return null;
			var call = await _callsService.GetCallByIdAsync(callId.Value, false);
			if (call == null || call.DepartmentId != DepartmentId)
				return null;
			var number = string.IsNullOrWhiteSpace(call.Number) ? "#" + call.CallId.ToString(CultureInfo.InvariantCulture) : call.Number;
			return number + " · " + call.Name + " · " + WellnessPresentation.LocalDate(call.LoggedOn, await DepartmentAsync());
		}

		private async Task<List<WellnessCallOption>> RecentCallsAsync(int days)
		{
			var department = await DepartmentAsync();
			var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, DateTime.UtcNow.AddDays(-days), DateTime.UtcNow.AddDays(1)) ?? new List<Call>();
			return calls.Where(c => !c.IsDeleted).OrderByDescending(c => c.LoggedOn).Take(100)
				.Select(c => new WellnessCallOption
				{
					CallId = c.CallId,
					Label = (string.IsNullOrWhiteSpace(c.Number) ? "#" + c.CallId.ToString(CultureInfo.InvariantCulture) : c.Number) + " · " + c.Name + " · " + WellnessPresentation.LocalDate(c.LoggedOn, department)
				}).ToList();
		}

		private async Task<DateTime?> ParseLocalAsync(string value)
		{
			if (!DateTime.TryParseExact(value, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
				return null;
			return DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(await DepartmentAsync()), DateTimeKind.Utc);
		}

		private static DateTime? ParseDate(string value) =>
			DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateTime?)null;

		private async Task<string> NowLocalInputAsync() =>
			DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc).TimeConverter(await DepartmentAsync()).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

		/// <summary>Runs a service call, flashing its localized result; false when it was refused.</summary>
		private async Task<bool> RunAsync(Func<Task> act, string messageKey)
		{
			try
			{
				await act();
				Flash(L(messageKey));
				return true;
			}
			catch (UnauthorizedAccessException)
			{
				Flash(null, L("Error.NotAllowed"));
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				Flash(null, ErrorText(ex));
			}
			return false;
		}

		#region Home

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!Access.IsMember)
				return Unauthorized();
			MarkSeat(UserId, await NameOfAsync(UserId));
			var model = new MemberHealthIndexView
			{
				Access = Access,
				Department = await DepartmentAsync(),
				Names = await NamesAsync(),
				MyCases = await _health.GetCasesForMemberAsync(DepartmentId, UserId, UserId),
				MyInjuries = await _health.GetInjuriesForMemberAsync(DepartmentId, UserId, UserId),
				MyStatus = await _health.GetDutyStatusAsync(DepartmentId, UserId, UserId),
				Message = TempMessage(),
				Error = TempError()
			};
			if (Access.IsDesignatedOfficer)
				model.OpenCases = await _health.GetDepartmentCasesAsync(DepartmentId, UserId, false);
			if (Access.IsHealthOfficer)
				model.OpenInjuries = await _health.GetOpenInjuriesAsync(DepartmentId, UserId);
			if (Access.IsHealthOfficer || Access.IsSupervisor)
				model.DutyBoard = await _health.GetDutyBoardAsync(DepartmentId, UserId);
			return View(model);
		}

		#endregion Home

		#region Exposure cases

		[HttpGet]
		public async Task<IActionResult> ReportExposure(string userId, string exposureId)
		{
			var memberUserId = string.IsNullOrWhiteSpace(userId) ? UserId : userId;
			if (!string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase) && !Access.IsDesignatedOfficer)
				return Unauthorized();
			var model = new ReportExposureView
			{
				MemberUserId = memberUserId,
				ResponderExposureId = exposureId,
				OccurredOnLocal = await NowLocalInputAsync(),
				Error = TempError()
			};
			await FillReportExposureAsync(model);
			return View(model);
		}

		private async Task FillReportExposureAsync(ReportExposureView model)
		{
			model.IsDesignatedOfficer = Access.IsDesignatedOfficer;
			model.Calls = await RecentCallsAsync(90);
			if (model.IsDesignatedOfficer)
				model.Members = await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>();

			// The member's own confirmed bloodborne (or Other) entries without an open case can be followed up from here.
			try
			{
				var overview = await _wellness.GetOverviewAsync(DepartmentId, UserId, model.MemberUserId);
				var open = (await _health.GetCasesForMemberAsync(DepartmentId, UserId, model.MemberUserId)).Where(c => c.IsOpen).Select(c => c.ResponderExposureId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				var department = await DepartmentAsync();
				model.Entries = overview.Entries.Where(e => ExposureIncidentWorkflow.CanOpenFrom(e) && !open.Contains(e.RmsResponderExposureId))
					.Select(e => new ExposureEntryOption
					{
						Id = e.RmsResponderExposureId,
						Label = WellnessPresentation.LocalDateTime(e.OccurredOn, department) + (e.CallId.HasValue ? " · #" + e.CallId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)
					}).ToList();
			}
			catch (UnauthorizedAccessException)
			{
				// A designated officer who is not a wellness officer cannot read the WL0 log: the new-entry path still works.
				model.Entries = new List<ExposureEntryOption>();
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ReportExposure(ReportExposureView model, CancellationToken cancellationToken)
		{
			model ??= new ReportExposureView();
			var report = new ExposureIncidentReport
			{
				UserId = string.IsNullOrWhiteSpace(model.MemberUserId) ? UserId : model.MemberUserId,
				ResponderExposureId = string.IsNullOrWhiteSpace(model.ResponderExposureId) ? null : model.ResponderExposureId,
				CallId = model.CallId > 0 ? model.CallId : null,
				Route = model.Route,
				OccurredOn = await ParseLocalAsync(model.OccurredOnLocal) ?? default,
				Narrative = model.Narrative,
				PpeWorn = model.PpeWorn
			};
			try
			{
				var incident = await _health.ReportExposureAsync(DepartmentId, UserId, report, cancellationToken);
				Flash(L("Msg.ExposureReported"));
				return RedirectToAction("Case", new { id = incident.RmsExposureIncidentId });
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				model.Error = ErrorText(ex);
				model.MemberUserId = report.UserId;
				await FillReportExposureAsync(model);
				return View(model);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Case(string id)
		{
			var incident = await _health.GetCaseAsync(DepartmentId, UserId, id);
			if (incident == null)
				return NotFound();
			MarkSeat(incident.UserId, await NameOfAsync(incident.UserId));
			var model = new ExposureCaseView
			{
				Case = incident,
				IsDesignatedOfficer = Access.IsDesignatedOfficer,
				IsSelf = string.Equals(incident.UserId, UserId, StringComparison.OrdinalIgnoreCase),
				ViewerUserId = UserId,
				MemberName = await NameOfAsync(incident.UserId),
				CallName = await CallNameAsync(incident.CallId),
				BbpTraining = await _health.GetBbpTrainingStatusAsync(DepartmentId, incident.UserId),
				FollowUps = await _health.GetCaseFollowUpsAsync(DepartmentId, UserId, incident.RmsExposureIncidentId),
				Names = await NamesAsync(),
				Department = await DepartmentAsync(),
				ProtectedWithheld = MemberHealthProtectedFields.ExposureIncidents.Values.Any(a => WellnessPresentation.IsWithheld(a.Get(incident))),
				Message = TempMessage(),
				Error = TempError()
			};
			if (model.IsDesignatedOfficer)
			{
				// The member and the designated officers who act today (role, and the certification rule when the switch is on).
				var officers = (await _wellness.GetActingMemberIdsAsync(DepartmentId, WellnessRoleKinds.DesignatedOfficer) ?? new List<string>())
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				officers.Add(incident.UserId);
				model.FollowUpAssignees = (await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>()).Where(p => officers.Contains(p.UserId)).ToList();
			}
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AdvanceCase(string id, int toStage, string on, string sourceFacility, string notes, long rowVersion, CancellationToken cancellationToken)
		{
			var step = new ExposureIncidentStep
			{
				ToStage = toStage,
				On = await ParseLocalAsync(on),
				SourceFacility = sourceFacility,
				Notes = notes,
				ExpectedRowVersion = rowVersion > 0 ? rowVersion : (long?)null
			};
			await RunAsync(() => _health.AdvanceCaseAsync(DepartmentId, UserId, id, step, cancellationToken), "Msg.CaseMoved");
			return RedirectToAction("Case", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveCaseNotes(string id, string sourceFacility, string sourceFollowUpNotes, string notificationResponse, string medicalNotes, long rowVersion,
			CancellationToken cancellationToken)
		{
			await RunAsync(() => _health.UpdateCaseNotesAsync(DepartmentId, UserId, id, sourceFacility, sourceFollowUpNotes, notificationResponse, medicalNotes,
				rowVersion > 0 ? rowVersion : (long?)null, cancellationToken), "Msg.Saved");
			return RedirectToAction("Case", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AddCaseFollowUp(string id, string kind, string assigneeUserId, string dueOn, CancellationToken cancellationToken)
		{
			await RunAsync(() => _health.AddCaseFollowUpAsync(DepartmentId, UserId, id, kind, assigneeUserId, ParseDate(dueOn), cancellationToken), "Msg.FollowUpAdded");
			return RedirectToAction("Case", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteCaseFollowUp(string id, string assignmentId, CancellationToken cancellationToken)
		{
			await RunAsync(() => _health.CompleteCaseFollowUpAsync(DepartmentId, UserId, assignmentId, cancellationToken), "Msg.FollowUpCompleted");
			return RedirectToAction("Case", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelCaseFollowUp(string id, string assignmentId, CancellationToken cancellationToken)
		{
			await RunAsync(() => _health.CancelCaseFollowUpAsync(DepartmentId, UserId, assignmentId, cancellationToken), "Msg.FollowUpCancelled");
			return RedirectToAction("Case", new { id });
		}

		#endregion Exposure cases

		#region Injuries

		[HttpGet]
		public async Task<IActionResult> ReportInjury(string userId, string exposureIncidentId)
		{
			var memberUserId = string.IsNullOrWhiteSpace(userId) ? UserId : userId;
			RmsExposureIncident incident = null;
			if (!string.IsNullOrWhiteSpace(exposureIncidentId))
			{
				incident = await _health.GetCaseAsync(DepartmentId, UserId, exposureIncidentId);
				if (incident == null)
					return NotFound();
				memberUserId = incident.UserId;
			}
			if (!string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase) && !Access.IsHealthOfficer)
				return Unauthorized();

			var department = await DepartmentAsync();
			var model = new InjuryView
			{
				IsHealthOfficer = Access.IsHealthOfficer,
				IsSelf = string.Equals(memberUserId, UserId, StringComparison.OrdinalIgnoreCase),
				MemberUserId = memberUserId,
				MemberName = await NameOfAsync(memberUserId),
				ExposureIncidentId = incident?.RmsExposureIncidentId,
				CallId = incident?.CallId,
				OccurredOnLocal = incident == null ? await NowLocalInputAsync()
					: DateTime.SpecifyKind(incident.OccurredOn, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
				IsRecordable = incident != null,
				IsPrivacyCase = incident != null,
				Error = TempError()
			};
			await FillInjuryFormAsync(model);
			return View("Injury", model);
		}

		private async Task FillInjuryFormAsync(InjuryView model)
		{
			model.IsHealthOfficer = Access.IsHealthOfficer;
			model.Department = await DepartmentAsync();
			model.Calls = await RecentCallsAsync(90);
			if (model.IsNew && model.IsHealthOfficer && string.IsNullOrWhiteSpace(model.ExposureIncidentId))
				model.Members = await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>();
			if (model.CallId.HasValue)
				model.CallName = await CallNameAsync(model.CallId);
		}

		private async Task<MemberInjuryInput> InputAsync(InjuryView model)
		{
			return new MemberInjuryInput
			{
				UserId = string.IsNullOrWhiteSpace(model.MemberUserId) ? UserId : model.MemberUserId,
				ExposureIncidentId = string.IsNullOrWhiteSpace(model.ExposureIncidentId) ? null : model.ExposureIncidentId,
				CallId = model.CallId > 0 ? model.CallId : null,
				OccurredOn = await ParseLocalAsync(model.OccurredOnLocal) ?? default,
				Location = model.Location,
				JobTitle = model.JobTitle,
				Description = model.Description,
				InjuryDetails = model.InjuryDetails,
				TreatmentDetails = model.TreatmentDetails,
				IsRecordable = model.IsRecordable,
				OshaInjuryType = model.OshaInjuryType,
				IsPrivacyCase = model.IsPrivacyCase,
				TreatedInEmergencyRoom = model.TreatedInEmergencyRoom,
				HospitalizedOvernight = model.HospitalizedOvernight,
				DiedOn = ParseDate(model.DiedOn)
			};
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveInjury(InjuryView model, CancellationToken cancellationToken)
		{
			model ??= new InjuryView();
			var injuryId = model.InjuryId;
			var input = await InputAsync(model);
			try
			{
				RmsMemberInjury saved;
				if (string.IsNullOrWhiteSpace(injuryId))
				{
					saved = await _health.ReportInjuryAsync(DepartmentId, UserId, input, cancellationToken);
					Flash(L("Msg.InjuryReported"));
				}
				else
				{
					saved = await _health.UpdateInjuryAsync(DepartmentId, UserId, injuryId, input, model.RowVersion > 0 ? model.RowVersion : (long?)null, cancellationToken);
					Flash(L("Msg.Saved"));
				}
				return RedirectToAction("Injury", new { id = saved.RmsMemberInjuryId });
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (Exception ex) when (ex is MemberWellnessException || ex is ArgumentException)
			{
				if (!string.IsNullOrWhiteSpace(injuryId))
				{
					Flash(null, ErrorText(ex));
					return RedirectToAction("Injury", new { id = injuryId });
				}
				model.Error = ErrorText(ex);
				model.IsSelf = string.Equals(input.UserId, UserId, StringComparison.OrdinalIgnoreCase);
				model.MemberName = await NameOfAsync(input.UserId);
				await FillInjuryFormAsync(model);
				return View("Injury", model);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Injury(string id)
		{
			var injury = await _health.GetInjuryAsync(DepartmentId, UserId, id);
			if (injury == null)
				return NotFound();
			MarkSeat(injury.UserId, await NameOfAsync(injury.UserId));
			var department = await DepartmentAsync();
			var model = new InjuryView
			{
				Injury = injury,
				Periods = await _health.GetDutyPeriodsAsync(DepartmentId, UserId, injury.RmsMemberInjuryId),
				IsSelf = string.Equals(injury.UserId, UserId, StringComparison.OrdinalIgnoreCase),
				MemberName = await NameOfAsync(injury.UserId),
				ProtectedWithheld = MemberHealthProtectedFields.Injuries.Values.Any(a => WellnessPresentation.IsWithheld(a.Get(injury))),
				Message = TempMessage(),
				Error = TempError(),
				MemberUserId = injury.UserId,
				ExposureIncidentId = injury.ExposureIncidentId,
				OccurredOnLocal = DateTime.SpecifyKind(injury.OccurredOn, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
				CallId = injury.CallId,
				Location = injury.Location,
				JobTitle = injury.JobTitle,
				// A withheld value stays the placeholder: the form posts it back and the service keeps the stored value.
				Description = injury.Description,
				InjuryDetails = injury.InjuryDetails,
				TreatmentDetails = injury.TreatmentDetails,
				IsRecordable = injury.IsRecordable,
				OshaInjuryType = injury.OshaInjuryType,
				IsPrivacyCase = injury.IsPrivacyCase,
				TreatedInEmergencyRoom = injury.TreatedInEmergencyRoom,
				HospitalizedOvernight = injury.HospitalizedOvernight,
				DiedOn = MemberHealthPresentation.InputDate(injury.DiedOn),
				RowVersion = injury.RowVersion
			};
			await FillInjuryFormAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetDutyStatus(string id, int dutyStatus, string startsOn, string expectedReturnOn, string restrictions, CancellationToken cancellationToken)
		{
			var start = ParseDate(startsOn);
			if (!start.HasValue)
				Flash(null, L("Error.date_invalid"));
			else
				await RunAsync(() => _health.SetDutyStatusAsync(DepartmentId, UserId, id, dutyStatus, start.Value, ParseDate(expectedReturnOn), restrictions, cancellationToken), "Msg.DutyStatusSaved");
			return RedirectToAction("Injury", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ClearReturnToDuty(string id, string returnedOn, string clearanceNotes, CancellationToken cancellationToken)
		{
			var returned = ParseDate(returnedOn);
			if (!returned.HasValue)
				Flash(null, L("Error.date_invalid"));
			else
				await RunAsync(() => _health.ClearReturnToDutyAsync(DepartmentId, UserId, id, returned.Value, clearanceNotes, cancellationToken), "Msg.ReturnedToDuty");
			return RedirectToAction("Injury", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> VoidInjury(string id, CancellationToken cancellationToken)
		{
			await RunAsync(() => _health.VoidInjuryAsync(DepartmentId, UserId, id, cancellationToken), "Msg.InjuryVoided");
			return RedirectToAction("Injury", new { id });
		}

		#endregion Injuries

		#region Medical clearance

		[HttpGet]
		public async Task<IActionResult> Clearances(string userId)
		{
			if (!Access.IsHealthOfficer)
				return Unauthorized();
			var model = new ClearancesView
			{
				Current = await _health.GetCurrentClearancesAsync(DepartmentId, UserId),
				Members = await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>(),
				Names = await NamesAsync(),
				ClearanceTypeConfigured = (await _wellness.GetSettingsAsync(DepartmentId)).MedicalClearanceCertificationTypeId > 0,
				Department = await DepartmentAsync(),
				Message = TempMessage(),
				Error = TempError()
			};
			if (!string.IsNullOrWhiteSpace(userId))
			{
				model.HistoryUserId = userId;
				model.History = await _health.GetClearancesForMemberAsync(DepartmentId, UserId, userId);
				model.ProtectedWithheld = model.History.Any(c => WellnessPresentation.IsWithheld(c.RestrictionDetail));
			}
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RecordClearance(string userId, int status, string evaluatedOn, string validUntil, string restrictionDetail, CancellationToken cancellationToken)
		{
			var evaluated = ParseDate(evaluatedOn);
			if (!evaluated.HasValue)
				Flash(null, L("Error.date_invalid"));
			else
				await RunAsync(() => _health.RecordClearanceAsync(DepartmentId, UserId, new MedicalClearanceInput
				{
					UserId = userId,
					Status = status,
					EvaluatedOn = evaluated.Value,
					ValidUntil = ParseDate(validUntil),
					RestrictionDetail = restrictionDetail
				}, cancellationToken), "Msg.ClearanceRecorded");
			return RedirectToAction("Clearances", new { userId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> VoidClearance(string userId, string clearanceId, CancellationToken cancellationToken)
		{
			await RunAsync(() => _health.VoidClearanceAsync(DepartmentId, UserId, clearanceId, cancellationToken), "Msg.ClearanceVoided");
			return RedirectToAction("Clearances", new { userId });
		}

		#endregion Medical clearance

		#region OSHA 300 / 301

		[HttpGet]
		public async Task<IActionResult> Osha(int? year)
		{
			if (!Access.IsHealthOfficer)
				return Unauthorized();
			var department = await DepartmentAsync();
			var thisYear = MemberHealthService.LocalDate(DateTime.UtcNow, department).Year;
			var model = new OshaLogView { Year = year ?? thisYear, Years = Enumerable.Range(thisYear - 5, 6).Reverse().ToList() };
			try
			{
				model.Log = await BuildLogAsync(model.Year);
			}
			catch (MemberWellnessException ex)
			{
				model.Error = ErrorText(ex);
			}
			return View(model);
		}

		private async Task<OshaLog> BuildLogAsync(int year)
		{
			var names = await NamesAsync();
			return await _health.BuildOshaLogAsync(DepartmentId, UserId, year, id => MemberHealthPresentation.NameOf(names, id));
		}

		private Dictionary<string, string> DocumentLabels() =>
			MemberHealthDocuments.LabelKeys.Distinct(StringComparer.Ordinal).ToDictionary(k => k, k => L(k), StringComparer.Ordinal);

		[HttpGet]
		public async Task<IActionResult> OshaLogCsv(int year) => await OshaFileAsync(year, log => CsvFile(MemberHealthDocuments.LogCsv(log, DocumentLabels()), $"osha-300-{year}.csv"));

		[HttpGet]
		public async Task<IActionResult> OshaReportsCsv(int year) => await OshaFileAsync(year, log => CsvFile(MemberHealthDocuments.ReportsCsv(log, DocumentLabels()), $"osha-301-{year}.csv"));

		[HttpGet]
		public async Task<IActionResult> OshaLogPdf(int year) => await OshaFileAsync(year, log =>
			File(_pdfProvider.ConvertHtmlToPdf(MemberHealthDocuments.LogHtml(log, DocumentLabels(), CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft, LocalNow()), PageSize()),
				"application/pdf", $"osha-300-{year}.pdf"));

		[HttpGet]
		public async Task<IActionResult> OshaReportsPdf(int year) => await OshaFileAsync(year, log =>
			File(_pdfProvider.ConvertHtmlToPdf(MemberHealthDocuments.ReportsHtml(log, DocumentLabels(), CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft, LocalNow()), PageSize()),
				"application/pdf", $"osha-301-{year}.pdf"));

		private DateTime LocalNow() => _department == null ? DateTime.UtcNow : DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc).TimeConverter(_department);

		private async Task<IActionResult> OshaFileAsync(int year, Func<OshaLog, IActionResult> render)
		{
			if (!Access.IsHealthOfficer)
				return Unauthorized();
			try
			{
				await DepartmentAsync();
				return render(await BuildLogAsync(year));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (MemberWellnessException ex)
			{
				Flash(null, ErrorText(ex));
				return RedirectToAction("Osha", new { year });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "OSHA log export could not be generated.");
				Flash(null, L("Error.ExportFailed"));
				return RedirectToAction("Osha", new { year });
			}
		}

		private IActionResult CsvFile(string csv, string fileName)
		{
			var body = Encoding.UTF8.GetBytes(csv ?? string.Empty);
			var preamble = Encoding.UTF8.GetPreamble();
			var bytes = new byte[preamble.Length + body.Length];
			Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
			Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
			return File(bytes, "text/csv", fileName);
		}

		private static string PageSize() =>
			string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase) ? "Letter" : "A4";

		#endregion OSHA 300 / 301
	}
}
