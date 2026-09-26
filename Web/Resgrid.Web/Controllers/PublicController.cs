using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Resgrid.Web.Controllers
{
	[AllowAnonymous]
	public class PublicController : Controller
	{
		public async Task<IActionResult> Error()
		{
			return View();
		}

		public async Task<IActionResult> Unauthorized()
		{
			return View();
		}

		/// <summary>
		/// The cookie AccessDeniedPath: every Forbid() and failed authorization policy lands here. Reuses the
		/// permissions page, but answers 403 so the refusal is not mistaken for a successful page.
		/// </summary>
		public IActionResult Forbidden()
		{
			var view = View("Unauthorized");
			view.StatusCode = StatusCodes.Status403Forbidden;
			return view;
		}
	}
}
