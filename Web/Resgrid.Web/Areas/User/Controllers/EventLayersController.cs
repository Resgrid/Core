using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Mapping → Layers: importing a layer from a KML, KMZ or GeoJSON file (a marathon route, a festival site, the vendors
	/// along a parade) and a layer's event details. A layer with event dates is shown on every map by default while the event
	/// runs (<see cref="MapLayerEvents"/>); deleting it (Mapping → Layers) removes all of its shapes.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class EventLayersController : SecureBaseController
	{
		private static readonly string[] AllowedExtensions = { ".kml", ".kmz", ".geojson", ".json" };

		private readonly IMappingService _mappingService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.EventLayers> _localizer;

		public EventLayersController(IMappingService mappingService, IDepartmentsService departmentsService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.EventLayers> localizer)
		{
			_mappingService = mappingService;
			_departmentsService = departmentsService;
			_localizer = localizer;
		}

		// Same gate as the rest of Mapping → Layers (MappingController.CanManageMapData).
		private static bool CanManageMapData() => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		[HttpGet]
		public async Task<IActionResult> Import()
		{
			if (!CanManageMapData())
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			return View(new EventLayerView { TimeZoneName = department?.TimeZone });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(MapLayerFileLimits.MaxFileBytes + 1024 * 1024)]
		[RequestFormLimits(MultipartBodyLengthLimit = MapLayerFileLimits.MaxFileBytes + 1024 * 1024)]
		public async Task<IActionResult> Import(EventLayerView model, IFormFile layerFile, CancellationToken cancellationToken)
		{
			if (!CanManageMapData())
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.TimeZoneName = department?.TimeZone;

			if (layerFile == null || layerFile.Length == 0)
				ModelState.AddModelError(nameof(layerFile), _localizer["FileRequired"]);

			ValidateDetails(model);

			if (!ModelState.IsValid)
				return View(model);

			var read = ReadFile(layerFile, out var data);
			if (read == null)
				return View(model);

			var layer = new MapLayer
			{
				DepartmentId = DepartmentId,
				Type = (int)MapLayerTypes.TopLevel,
				AddedById = UserId,
				AddedOn = DateTime.UtcNow
			};

			ApplyDetails(layer, model, department);
			ApplyFile(layer, data, layerFile.FileName);

			var saved = await _mappingService.SaveMapLayerAsync(layer);
			if (saved == null)
			{
				ModelState.AddModelError("", _localizer["SaveFailed"]);
				return View(model);
			}

			TempData["LayersMessage"] = string.Format(_localizer["ImportedSummary"].Value, layer.Name, read.Points, read.Lines, read.Polygons)
				+ SkippedNote(read);

			return RedirectToAction("Layers", "Mapping", new { area = "User" });
		}

		[HttpGet]
		public async Task<IActionResult> Details(string layerId)
		{
			if (!CanManageMapData())
				return Unauthorized();

			var layer = await GetOwnLayerAsync(layerId);
			if (layer == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			return View(ToView(layer, department));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(MapLayerFileLimits.MaxFileBytes + 1024 * 1024)]
		[RequestFormLimits(MultipartBodyLengthLimit = MapLayerFileLimits.MaxFileBytes + 1024 * 1024)]
		public async Task<IActionResult> Details(EventLayerView model, IFormFile layerFile, CancellationToken cancellationToken)
		{
			if (!CanManageMapData())
				return Unauthorized();

			var layer = await GetOwnLayerAsync(model?.Id);
			if (layer == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			ValidateDetails(model);

			// A blank file input keeps the layer's shapes; a file replaces all of them.
			MapLayerFileReadResult read = null;
			MapLayerData data = null;
			if (ModelState.IsValid && layerFile != null && layerFile.Length > 0)
				read = ReadFile(layerFile, out data);

			if (!ModelState.IsValid)
				return View(Redisplay(model, layer, department));

			ApplyDetails(layer, model, department);
			if (read != null)
				ApplyFile(layer, data, layerFile.FileName);

			layer.UpdatedById = UserId;
			layer.UpdatedOn = DateTime.UtcNow;

			await _mappingService.SaveMapLayerAsync(layer);

			TempData["LayersMessage"] = read == null
				? string.Format(_localizer["SavedSummary"].Value, layer.Name)
				: string.Format(_localizer["ReplacedSummary"].Value, layer.Name, read.Points, read.Lines, read.Polygons) + SkippedNote(read);

			return RedirectToAction("Layers", "Mapping", new { area = "User" });
		}

		private async Task<MapLayer> GetOwnLayerAsync(string layerId)
		{
			if (string.IsNullOrWhiteSpace(layerId))
				return null;

			var layer = await _mappingService.GetMapLayersByIdAsync(layerId);
			if (layer == null || layer.IsDeleted || layer.DepartmentId != DepartmentId)
				return null;

			return layer;
		}

		private void ValidateDetails(EventLayerView model)
		{
			if (model == null)
				return;

			if (model.StartsOn.HasValue && model.EndsOn.HasValue && model.EndsOn.Value < model.StartsOn.Value)
				ModelState.AddModelError(nameof(EventLayerView.EndsOn), _localizer["EndsBeforeStarts"]);

			if (!string.IsNullOrWhiteSpace(model.ContactEmail) && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(model.ContactEmail.Trim()))
				ModelState.AddModelError(nameof(EventLayerView.ContactEmail), _localizer["ContactEmailInvalid"]);

			if (string.IsNullOrWhiteSpace(model.Color) || !System.Text.RegularExpressions.Regex.IsMatch(model.Color.Trim(), "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"))
				ModelState.AddModelError(nameof(EventLayerView.Color), _localizer["ColorInvalid"]);
		}

		/// <summary>
		/// The file's shapes and the layer data they make, or null with a model error saying why it cannot be used. The data is
		/// measured as stored: one layer is one document, whatever the shape and point counts.
		/// </summary>
		private MapLayerFileReadResult ReadFile(IFormFile file, out MapLayerData data)
		{
			data = null;
			var read = ReadShapes(file);
			if (read == null)
				return null;

			var hydrated = new MapLayerData();
			hydrated.Hydrate(read.Features);
			if (hydrated.StoredBytes() > MapLayerFileLimits.MaxStoredBytes)
			{
				ModelState.AddModelError("layerFile", string.Format(_localizer["FileTooMuchData"].Value, MapLayerFileLimits.MaxStoredBytes / (1024 * 1024)));
				return null;
			}

			data = hydrated;
			return read;
		}

		private MapLayerFileReadResult ReadShapes(IFormFile file)
		{
			var extension = (Path.GetExtension(file.FileName ?? string.Empty) ?? string.Empty).ToLowerInvariant();
			if (!AllowedExtensions.Contains(extension))
			{
				ModelState.AddModelError("layerFile", _localizer["FileTypeUnsupported"]);
				return null;
			}

			if (file.Length > MapLayerFileLimits.MaxFileBytes)
			{
				ModelState.AddModelError("layerFile", string.Format(_localizer["FileTooLarge"].Value, MapLayerFileLimits.MaxFileBytes / (1024 * 1024)));
				return null;
			}

			MapLayerFileReadResult read;
			using (var stream = file.OpenReadStream())
				read = MapLayerFileReader.Read(stream, file.FileName);

			if (read.Success)
				return read;

			switch (read.Error)
			{
				case MapLayerFileReadErrors.TooLarge:
					ModelState.AddModelError("layerFile", string.Format(_localizer["FileTooLarge"].Value, MapLayerFileLimits.MaxFileBytes / (1024 * 1024)));
					break;
				case MapLayerFileReadErrors.TooManyShapes:
					ModelState.AddModelError("layerFile", string.Format(_localizer["FileTooManyShapes"].Value, MapLayerFileLimits.MaxFeatures));
					break;
				case MapLayerFileReadErrors.TooManyPoints:
					ModelState.AddModelError("layerFile", string.Format(_localizer["FileTooManyPoints"].Value, MapLayerFileLimits.MaxPositions));
					break;
				case MapLayerFileReadErrors.NoShapes:
					ModelState.AddModelError("layerFile", read.SkippedWithoutGeometry > 0
						? string.Format(_localizer["FileOnlyAddresses"].Value, read.SkippedWithoutGeometry)
						: _localizer["FileNoShapes"].Value);
					break;
				case MapLayerFileReadErrors.UnsupportedType:
					ModelState.AddModelError("layerFile", _localizer["FileTypeUnsupported"]);
					break;
				default:
					ModelState.AddModelError("layerFile", _localizer["FileUnreadable"]);
					break;
			}

			return null;
		}

		private string SkippedNote(MapLayerFileReadResult read)
		{
			var skipped = read.SkippedWithoutGeometry + read.SkippedInvalid;
			return skipped > 0 ? " " + string.Format(_localizer["SkippedNote"].Value, skipped) : string.Empty;
		}

		private static void ApplyDetails(MapLayer layer, EventLayerView model, Department department)
		{
			layer.Name = model.Name?.Trim();
			layer.Description = Blank(model.Description);
			layer.Color = model.Color.Trim().ToLowerInvariant();
			layer.IsOnByDefault = model.IsOnByDefault;
			layer.IsSearchable = model.IsSearchable;
			layer.Organizer = Blank(model.Organizer);
			layer.ContactName = Blank(model.ContactName);
			layer.ContactPhone = Blank(model.ContactPhone);
			layer.ContactEmail = Blank(model.ContactEmail);
			layer.EventStartsOn = ToUtc(model.StartsOn, department);
			layer.EventEndsOn = ToUtc(model.EndsOn, department);
		}

		private void ApplyFile(MapLayer layer, MapLayerData data, string fileName)
		{
			layer.Data = data;

			var name = Path.GetFileName(fileName ?? string.Empty);
			layer.SourceFileName = string.IsNullOrWhiteSpace(name) ? "upload" : (name.Length > 260 ? name.Substring(name.Length - 260) : name);
			layer.ImportedById = UserId;
			layer.ImportedOn = DateTime.UtcNow;
		}

		private static DateTime? ToUtc(DateTime? local, Department department)
		{
			if (!local.HasValue)
				return null;

			var unspecified = DateTime.SpecifyKind(local.Value, DateTimeKind.Unspecified);
			return string.IsNullOrWhiteSpace(department?.TimeZone)
				? DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)
				: DateTimeHelpers.ConvertToUtc(unspecified, department.TimeZone, true);
		}

		private static DateTime? ToLocal(DateTime? utc, Department department)
		{
			if (!utc.HasValue)
				return null;

			var value = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
			return department == null ? value : value.TimeConverter(department);
		}

		private static EventLayerView ToView(MapLayer layer, Department department) => Redisplay(new EventLayerView
		{
			Id = layer.GetId(),
			Name = layer.Name,
			Description = layer.Description,
			Color = string.IsNullOrWhiteSpace(layer.Color) ? "#e8590c" : layer.Color,
			IsOnByDefault = layer.IsOnByDefault,
			IsSearchable = layer.IsSearchable,
			StartsOn = ToLocal(layer.EventStartsOn, department),
			EndsOn = ToLocal(layer.EventEndsOn, department),
			Organizer = layer.Organizer,
			ContactName = layer.ContactName,
			ContactPhone = layer.ContactPhone,
			ContactEmail = layer.ContactEmail
		}, layer, department);

		/// <summary>The read-only parts of the Details page, from the stored layer.</summary>
		private static EventLayerView Redisplay(EventLayerView model, MapLayer layer, Department department)
		{
			model.Id = layer.GetId();
			model.SourceFileName = layer.SourceFileName;
			model.ImportedOn = ToLocal(layer.ImportedOn, department);
			model.ShapeCount = layer.Data?.Features?.Count ?? 0;
			model.IsImported = layer.IsImported;
			model.Phase = MapLayerEvents.GetPhase(layer, DateTime.UtcNow);
			model.TimeZoneName = department?.TimeZone;
			return model;
		}

		private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
