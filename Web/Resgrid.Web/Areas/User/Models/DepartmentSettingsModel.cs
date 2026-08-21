using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
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

		/// <summary>
		/// New-call field policy, one row per configurable built-in field. Bound as a list so the admin
		/// screen can render a Visible/Required grid without a per-field property explosion.
		/// </summary>
		public List<NewCallFieldPolicyRow> NewCallFields { get; set; } = new List<NewCallFieldPolicyRow>();

		/// <summary>
		/// Time-in-status thresholds, one row per canonical status meaning. Entered in minutes because
		/// that is how dispatchers talk about them ("four minutes without reporting departed").
		/// </summary>
		public List<UnitStatusThresholdRow> UnitStatusThresholds { get; set; } = new List<UnitStatusThresholdRow>();

		[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
		public string MapCenterPointAddressAddress1 { get; set; }

		[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
		public string MapCenterPointAddressCity { get; set; }

		[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
		public string MapCenterPointAddressState { get; set; }

		[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
		public string MapCenterPointAddressPostalCode { get; set; }

		[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
		public string MapCenterPointAddressCountry { get; set; }
		public string MapCenterGpsCoordinatesLatitude { get; set; }
		public string MapCenterGpsCoordinatesLongitude { get; set; }
		public bool MapHideUnavailable { get; set; }
		public string ActiveCallRssKey { get; set; }
		public bool DisableAutoAvailable { get; set; }
		public string TtsLanguage { get; set; }
		public SelectList TtsLanguages { get; set; }

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

		public bool EnableModernNotifications { get; set; }
		public bool RequirePasswordResetViaEmail { get; set; }

		[Display(Name = "Require security PIN for dangerous chatbot/text actions")]
		public bool ForceChatbotSecurityPin { get; set; }

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
	/// <summary>One configurable built-in new-call field on the department settings screen.</summary>
	public class NewCallFieldPolicyRow
	{
		/// <summary>Stable key from Resgrid.Model.NewCallFieldKeys.</summary>
		public string Key { get; set; }

		public bool Visible { get; set; } = true;

		public bool Required { get; set; }
	}
	/// <summary>One time-in-status threshold row on the department settings screen.</summary>
	public class UnitStatusThresholdRow
	{
		/// <summary>The ActionBaseTypes value this row configures.</summary>
		public int BaseType { get; set; }

		/// <summary>Minutes before the unit is highlighted. Blank or 0 disables the warning.</summary>
		public int WarnMinutes { get; set; }

		/// <summary>Minutes before the unit is escalated to a high-priority alert. Blank or 0 disables it.</summary>
		public int AlertMinutes { get; set; }
	}


}
