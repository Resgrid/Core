using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	// ── List all maps ────────────────────────────────────────────────────────────

	public class GetAllCustomMapsResult : StandardApiResponseV4Base
	{
		/// <summary>Response data</summary>
		public GetAllCustomMapsResultData Data { get; set; }

		public GetAllCustomMapsResult()
		{
			Data = new GetAllCustomMapsResultData();
		}
	}

	public class GetAllCustomMapsResultData
	{
		public List<CustomMapResultData> Maps { get; set; } = new List<CustomMapResultData>();
	}

	// ── Single map ───────────────────────────────────────────────────────────────

	public class GetCustomMapResult : StandardApiResponseV4Base
	{
		/// <summary>Response data</summary>
		public CustomMapResultData Data { get; set; }
	}

	// ── Shared data shapes ────────────────────────────────────────────────────────

	public class CustomMapResultData
	{
		public string CustomMapId { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }

		/// <summary>CustomMapType integer value</summary>
		public int Type { get; set; }

		public double BoundsTopLeftLat { get; set; }
		public double BoundsTopLeftLng { get; set; }
		public double BoundsBottomRightLat { get; set; }
		public double BoundsBottomRightLng { get; set; }
		public int DefaultZoom { get; set; }
		public int MinZoom { get; set; }
		public int MaxZoom { get; set; }
		public bool IsActive { get; set; }
		public DateTime? EventStartsOn { get; set; }
		public DateTime? EventEndsOn { get; set; }
		public string AddedById { get; set; }
		public DateTime AddedOn { get; set; }
		public string UpdatedById { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public List<CustomMapFloorResultData> Floors { get; set; } = new List<CustomMapFloorResultData>();
	}

	public class CustomMapFloorResultData
	{
		public string CustomMapFloorId { get; set; }
		public string CustomMapId { get; set; }
		public int FloorNumber { get; set; }
		public string Name { get; set; }
		public string ImageUrl { get; set; }

		/// <summary>Tile base URL when the image has been processed into a tile pyramid</summary>
		public string TileBaseUrl { get; set; }

		public double? Elevation { get; set; }
		public int SortOrder { get; set; }
		public bool IsDefault { get; set; }
		public List<CustomMapZoneResultData> Zones { get; set; } = new List<CustomMapZoneResultData>();
	}

	public class CustomMapZoneResultData
	{
		public string CustomMapZoneId { get; set; }
		public string CustomMapFloorId { get; set; }
		public string Name { get; set; }

		/// <summary>CustomMapZoneType integer value</summary>
		public int ZoneType { get; set; }

		/// <summary>GeoJSON polygon in geo-projected (lat/lng) coordinates</summary>
		public string PolygonGeoJson { get; set; }

		public string Color { get; set; }
		public string Metadata { get; set; }
		public double? Elevation { get; set; }
		public bool IsSearchable { get; set; }
		public bool IsActive { get; set; }
	}

	// ── Save/Update input models ──────────────────────────────────────────────────

	public class SaveCustomMapInput
	{
		public string CustomMapId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int Type { get; set; }
		public double BoundsTopLeftLat { get; set; }
		public double BoundsTopLeftLng { get; set; }
		public double BoundsBottomRightLat { get; set; }
		public double BoundsBottomRightLng { get; set; }
		public int DefaultZoom { get; set; }
		public int MinZoom { get; set; }
		public int MaxZoom { get; set; }
		public bool IsActive { get; set; }
		public DateTime? EventStartsOn { get; set; }
		public DateTime? EventEndsOn { get; set; }
	}

	public class SaveCustomMapFloorInput
	{
		public string CustomMapFloorId { get; set; }
		public string CustomMapId { get; set; }
		public int FloorNumber { get; set; }
		public string Name { get; set; }
		public string ImageUrl { get; set; }
		public double? Elevation { get; set; }
		public int SortOrder { get; set; }
		public bool IsDefault { get; set; }
	}

	public class SaveCustomMapZoneInput
	{
		public string CustomMapZoneId { get; set; }
		public string CustomMapFloorId { get; set; }
		public string Name { get; set; }
		public int ZoneType { get; set; }
		public string PolygonGeoJson { get; set; }
		public string Color { get; set; }
		public string Metadata { get; set; }
		public double? Elevation { get; set; }
		public bool IsSearchable { get; set; }
		public bool IsActive { get; set; }
	}

	public class ResolveCoordinateInput
	{
		public string CustomMapFloorId { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
	}

	public class ResolveCoordinateResult : StandardApiResponseV4Base
	{
		public ResolveCoordinateResultData Data { get; set; }
	}

	public class ResolveCoordinateResultData
	{
		public bool Resolved { get; set; }
		public CustomMapZoneResultData Zone { get; set; }
	}

	// ── Floor result wrapper ──────────────────────────────────────────────────────

	public class GetCustomMapFloorResult : StandardApiResponseV4Base
	{
		public CustomMapFloorResultData Data { get; set; }
	}

	public class GetAllCustomMapFloorsResult : StandardApiResponseV4Base
	{
		public GetAllCustomMapFloorsResultData Data { get; set; }

		public GetAllCustomMapFloorsResult()
		{
			Data = new GetAllCustomMapFloorsResultData();
		}
	}

	public class GetAllCustomMapFloorsResultData
	{
		public List<CustomMapFloorResultData> Floors { get; set; } = new List<CustomMapFloorResultData>();
	}

	// ── Zone result wrapper ───────────────────────────────────────────────────────

	public class GetCustomMapZoneResult : StandardApiResponseV4Base
	{
		public CustomMapZoneResultData Data { get; set; }
	}

	public class GetAllCustomMapZonesResult : StandardApiResponseV4Base
	{
		public GetAllCustomMapZonesResultData Data { get; set; }

		public GetAllCustomMapZonesResult()
		{
			Data = new GetAllCustomMapZonesResultData();
		}
	}

	public class GetAllCustomMapZonesResultData
	{
		public List<CustomMapZoneResultData> Zones { get; set; } = new List<CustomMapZoneResultData>();
	}
}

