using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.AdminAssist;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	public sealed class AdminAssistSetupPromptViewComponent(IAdminAssistAccessService access,
		IAdminAssistRepository repository, IAdminAssistCatalog catalog) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var actor = new AdminAssistActor(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId());
				if (!await access.CanAccessAsync(actor, true, HttpContext.RequestAborted)) return Content(string.Empty);
				var workspace = await repository.GetWorkspaceAsync(actor.DepartmentId, actor.UserId, catalog.Version, HttpContext.RequestAborted);
				if (workspace.SetupPromptDismissed || workspace.LearnedCapabilityIds.Count >= catalog.Capabilities.Count) return Content(string.Empty);
				return View("~/Areas/User/Views/Shared/_AdminAssistSetupPrompt.cshtml", workspace);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return Content(string.Empty); } // Optional guidance must not take down the operational dashboard.
		}
	}
}
