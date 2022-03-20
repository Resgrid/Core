using System.Collections.Generic;

namespace Resgrid.Providers.Geo.Models.LocationIQ
{
	public class LocationIQAddress
	{
		public string house_number { get; set; }
		public string road { get; set; }
		public string city { get; set; }
		public string county { get; set; }
		public string state { get; set; }
		public string postcode { get; set; }
		public string country { get; set; }
		public string country_code { get; set; }
	}

	public class LocationIQReverseResult
	{
		public string place_id { get; set; }
		public string licence { get; set; }
		public string lat { get; set; }
		public string lon { get; set; }
		public string display_name { get; set; }
		public List<string> boundingbox { get; set; }
		public double importance { get; set; }
		public LocationIQAddress address { get; set; }
	}
}
