using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class GetMapDataResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetMapDataResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetMapDataResult()
		{
			Data = new GetMapDataResultData();
		}
	}

	public class GetMapDataResultData
	{
		public GetMapDataResultData()
		{
			MapMakerInfos = new List<MapMakerInfoData>();
			PoiLayers = new List<PoiLayerData>();
		}

		public bool HydrantsAvailable { get; set; }
		public string HydrantsError { get; set; }
		public double CenterLat { get; set; }
		public double CenterLon { get; set; }
		public int ZoomLevel { get; set; }
		public List<MapMakerInfoData> MapMakerInfos { get; set; }
		public List<PoiLayerData> PoiLayers { get; set; }
	}

	public class MapMakerInfoData
	{
		public string Id { get; set; }
		public double Longitude { get; set; }
		public double Latitude { get; set; }
		public string Title { get; set; }
		public int zIndex { get; set; }
		public string ImagePath { get; set; }
		public string InfoWindowContent { get; set; }
		public string Color { get; set; }
		public int Type { get; set; }
		public string Marker { get; set; }
		public int? PoiTypeId { get; set; }
		public string PoiTypeName { get; set; }
		public string Address { get; set; }
		public string Note { get; set; }
		public string LayerId { get; set; }
		public string LayerName { get; set; }

		/// <summary>
		/// The POI-specific custom icon image name (only set for POI markers, Type=4).
		/// New app versions should use this field instead of ImagePath for POI icons.
		/// ImagePath carries the bitmap-asset name resolved by PoiIconHelper for clients that ship PNGs.
		/// </summary>
		public string PoiImage { get; set; }

		/// <summary>
		/// Absolute URL of the department's uploaded type icon for the marker's unit type (Type=1), call type (Type=0) or
		/// POI type (Type=4), or for a person (Type=3) the icon of the first role they hold, in the department's role
		/// order, that has one: a 128 px square PNG, anonymous GET, immutable. For a call, unit or person it is the icon
		/// uploaded for the current priority or status when there is one; otherwise the type's own icon, carrying a badge in
		/// the colour of <see cref="IconStyle"/> when that is set. Null when there is no uploaded icon; PinIconUrl, then
		/// ImagePath / PoiImage, are the fallback.
		/// </summary>
		public string IconUrl { get; set; }

		/// <summary>
		/// Absolute URL of the built-in pin for a call (Type=0), unit (Type=1) or person (Type=3) recoloured to the style of
		/// its priority or status: a 32 x 37 PNG whose bottom tip is the position, anonymous GET, immutable. Shown when
		/// IconUrl is null. A priority or status with no meaning (and a name that suggests none), or a unit or person with no
		/// status, gets the gray "unknown" pin. Null for every other marker type, so ImagePath stays the bundled fallback.
		/// </summary>
		public string PinIconUrl { get; set; }

		/// <summary>
		/// The style of a call's priority or a unit's or person's status: available, dispatched, responding, onscene,
		/// transporting, returning, staging, delayed, unavailable (statuses), low, medium, high, emergency (priorities) or
		/// unknown (no meaning, or no status). Null for every other marker type.
		/// </summary>
		public string IconStyle { get; set; }
	}

	public class PoiLayerData
	{
		public int PoiTypeId { get; set; }
		public string Name { get; set; }
		public string Color { get; set; }
		public string ImagePath { get; set; }
		public string Marker { get; set; }
		public bool IsDestination { get; set; }

		/// <summary>
		/// The POI-specific custom icon image name.
		/// New app versions should use this field for POI type icons.
		/// ImagePath carries the bitmap-asset name resolved by PoiIconHelper for clients that ship PNGs.
		/// </summary>
		public string PoiImage { get; set; }

		/// <summary>
		/// Absolute URL of the department's uploaded icon for the POI type (a 128 px square PNG, anonymous GET,
		/// immutable); null when the type has no uploaded icon, so PoiImage / ImagePath stay the marker icon.
		/// </summary>
		public string IconUrl { get; set; }
	}
}
