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
		}

		public double CenterLat { get; set; }
		public double CenterLon { get; set; }
		public int ZoomLevel { get; set; }
		public List<MapMakerInfoData> MapMakerInfos { get; set; }
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
	}
}
