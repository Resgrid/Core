using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.AdminAssist;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	public sealed class AdminAssistReturnViewComponent(IAdminAssistAccessService access, IDataProtectionProvider protection, TimeProvider clock) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			var requested = HttpContext.Request.Query["aaReturn"].ToString();
			var token = HttpContext.Request.Cookies[AdminAssistReturnLink.CookieName];
			if (requested is not ("wizard" or "report") && token == null) return Content(string.Empty);
			var options = new CookieOptions { Path = "/User", HttpOnly = true, Secure = HttpContext.Request.IsHttps, SameSite = SameSiteMode.Lax };
			try
			{
				var controller = ViewContext.RouteData.Values["controller"]?.ToString();
				var action = ViewContext.RouteData.Values["action"]?.ToString();
				var actor = new AdminAssistActor(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId());
				if (controller == "AdminAssist" || controller == "Department" && action == "SetupWizard" || controller == "Help" && action == "SetupReport" ||
					!await access.CanAccessAsync(actor, true, HttpContext.RequestAborted))
				{
					HttpContext.Response.Cookies.Delete(AdminAssistReturnLink.CookieName, options); return Content(string.Empty);
				}
				var now = clock.GetUtcNow();
				var page = AdminAssistReturnLink.Read(protection, actor, token, now);
				if (requested is "wizard" or "report")
				{
					page = requested; options.Expires = now.AddMinutes(30);
					HttpContext.Response.Cookies.Append(AdminAssistReturnLink.CookieName, AdminAssistReturnLink.Create(protection, actor, page, now), options);
				}
				if (page == null) { HttpContext.Response.Cookies.Delete(AdminAssistReturnLink.CookieName, options); return Content(string.Empty); }
				return View("~/Areas/User/Views/Shared/_AdminAssistReturn.cshtml", page);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return Content(string.Empty); } // Optional navigation never blocks the owning editor.
		}
	}
}
