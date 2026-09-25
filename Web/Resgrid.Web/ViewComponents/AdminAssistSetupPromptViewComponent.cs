using System;
using System.Linq;
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
				// A review against the current catalog and scope settles setup until its chosen revisit date arrives.
				var reviewCurrent = workspace.ReviewEvidence is { } review && review.CatalogVersion == catalog.Version &&
					review.ScopeRevision == workspace.ScopeRevision && !(workspace.RevisitOnUtc <= DateTime.UtcNow);
				// Learning carries across releases, so count only capabilities that are still in the catalog. Setup teaches key
				// features only; detail features have no learn control, so requiring them would keep the prompt up forever.
				var learnedAll = catalog.Capabilities.Where(c => c.IsKey).All(c => workspace.LearnedCapabilityIds.Contains(c.Id));
				if (workspace.SetupPromptDismissed || reviewCurrent || learnedAll) return Content(string.Empty);
				return View("~/Areas/User/Views/Shared/_AdminAssistSetupPrompt.cshtml", workspace);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return Content(string.Empty); } // Optional guidance must not take down the operational dashboard.
		}
	}
}
