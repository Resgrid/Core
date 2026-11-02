using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	/// <summary>A maximum response area to measure (§19.3).</summary>
	public class EstimateResponseAreaInput
	{
		/// <summary>GeoJSON Polygon, MultiPolygon, Feature or FeatureCollection; empty measures the fallback area.</summary>
		public string GeoJson { get; set; }
	}

	/// <summary>Base-map download size of a response area.</summary>
	public class EstimateResponseAreaResult : StandardApiResponseV4Base
	{
		public OfflineResponseAreaEstimate Data { get; set; }
	}
}
