using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Controllers.Version3.Models.CoreData;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using Resgrid.Web.Services.Controllers.Version3.Models.Roles;
using Resgrid.Web.Services.Controllers.Version3.Models.Security;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;
using CallPriorityResult = Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities.CallPriorityResult;

namespace Resgrid.Web.Services.Controllers.Version3.Models
{
	public class CoreDataResult
	{
		public DepartmentRightsResult Rights { get; set; }
		public List<PersonnelInfoResult> Personnel { get; set; }
		public List<GroupInfoResult> Groups { get; set; }
		public List<UnitInfoResult> Units { get; set; }
		public List<RoleInfoResult> Roles { get; set; }
		public List<CustomStatusesResult> Statuses { get; set; }
		public List<CallPriorityResult> Priorities { get; set; }
		public List<JoinedDepartmentResult> Departments { get; set; }
		public List<PersonnelListStatusOrder> PersonnelListStatusOrders { get; set; }
		public int PersonnelNameSorting {get;set;}
	}
}
