using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class GetIndoorMapsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<IndoorMapResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetIndoorMapsResult()
		{
			Data = new List<IndoorMapResultData>();
		}
	}

	public class GetIndoorMapResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetIndoorMapResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetIndoorMapResult()
		{
			Data = new GetIndoorMapResultData();
		}
	}

	public class GetIndoorMapResultData
	{
		public IndoorMapResultData Map { get; set; }
		public List<IndoorMapFloorResultData> Floors { get; set; }

		public GetIndoorMapResultData()
		{
			Floors = new List<IndoorMapFloorResultData>();
		}
	}

	public class GetIndoorMapFloorResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public IndoorMapFloorResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetIndoorMapFloorResult()
		{
			Data = new IndoorMapFloorResultData();
		}
	}

	public class SearchIndoorLocationsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<IndoorMapZoneResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public SearchIndoorLocationsResult()
		{
			Data = new List<IndoorMapZoneResultData>();
		}
	}

	public class GetIndoorMapZonesGeoJSONResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// GeoJSON FeatureCollection string ready for direct rnmapbox ShapeSource consumption
		/// </summary>
		public string GeoJson { get; set; }
	}
}
