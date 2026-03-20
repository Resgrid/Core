using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class SearchAllMapFeaturesResult : StandardApiResponseV4Base
	{
		public List<MapFeatureResultData> Data { get; set; }

		public SearchAllMapFeaturesResult()
		{
			Data = new List<MapFeatureResultData>();
		}
	}

	public class MapFeatureResultData
	{
		/// <summary>"indoor_zone" or "custom_region"</summary>
		public string FeatureType { get; set; }
		public string Id { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string MapId { get; set; }
		public string MapName { get; set; }
		public string FloorOrLayerId { get; set; }
		public string FloorOrLayerName { get; set; }
		public decimal CenterLatitude { get; set; }
		public decimal CenterLongitude { get; set; }
		public bool IsDispatchable { get; set; }
	}
}
