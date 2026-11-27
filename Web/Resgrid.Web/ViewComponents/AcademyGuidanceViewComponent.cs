using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Academy;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>
	/// A3 on the home dashboard: the member's role tour while it is open and the newest release's "What's new" lesson until read, each
	/// closable and remembered per member. Optional guidance: any failure renders nothing rather than taking down the dashboard.
	/// </summary>
	public sealed class AcademyGuidanceViewComponent : ViewComponent
	{
		private readonly IAcademyGuidanceService _guidance;

		public AcademyGuidanceViewComponent(IAcademyGuidanceService guidance)
		{
			_guidance = guidance;
		}

		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var actor = new AcademyGuidanceActor
				{
					DepartmentId = ClaimsAuthorizationHelper.GetDepartmentId(),
					UserId = ClaimsAuthorizationHelper.GetUserId(),
					Locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
					IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
					MayAssignTraining = ClaimsAuthorizationHelper.CanAssignTraining()
				};
				var view = await _guidance.GetDashboardAsync(actor, DateTime.UtcNow, HttpContext.RequestAborted);
				if (view == null || view.IsEmpty)
					return Content(string.Empty);
				return View("~/Areas/User/Views/Shared/_AcademyGuidanceCard.cshtml", view);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return Content(string.Empty); }
		}
	}
}
