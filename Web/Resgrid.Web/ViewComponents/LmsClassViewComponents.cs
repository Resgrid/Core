using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The class-session line on a calendar item's page (Training T4, LMS): "this is a class session — open the roster / sign in". Rendered by
	/// the baseline Calendar page by name, so the page never depends on LMS types (§17.3). Nothing unless the Training.Lms flag is on (the
	/// cached per-request flag read; no billing call) and the item is a class session.
	/// </summary>
	public sealed class LmsClassSessionLinkViewComponent(ILmsClassService classes, ILmsAccessService access) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(int calendarItemId)
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				if (departmentId <= 0 || calendarItemId <= 0 || !await LmsRequestGate.IsFlagEnabledAsync(HttpContext, access, departmentId))
					return Content(string.Empty);
				var session = await classes.GetSessionForCalendarItemAsync(departmentId, calendarItemId);
				return session == null ? Content(string.Empty) : View("~/Areas/User/Views/LmsClasses/_CalendarLink.cshtml", session);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The class session link could not be shown.");
				return Content(string.Empty);
			}
		}
	}

	/// <summary>
	/// Training on the shift calendar (§13a, T4): the next two weeks' class sessions with the enrolled members who are on shift at that time
	/// (the conflict flag). Rendered by the baseline Shifts page by name (§17.3); nothing unless the Training.Lms flag is on and there are
	/// upcoming classes. An optional panel never takes the page down.
	/// </summary>
	public sealed class LmsClassShiftConflictsViewComponent(ILmsClassService classes, ILmsAccessService access, IDepartmentsService departments) : ViewComponent
	{
		public const int Days = 14;

		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				if (departmentId <= 0 || !await LmsRequestGate.IsFlagEnabledAsync(HttpContext, access, departmentId))
					return Content(string.Empty);
				var conflicts = await classes.GetUpcomingConflictsAsync(departmentId, DateTime.UtcNow, Days) ?? new List<LmsClassShiftConflict>();
				if (conflicts.Count == 0)
					return Content(string.Empty);
				var names = (await departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
					.Where(n => n?.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
				ViewData["LmsClassNames"] = names;
				ViewData["LmsClassDepartment"] = await departments.GetDepartmentByIdAsync(departmentId, false);
				return View("~/Areas/User/Views/LmsClasses/_ShiftConflicts.cshtml", conflicts);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The training panel of the shift calendar could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
