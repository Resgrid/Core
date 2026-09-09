using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
	public partial class ReportsController
	{
		[HttpPost, ValidateAntiForgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> ChecklistComplianceReport(ChecklistReportQuery query, [FromServices] IChecklistsService checklists, [FromServices] IProtectedGrantContext grant, [FromServices] IDepartmentDataProtectionService protection, bool missedOnly = false)
		{
			Response.Headers["Cache-Control"] = "no-store";
			try
			{
				var report = await checklists.GetComplianceSummaryAsync(new ChecklistActor { DepartmentId = DepartmentId, UserId = UserId, GrantToken = grant.GrantToken }, query);
				ViewBag.ProtectionEnforced = await protection.IsProtectionEnforcedAsync(DepartmentId);
				ViewBag.ProtectedGrant = grant.GrantToken; ViewBag.GrantExpiresOn = Resgrid.Web.Helpers.HttpProtectedGrantContext.ReadExpiry(Request);
				ViewBag.MissedOnly = missedOnly;
				return View("ChecklistComplianceReport", report);
			}
			catch (ChecklistException ex) { return StatusCode(ex.StatusCode, ChecklistReportDocuments.Text("The request could not be completed.")); }
		}
	}
}
