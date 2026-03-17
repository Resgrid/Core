using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.CustomMaps;
using Resgrid.Web.Helpers;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class CustomMapsController : SecureBaseController
	{
		private readonly ICustomMapService _customMapService;

		public CustomMapsController(ICustomMapService customMapService)
		{
			_customMapService = customMapService;
		}

		#region Map CRUD

		[HttpGet]
		public async Task<IActionResult> Index(int? type)
		{
			var model = new CustomMapIndexView();
			CustomMapType? filterType = type.HasValue ? (CustomMapType)type.Value : null;
			model.FilterType = filterType;
			model.Maps = await _customMapService.GetCustomMapsForDepartmentAsync(DepartmentId, filterType);
			return View(model);
		}

		[HttpGet]
		public IActionResult New(int? type)
		{
			var model = new CustomMapNewView();
			if (type.HasValue)
				model.Map.MapType = type.Value;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> New(CustomMapNewView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				model.Map.DepartmentId = DepartmentId;
				model.Map.AddedById = UserId;
				model.Map.AddedOn = DateTime.UtcNow;
				model.Map.IsDeleted = false;

				await _customMapService.SaveCustomMapAsync(model.Map, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			var map = await _customMapService.GetCustomMapByIdAsync(id);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new CustomMapNewView();
			model.Map = map;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(CustomMapNewView model, CancellationToken cancellationToken)
		{
			var existing = await _customMapService.GetCustomMapByIdAsync(model.Map.IndoorMapId);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			if (ModelState.IsValid)
			{
				model.Map.DepartmentId = DepartmentId;
				model.Map.AddedById = existing.AddedById;
				model.Map.AddedOn = existing.AddedOn;
				model.Map.UpdatedById = UserId;
				model.Map.UpdatedOn = DateTime.UtcNow;
				model.Map.IsDeleted = existing.IsDeleted;

				await _customMapService.SaveCustomMapAsync(model.Map, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var map = await _customMapService.GetCustomMapByIdAsync(id);
			if (map != null && map.DepartmentId == DepartmentId)
			{
				await _customMapService.DeleteCustomMapAsync(id, cancellationToken);
			}

			return RedirectToAction("Index");
		}

		#endregion Map CRUD

		#region Layers

		[HttpGet]
		public async Task<IActionResult> Layers(string id)
		{
			var map = await _customMapService.GetCustomMapByIdAsync(id);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new CustomMapLayersView();
			model.Map = map;
			model.Layers = await _customMapService.GetLayersForMapAsync(id);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> NewLayer(string indoorMapId, string name, int floorOrder, int layerType, IFormFile layerImage, CancellationToken cancellationToken)
		{
			var map = await _customMapService.GetCustomMapByIdAsync(indoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var layer = new IndoorMapFloor();
			layer.IndoorMapId = indoorMapId;
			layer.Name = name;
			layer.FloorOrder = floorOrder;
			layer.LayerType = layerType;
			layer.Opacity = 0.8m;
			layer.IsDeleted = false;
			layer.AddedOn = DateTime.UtcNow;

			byte[] imageData = null;
			if (layerImage != null && layerImage.Length > 0)
			{
				using (var ms = new MemoryStream())
				{
					await layerImage.CopyToAsync(ms);
					imageData = ms.ToArray();
				}
				layer.ImageContentType = layerImage.ContentType;
				layer.SourceFileSize = layerImage.Length;
			}

			// Save the layer first to get it in DB
			await _customMapService.SaveLayerAsync(layer, cancellationToken);

			// Process tiles if image provided
			if (imageData != null)
			{
				await _customMapService.ProcessAndStoreTilesAsync(layer.IndoorMapFloorId, imageData, cancellationToken);
			}

			return RedirectToAction("Layers", new { id = indoorMapId });
		}

		[HttpGet]
		public async Task<IActionResult> DeleteLayer(string id, CancellationToken cancellationToken)
		{
			var layer = await _customMapService.GetLayerByIdAsync(id);
			if (layer == null)
				return RedirectToAction("Index");

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			await _customMapService.DeleteLayerAsync(id, cancellationToken);

			return RedirectToAction("Layers", new { id = layer.IndoorMapId });
		}

		#endregion Layers

		#region Region Editor

		[HttpGet]
		public async Task<IActionResult> RegionEditor(string id)
		{
			var layer = await _customMapService.GetLayerByIdAsync(id);
			if (layer == null)
				return RedirectToAction("Index");

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new CustomMapRegionEditorView();
			model.Layer = layer;
			model.Map = map;
			model.Regions = await _customMapService.GetRegionsForLayerAsync(id);
			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> SaveRegion([FromBody] IndoorMapZone region, CancellationToken cancellationToken)
		{
			var layer = await _customMapService.GetLayerByIdAsync(region.IndoorMapFloorId);
			if (layer == null)
				return Json(new { success = false, message = "Layer not found" });

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return Json(new { success = false, message = "Unauthorized" });

			if (string.IsNullOrWhiteSpace(region.IndoorMapZoneId))
			{
				region.IndoorMapZoneId = null;
				region.AddedOn = DateTime.UtcNow;
			}
			region.IsDeleted = false;

			var saved = await _customMapService.SaveRegionAsync(region, cancellationToken);
			return Json(new { success = true, regionId = saved.IndoorMapZoneId });
		}

		[HttpPost]
		public async Task<IActionResult> DeleteRegion([FromBody] DeleteRegionRequest request, CancellationToken cancellationToken)
		{
			var region = await _customMapService.GetRegionByIdAsync(request.RegionId);
			if (region == null)
				return Json(new { success = false });

			var layer = await _customMapService.GetLayerByIdAsync(region.IndoorMapFloorId);
			if (layer == null)
				return Json(new { success = false });

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return Json(new { success = false });

			await _customMapService.DeleteRegionAsync(request.RegionId, cancellationToken);
			return Json(new { success = true });
		}

		#endregion Region Editor

		#region Import

		[HttpGet]
		public async Task<IActionResult> Import(string id)
		{
			var map = await _customMapService.GetCustomMapByIdAsync(id);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new CustomMapImportView();
			model.Map = map;
			model.Layers = await _customMapService.GetLayersForMapAsync(id);
			model.Imports = await _customMapService.GetImportsForMapAsync(id);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ImportUpload(string mapId, string layerId, IFormFile importFile, CancellationToken cancellationToken)
		{
			var map = await _customMapService.GetCustomMapByIdAsync(mapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			if (importFile == null || importFile.Length == 0)
				return RedirectToAction("Import", new { id = mapId });

			var fileName = importFile.FileName.ToLowerInvariant();

			if (fileName.EndsWith(".geojson") || fileName.EndsWith(".json"))
			{
				using (var reader = new StreamReader(importFile.OpenReadStream()))
				{
					var geoJson = await reader.ReadToEndAsync();
					await _customMapService.ImportGeoJsonAsync(mapId, layerId, geoJson, UserId, cancellationToken);
				}
			}
			else if (fileName.EndsWith(".kml"))
			{
				await _customMapService.ImportKmlAsync(mapId, layerId, importFile.OpenReadStream(), false, UserId, cancellationToken);
			}
			else if (fileName.EndsWith(".kmz"))
			{
				await _customMapService.ImportKmlAsync(mapId, layerId, importFile.OpenReadStream(), true, UserId, cancellationToken);
			}

			return RedirectToAction("Import", new { id = mapId });
		}

		#endregion Import

		#region Search & Image Endpoints

		[HttpGet]
		public async Task<IActionResult> SearchRegions(string term)
		{
			var regions = await _customMapService.SearchRegionsAsync(DepartmentId, term);
			var results = new List<object>();

			foreach (var region in regions)
			{
				var displayName = await _customMapService.GetRegionDisplayNameAsync(region.IndoorMapZoneId);
				results.Add(new { id = region.IndoorMapZoneId, text = displayName, layerId = region.IndoorMapFloorId });
			}

			return Json(new { results = results });
		}

		[HttpGet]
		public async Task<IActionResult> GetLayerImage(string id)
		{
			var layer = await _customMapService.GetLayerByIdAsync(id);
			if (layer == null || layer.ImageData == null)
				return NotFound();

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			return File(layer.ImageData, layer.ImageContentType ?? "image/png");
		}

		[HttpGet]
		public async Task<IActionResult> GetLayerTile(string id, int z, int x, int y)
		{
			var tile = await _customMapService.GetTileAsync(id, z, x, y);
			if (tile == null)
				return NotFound();

			return File(tile.TileData, tile.TileContentType ?? "image/png");
		}

		#endregion Search & Image Endpoints
	}

	public class DeleteRegionRequest
	{
		public string RegionId { get; set; }
	}
}
