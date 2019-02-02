using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Queue;
using Resgrid.Model.Helpers;

namespace Resgrid.Services
{
	public class NotificationService : INotificationService
	{
		private readonly IDepartmentNotificationRepository _departmentNotificationRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsService _unitsService;
		private readonly IUserStateService _userStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICalendarService _calendarService;
		private readonly IDocumentsService _documentsService;
		private readonly INotesService _notesService;
		private readonly IWorkLogsService _workLogsService;
		private readonly IShiftsService _shiftsService;
		private readonly ICustomStateService _customStateService;

		public NotificationService(IDepartmentNotificationRepository departmentNotificationRepository, IDepartmentsService departmentsService,
			IUnitsService unitsService, IUserStateService userStateService, IDepartmentGroupsService departmentGroupsService, IActionLogsService actionLogsService,
			IPersonnelRolesService personnelRolesService, IUserProfileService userProfileService, ICalendarService calendarService, IDocumentsService documentsService,
			INotesService notesService, IWorkLogsService workLogsService, IShiftsService shiftsService, ICustomStateService customStateService)
		{
			_departmentNotificationRepository = departmentNotificationRepository;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_actionLogsService = actionLogsService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_calendarService = calendarService;
			_documentsService = documentsService;
			_notesService = notesService;
			_workLogsService = workLogsService;
			_shiftsService = shiftsService;
			_customStateService = customStateService;
		}

		public List<DepartmentNotification> GetAll()
		{
			return _departmentNotificationRepository.GetAll().ToList();
		}

		public DepartmentNotification Save(DepartmentNotification notification)
		{
			_departmentNotificationRepository.SaveOrUpdate(notification);

			return notification;
		}

		public List<DepartmentNotification> GetNotificationsByDepartment(int departmentId)
		{
			return _departmentNotificationRepository.GetNotificationsByDepartment(departmentId);
		}

		public int GetDepartmentIdForType(NotificationItem ni)
		{
			switch((EventTypes)ni.Type)
			{
				case EventTypes.PersonnelStaffingChanged:
					var state = _userStateService.GetUserStateById(ni.StateId);

					if (state != null)
					{
						var department = _departmentsService.GetDepartmentByUserId(state.UserId, true);

						if (department != null)
							return department.DepartmentId;
						else
							return 0;
					}
					else
						return 0;
				case EventTypes.PersonnelStatusChanged:
					var status = _actionLogsService.GetActionlogById(ni.StateId);

					if (status != null)
					{
						var department = _departmentsService.GetDepartmentByUserId(status.UserId, true);

						if (department != null)
							return department.DepartmentId;
						else
							return 0;
					}
					else
						return 0;
				case EventTypes.CalendarEventAdded:
					var cal = _calendarService.GetCalendarItemById(ni.ItemId);

					if (cal != null)
						return cal.DepartmentId;
					else
						return 0;
				case EventTypes.CalendarEventUpcoming:
					var calUp = _calendarService.GetCalendarItemById(ni.ItemId);

					if (calUp != null)
						return calUp.DepartmentId;
					else
						return 0;
				case EventTypes.CalendarEventUpdated:
					var calUpdate = _calendarService.GetCalendarItemById(ni.ItemId);

					if (calUpdate != null)
						return calUpdate.DepartmentId;
					else
						return 0;
				case EventTypes.DocumentAdded:
					var docAdded = _documentsService.GetDocumentById(ni.ItemId);

					if (docAdded != null)
						return docAdded.DepartmentId;
					else
						return 0;
				case EventTypes.LogAdded:
					var logAdded = _workLogsService.GetWorkLogById(ni.ItemId);

					if (logAdded != null)
						return logAdded.DepartmentId;
					else
						return 0;
				case EventTypes.NoteAdded:
					var noteAdded = _notesService.GetNoteById(ni.ItemId);

					if (noteAdded != null)
						return noteAdded.DepartmentId;
					else
						return 0;
				case EventTypes.ShiftCreated:
					var shiftCreated = _shiftsService.GetShiftById(ni.ItemId);

					if (shiftCreated != null)
						return shiftCreated.DepartmentId;
					else
						return 0;
				case EventTypes.ShiftDaysAdded:
					var shiftDaysAdded = _shiftsService.GetShiftById(ni.ItemId);

					if (shiftDaysAdded != null)
						return shiftDaysAdded.DepartmentId;
					else
						return 0;
				case EventTypes.ShiftUpdated:
					var shiftUpdated = _shiftsService.GetShiftById(ni.ItemId);

					if (shiftUpdated != null)
						return shiftUpdated.DepartmentId;
					else
						return 0;
				case EventTypes.UnitAdded:
					var unitAdded = _unitsService.GetUnitById(ni.UnitId);

					if (unitAdded != null)
						return unitAdded.DepartmentId;
					else
						return 0;
				case EventTypes.UnitStatusChanged:
					var unitStatusChanged = _unitsService.GetUnitStateById(ni.StateId);

					if (unitStatusChanged != null)
					{
						var unit = _unitsService.GetUnitById(unitStatusChanged.UnitId);

						if (unit != null)
							return unit.DepartmentId;
						else
							return 0;
					}
					else
						return 0;
			}

			return 0;
		}

