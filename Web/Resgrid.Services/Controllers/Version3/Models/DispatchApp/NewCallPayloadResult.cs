using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;
using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Controllers.Version3.Models.CoreData;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Controllers.Version3.Models.Roles;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.DispatchApp
{
	public class NewCallPayloadResult
	{
		public List<PersonnelInfoResult> Personnel { get; set; }
		public List<GroupInfoResult> Groups { get; set; }
		public List<UnitInfoResult> Units { get; set; }
		public List<RoleInfoResult> Roles { get; set; }
		public List<CustomStatusesResult> Statuses { get; set; }
		public List<UnitStatusCoreResult> UnitStatuses { get; set; }
		public List<UnitRoleResult> UnitRoles { get; set; }
		public List<CallPriorityResult> Priorities { get; set; }
		public List<CallTypeResult> CallTypes { get; set; }
	}
}
