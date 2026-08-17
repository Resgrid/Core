using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	/// <summary>
	/// The group/role/personnel pickers shown on the new and edit test screens, plus what the
	/// admin currently has selected. All three selections empty means the test covers everyone.
	/// </summary>
	public class CommunicationTestTargetOptions
	{
		public List<DepartmentGroup> Groups { get; set; } = new List<DepartmentGroup>();
		public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();
		public List<CommunicationTestPersonnelOption> Personnel { get; set; } = new List<CommunicationTestPersonnelOption>();
	}

	public class CommunicationTestPersonnelOption
	{
		public string UserId { get; set; }
		public string Name { get; set; }
	}
}
