using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class MappingSettingsView
	{
		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

		public int PersonnelLocationTTL { get; set; }
		public int UnitLocationTTL { get; set; }

		public bool PersonnelAllowStatusWithNoLocationToOverwrite { get; set; }
		public bool UnitAllowStatusWithNoLocationToOverwrite { get; set; }
	}
}
