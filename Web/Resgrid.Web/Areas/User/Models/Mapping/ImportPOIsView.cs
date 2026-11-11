using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class ImportPOIsView
	{
		public bool HydrantsEnabled { get; set; }
		public string Message { get; set; }
		public int TypeId { get; set; }

		/// <summary>The recent imports the caller may follow (see MappingController.GetPoiImportListAsync).</summary>
		public List<PoiImportListItem> Imports { get; set; } = new List<PoiImportListItem>();
	}
}