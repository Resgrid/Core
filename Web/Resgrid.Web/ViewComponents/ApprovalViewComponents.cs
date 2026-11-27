using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The approvals waiting for the member (e-Signature S4, plan §23 UX: "requests the user must sign appear on My Training / home as
	/// rgw-next-action items"). Renders nothing when there are none; a failure never takes the host page down.
	/// </summary>
	public sealed class PendingApprovalsViewComponent(IApprovalChainService approvals) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var items = await approvals.GetInboxAsync(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId());
				return items.Count == 0 ? Content(string.Empty) : View("~/Areas/User/Views/Shared/_PendingApprovals.cshtml", items);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, "Pending approvals could not be shown.");
				return Content(string.Empty);
			}
		}
	}

	/// <summary>
	/// A subject's approvals (S4) on its module page and beside the shared signature panel: the latest request and its state, a warning
	/// when the subject changed after it was submitted or approved, and "Submit for approval" when the member may. Renders nothing where
	/// the department has no active chain for the kind or the module is not available.
	/// </summary>
	public sealed class ApprovalPanelViewComponent(IApprovalChainService approvals) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(SignatureSubjectKinds kind, string subjectId)
		{
			try
			{
				var status = await approvals.GetSubjectStatusAsync(ClaimsAuthorizationHelper.GetDepartmentId(), kind, subjectId, ClaimsAuthorizationHelper.GetUserId(),
					ClaimsAuthorizationHelper.IsUserDepartmentAdmin());
				return !status.ModuleAvailable || (!status.ChainActive && status.Latest == null) ? Content(string.Empty)
					: View("~/Areas/User/Views/Shared/_ApprovalPanel.cshtml", status);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, "A subject's approvals could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
