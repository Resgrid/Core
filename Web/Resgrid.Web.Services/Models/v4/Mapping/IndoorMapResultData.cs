namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class IndoorMapResultData
	{
		public string IndoorMapId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public decimal CenterLatitude { get; set; }
		public decimal CenterLongitude { get; set; }
		public decimal BoundsNELat { get; set; }
		public decimal BoundsNELon { get; set; }
		public decimal BoundsSWLat { get; set; }
		public decimal BoundsSWLon { get; set; }
		public string DefaultFloorId { get; set; }
	}
}
