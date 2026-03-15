using Resgrid.Model;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class ManageZonesView
	{
		public string CustomMapFloorId { get; set; }
		public string CustomMapId { get; set; }
		public string FloorName { get; set; }
		public string CustomMapName { get; set; }
		public string ImageUrl { get; set; }
		public string TileBaseUrl { get; set; }
		public CustomMapFloorStorageType StorageType { get; set; }
		public int TileZoomLevels { get; set; } = 1;
		public string Message { get; set; }

		/// <summary>GeoJSON FeatureCollection of all zones on this floor (round-tripped via hidden field)</summary>
		public string ZonesGeoJson { get; set; }

		/// <summary>Geo-bounds of the floor image overlay for Leaflet</summary>
		public double BoundsTopLeftLat { get; set; }
		public double BoundsTopLeftLng { get; set; }
		public double BoundsBottomRightLat { get; set; }
		public double BoundsBottomRightLng { get; set; }
		public int DefaultZoom { get; set; } = 18;

		public List<CustomMapZone> Zones { get; set; } = new List<CustomMapZone>();

		public List<SelectListItem> ZoneTypes { get; set; } = new List<SelectListItem>();
	}
}

