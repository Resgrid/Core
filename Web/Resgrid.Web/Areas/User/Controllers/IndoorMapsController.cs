using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class IndoorMapsController : SecureBaseController
	{
		private readonly IIndoorMapService _indoorMapService;
		private readonly Microsoft.Extensions.Logging.ILogger<IndoorMapsController> _logger;

		public IndoorMapsController(IIndoorMapService indoorMapService, Microsoft.Extensions.Logging.ILogger<IndoorMapsController> logger)
		{
			_indoorMapService = indoorMapService;
			_logger = logger;
		}

		[HttpGet]
		public async Task<IActionResult> SearchZones(string term)
		{
			if (string.IsNullOrWhiteSpace(term))
				return Json(new { results = System.Array.Empty<object>() });

			var zones = await _indoorMapService.SearchZonesAsync(DepartmentId, term);

			var results = zones.Select(z => new
			{
				id = z.IndoorMapZoneId,
				text = z.Name,
				floorId = z.IndoorMapFloorId
			});

			return Json(new { results });
		}

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

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteFloor(string id, CancellationToken cancellationToken)
		{
			var floor = await _indoorMapService.GetFloorByIdAsync(id);
			if (floor == null)
				return NotFound();

			var map = await _indoorMapService.GetIndoorMapByIdAsync(floor.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			var success = await _indoorMapService.DeleteFloorAsync(id, cancellationToken);
			if (!success)
			{
				_logger.LogWarning("DeleteFloor failed for floor {FloorId} in department {DepartmentId}.", id, DepartmentId);
				TempData["ErrorMessage"] = "Failed to delete the floor. Please try again.";
			}

			return RedirectToAction("Layers", "CustomMaps", new { area = "User", id = floor.IndoorMapId });
		}
	}
}
