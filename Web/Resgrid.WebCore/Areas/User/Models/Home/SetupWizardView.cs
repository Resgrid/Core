using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Home
{
	public class SetupWizardView
	{
		public Department Department { get; set; }
		public DepartmentGroup Station { get; set; }
		public bool DisableAutoAvailable { get; set; }
		public string UnitName { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public DepartmentCallEmail EmailSettings { get; set; }
		public List<Unit> Units { get; set; }
		public bool CanProvisionNumber { get; set; }
		public string DepartmentTextToCallNumber { get; set; }
		public string DepartmentTextToCallSourceNumbers { get; set; }
		public int StationCount { get; set; }
		public int UnitCount { get; set; }
		public string CallImportOption { get; set; }
		public SelectList CallImportOptions { get; set; }
		public string DepartmentEmailAddress { get; set; }
		public SetupWizardView()
		{
			StationCount = 0;
			UnitCount = 0;

			Station = new DepartmentGroup();
			Station.Address = new Address();
			EmailSettings = new DepartmentCallEmail();
			EmailSettings.Port = 110;

			CallImportOptions = new SelectList(new List<string> { "No Call Importing", "Email Call Importing", "Text Message Call Importing", "Direct Connection" }, "No Call Importing");
		}
	}
}