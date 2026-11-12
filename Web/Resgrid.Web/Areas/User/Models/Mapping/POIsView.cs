using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class POIsView
	{
		public List<PoiType> Types { get; set; }
		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		/// <summary>The department's recent background POI imports, newest first.</summary>
		public List<PoiImportListItem> Imports { get; set; }

		public POIsView()
		{
			Types = new List<PoiType>();
			Imports = new List<PoiImportListItem>();
		}
	}

	/// <summary>One background POI import as the POIs page lists it; the text is already localized.</summary>
	public class PoiImportListItem
	{
		public string FileName { get; set; }
		public string TypeName { get; set; }
		public DateTime Uploaded { get; set; }
		public string Status { get; set; }
		public bool IsActive { get; set; }
		public bool IsFailed { get; set; }
		public string Error { get; set; }
		public List<string> Lines { get; set; } = new List<string>();
	}
}
