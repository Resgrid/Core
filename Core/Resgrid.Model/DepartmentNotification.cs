using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model.Events;

namespace Resgrid.Model
{
	[Table("DepartmentNotifications")]
	public class DepartmentNotification : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentNotificationId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int EventType { get; set; }

		public string UsersToNotify { get; set; }
		public string RolesToNotify { get; set; }
		public string GroupsToNotify { get; set; }
		public bool LockToGroup { get; set; }
		public bool SelectedGroupsAdminsOnly { get; set; }
		public bool DepartmentAdmins { get; set; }
		public bool Everyone { get; set; }
		public bool Disabled { get; set; }
		public string BeforeData { get; set; }
		public string CurrentData { get; set; }
		public int? UpperLimit { get; set; }
		public int? LowerLimit { get; set; }
		public string Data { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentNotificationId; }
			set { DepartmentNotificationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentNotifications";

		[NotMapped]
		public string IdName => "DepartmentNotificationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };

		public void AddUserToNotify(string userId)
		{
			if (!String.IsNullOrEmpty(UsersToNotify))
				UsersToNotify = string.Format("{0},{1}", UsersToNotify, userId);
			else
				UsersToNotify = userId.ToString();
		}

		public void AddRoleToNotify(int roleId)
		{
			if (!String.IsNullOrEmpty(RolesToNotify))
				RolesToNotify = string.Format("{0},{1}", RolesToNotify, roleId);
			else
				RolesToNotify = roleId.ToString();
		}

		public void AddGroupToNotify(int groupId)
		{
			if (!String.IsNullOrEmpty(GroupsToNotify))
				GroupsToNotify = string.Format("{0},{1}", GroupsToNotify, groupId);
			else
				GroupsToNotify = groupId.ToString();
		}

		/// <summary>
		/// The label of a status, staffing or unit status value a notification rule matches on: "Any" for no value, the
		/// department's own option when the value is one of its custom option ids of that kind, else the built-in value.
		/// <paramref name="label"/> localizes the fixed words and meaning names: (resource key, English fallback) to text.
		/// </summary>
		private static string TranslateStatusValue(string data, List<CustomState> customStates, CustomStateTypes type, Func<string, string, string> label)
		{
			var tokens = StatusMatch.Tokens(data);
			if (tokens.Count == 0 || tokens.Any(StatusMatch.IsAny))
				return Label(label, "StatusRuleAny", "Any");

			return string.Join(", ", tokens.Select(token => TranslateStatusToken(token, customStates, type, label)));
		}

		private static string Label(Func<string, string, string> label, string key, string fallback) =>
			label == null ? fallback : label(key, fallback) ?? fallback;

		private static string TranslateStatusToken(string token, List<CustomState> customStates, CustomStateTypes type, Func<string, string, string> label)
		{
			var baseType = StatusMatch.BaseTypeOf(token);
			if (baseType.HasValue)
			{
				var meaning = type == CustomStateTypes.Staffing
					? Label(label, "Staffing" + (StaffingBaseTypes)baseType.Value, ((StaffingBaseTypes)baseType.Value).GetDisplayString())
					: Label(label, ((ActionBaseTypes)baseType.Value).ToString(), ((ActionBaseTypes)baseType.Value).GetDisplayString());

				return string.Format(Label(label, "AnyStatusWithMeaning", "Any \"{0}\" status"), meaning);
			}

			if (!int.TryParse(token, out var id))
				return Label(label, "StatusRuleUnknown", "Unknown");

			var custom = StatusOptionIds.FindCustom(customStates, type, id);
			if (custom != null)
				return custom.ButtonText;

			if (id > CallStatusLinkage.MaxBuiltInStatusId)
				return Label(label, "StatusRuleUnknown", "Unknown");

			switch (type)
			{
				case CustomStateTypes.Unit:
					return ((UnitStateTypes)id).GetDisplayString();
				case CustomStateTypes.Staffing:
					return ((UserStateTypes)id).GetDisplayString();
				default:
					return ((ActionTypes)id).GetDisplayString();
			}
		}

		public string TranslateBefore(List<CustomState> customStates = null, Func<string, string, string> label = null)
		{
			switch ((EventTypes)EventType)
			{
				case EventTypes.UnitStatusChanged:
					return TranslateStatusValue(BeforeData, customStates, CustomStateTypes.Unit, label);
				case EventTypes.PersonnelStaffingChanged:
					return TranslateStatusValue(BeforeData, customStates, CustomStateTypes.Staffing, label);
				case EventTypes.PersonnelStatusChanged:
					return TranslateStatusValue(BeforeData, customStates, CustomStateTypes.Personnel, label);
				case EventTypes.UserCreated:
					break;
				case EventTypes.UserAssignedToGroup:
					break;
				case EventTypes.CalendarEventUpcoming:
					break;
				case EventTypes.DocumentAdded:
					break;
				case EventTypes.NoteAdded:
					break;
				case EventTypes.UnitAdded:
					break;
				case EventTypes.LogAdded:
					break;
				case EventTypes.DepartmentSettingsChanged:
					break;
				case EventTypes.RolesInGroupAvailabilityAlert:
					break;
				case EventTypes.UnitTypesInGroupAvailabilityAlert:
					break;
				case EventTypes.RolesInDepartmentAvailabilityAlert:
					break;
				case EventTypes.UnitTypesInDepartmentAvailabilityAlert:
					break;
			}

			return Label(label, "StatusRuleNone", "None");
		}

		public string TranslateCurrent(List<CustomState> customStates = null, Func<string, string, string> label = null)
		{
			switch ((EventTypes)EventType)
			{
				case EventTypes.UnitStatusChanged:
					return TranslateStatusValue(CurrentData, customStates, CustomStateTypes.Unit, label);
				case EventTypes.PersonnelStaffingChanged:
					return TranslateStatusValue(CurrentData, customStates, CustomStateTypes.Staffing, label);
				case EventTypes.PersonnelStatusChanged:
					return TranslateStatusValue(CurrentData, customStates, CustomStateTypes.Personnel, label);
				case EventTypes.UserCreated:
					break;
				case EventTypes.UserAssignedToGroup:
					break;
				case EventTypes.CalendarEventUpcoming:
					break;
				case EventTypes.DocumentAdded:
					break;
				case EventTypes.NoteAdded:
					break;
				case EventTypes.UnitAdded:
					break;
				case EventTypes.LogAdded:
					break;
				case EventTypes.DepartmentSettingsChanged:
					break;
				case EventTypes.RolesInGroupAvailabilityAlert:
					break;
				case EventTypes.UnitTypesInGroupAvailabilityAlert:
					break;
				case EventTypes.RolesInDepartmentAvailabilityAlert:
					break;
				case EventTypes.UnitTypesInDepartmentAvailabilityAlert:
					break;
			}

			return Label(label, "StatusRuleNone", "None");
		}
	}
}
