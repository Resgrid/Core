using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class PoiTypeResultData
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

	public class PoiResultData
	{
		public int PoiId { get; set; }
		public int PoiTypeId { get; set; }
		public string PoiTypeName { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }
		public string Note { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public string Color { get; set; }
		public string ImagePath { get; set; }
		public string Marker { get; set; }
		public bool IsDestination { get; set; }

		/// <summary>
		/// The POI-specific custom icon image name.
		/// New app versions should use this field for POI icons.
		/// ImagePath is set to null so old apps fall back to their default icon.
		/// </summary>
		public string PoiImage { get; set; }
	}

	public class PoiTypesResult : StandardApiResponseV4Base
	{
		public List<PoiTypeResultData> Data { get; set; } = new List<PoiTypeResultData>();
	}

	public class PoisResult : StandardApiResponseV4Base
	{
		public List<PoiResultData> Data { get; set; } = new List<PoiResultData>();
	}

	public class PoiResult : StandardApiResponseV4Base
	{
		public PoiResultData Data { get; set; }
	}
}
