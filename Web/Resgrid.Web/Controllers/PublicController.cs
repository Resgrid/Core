using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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
	}
}
