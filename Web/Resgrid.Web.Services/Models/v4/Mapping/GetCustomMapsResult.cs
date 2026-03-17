using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class GetCustomMapsResult : StandardApiResponseV4Base
	{
		public List<CustomMapResultData> Data { get; set; }

		public GetCustomMapsResult()
		{
			Data = new List<CustomMapResultData>();
		}
	}

	public class GetCustomMapResult : StandardApiResponseV4Base
	{
		public GetCustomMapResultData Data { get; set; }

		public GetCustomMapResult()
		{
			Data = new GetCustomMapResultData();
		}
	}

	public class GetCustomMapResultData
	{
		public CustomMapResultData Map { get; set; }
		public List<CustomMapLayerResultData> Layers { get; set; }

		public GetCustomMapResultData()
		{
			Layers = new List<CustomMapLayerResultData>();
		}
	}

	public class GetCustomMapLayerResult : StandardApiResponseV4Base
	{
		public CustomMapLayerResultData Data { get; set; }

		public GetCustomMapLayerResult()
		{
			Data = new CustomMapLayerResultData();
		}
	}

	public class SearchCustomMapRegionsResult : StandardApiResponseV4Base
	{
		public List<CustomMapRegionResultData> Data { get; set; }

		public SearchCustomMapRegionsResult()
		{
			Data = new List<CustomMapRegionResultData>();
		}
	}
}
