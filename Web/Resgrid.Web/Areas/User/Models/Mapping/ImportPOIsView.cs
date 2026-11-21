using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class ImportPOIsView
	{
		public bool HydrantsEnabled { get; set; }
		public string Message { get; set; }
		public int TypeId { get; set; }

		/// <summary>The department's POI types for the type picker (a hydrant manager arrives without a type to import into).</summary>
		public List<PoiType> PoiTypes { get; set; } = new List<PoiType>();

		/// <summary>Department admins go back to the POIs page; other importers (hydrant managers) cannot open it.</summary>
		public bool CanManageMapData { get; set; }

		/// <summary>The recent imports the caller may follow (see MappingController.GetPoiImportListAsync).</summary>
		public List<PoiImportListItem> Imports { get; set; } = new List<PoiImportListItem>();
	}
}
