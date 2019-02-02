namespace Resgrid.Providers.GeoLocationProvider
{
	public class LocationIQReverseGeocodeResponse
	{
		public string place_id { get; set; }
		public string license { get; set; }
		public string osm_type { get; set; }
		public string osm_id { get; set; }
		public string[] boundingbox { get; set; }
		public string lat { get; set; }
		public string lon { get; set; }
		public string display_name { get; set; }
		public string type { get; set; }
		public string importance { get; set; }
	}
}