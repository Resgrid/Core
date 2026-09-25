using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Help;

namespace Resgrid.Web.Areas.User.Controllers
{
	// Both actions render help content for the caller's own department.
	[Area("User")]
	[Authorize]
	public class HelpController : SecureBaseController
	{
		private readonly IDepartmentsService _departmentsService;

		public HelpController(IDepartmentsService departmentsService)
		{
			_departmentsService = departmentsService;
		}

		[HttpGet]
		public IActionResult DashboardTutorial()
		{
			return PartialView();
		}

		[HttpGet]
		public IActionResult SetupReport() => RedirectToAction("SetupReport", "AdminAssist", new { Area = "User" });
	}
}
