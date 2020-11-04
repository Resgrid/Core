

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Resgrid.Web.Services.Controllers
{
	[Route("")]
	[Route("api/")]
	[ApiVersionNeutral]
	[ApiController]
	[AllowAnonymous]
	public class HomeController : Controller
	{
		[HttpGet("")]
		[AllowAnonymous]
		public ActionResult Index()
		{
			return Redirect("/index.html");
		}
	}
}
