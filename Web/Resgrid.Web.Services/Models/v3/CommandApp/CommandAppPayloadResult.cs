using System.Collections.Generic;
using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Controllers.Version3.Models.CoreData;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Controllers.Version3.Models.Roles;
using Resgrid.Web.Services.Controllers.Version3.Models.Security;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using UnitInfoResult = Resgrid.Web.Services.Controllers.Version3.Models.Units.UnitInfoResult;

namespace Resgrid.Web.Services.Controllers.Version3.Models.CommandApp
{
	public class CommandAppPayloadResult
	{
		public DepartmentRightsResult Rights { get; set; }
		public List<PersonnelInfoResult> Personnel { get; set; }
		public List<GroupInfoResult> Groups { get; set; }
		public List<UnitInfoResult> Units { get; set; }
		public List<RoleInfoResult> Roles { get; set; }
		public List<CustomStatusesResult> Statuses { get; set; }
		public List<CallResult> Calls { get; set; }
		public List<UnitStatusResult> UnitStatuses { get; set; }
		public List<UnitRoleResult> UnitRoles { get; set; }
	}
}
