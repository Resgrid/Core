namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class PoiJson
	{
		public int PoiId { get; set; }
		public int PoiTypeId { get; set; }
		public string Name { get; set; }
		public double Longitude { get; set; }
		public double Latitude { get; set; }
		public string Address { get; set; }
		public string Note { get; set; }
	}
}
