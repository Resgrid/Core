using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Wellness;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The duty board on the personnel page (member wellness WL1): members on restricted or off duty, or not fully cleared, for the viewer's
	/// supervisors view (department admins and health officers: everyone; group admins: their group). Operational only: duty status with
	/// dates and the clearance status, never a reason. Renders nothing for anyone else or when nobody is listed. Read only.
	/// </summary>
	public sealed class MemberDutyBoardViewComponent(IMemberHealthService health, IDepartmentsService departments) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var userId = ClaimsAuthorizationHelper.GetUserId();
				var access = await health.GetAccessAsync(departmentId, userId);
				if (!access.IsSupervisor && !access.IsHealthOfficer)
					return Content(string.Empty);

				var rows = await health.GetDutyBoardAsync(departmentId, userId);
				if (rows == null || rows.Count == 0)
					return Content(string.Empty);

				var names = (await departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
					.Where(n => !string.IsNullOrWhiteSpace(n.UserId))
					.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().Name?.Trim(), StringComparer.OrdinalIgnoreCase);
				return View("~/Areas/User/Views/MemberHealth/_DutyBoard.cshtml", new DutyBoardView
				{
					Rows = rows,
					Names = names,
					Department = await departments.GetDepartmentByIdAsync(departmentId, false)
				});
			}
			catch (Exception ex)
			{
				// An optional panel must not take down the personnel page.
				Logging.LogException(ex, "The duty board could not be drawn.");
				return Content(string.Empty);
			}
		}
	}
}
