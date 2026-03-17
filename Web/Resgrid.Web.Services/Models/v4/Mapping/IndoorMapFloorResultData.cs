using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class IndoorMapFloorResultData
	{
		public string IndoorMapFloorId { get; set; }
		public string IndoorMapId { get; set; }
		public string Name { get; set; }
		public int FloorOrder { get; set; }
		public bool HasImage { get; set; }
		public decimal? BoundsNELat { get; set; }
		public decimal? BoundsNELon { get; set; }
		public decimal? BoundsSWLat { get; set; }
		public decimal? BoundsSWLon { get; set; }
		public decimal Opacity { get; set; }
		public List<IndoorMapZoneResultData> Zones { get; set; }
	}
}
