using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Calendar;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// iCal export and external calendar sync endpoints.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CalendarExportController : V4AuthenticatedApiControllerbase
	{
		private readonly ICalendarExportService _calendarExportService;
		private readonly ICalendarService _calendarService;
		private readonly IUserProfileService _userProfileService;
		private readonly IPermissionsService _permissionsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public CalendarExportController(
			ICalendarExportService calendarExportService,
			ICalendarService calendarService,
			IUserProfileService userProfileService,
			IPermissionsService permissionsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService)
		{
			_calendarExportService = calendarExportService;
			_calendarService = calendarService;
			_userProfileService = userProfileService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
		}

		/// <summary>
		/// Downloads a single calendar event as an .ics (iCal) file.
		/// </summary>
		/// <param name="calendarItemId">ID of the calendar item to export.</param>
		[HttpGet("ExportICalFile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<IActionResult> ExportICalFile(int calendarItemId)
		{
			var icsContent = await _calendarExportService.GenerateICalForItemAsync(calendarItemId);
			if (icsContent == null)
				return NotFound();

			var bytes = Encoding.UTF8.GetBytes(icsContent);
			return File(bytes, "text/calendar", $"event-{calendarItemId}.ics");
		}

		/// <summary>
		/// Downloads all department calendar events as a single .ics (iCal) file.
		/// </summary>
		[HttpGet("ExportDepartmentICalFeed")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<IActionResult> ExportDepartmentICalFeed()
		{
			var icsContent = await _calendarExportService.GenerateICalForDepartmentAsync(DepartmentId);
			var bytes = Encoding.UTF8.GetBytes(icsContent);
			return File(bytes, "text/calendar", "department-calendar.ics");
		}

		/// <summary>
		/// Returns (or generates on first call) the encrypted subscription URL token for
		/// the authenticated user's department calendar. Used to populate the Subscribe panel.
		/// </summary>
		[HttpGet("GetCalendarSubscriptionUrl")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetCalendarSubscriptionUrlResult>> GetCalendarSubscriptionUrl()
		{
			if (!await HasCalendarSyncPermissionAsync(DepartmentId, UserId))
				return Unauthorized();

			var result = new GetCalendarSubscriptionUrlResult();

			// Activate if not already done; returns existing token if already active.
			var token = await _calendarService.GetCalendarFeedTokenAsync(DepartmentId, UserId)
				?? await _calendarService.ActivateCalendarSyncAsync(DepartmentId, UserId);

			result.SubscriptionUrl = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CalendarExport/CalendarFeed/{token}";
			result.WebcalUrl = result.SubscriptionUrl.Replace("https://", "webcal://").Replace("http://", "webcal://");
			result.Token = token;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Regenerates the encrypted subscription token, invalidating all previously issued
		/// subscription URLs for the authenticated user.
		/// </summary>
		[HttpPost("RegenerateCalendarSubscriptionUrl")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetCalendarSubscriptionUrlResult>> RegenerateCalendarSubscriptionUrl(
			CancellationToken cancellationToken)
		{
			if (!await HasCalendarSyncPermissionAsync(DepartmentId, UserId))
				return Unauthorized();

			var result = new GetCalendarSubscriptionUrlResult();

			var token = await _calendarService.RegenerateCalendarSyncAsync(DepartmentId, UserId, cancellationToken);

			result.SubscriptionUrl = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CalendarExport/CalendarFeed/{token}";
			result.WebcalUrl = result.SubscriptionUrl.Replace("https://", "webcal://").Replace("http://", "webcal://");
			result.Token = token;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Public unauthenticated endpoint for iCal feed subscription.
		/// The {token} is an AES-encrypted, URL-safe Base64 payload containing departmentId|userId|syncGuid.
		/// Returns the department calendar as text/calendar for import into Google Calendar,
		/// Microsoft Outlook, Apple Calendar, or any RFC 5545 compliant application.
		/// </summary>
		[HttpGet("CalendarFeed/{token}")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<IActionResult> CalendarFeed(string token)
		{
			if (!CalendarConfig.ICalFeedEnabled)
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Calendar feed is disabled.");

			var validated = await _calendarService.ValidateCalendarFeedTokenAsync(token);
			if (validated == null)
				return Unauthorized();

			if (!await HasCalendarSyncPermissionAsync(validated.Value.DepartmentId, validated.Value.UserId))
				return Unauthorized();

			var icsContent = await _calendarExportService.GenerateICalForDepartmentAsync(validated.Value.DepartmentId);
			var bytes = Encoding.UTF8.GetBytes(icsContent);

			Response.Headers["X-WR-CACHETIME"] = $"PT{CalendarConfig.ICalFeedCacheDurationMinutes}M";
			return File(bytes, "text/calendar", "calendar.ics");
		}

		private async Task<bool> HasCalendarSyncPermissionAsync(int departmentId, string userId)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.UseCalendarSync);
			if (permission == null)
				return true; // No permission configured = default Everyone

			var dept = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var isAdmin = dept != null && dept.IsUserAnAdmin(userId);
			var grp = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			var isGroupAdmin = grp != null && grp.IsUserGroupAdmin(userId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

			return _permissionsService.IsUserAllowed(permission, isAdmin, isGroupAdmin, roles);
		}
	}
}
