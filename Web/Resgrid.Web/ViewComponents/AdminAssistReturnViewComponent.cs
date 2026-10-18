using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.AdminAssist;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The "save, then return" link shown on an owning screen opened from Admin Assist (wizard, Setup Report, Plans) or from
	/// Import &amp; Migration ("import", wizard plan section 6.4): the import return needs import access, not Admin Assist access.
	/// </summary>
	public sealed class AdminAssistReturnViewComponent(IAdminAssistAccessService access, Resgrid.Model.Services.IDepartmentImportAccessService importAccess,
		IDataProtectionProvider protection, TimeProvider clock) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			var requested = HttpContext.Request.Query["aaReturn"].ToString();
			var token = HttpContext.Request.Cookies[AdminAssistReturnLink.CookieName];
			if (requested is not ("wizard" or "report" or "plans" or "import") && token == null) return Content(string.Empty);
			var options = new CookieOptions { Path = "/User", HttpOnly = true, Secure = HttpContext.Request.IsHttps, SameSite = SameSiteMode.Lax };
			try
			{
				var controller = ViewContext.RouteData.Values["controller"]?.ToString();
				var action = ViewContext.RouteData.Values["action"]?.ToString();
				var actor = new AdminAssistActor(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId());
				var stored = AdminAssistReturnLink.Read(protection, actor, token, clock.GetUtcNow());
				var isImport = requested == "import" || requested.Length == 0 && stored == "import";
				var allowed = isImport
					? await importAccess.CanManageAsync(actor.DepartmentId, actor.UserId)
					: await access.CanAccessAsync(actor, requested != "plans" && stored != "plans", HttpContext.RequestAborted);
				// The import's own pages never link back to themselves; opened from the setup wizard they still link back to it.
				if (controller == "AdminAssist" || isImport && controller == "DepartmentImport" || controller == "Department" && action == "SetupWizard" ||
					controller == "Help" && action == "SetupReport" || !allowed)
				{
					HttpContext.Response.Cookies.Delete(AdminAssistReturnLink.CookieName, options); return Content(string.Empty);
				}
				var now = clock.GetUtcNow();
				var page = AdminAssistReturnLink.Read(protection, actor, token, now);
				if (requested is "wizard" or "report" or "plans" or "import")
				{
					page = requested; options.Expires = now.AddMinutes(30);
					HttpContext.Response.Cookies.Append(AdminAssistReturnLink.CookieName, AdminAssistReturnLink.Create(protection, actor, page, now), options);
				}
				if (page == "plans" && !Resgrid.Config.AdminAssistConfig.PlansEnabled) page = null;
				if (page == null) { HttpContext.Response.Cookies.Delete(AdminAssistReturnLink.CookieName, options); return Content(string.Empty); }
				return View("~/Areas/User/Views/Shared/_AdminAssistReturn.cshtml", page);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return Content(string.Empty); } // Optional navigation never blocks the owning editor.
		}
	}
}
