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
	/// Round 10 A3: the banner on a department's own protocol or course that is a managed copy of a linked department's set item: who publishes
	/// it, how locked it is, and the customize (managers, open blocks) and propose-a-change links. Nothing for any other item.
	/// </summary>
	public sealed class ContentSetCopyBannerViewComponent(IContentSetService sets) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(int kind, int localId)
		{
			try
			{
				var actor = await sets.GetActorAsync(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId());
				var info = await sets.GetCopyInfoAsync(actor, (ContentSetItemKinds)kind, localId);
				return info == null ? Content(string.Empty) : View("~/Areas/User/Views/ContentSets/_CopyBanner.cshtml", info);
			}
			catch (Exception ex)
			{
				// An optional banner must not take down the protocol or course page.
				Logging.LogException(ex);
				return Content(string.Empty);
			}
		}
	}
}
