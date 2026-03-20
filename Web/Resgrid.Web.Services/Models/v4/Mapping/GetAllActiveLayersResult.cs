using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class GetAllActiveLayersResult : StandardApiResponseV4Base
	{
		public List<ActiveLayerResultData> Data { get; set; }

		public GetAllActiveLayersResult()
		{
			Data = new List<ActiveLayerResultData>();
		}
	}

	public class ActiveLayerResultData
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string LayerSource { get; set; } // "maplayer" or "custommaplayer"
		public int Type { get; set; }
		public string Color { get; set; }
		public bool IsSearchable { get; set; }
		public bool IsOnByDefault { get; set; }
	}
}
