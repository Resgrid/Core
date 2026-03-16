namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class IndoorMapZoneResultData
	{
		public string IndoorMapZoneId { get; set; }
		public string IndoorMapFloorId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int ZoneType { get; set; }
		public string PixelGeometry { get; set; }
		public string GeoGeometry { get; set; }
		public double CenterPixelX { get; set; }
		public double CenterPixelY { get; set; }
		public decimal CenterLatitude { get; set; }
		public decimal CenterLongitude { get; set; }
		public string Color { get; set; }
		public string Metadata { get; set; }
		public bool IsSearchable { get; set; }
	}
}
