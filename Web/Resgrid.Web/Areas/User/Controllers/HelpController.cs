using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Resgrid.Web.Areas.User.Controllers
{
	// Help content; the legacy count-based Setup Report now lives in Admin Assist's evidence-based report.
	[Area("User")]
	[Authorize]
	public class HelpController : SecureBaseController
	{
		[HttpGet]
		public IActionResult DashboardTutorial()
		{
			return PartialView();
		}

		[HttpGet]
		public IActionResult SetupReport() => RedirectToAction("SetupReport", "AdminAssist", new { Area = "User" });
	}
}
