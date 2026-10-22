using Microsoft.AspNetCore.Http;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class AddPOITypeView
	{
		public string Message { get; set; }
		public string MarkerType { get; set; }
		public PoiType Type { get; set; }

		/// <summary>An uploaded custom icon image for the type's markers.</summary>
		public IFormFile IconFile { get; set; }

		public AddPOITypeView()
		{
			MarkerType = "MAP_PIN";
		}
	}
}