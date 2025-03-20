using System.Collections.Generic;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Controllers.Version3.Models.Roles;
using Resgrid.Web.Services.Controllers.Version3.Models.Security;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;

namespace Resgrid.Web.Services.Controllers.Version3.Models.CoreData
{
	public class CoreDataResult
	{
		public DepartmentRightsResult Rights { get; set; }
		public List<PersonnelInfoResult> Personnel { get; set; }
		public List<GroupInfoResult> Groups { get; set; }
		public List<UnitInfoResult> Units { get; set; }
		public List<RoleInfoResult> Roles { get; set; }
	}

	public class CustomStatusesResult
	{
		public int Id { get; set; }
		public int Type { get; set; }
		public int StateId { get; set; }
		public string Text { get; set; }
		public string BColor { get; set; }
		public string Color { get; set; }
		public bool Gps { get; set; }
		public int Note { get; set; }
		public int Detail { get; set; }
		public bool IsDeleted { get; set; }
	}
}
