using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class DepartmentSettingsModel : BaseUserModel
	{
		public string Message { get; set; }
		public string UserId { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Dictionary<string, string> Users;
		public bool Use24HourTime { get; set; }
		public string MapZoomLevel { get; set; }
		public string RefreshTime { get; set; }

		public string MapCenterPointAddressAddress1 { get; set; }
		public string MapCenterPointAddressCity { get; set; }
		public string MapCenterPointAddressState { get; set; }
		public string MapCenterPointAddressPostalCode { get; set; }
		public string MapCenterPointAddressCountry { get; set; }
		public string MapCenterGpsCoordinatesLatitude { get; set; }
		public string MapCenterGpsCoordinatesLongitude { get; set; }
		public bool MapHideUnavailable { get; set; }
		public string ActiveCallRssKey { get; set; }
		public bool DisableAutoAvailable { get; set; }

		public bool EnableStaffingReset { get; set; }
		public string TimeToResetStaffing { get; set; }
		public SelectList StaffingLevels { get; set; }
		public List<CustomStateDetail> Staffings { get; set; }
		public int ResetStaffingTo { get; set; }
		public UserStateTypes UserStateTypes { get; set; }

		public bool EnableStaffingSupress { get; set; }
		public DepartmentSuppressStaffingInfo SuppressStaffingInfo { get; set; }

		public bool EnableStatusReset { get; set; }
		public string TimeToResetStatus { get; set; }
		public SelectList StatusLevels { get; set; }
		public int ResetStatusTo { get; set; }
		public ActionTypes UserStatusTypes { get; set; }

		public int PersonnelSort { get; set; }
		public SelectList PersonnelSortTypes { get; set; }

		public int UnitsSort { get; set; }
		public SelectList UnitSortTypes { get; set; }

		public int CallsSort { get; set; }
		public SelectList CallSortTypes { get; set; }

		public DepartmentSettingsModel()
		{
			Users = new Dictionary<string, string>();
		}

		public void SetUsers(List<IdentityUser> users, List<PersonName> names)
		{
			foreach (var u in users)
			{
				var name = names.FirstOrDefault(x => x.UserId == u.UserId);

				if (name != null)
					Users.Add(u.UserId, name.Name);
			}
		}
	}
}
