namespace Resgrid.Web.Areas.User.Models.Home
{
	public class MapMakerInfo
	{
		public double Longitude { get; set; }
		public double Latitude { get; set; }
		public string Title { get; set; }
		public int zIndex { get; set; }
		public string ImagePath { get; set; }
		public string Marker { get; set; }
		public string InfoWindowContent { get; set; }
		public string Color { get; set; }
	}
}