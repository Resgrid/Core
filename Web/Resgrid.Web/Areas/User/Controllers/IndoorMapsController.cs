using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class IndoorMapsController : SecureBaseController
	{
		[HttpGet]
		public IActionResult Index()
		{
			return RedirectToAction("Index", "CustomMaps", new { area = "User", type = 0 });
		}

		[HttpGet]
		public IActionResult New()
		{
			return RedirectToAction("New", "CustomMaps", new { area = "User", type = 0 });
		}

		[HttpGet]
		public IActionResult Edit(string id)
		{
			return RedirectToAction("Edit", "CustomMaps", new { area = "User", id = id });
		}

		[HttpGet]
		public IActionResult Delete(string id)
		{
			return RedirectToAction("Delete", "CustomMaps", new { area = "User", id = id });
		}

		[HttpGet]
		public IActionResult Floors(string id)
		{
			return RedirectToAction("Layers", "CustomMaps", new { area = "User", id = id });
		}

		[HttpGet]
		public IActionResult ZoneEditor(string id)
		{
			return RedirectToAction("RegionEditor", "CustomMaps", new { area = "User", id = id });
		}

		[HttpGet]
		public IActionResult GetFloorImage(string id)
		{
			return RedirectToAction("GetLayerImage", "CustomMaps", new { area = "User", id = id });
		}
	}
}
