using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Controllers.Version3.Models.CoreData;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v3.Dispatch
{
	public class GetSetUnitStateDataResult
	{
		public int UnitId { get; set; }
		public string UnitName { get; set; }
		public List<GroupInfoResult> Stations { get; set; }
		public List<CallResult> Calls { get; set; }
		public List<CustomStatusesResult> Statuses { get; set; }
	}
}
