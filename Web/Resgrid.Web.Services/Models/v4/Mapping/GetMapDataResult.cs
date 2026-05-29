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
		/// ImagePath is set to null for POI markers so old apps fall back to their default icon.
		/// </summary>
		public string PoiImage { get; set; }
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
		/// ImagePath is set to null so old apps fall back to their default icon.
		/// </summary>
		public string PoiImage { get; set; }
	}
}
