using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	public class ModerationController : SecureBaseController
	{
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IFeatureToggleService _featureToggleService;

		public ModerationController(IDepartmentGroupsService departmentGroupsService, IFeatureToggleService featureToggleService)
		{
			_departmentGroupsService = departmentGroupsService;
			_featureToggleService = featureToggleService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!await _featureToggleService.IsEnabledAsync(Resgrid.Model.FeatureFlagKeys.ChatSystem, DepartmentId))
				return RedirectToAction("Dashboard", "Home", new { Area = "User" });

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() &&
				!await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId))
				return Unauthorized();

			return View();
		}
	}
}
