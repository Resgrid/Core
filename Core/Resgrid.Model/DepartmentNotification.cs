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

		public string TranslateBefore(List<CustomState> customStates = null)
		{
			switch ((EventTypes)EventType)
			{
				case EventTypes.UnitStatusChanged:
					if (BeforeData == "-1")
						return "Any";
					else
					{
						int id = int.Parse(BeforeData);
						if (id <= 25)
							return ((UnitStateTypes) id).GetDisplayString();
						else
						{
							if (customStates != null)
							{
								var data = customStates.SelectMany(x => x.Details);
								var result = data.FirstOrDefault(x => x.CustomStateDetailId == id);

								if (result != null)
									return result.ButtonText;
								else
									return "Unknown";
							}
							else
							{
								return "Unknown";
							}
						}
					}
					break;
				case EventTypes.PersonnelStaffingChanged:
					if (BeforeData == "-1")
						return "Any";
					else
					{
						int id = int.Parse(BeforeData);
						if (id <= 25)
							return ((UserStateTypes) id).GetDisplayString();
						else
						{
							if (customStates != null)
							{
								var data = customStates.SelectMany(x => x.Details);
								var result = data.FirstOrDefault(x => x.CustomStateDetailId == id);

								if (result != null)
									return result.ButtonText;
								else
									return "Unknown";
							}
							else
							{
								return "Unknown";
							}
						}
					}
					break;
				case EventTypes.PersonnelStatusChanged:
					if (BeforeData == "-1")
						return "Any";
					else
					{
						int id = int.Parse(BeforeData);
						if (id <= 25)
							return ((ActionTypes) int.Parse(BeforeData)).GetDisplayString();
						else
						{
							if (customStates != null)
							{
								var data = customStates.SelectMany(x => x.Details);
								var result = data.FirstOrDefault(x => x.CustomStateDetailId == id);

								if (result != null)
									return result.ButtonText;
								else
									return "Unknown";
							}
							else
							{
								return "Unknown";
							}
						}
					}
					break;
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

			return "None";
		}

		public string TranslateCurrent(List<CustomState> customStates = null)
		{
			switch ((EventTypes)EventType)
			{
				case EventTypes.UnitStatusChanged:
					if (CurrentData == "-1")
						return "Any";
					else
					{
						int id = int.Parse(CurrentData);
						if (id <= 25)
							return ((UnitStateTypes) id).GetDisplayString();
						else
						{
							if (customStates != null)
							{
								var data = customStates.SelectMany(x => x.Details);
								var result = data.FirstOrDefault(x => x.CustomStateDetailId == id);

								if (result != null)
									return result.ButtonText;
								else
									return "Unknown";
							}
							else
							{
								return "Unknown";
							}
						}
					}
					break;
				case EventTypes.PersonnelStaffingChanged:
					if (CurrentData == "-1")
						return "Any";
					else
					{
						int id = int.Parse(CurrentData);
						if (id <= 25)
							return ((UserStateTypes) int.Parse(CurrentData)).GetDisplayString();
						else
						{
							if (customStates != null)
							{
								var data = customStates.SelectMany(x => x.Details);
								var result = data.FirstOrDefault(x => x.CustomStateDetailId == id);

								if (result != null)
									return result.ButtonText;
								else
									return "Unknown";
							}
							else
							{
								return "Unknown";
							}
						}
					}
					break;
				case EventTypes.PersonnelStatusChanged:
					if (CurrentData == "-1")
						return "Any";
					else
					{
						int id = int.Parse(CurrentData);
						if (id <= 25)
							return ((ActionTypes) int.Parse(CurrentData)).GetDisplayString();
						else
						{
							if (customStates != null)
							{
								var data = customStates.SelectMany(x => x.Details);
								var result = data.FirstOrDefault(x => x.CustomStateDetailId == id);

								if (result != null)
									return result.ButtonText;
								else
									return "Unknown";
							}
							else
							{
								return "Unknown";
							}
						}
					}
					break;
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

			return "None";
		}
	}
}
