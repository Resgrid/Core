using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Mapping;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Custom Maps — department-owned indoor/satellite/schematic map management.
	/// Provides CRUD for maps, floors, and polygon zones; also resolves GPS coordinates
	/// to human-readable zone names for use as call locations.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CustomMapsController : V4AuthenticatedApiControllerbase
	{
		private readonly ICustomMapsService _customMapsService;
		private readonly IDepartmentsService _departmentsService;

		public CustomMapsController(ICustomMapsService customMapsService, IDepartmentsService departmentsService)
		{
			_customMapsService = customMapsService;
			_departmentsService = departmentsService;
		}

		// ── Maps ─────────────────────────────────────────────────────────────────

		/// <summary>
		/// Gets all custom maps for the caller's department.
		/// </summary>
		[HttpGet("GetCustomMaps")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CustomMap_View)]
		public async Task<ActionResult<GetAllCustomMapsResult>> GetCustomMaps()
		{
			var result = new GetAllCustomMapsResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var maps = await _customMapsService.GetCustomMapsForDepartmentAsync(DepartmentId);

			result.Data = new GetAllCustomMapsResultData
			{
				Maps = maps.Select(MapToResultData).ToList()
			};
			result.PageSize = result.Data.Maps.Count;
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Gets a single custom map by id, including its floors and zones.
		/// </summary>
		[HttpGet("GetCustomMap/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_View)]
		public async Task<ActionResult<GetCustomMapResult>> GetCustomMap(string id)
		{
			var result = new GetCustomMapResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var map = await _customMapsService.GetCustomMapByIdAsync(id);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			result.Data = MapToResultData(map);
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Creates a new custom map for the department.
		/// </summary>
		[HttpPost("SaveCustomMap")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CustomMap_Create)]
		public async Task<ActionResult<GetCustomMapResult>> SaveCustomMap([FromBody] SaveCustomMapInput input)
		{
			var result = new GetCustomMapResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var map = new CustomMap
			{
				DepartmentId = DepartmentId,
				Name = input.Name,
				Description = input.Description,
				Type = input.Type,
				BoundsTopLeftLat = input.BoundsTopLeftLat,
				BoundsTopLeftLng = input.BoundsTopLeftLng,
				BoundsBottomRightLat = input.BoundsBottomRightLat,
				BoundsBottomRightLng = input.BoundsBottomRightLng,
				DefaultZoom = input.DefaultZoom,
				MinZoom = input.MinZoom,
				MaxZoom = input.MaxZoom,
				IsActive = input.IsActive,
				EventStartsOn = input.EventStartsOn,
				EventEndsOn = input.EventEndsOn,
				AddedById = UserId,
				AddedOn = DateTime.UtcNow
			};

			var saved = await _customMapsService.SaveCustomMapAsync(map);
			result.Data = MapToResultData(saved);
			result.Status = ResponseHelper.Created;

			return Ok(result);
		}

		/// <summary>
		/// Updates an existing custom map.
		/// </summary>
		[HttpPut("UpdateCustomMap")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_Update)]
		public async Task<ActionResult<GetCustomMapResult>> UpdateCustomMap([FromBody] SaveCustomMapInput input)
		{
			var result = new GetCustomMapResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var existing = await _customMapsService.GetCustomMapByIdAsync(input.CustomMapId);
			if (existing == null || existing.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			existing.Name = input.Name;
			existing.Description = input.Description;
			existing.Type = input.Type;
			existing.BoundsTopLeftLat = input.BoundsTopLeftLat;
			existing.BoundsTopLeftLng = input.BoundsTopLeftLng;
			existing.BoundsBottomRightLat = input.BoundsBottomRightLat;
			existing.BoundsBottomRightLng = input.BoundsBottomRightLng;
			existing.DefaultZoom = input.DefaultZoom;
			existing.MinZoom = input.MinZoom;
			existing.MaxZoom = input.MaxZoom;
			existing.IsActive = input.IsActive;
			existing.EventStartsOn = input.EventStartsOn;
			existing.EventEndsOn = input.EventEndsOn;
			existing.UpdatedById = UserId;
			existing.UpdatedOn = DateTime.UtcNow;

			var saved = await _customMapsService.SaveCustomMapAsync(existing);
			result.Data = MapToResultData(saved);
			result.Status = ResponseHelper.Updated;

			return Ok(result);
		}

		/// <summary>
		/// Deletes a custom map and all its floors/zones.
		/// </summary>
		[HttpDelete("DeleteCustomMap/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_Delete)]
		public async Task<ActionResult<GetCustomMapResult>> DeleteCustomMap(string id)
		{
			var result = new GetCustomMapResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var existing = await _customMapsService.GetCustomMapByIdAsync(id);
			if (existing == null || existing.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			await _customMapsService.DeleteCustomMapAsync(id);
			result.Status = ResponseHelper.Deleted;

			return Ok(result);
		}

		// ── Floors ────────────────────────────────────────────────────────────────

		/// <summary>
		/// Gets all floors for a custom map.
		/// </summary>
		[HttpGet("GetFloors/{customMapId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CustomMap_View)]
		public async Task<ActionResult<GetAllCustomMapFloorsResult>> GetFloors(string customMapId)
		{
			var result = new GetAllCustomMapFloorsResult();
			ResponseHelper.PopulateV4ResponseData(result);

			// Verify ownership
			var map = await _customMapsService.GetCustomMapByIdAsync(customMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var floors = await _customMapsService.GetFloorsForMapAsync(customMapId);
			result.Data = new GetAllCustomMapFloorsResultData
			{
				Floors = floors.Select(FloorToResultData).ToList()
			};
			result.PageSize = result.Data.Floors.Count;
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Creates or updates a floor on a custom map.
		/// </summary>
		[HttpPost("SaveFloor")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_Create)]
		public async Task<ActionResult<GetCustomMapFloorResult>> SaveFloor([FromBody] SaveCustomMapFloorInput input)
		{
			var result = new GetCustomMapFloorResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var map = await _customMapsService.GetCustomMapByIdAsync(input.CustomMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			CustomMapFloor floor;
			string status;

			if (!string.IsNullOrEmpty(input.CustomMapFloorId))
			{
				floor = await _customMapsService.GetFloorByIdAsync(input.CustomMapFloorId);
				if (floor == null)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return NotFound(result);
				}

				floor.FloorNumber = input.FloorNumber;
				floor.Name = input.Name;
				floor.ImageUrl = input.ImageUrl;
				floor.Elevation = input.Elevation;
				floor.SortOrder = input.SortOrder;
				floor.IsDefault = input.IsDefault;
				status = ResponseHelper.Updated;
			}
			else
			{
				floor = new CustomMapFloor
				{
					CustomMapId = input.CustomMapId,
					FloorNumber = input.FloorNumber,
					Name = input.Name,
					ImageUrl = input.ImageUrl,
					Elevation = input.Elevation,
					SortOrder = input.SortOrder,
					IsDefault = input.IsDefault
				};
				status = ResponseHelper.Created;
			}

			var saved = await _customMapsService.SaveFloorAsync(floor);
			result.Data = FloorToResultData(saved);
			result.Status = status;

			return Ok(result);
		}

		/// <summary>
		/// Deletes a floor and all its zones.
		/// </summary>
		[HttpDelete("DeleteFloor/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_Delete)]
		public async Task<ActionResult<GetCustomMapFloorResult>> DeleteFloor(string id)
		{
			var result = new GetCustomMapFloorResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var floor = await _customMapsService.GetFloorByIdAsync(id);
			if (floor == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			// Verify map ownership
			var map = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			await _customMapsService.DeleteFloorAsync(id);
			result.Status = ResponseHelper.Deleted;

			return Ok(result);
		}

		// ── Zones ─────────────────────────────────────────────────────────────────

		/// <summary>
		/// Gets all zones for a specific floor.
		/// </summary>
		[HttpGet("GetZones/{floorId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CustomMap_View)]
		public async Task<ActionResult<GetAllCustomMapZonesResult>> GetZones(string floorId)
		{
			var result = new GetAllCustomMapZonesResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var floor = await _customMapsService.GetFloorByIdAsync(floorId);
			if (floor == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var map = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var zones = await _customMapsService.GetZonesForFloorAsync(floorId);
			result.Data = new GetAllCustomMapZonesResultData
			{
				Zones = zones.Select(ZoneToResultData).ToList()
			};
			result.PageSize = result.Data.Zones.Count;
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Creates or updates a polygon zone on a floor.
		/// </summary>
		[HttpPost("SaveZone")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_Create)]
		public async Task<ActionResult<GetCustomMapZoneResult>> SaveZone([FromBody] SaveCustomMapZoneInput input)
		{
			var result = new GetCustomMapZoneResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var floor = await _customMapsService.GetFloorByIdAsync(input.CustomMapFloorId);
			if (floor == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var map = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			CustomMapZone zone;
			string status;

			if (!string.IsNullOrEmpty(input.CustomMapZoneId))
			{
				zone = await _customMapsService.GetZoneByIdAsync(input.CustomMapZoneId);
				if (zone == null)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return NotFound(result);
				}

				zone.Name = input.Name;
				zone.ZoneType = input.ZoneType;
				zone.PolygonGeoJson = input.PolygonGeoJson;
				zone.Color = input.Color;
				zone.Metadata = input.Metadata;
				zone.Elevation = input.Elevation;
				zone.IsSearchable = input.IsSearchable;
				zone.IsActive = input.IsActive;
				status = ResponseHelper.Updated;
			}
			else
			{
				zone = new CustomMapZone
				{
					CustomMapFloorId = input.CustomMapFloorId,
					Name = input.Name,
					ZoneType = input.ZoneType,
					PolygonGeoJson = input.PolygonGeoJson,
					Color = input.Color,
					Metadata = input.Metadata,
					Elevation = input.Elevation,
					IsSearchable = input.IsSearchable,
					IsActive = input.IsActive
				};
				status = ResponseHelper.Created;
			}

			var saved = await _customMapsService.SaveZoneAsync(zone);
			result.Data = ZoneToResultData(saved);
			result.Status = status;

			return Ok(result);
		}

		/// <summary>
		/// Deletes a polygon zone.
		/// </summary>
		[HttpDelete("DeleteZone/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.CustomMap_Delete)]
		public async Task<ActionResult<GetCustomMapZoneResult>> DeleteZone(string id)
		{
			var result = new GetCustomMapZoneResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var zone = await _customMapsService.GetZoneByIdAsync(id);
			if (zone == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var floor = await _customMapsService.GetFloorByIdAsync(zone.CustomMapFloorId);
			if (floor != null)
			{
				var map = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
				if (map == null || map.DepartmentId != DepartmentId)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return NotFound(result);
				}
			}

			await _customMapsService.DeleteZoneAsync(id);
			result.Status = ResponseHelper.Deleted;

			return Ok(result);
		}

		// ── Coordinate Resolution ─────────────────────────────────────────────────

		/// <summary>
		/// Resolves a geo-projected (lat/lng) coordinate to a zone name using
		/// point-in-polygon. Useful for auto-filling call locations from GPS.
		/// </summary>
		[HttpPost("ResolveCoordinate")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CustomMap_View)]
		public async Task<ActionResult<ResolveCoordinateResult>> ResolveCoordinate([FromBody] ResolveCoordinateInput input)
		{
			var result = new ResolveCoordinateResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var floor = await _customMapsService.GetFloorByIdAsync(input.CustomMapFloorId);
			if (floor == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var map = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var zone = await _customMapsService.ResolveCoordinateToZoneAsync(
				input.CustomMapFloorId, input.Latitude, input.Longitude);

			result.Data = new ResolveCoordinateResultData
			{
				Resolved = zone != null,
				Zone = zone != null ? ZoneToResultData(zone) : null
			};
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		/// <summary>
		/// Gets all searchable zones across all floors of a map (for name/coordinate search).
		/// </summary>
		[HttpGet("GetSearchableZones/{customMapId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CustomMap_View)]
		public async Task<ActionResult<GetAllCustomMapZonesResult>> GetSearchableZones(string customMapId)
		{
			var result = new GetAllCustomMapZonesResult();
			ResponseHelper.PopulateV4ResponseData(result);

			var map = await _customMapsService.GetCustomMapByIdAsync(customMapId);
			if (map == null || map.DepartmentId != DepartmentId)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return NotFound(result);
			}

			var zones = await _customMapsService.GetSearchableZonesByMapIdAsync(customMapId);
			result.Data = new GetAllCustomMapZonesResultData
			{
				Zones = zones.Select(ZoneToResultData).ToList()
			};
			result.PageSize = result.Data.Zones.Count;
			result.Status = ResponseHelper.Success;

			return Ok(result);
		}

		// ── Private Mapping Helpers ───────────────────────────────────────────────

		private static CustomMapResultData MapToResultData(CustomMap map)
		{
			var data = new CustomMapResultData
			{
				CustomMapId = map.CustomMapId,
				DepartmentId = map.DepartmentId,
				Name = map.Name,
				Description = map.Description,
				Type = map.Type,
				BoundsTopLeftLat = map.BoundsTopLeftLat,
				BoundsTopLeftLng = map.BoundsTopLeftLng,
				BoundsBottomRightLat = map.BoundsBottomRightLat,
				BoundsBottomRightLng = map.BoundsBottomRightLng,
				DefaultZoom = map.DefaultZoom,
				MinZoom = map.MinZoom,
				MaxZoom = map.MaxZoom,
				IsActive = map.IsActive,
				EventStartsOn = map.EventStartsOn,
				EventEndsOn = map.EventEndsOn,
				AddedById = map.AddedById,
				AddedOn = map.AddedOn,
				UpdatedById = map.UpdatedById,
				UpdatedOn = map.UpdatedOn
			};

			if (map.Floors != null)
				data.Floors = map.Floors.Select(FloorToResultData).ToList();

			return data;
		}

		private static CustomMapFloorResultData FloorToResultData(CustomMapFloor floor)
		{
			var data = new CustomMapFloorResultData
			{
				CustomMapFloorId = floor.CustomMapFloorId,
				CustomMapId = floor.CustomMapId,
				FloorNumber = floor.FloorNumber,
				Name = floor.Name,
				ImageUrl = floor.ImageUrl,
				TileBaseUrl = floor.TileBaseUrl,
				Elevation = floor.Elevation,
				SortOrder = floor.SortOrder,
				IsDefault = floor.IsDefault
			};

			if (floor.Zones != null)
				data.Zones = floor.Zones.Select(ZoneToResultData).ToList();

			return data;
		}

		private static CustomMapZoneResultData ZoneToResultData(CustomMapZone zone) =>
			new CustomMapZoneResultData
			{
				CustomMapZoneId = zone.CustomMapZoneId,
				CustomMapFloorId = zone.CustomMapFloorId,
				Name = zone.Name,
				ZoneType = zone.ZoneType,
				PolygonGeoJson = zone.PolygonGeoJson,
				Color = zone.Color,
				Metadata = zone.Metadata,
				Elevation = zone.Elevation,
				IsSearchable = zone.IsSearchable,
				IsActive = zone.IsActive
			};
	}
}

