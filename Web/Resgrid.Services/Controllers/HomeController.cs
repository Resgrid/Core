using System.Web.Mvc;

namespace Resgrid.Web.Services.Controllers
{
	public class HomeController : Controller
	{
		public ActionResult Index()
		{
            return Redirect("/Help");
		}

		public ActionResult Error()
		{
			return View();
		}
	}
}