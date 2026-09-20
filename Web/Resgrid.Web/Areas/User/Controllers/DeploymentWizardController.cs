using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.ContractorBilling;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// "Schedule Deployment Call" wizard (Workforce &amp; Business Operations plan, Phase C6): six server-rendered
	/// INSPINIA steps prefilled from an accepted bid — call details, units, crew seats per unit (roster with status,
	/// staffing, roles, typed certifications and overlap conflicts), equipment, rates and premiums, review — ending in
	/// the transactional <see cref="IBidsService.ConvertBidToDeploymentAsync"/>. Needs Bids_Update and
	/// Deployments_Update (admins by default) plus the Invoicing.ContractorBilling entitlement.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class DeploymentWizardController : SecureBaseController
	{
		private readonly IBidsService _bids;
		private readonly IDeploymentService _deployments;
		private readonly IDepartmentsService _departments;
		private readonly IUnitsService _units;
		private readonly ICallsService _calls;
		private readonly IPersonnelRolesService _roles;
		private readonly ICertificationService _certifications;
		private readonly IUserStateService _userStates;
		private readonly IActionLogsService _actionLogs;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> _strings;

		public DeploymentWizardController(IBidsService bids, IDeploymentService deployments, IDepartmentsService departments, IUnitsService units, ICallsService calls, IPersonnelRolesService roles,
			ICertificationService certifications, IUserStateService userStates, IActionLogsService actionLogs, IBusinessOperationsAccessService access,
			IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> strings)
		{
			_bids = bids;
			_deployments = deployments;
			_departments = departments;
			_units = units;
			_calls = calls;
			_roles = roles;
			_certifications = certifications;
			_userStates = userStates;
			_actionLogs = actionLogs;
			_access = access;
			_strings = strings;
		}

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanRun => IsAdmin || (ClaimsAuthorizationHelper.CanManageBids() && ClaimsAuthorizationHelper.CanManageDeployments());

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!CanRun || !await _access.CanUseContractorBillingAsync(DepartmentId))
			{
				context.Result = Unauthorized();
				return;
			}
			await next();
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		[HttpGet]
		public async Task<IActionResult> Index(string bidId)
		{
			var context = await _bids.GetBidConversionContextAsync(bidId, DepartmentId);
			if (context?.Bid == null) return NotFound();
			if (context.Bid.Status != (int)BidStatuses.Accepted || context.AlreadyConverted)
			{
				TempData["ContractorMessage"] = _strings[context.AlreadyConverted ? "bids_already_converted" : "bids_not_accepted"].Value;
				return RedirectToAction("View", "Bids", new { id = bidId });
			}

			var view = new WizardView { Context = context, CanManageBids = true, CanManageDeployments = true };
			view.Department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			view.CallTypes = await _calls.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			view.Priorities = await _calls.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			await LoadRosterAsync(view, context);
			view.ContextJson = JsonConvert.SerializeObject(new
			{
				bid = new { context.Bid.BidId, context.Bid.BidNumber, context.Bid.Title, context.Bid.IncidentNumber, context.Bid.RequestedStartOn, context.Bid.RequestedEndOn, context.Bid.DeliveryLocation, context.Bid.Description, lines = context.Bid.LineItems.Select(l => new { l.BidLineItemId, l.RateScheduleEntryId, l.LineType, l.Description, l.CrewSize, l.Quantity, l.PremiumIds }) },
				contract = context.Contract == null ? null : new { context.Contract.ServiceContractId, context.Contract.Name, context.Contract.PointOfHire, context.Contract.MaxDeploymentDays },
				schedule = context.Schedule == null ? null : new
				{
					context.Schedule.RateScheduleId, context.Schedule.Name, context.Schedule.Currency,
					entries = context.Schedule.Entries.Select(e => new { e.RateScheduleEntryId, e.Name, e.EntryType, e.BillingBasis, e.GroupKey, e.CrewSize, e.CertificationCode, e.UnitTypeId, e.InventoryItemId, requiredCertifications = e.RequiredCertifications, rate = Resgrid.Services.Invoicing.BidsService.SnapshotRate(context.Schedule, new BidLineItem { RateScheduleEntryId = e.RateScheduleEntryId }) }),
					premiums = context.Schedule.Premiums.Select(p => new { p.RatePremiumId, p.Name, p.DeploymentAdder, p.Overtime1Adder, p.StandbyAdder })
				},
				units = view.Units, personnel = view.Personnel, roles = view.Roles, timeZone = view.Department?.TimeZone
			});
			return View(view);
		}

		private async Task LoadRosterAsync(WizardView view, BidConversionContext context)
		{
			var windowStart = context.Bid.RequestedStartOn ?? DateTime.UtcNow;
			var windowEnd = context.Bid.RequestedEndOn ?? windowStart.AddDays(14);

			// Units with type, live state, staffing count and their seats (unit roles) for step 2/3.
			var units = await _units.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>();
			var states = new Dictionary<int, string>();
			try { foreach (var state in await _units.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId) ?? new List<UnitState>()) states[state.UnitId] = state.GetStatusText(); }
			catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: unit states unavailable."); }
			var seats = new Dictionary<int, List<UnitRole>>();
			try { foreach (var role in await _units.GetAllRolesForDepartmentAsync(DepartmentId) ?? new List<UnitRole>()) { if (!seats.TryGetValue(role.UnitId, out var list)) seats[role.UnitId] = list = new List<UnitRole>(); list.Add(role); } }
			catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: unit roles unavailable."); }
			var unitConflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var personConflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
				foreach (var warning in await _deployments.GetWindowConflictsAsync(DepartmentId, windowStart, windowEnd, names.Select(n => n.UserId), units.Select(u => u.UnitId)))
				{
					if (warning.Code != DeploymentRosterWarning.ScheduleConflict) continue;
					if (int.TryParse(warning.SubjectId, out _)) unitConflicts.Add(warning.SubjectId); else personConflicts.Add(warning.SubjectId);
				}
			}
			catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: overlap warnings unavailable."); }

			view.Units = units.OrderBy(u => u.Name).Select(u => new WizardUnit
			{
				UnitId = u.UnitId, Name = u.Name, Type = u.Type, State = states.TryGetValue(u.UnitId, out var state) ? state : null,
				Staffing = seats.TryGetValue(u.UnitId, out var unitSeats) ? unitSeats.Count : 0, Conflict = unitConflicts.Contains(u.UnitId.ToString()),
				Seats = (seats.TryGetValue(u.UnitId, out var roleList) ? roleList : new List<UnitRole>()).Select(r => new WizardSeat { UnitRoleId = r.UnitRoleId, Name = r.Name, PersonnelRoleRequired = r.PersonnelRoleRequired, PersonnelRoleId = r.PersonnelRoleId }).ToList()
			}).ToList();

			// Personnel roster with status, staffing, roles and typed certifications (Phase D) for step 3.
			var people = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			var roleMap = new Dictionary<string, List<PersonnelRole>>(StringComparer.OrdinalIgnoreCase);
			try { roleMap = await _roles.GetAllRolesForUsersInDepartmentAsync(DepartmentId) ?? roleMap; } catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: roles unavailable."); }
			var statusMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var staffingMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try { foreach (var log in await _actionLogs.GetLastActionLogsForDepartmentAsync(DepartmentId) ?? new List<ActionLog>()) statusMap[log.UserId] = log.GetActionText(); } catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: statuses unavailable."); }
			try { foreach (var state in await _userStates.GetLatestStatesForDepartmentAsync(DepartmentId) ?? new List<UserState>()) staffingMap[state.UserId] = state.GetStaffingText(); } catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: staffing unavailable."); }
			var certifications = new Dictionary<string, List<PersonnelCertification>>(StringComparer.OrdinalIgnoreCase);
			var typeCodes = new Dictionary<int, string>();
			try
			{
				foreach (var type in await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>()) typeCodes[type.DepartmentCertificationTypeId] = type.Code ?? type.Type;
				foreach (var record in await _certifications.GetCertificationsForDepartmentAsync(DepartmentId) ?? new List<PersonnelCertification>())
				{
					if (record.IsDeleted || string.IsNullOrWhiteSpace(record.UserId)) continue;
					if (!certifications.TryGetValue(record.UserId, out var list)) certifications[record.UserId] = list = new List<PersonnelCertification>();
					list.Add(record);
				}
			}
			catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: certifications unavailable."); }

			view.Personnel = people.OrderBy(p => p.Name).Select(p => new WizardPerson
			{
				UserId = p.UserId, Name = p.Name, Status = statusMap.TryGetValue(p.UserId, out var status) ? status : null, Staffing = staffingMap.TryGetValue(p.UserId, out var staffing) ? staffing : null,
				RoleIds = roleMap.TryGetValue(p.UserId, out var roles) ? roles.Select(r => r.PersonnelRoleId).ToList() : new List<int>(),
				Conflict = personConflicts.Contains(p.UserId),
				Certifications = (certifications.TryGetValue(p.UserId, out var certs) ? certs : new List<PersonnelCertification>()).Select(c => new WizardCertification
				{
					Code = c.DepartmentCertificationTypeId.HasValue && typeCodes.TryGetValue(c.DepartmentCertificationTypeId.Value, out var code) ? code : c.Type, Name = ProtectedDataEnvelope.SafeDisplay(c.Name), Status = c.Status, ExpiresOn = c.ExpiresOn,
					ExpiringInWindow = c.ExpiresOn.HasValue && c.ExpiresOn.Value >= windowStart && c.ExpiresOn.Value <= windowEnd
				}).ToList()
			}).ToList();
			try { view.Roles = (await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).Select(r => new WizardRole { PersonnelRoleId = r.PersonnelRoleId, Name = r.Name }).ToList(); }
			catch (Exception ex) { Logging.LogException(ex, "Deployment wizard: department roles unavailable."); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(WizardSubmitInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.BidId)) return BadRequest();
			BidConversionRequest request;
			try { request = JsonConvert.DeserializeObject<BidConversionRequest>(input.RequestJson ?? string.Empty); }
			catch (JsonException) { request = null; }
			if (request == null) return BadRequest();
			request.BidId = input.BidId;
			try
			{
				var result = await _bids.ConvertBidToDeploymentAsync(request, DepartmentId, UserId, Ip, Agent, cancellationToken);
				TempData["ContractorSaved"] = true;
				if (result.Warnings.Count > 0) TempData["ContractorMessage"] = string.Join(" ", result.Warnings.Select(w => w.Code).Distinct().Select(code => _strings["Warning" + code].Value));
				return Json(new { success = true, deploymentId = result.Deployment?.DeploymentId, callId = result.CallId, url = Url.Action("View", "Deployments", new { area = "User", id = result.Deployment?.DeploymentId }) });
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal) || ex.Message.StartsWith("deployments_", StringComparison.Ordinal))
			{
				var text = _strings[ex.Message];
				return StatusCode(400, new { success = false, message = text.ResourceNotFound ? _strings["SaveFailed"].Value : text.Value, code = ex.Message });
			}
		}
	}
}
