using System.Collections.Generic;

namespace Resgrid.Providers.GeoLocationProvider
{
	public class W3WResponse
	{
		public string type { get; set; }
		public string words { get; set; }
		public Geometry coordinates { get; set; }
		public string language { get; set; }
		public string map { get; set; }
		public dynamic bounds { get; set; }
	}

	public class ReverseW3WResponse
	{
		public string type { get; set; }
		public string words { get; set; }
		public Geometry coordinates { get; set; }
		public string language { get; set; }
		public string map { get; set; }
		public dynamic bounds { get; set; }
	}

	public class Geometry
	{
		public double lat { get; set; }
		public double lng { get; set; }
	}
}