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

		public ModerationController(IDepartmentGroupsService departmentGroupsService)
		{
			_departmentGroupsService = departmentGroupsService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() &&
				!await _departmentGroupsService.IsUserAGroupAdminAsync(UserId, DepartmentId))
				return Unauthorized();

			return View();
		}
	}
}
