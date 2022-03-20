using Resgrid.Web.Services.Models.v4.CallPriorities;
using Resgrid.Web.Services.Models.v4.CallTypes;
using Resgrid.Web.Services.Models.v4.CustomStatuses;
using Resgrid.Web.Services.Models.v4.Groups;
using Resgrid.Web.Services.Models.v4.Personnel;
using Resgrid.Web.Services.Models.v4.Roles;
using Resgrid.Web.Services.Models.v4.UnitRoles;
using Resgrid.Web.Services.Models.v4.Units;
using Resgrid.Web.Services.Models.v4.UnitStatus;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// Result containing all the data required to populate the New Call form
	/// </summary>
	public class NewCallFormResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public NewCallResultData Data { get; set; }
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class NewCallResultData
	{
		public List<PersonnelInfoResultData> Personnel { get; set; }	
		public List<GroupResultData> Groups { get; set; }
		public List<UnitResultData> Units { get; set; }
		public List<RoleResultData> Roles { get; set; }
		public List<CustomStatusResultData> Statuses { get; set; }
		public List<UnitStatusResultData> UnitStatuses { get; set; }
		public List<UnitRoleResultData> UnitRoles { get; set; }
		public List<CallPriorityResultData> Priorities { get; set; }
		public List<CallTypeResultData> CallTypes { get; set; }
	}
}
