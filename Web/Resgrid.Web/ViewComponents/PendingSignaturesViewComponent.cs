using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// The member's signatures whose turn it is (e-Signature S3, plan §23 UX: "requests the user must sign appear on My Training /
	/// home as rgw-next-action items"). Renders nothing when there are none; a failure never takes the host page down.
	/// </summary>
	public sealed class PendingSignaturesViewComponent(ISignatureRequestService requests) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var items = await requests.GetPendingForUserAsync(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId());
				return items.Count == 0 ? Content(string.Empty) : View("~/Areas/User/Views/Shared/_PendingSignatures.cshtml", items);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, "Pending signatures could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