		public List<ProcessedNotification> ProcessNotifications(List<ProcessedNotification> notifications, List<DepartmentNotification> settings)
		{
			if (notifications == null || notifications.Count < 0)
				return null;

			//if (settings == null || settings.Count < 0)
			//	return null;

			var processNotifications = new List<ProcessedNotification>();

			foreach (var notification in notifications)
			{
				if (settings != null && settings.Any())
				{
					var typeSettings = settings.Where(x => x.EventType == (int)notification.Type);

					if (typeSettings != null && typeSettings.Any())
					{
						processNotifications.Add(notification);

						foreach (var setting in typeSettings)
						{
							if (ValidateNotificationForProcessing(notification, setting))
							{
								if (setting.Everyone) // Add Everyone
								{
									notification.Users =
										_departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true).Select(x => x.UserId).ToList();
								}
								else if (setting.DepartmentAdmins) // Add Only Department Admins
								{
									notification.Users =
										_departmentsService.GetAllAdminsForDepartment(notification.DepartmentId).Select(x => x.UserId).ToList();
								}
								else if (setting.LockToGroup && EventOptions.GroupEvents.Contains(notification.Type))
								// We are locked to the source group
								{
									var group = GetGroupForEvent(notification);
									if (group != null)
									{
										if (setting.SelectedGroupsAdminsOnly) // Only add source group admins
										{
											var usersInGroup = _departmentGroupsService.GetAllAdminsForGroup(group.DepartmentGroupId);

											if (usersInGroup != null)
											{
												foreach (var user in usersInGroup)
												{
													if (!notification.Users.Contains(user.UserId))
														notification.Users.Add(user.UserId);
												}
											}
										}
										else // Add source group users in selected roles
										{
											var usersInGroup = _departmentGroupsService.GetAllMembersForGroup(group.DepartmentGroupId)
												.Select(x => x.UserId);
											var roles = setting.RolesToNotify.Split(char.Parse(","));

											foreach (var roleId in roles)
											{
												var usersInRole = _personnelRolesService.GetAllMembersOfRole(int.Parse(roleId));

												if (usersInRole != null)
												{
													foreach (var user in usersInRole)
													{
														if (usersInGroup.Contains(user.UserId) && !notification.Users.Contains(user.UserId))
															notification.Users.Add(user.UserId);
													}
												}
											}
										}
									}
								}
								else // Run through users, Roles and groups
								{
									if (!String.IsNullOrWhiteSpace(setting.RolesToNotify))
									{
										var roles = setting.RolesToNotify.Split(char.Parse(","));
										foreach (var roleId in roles) // Add all users in Roles
										{
											var usersInRole = _personnelRolesService.GetAllMembersOfRole(int.Parse(roleId));
											foreach (var user in usersInRole)
											{
												if (!notification.Users.Contains(user.UserId))
													notification.Users.Add(user.UserId);
											}
										}
									}

									if (!String.IsNullOrWhiteSpace(setting.UsersToNotify))
									{
										var users = setting.UsersToNotify.Split(char.Parse(","));
										if (users != null)
										{
											foreach (var userId in users) // Add all Users
											{
												if (!notification.Users.Contains(userId))
													notification.Users.Add(userId);
											}
										}
									}

									if (!String.IsNullOrWhiteSpace(setting.GroupsToNotify))
									{
										var groups = setting.GroupsToNotify.Split(char.Parse(","));
										if (groups != null)
										{
											foreach (var groupId in groups) // Add all users in Groups
											{
												if (setting.SelectedGroupsAdminsOnly) // Only add group admins
												{
													var usersInGroup = _departmentGroupsService.GetAllAdminsForGroup(int.Parse(groupId));

													if (usersInGroup != null)
													{
														foreach (var user in usersInGroup)
														{
															if (!notification.Users.Contains(user.UserId))
																notification.Users.Add(user.UserId);
														}
													}
												}
												else
												{
													var usersInGroup = _departmentGroupsService.GetAllMembersForGroup(int.Parse(groupId));

													if (usersInGroup != null)
													{
														foreach (var user in usersInGroup)
														{
															if (!notification.Users.Contains(user.UserId))
																notification.Users.Add(user.UserId);
														}
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}

				if (notification.Type == EventTypes.CalendarEventAdded)
				{
					var calEvent = _calendarService.GetCalendarItemById(notification.ItemId);

					if (calEvent != null)
					{
						if (calEvent.ItemType == 0 && !String.IsNullOrWhiteSpace(calEvent.Entities)) // NONE: Notify based on entities
						{
							var items = calEvent.Entities.Split(char.Parse(","));

							if (items.Any(x => x.StartsWith("D:")))
							{
								notification.Users =
									_departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true).Select(x => x.UserId).ToList();
							}
							else
							{
								foreach (var val in items)
								{
									int groupId = 0;
									if (int.TryParse(val.Replace("G:", ""), out groupId))
									{
										var usersInGroup = _departmentGroupsService.GetAllMembersForGroup(groupId);

										if (usersInGroup != null)
										{
											foreach (var user in usersInGroup)
											{
												if (!notification.Users.Contains(user.UserId))
													notification.Users.Add(user.UserId);
											}
										}
									}
								}
							}
						}
						else if (calEvent.ItemType == 1) // ASSIGNED: Notify based on Required and Optional attendees
						{

						}
						else if (calEvent.ItemType == 1) // RSVP: Notify entire department
						{
							notification.Users =
								_departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true).Select(x => x.UserId).ToList();
						}
					}
				}
				else if (notification.Type == EventTypes.CalendarEventUpdated)
				{
					var calEvent = _calendarService.GetCalendarItemById(notification.ItemId);

					if (calEvent != null)
					{
						if (calEvent.ItemType == 0 && !String.IsNullOrWhiteSpace(calEvent.Entities)) // NONE: Notify based on entities
						{
							var items = calEvent.Entities.Split(char.Parse(","));

							if (items.Any(x => x.StartsWith("D:")))
							{
								notification.Users =
									_departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true).Select(x => x.UserId).ToList();
							}
							else
							{
								foreach (var val in items)
								{
									int groupId = 0;
									if (int.TryParse(val.Replace("G:", ""), out groupId))
									{
										var usersInGroup = _departmentGroupsService.GetAllMembersForGroup(groupId);

										if (usersInGroup != null)
										{
											foreach (var user in usersInGroup)
											{
												if (!notification.Users.Contains(user.UserId))
													notification.Users.Add(user.UserId);
											}
										}
									}
								}
							}
						}
						else if (calEvent.ItemType == 1) // ASSIGNED: Notify based on Required and Optional attendees
						{

						}
						else if (calEvent.ItemType == 1 && calEvent.Attendees != null && calEvent.Attendees.Any()) // RSVP: Notify people who've signed up
						{
							foreach (var attendee in calEvent.Attendees)
							{
								if (!notification.Users.Contains(attendee.UserId))
									notification.Users.Add(attendee.UserId);
							}
						}
					}
				}
			}

			return processNotifications;
		}

		public DepartmentGroup GetGroupForEvent(ProcessedNotification notification)
		{
			//NotificationItem dynamicData = (NotificationItem)JsonConvert.DeserializeObject(data);
			NotificationItem dynamicData = ObjectSerialization.Deserialize<NotificationItem>(notification.Data);

			if (notification.Type == EventTypes.UnitStatusChanged)
			{
				var unitEvent = _unitsService.GetUnitStateById(dynamicData.StateId);
				return _departmentGroupsService.GetGroupById(unitEvent.Unit.StationGroupId.GetValueOrDefault());
			}
			else if (notification.Type == EventTypes.PersonnelStatusChanged)
			{
				var userStaffing = _userStateService.GetUserStateById(dynamicData.StateId);
				var group = _departmentGroupsService.GetGroupForUser(userStaffing.UserId, notification.DepartmentId);
				return group;
			}
			else if (notification.Type == EventTypes.PersonnelStatusChanged)
			{
				var actionLog = _actionLogsService.GetActionlogById(dynamicData.StateId);
				var group = _departmentGroupsService.GetGroupForUser(actionLog.UserId, notification.DepartmentId);
				return group;
			}
			else if (notification.Type == EventTypes.UserAssignedToGroup)
			{
				return _departmentGroupsService.GetGroupById(dynamicData.GroupId);
			}

			return null;
		}

		public bool ValidateNotificationForProcessing(ProcessedNotification notification, DepartmentNotification setting)
		{
			//dynamic dynamicData = JsonConvert.DeserializeObject(notification.Data);
			NotificationItem dynamicData = ObjectSerialization.Deserialize<NotificationItem>(notification.Data);

			switch (notification.Type)
			{
				case EventTypes.UnitStatusChanged:
					if (!String.IsNullOrWhiteSpace(setting.BeforeData) && !String.IsNullOrWhiteSpace(setting.CurrentData))
					{
						if (setting.BeforeData.Contains("-1") && setting.CurrentData.Contains("-1"))
							return true;

						bool beforeAny = setting.BeforeData.Contains("-1");
						bool currentAny = setting.CurrentData.Contains("-1");

						UnitState beforeState = null;
						UnitState currentState = null;

						currentState = _unitsService.GetUnitStateById((int)dynamicData.StateId);

						if (!beforeAny)
							beforeState = _unitsService.GetLastUnitStateBeforeId(currentState.UnitId, currentState.UnitStateId);

						if ((currentAny || currentState.State == int.Parse(setting.CurrentData)) &&
							(beforeAny || beforeState.State == int.Parse(setting.BeforeData)))
							return true;
					}
					else
					{
						return false;
					}
					break;
				case EventTypes.PersonnelStaffingChanged:
					if (!String.IsNullOrWhiteSpace(setting.BeforeData) && !String.IsNullOrWhiteSpace(setting.CurrentData))
					{
						if (setting.BeforeData.Contains("-1") && setting.CurrentData.Contains("-1"))
							return true;

						bool beforeAny = setting.BeforeData.Contains("-1");
						bool currentAny = setting.CurrentData.Contains("-1");

						UserState beforeState = null;
						UserState currentState = null;

						currentState = _userStateService.GetUserStateById((int)dynamicData.StateId);

						if (!beforeAny)
							beforeState = _userStateService.GetPerviousUserState(currentState.UserId, currentState.UserStateId);

						if ((currentAny || currentState.State == int.Parse(setting.CurrentData)) &&
							(beforeAny || beforeState.State == int.Parse(setting.BeforeData)))
							return true;
					}
					else
					{
						return false;
					}
					break;
				case EventTypes.PersonnelStatusChanged:
					if (!String.IsNullOrWhiteSpace(setting.BeforeData) && !String.IsNullOrWhiteSpace(setting.CurrentData))
					{
						if (setting.BeforeData.Contains("-1") && setting.CurrentData.Contains("-1"))
							return true;

						bool beforeAny = setting.BeforeData.Contains("-1");
						bool currentAny = setting.CurrentData.Contains("-1");

						ActionLog beforeState = null;
						ActionLog currentState = null;

						currentState = _actionLogsService.GetActionlogById((int)dynamicData.StateId);

						if (!beforeAny)
							beforeState = _actionLogsService.GetPreviousActionLog(currentState.UserId, currentState.ActionLogId);

						if ((currentAny || currentState.ActionTypeId == int.Parse(setting.CurrentData)) &&
							(beforeAny || beforeState.ActionTypeId == int.Parse(setting.BeforeData)))
							return true;
					}
					else
					{
						return false;
					}
					break;
				case EventTypes.RolesInGroupAvailabilityAlert:
					if (!String.IsNullOrWhiteSpace(setting.CurrentData) && !String.IsNullOrWhiteSpace(setting.Data))
					{
						int count = 0;
						var userStateChanged = _userStateService.GetUserStateById((int)dynamicData.StateId);
						var usersInRole = _personnelRolesService.GetAllMembersOfRole(int.Parse(setting.Data));
						var group = _departmentGroupsService.GetGroupForUser(userStateChanged.UserId, notification.DepartmentId);

						if (group == null || group.Members == null || !group.Members.Any())
							return false;

						var acceptableStaffingLevels = setting.CurrentData.Split(char.Parse(","));

						if (usersInRole != null && !usersInRole.Any())
							return false;

						var staffingLevels = _userStateService.GetLatestStatesForDepartment(setting.DepartmentId);

						if (staffingLevels != null && staffingLevels.Any())
						{
							foreach (var user in usersInRole)
							{
								var currentState = staffingLevels.FirstOrDefault(x => x.UserId == user.UserId);

								if (currentState != null && acceptableStaffingLevels.Any(x => x == currentState.State.ToString()) &&
										group.Members.Any(x => x.UserId == user.UserId))
									count++;
							}

							if (count <= setting.LowerLimit)
							{
								notification.PersonnelRoleTargeted = int.Parse(setting.Data);
								return true;
							}
						}
						else
						{
							return false;
						}
					}
					else
					{
						return false;
					}
					break;
				case EventTypes.RolesInDepartmentAvailabilityAlert:
					if (!String.IsNullOrWhiteSpace(setting.CurrentData) && !String.IsNullOrWhiteSpace(setting.Data))
					{
						int count = 0;
						var usersInRole = _personnelRolesService.GetAllMembersOfRole(int.Parse(setting.Data));
						var acceptableStaffingLevels = setting.CurrentData.Split(char.Parse(","));

						if (usersInRole != null && !usersInRole.Any())
							return false;

						var staffingLevels = _userStateService.GetLatestStatesForDepartment(setting.DepartmentId);

						if (staffingLevels != null && staffingLevels.Any())
						{
							foreach (var user in usersInRole)
							{
								var currentState = staffingLevels.FirstOrDefault(x => x.UserId == user.UserId);

								if (currentState != null && acceptableStaffingLevels.Any(x => x == currentState.State.ToString()))
									count++;
							}

							if (count <= setting.LowerLimit)
							{
								notification.PersonnelRoleTargeted = int.Parse(setting.Data);
								return true;
							}
						}
						else
						{
							return false;
						}
					}
					else
					{
						return false;
					}
					break;
				case EventTypes.UnitTypesInGroupAvailabilityAlert:
					if (!String.IsNullOrWhiteSpace(setting.CurrentData) && !String.IsNullOrWhiteSpace(setting.Data))
					{
						int count = 0;
						var currentUnitState = _unitsService.GetUnitStateById((int)dynamicData.StateId);
						var unitsForType = _unitsService.GetAllUnitsForType(setting.DepartmentId, setting.Data);
						var unitForEvent = _unitsService.GetUnitById(currentUnitState.UnitId);

						if (unitForEvent?.StationGroupId == null)
							return false;

						var acceptableUnitStates = setting.CurrentData.Split(char.Parse(","));
						var unitsInGroup = _unitsService.GetAllUnitsForGroup(unitForEvent.StationGroupId.Value);

						if (unitsForType != null && !unitsForType.Any())
							return false;

						var staffingLevels = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(setting.DepartmentId);

						foreach (var unit in unitsForType)
						{
							var currentState = staffingLevels.FirstOrDefault(x => x.UnitId == unit.UnitId);

							if (currentState != null && acceptableUnitStates.Any(x => x == currentState.State.ToString()) && unitsInGroup.Any(x => x.UnitId == unit.UnitId))
								count++;
						}

						if (count <= setting.LowerLimit)
						{
							notification.UnitTypeTargeted = setting.Data;
							return true;
						}
					}
					else
					{
						return false;
					}
					break;
				case EventTypes.UnitTypesInDepartmentAvailabilityAlert:
					if (!String.IsNullOrWhiteSpace(setting.CurrentData) && !String.IsNullOrWhiteSpace(setting.Data))
					{
						int count = 0;
						var unitsForType = _unitsService.GetAllUnitsForType(setting.DepartmentId, setting.Data);
						var acceptableUnitStates = setting.CurrentData.Split(char.Parse(","));

						if (unitsForType != null && !unitsForType.Any())
							return false;

						var staffingLevels = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(setting.DepartmentId);

						foreach (var unit in unitsForType)
						{
							var currentState = staffingLevels.FirstOrDefault(x => x.UnitId == unit.UnitId);

							if (currentState != null && acceptableUnitStates.Any(x => x == currentState.State.ToString()))
								count++;
						}

						if (count <= setting.LowerLimit)
						{
							notification.UnitTypeTargeted = setting.Data;
							return true;
						}
					}
					else
					{
						return false;
					}
					break;
				default:
					return true;
			}

			return false;
		}

		public string GetMessageForType(ProcessedNotification notification)
		{
			try
			{
				NotificationItem data = ObjectSerialization.Deserialize<NotificationItem>(notification.Data);

				switch (notification.Type)
				{
					case EventTypes.UnitStatusChanged:
						var unitEvent = _unitsService.GetUnitStateById((int)data.StateId);
						var unitStatus = _customStateService.GetCustomUnitState(unitEvent);

						if (unitEvent != null && unitEvent.Unit != null)
							return String.Format("Unit {0} is now {1}", unitEvent.Unit.Name, unitStatus.ButtonText);
						else if (unitEvent != null)
							return String.Format("A Unit's status is now {0}", unitStatus.ButtonText);
						else
							return "A unit's status changed";
					case EventTypes.PersonnelStaffingChanged:
						var userStaffing = _userStateService.GetUserStateById((int)data.StateId);
						var userProfile = _userProfileService.GetProfileByUserId(userStaffing.UserId);
						var userStaffingText = _customStateService.GetCustomPersonnelStaffing(data.DepartmentId, userStaffing);

						return String.Format("{0} staffing is now {1}", userProfile.FullName.AsFirstNameLastName, userStaffingText.ButtonText);
					case EventTypes.PersonnelStatusChanged:
						var actionLog = _actionLogsService.GetActionlogById(data.StateId);

						UserProfile profile = null;
						if (actionLog != null)
							profile = _userProfileService.GetProfileByUserId(actionLog.UserId);
						else if (data.UserId != String.Empty)
							profile = _userProfileService.GetProfileByUserId(data.UserId);

						var userStatusText = _customStateService.GetCustomPersonnelStatus(data.DepartmentId, actionLog);

						if (profile != null && userStatusText != null)
							return String.Format("{0} status is now {1}", profile.FullName.AsFirstNameLastName, userStatusText.ButtonText);
						else if (profile != null)
							return String.Format("{0} status has changed", profile.FullName.AsFirstNameLastName);

						return String.Empty;
					case EventTypes.UserCreated:
						var newUserprofile = _userProfileService.GetProfileByUserId(data.UserId);

						if (newUserprofile != null)
							return String.Format("{0} has been added to your department", newUserprofile.FullName.AsFirstNameLastName);
						else
							return "A new user has been added to your department";
					case EventTypes.UserAssignedToGroup:

						UserProfile groupUserprofile = null;
						try
						{
							if (data.UserId != String.Empty)
								groupUserprofile = _userProfileService.GetProfileByUserId(data.UserId);
						}
						catch { }

						DepartmentGroup newGroup = null;
						try
						{
							if (data.GroupId != 0)
								newGroup = _departmentGroupsService.GetGroupById((int)data.GroupId, false);
						}
						catch { }

						if (groupUserprofile != null && newGroup != null)
							return String.Format("{0} has been assigned to group {1}", groupUserprofile.FullName.AsFirstNameLastName, newGroup.Name);
						else if (groupUserprofile != null && newGroup == null)
							return String.Format("{0} has been assigned to group", groupUserprofile.FullName.AsFirstNameLastName);
						else if (newGroup != null && groupUserprofile == null)
							return String.Format("A user has been assigned to group {0}", newGroup.Name);
						else
							return String.Format("A has been assigned to a group");
					case EventTypes.CalendarEventUpcoming:
						var calandarItem = _calendarService.GetCalendarItemById((int)data.ItemId);
						return String.Format("Event {0} is upcoming", calandarItem.Title);
					case EventTypes.DocumentAdded:
						var document = _documentsService.GetDocumentById((int)data.ItemId);
						return String.Format("Document {0} has been added", document.Name);
					case EventTypes.NoteAdded:
						var note = _notesService.GetNoteById((int)data.ItemId);

						if (note != null)
							return String.Format("Message {0} has been added", note.Title);

						break;
					case EventTypes.UnitAdded:
						var unit = _unitsService.GetUnitById((int)data.UnitId);
						return String.Format("Unit {0} has been added", unit.Name);
					case EventTypes.LogAdded:
						var log = _workLogsService.GetWorkLogById((int)data.ItemId);

						if (log != null)
						{
							var logUserProfile = _userProfileService.GetProfileByUserId(log.LoggedByUserId);
							return String.Format("{0} created log {1}", logUserProfile.FullName.AsFirstNameLastName, log.LogId);
						}
						else
						{
							return String.Format("A new log was created");
						}
					case EventTypes.DepartmentSettingsChanged:
						return String.Format("Settings have been updated for your department");
					case EventTypes.RolesInGroupAvailabilityAlert:

						var userStateChanged = _userStateService.GetUserStateById(int.Parse(notification.Value));
						var roleForGroup = _personnelRolesService.GetRoleById(notification.PersonnelRoleTargeted);
						var groupForRole = _departmentGroupsService.GetGroupForUser(userStateChanged.UserId, notification.DepartmentId);

						return String.Format("Availability for role {0} in group {1} is at or below the lower limit", roleForGroup.Name, groupForRole.Name);
					case EventTypes.RolesInDepartmentAvailabilityAlert:
						if (notification != null)
						{
							var roleForDep = _personnelRolesService.GetRoleById(notification.PersonnelRoleTargeted);

							if (roleForDep != null)
								return String.Format("Availability for role {0} for the department is at or below the lower limit", roleForDep.Name);
						}
						break;
					case EventTypes.UnitTypesInGroupAvailabilityAlert:
						if (data.UnitId != 0)
						{
							var unitForGroup = _unitsService.GetUnitById(data.UnitId);

							if (unitForGroup != null && unitForGroup.StationGroupId.HasValue)
							{
								var groupForUnit = _departmentGroupsService.GetGroupById(unitForGroup.StationGroupId.Value);

								return String.Format("Availability for unit type {0} in group {1} is at or below the lower limit",
									unitForGroup.Type, groupForUnit.Name);
							}
						}
						return String.Empty;
					case EventTypes.UnitTypesInDepartmentAvailabilityAlert:
						return String.Format("Availability for unit type {0} for the department is at or below the lower limit", notification.UnitTypeTargeted);
					case EventTypes.CalendarEventAdded:
						var calEvent = _calendarService.GetCalendarItemById(notification.ItemId);
						var department = _departmentsService.GetDepartmentById(calEvent.DepartmentId);

						if (calEvent != null)
						{
							if (calEvent.ItemType == 0)
								if (calEvent.IsAllDay)
									return $"New Calendar Event {calEvent.Title} on {calEvent.Start.TimeConverter(department).ToShortDateString()}";
								else
									return $"New Calendar Event {calEvent.Title} on {calEvent.Start.TimeConverter(department).ToShortDateString()} at {calEvent.Start.TimeConverter(department).ToShortTimeString()}";
							else
								if (calEvent.IsAllDay)
								return $"New Calendar RSVP Event {calEvent.Title} on {calEvent.Start.TimeConverter(department).ToShortDateString()}";
							else
								return $"New Calendar RSVP Event {calEvent.Title} on {calEvent.Start.TimeConverter(department).ToShortDateString()} at {calEvent.Start.TimeConverter(department).ToShortTimeString()}";
						}
						else
							return String.Empty;
					case EventTypes.CalendarEventUpdated:
						var calUpdatedEvent = _calendarService.GetCalendarItemById(notification.ItemId);
						var calUpdatedEventDepartment = _departmentsService.GetDepartmentById(calUpdatedEvent.DepartmentId);

						if (calUpdatedEvent != null)
							return $"Calendar Event {calUpdatedEvent.Title} on {calUpdatedEvent.Start.TimeConverter(calUpdatedEventDepartment).ToShortDateString()} has changed";
						else
							return String.Empty;
					default:
						throw new ArgumentOutOfRangeException("type");
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, extraMessage: notification.Data);
				return String.Empty;
			}

			return String.Empty;
		}

		public void DeleteDepartmentNotificationById(int notifiationId)
		{
			var notification =
				_departmentNotificationRepository.GetAll().FirstOrDefault(x => x.DepartmentNotificationId == notifiationId);

			if (notification != null)
			{
				_departmentNotificationRepository.DeleteOnSubmit(notification);
			}
		}
	}
}
