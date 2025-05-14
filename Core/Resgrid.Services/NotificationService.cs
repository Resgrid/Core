using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

		public async Task<List<DepartmentNotification>> GetAllAsync()
		{
			var items = await _departmentNotificationRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentNotification>();
		}

		public async Task<DepartmentNotification> SaveAsync(DepartmentNotification notification, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _departmentNotificationRepository.SaveOrUpdateAsync(notification, cancellationToken);
		}

		public async Task<List<DepartmentNotification>> GetNotificationsByDepartmentAsync(int departmentId)
		{
			var items = await _departmentNotificationRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentNotification>();
		}

		public async Task<int> GetDepartmentIdForTypeAsync(NotificationItem ni)
		{
			switch ((EventTypes)ni.Type)
			{
				case EventTypes.PersonnelStaffingChanged:
					var state = await _userStateService.GetUserStateByIdAsync(ni.StateId);

					if (state != null)
					{
						var department = await _departmentsService.GetDepartmentByUserIdAsync(state.UserId, true);

						if (department != null)
							return department.DepartmentId;
						else
							return 0;
					}
					else
						return 0;
				case EventTypes.PersonnelStatusChanged:
					var status = await _actionLogsService.GetActionLogByIdAsync(ni.StateId);

					if (status != null)
					{
						var department = await _departmentsService.GetDepartmentByUserIdAsync(status.UserId, true);

						if (department != null)
							return department.DepartmentId;
						else
							return 0;
					}
					else
						return 0;
				case EventTypes.CalendarEventAdded:
					var cal = await _calendarService.GetCalendarItemByIdAsync(ni.ItemId);

					if (cal != null)
						return cal.DepartmentId;
					else
						return 0;
				case EventTypes.CalendarEventUpcoming:
					var calUp = await _calendarService.GetCalendarItemByIdAsync(ni.ItemId);

					if (calUp != null)
						return calUp.DepartmentId;
					else
						return 0;
				case EventTypes.CalendarEventUpdated:
					var calUpdate = await _calendarService.GetCalendarItemByIdAsync(ni.ItemId);

					if (calUpdate != null)
						return calUpdate.DepartmentId;
					else
						return 0;
				case EventTypes.DocumentAdded:
					var docAdded = await _documentsService.GetDocumentByIdAsync(ni.ItemId);

					if (docAdded != null)
						return docAdded.DepartmentId;
					else
						return 0;
				case EventTypes.LogAdded:
					var logAdded = await _workLogsService.GetWorkLogByIdAsync(ni.ItemId);

					if (logAdded != null)
						return logAdded.DepartmentId;
					else
						return 0;
				case EventTypes.NoteAdded:
					var noteAdded = await _notesService.GetNoteByIdAsync(ni.ItemId);

					if (noteAdded != null)
						return noteAdded.DepartmentId;
					else
						return 0;
				case EventTypes.ShiftCreated:
					var shiftCreated = await _shiftsService.GetShiftByIdAsync(ni.ItemId);

					if (shiftCreated != null)
						return shiftCreated.DepartmentId;
					else
						return 0;
				case EventTypes.ShiftDaysAdded:
					var shiftDaysAdded = await _shiftsService.GetShiftByIdAsync(ni.ItemId);

					if (shiftDaysAdded != null)
						return shiftDaysAdded.DepartmentId;
					else
						return 0;
				case EventTypes.ShiftUpdated:
					var shiftUpdated = await _shiftsService.GetShiftByIdAsync(ni.ItemId);

					if (shiftUpdated != null)
						return shiftUpdated.DepartmentId;
					else
						return 0;
				case EventTypes.UnitAdded:
					var unitAdded = await _unitsService.GetUnitByIdAsync(ni.UnitId);

					if (unitAdded != null)
						return unitAdded.DepartmentId;
					else
						return 0;
				case EventTypes.UnitStatusChanged:
					var unitStatusChanged = await _unitsService.GetUnitStateByIdAsync(ni.StateId);

					if (unitStatusChanged != null)
					{
						var unit = await _unitsService.GetUnitByIdAsync(unitStatusChanged.UnitId);

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

		public async Task<List<ProcessedNotification>> ProcessNotificationsAsync(List<ProcessedNotification> notifications, List<DepartmentNotification> settings)
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
							if (await ValidateNotificationForProcessingAsync(notification, setting))
							{
								if (setting.Everyone) // Add Everyone
								{
									notification.Users =
										(await _departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true)).Select(x => x.UserId).ToList();
								}
								else if (setting.DepartmentAdmins) // Add Only Department Admins
								{
									notification.Users =
										(await _departmentsService.GetAllAdminsForDepartmentAsync(notification.DepartmentId)).Select(x => x.UserId).ToList();
								}
								else if (setting.LockToGroup && EventOptions.GroupEvents.Contains(notification.Type))
								// We are locked to the source group
								{
									var group = await GetGroupForEventAsync(notification);
									if (group != null)
									{
										if (setting.SelectedGroupsAdminsOnly) // Only add source group admins
										{
											var usersInGroup = await _departmentGroupsService.GetAllAdminsForGroupAsync(group.DepartmentGroupId);

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
											var usersInGroup = (await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId))
												.Select(x => x.UserId);
											var roles = setting.RolesToNotify.Split(char.Parse(","));

											foreach (var roleId in roles)
											{
												var usersInRole = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(roleId));

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
											var usersInRole = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(roleId));
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
													var usersInGroup = await _departmentGroupsService.GetAllAdminsForGroupAsync(int.Parse(groupId));

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
													var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(int.Parse(groupId));

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
					var calEvent = await _calendarService.GetCalendarItemByIdAsync(notification.ItemId);

					if (calEvent != null)
					{
						if (calEvent.ItemType == 0 && !String.IsNullOrWhiteSpace(calEvent.Entities)) // NONE: Notify based on entities
						{
							var items = calEvent.Entities.Split(char.Parse(","));

							if (items.Any(x => x.StartsWith("D:")))
							{
								notification.Users =
									(await _departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true)).Select(x => x.UserId).ToList();
							}
							else
							{
								foreach (var val in items)
								{
									int groupId = 0;
									if (int.TryParse(val.Replace("G:", ""), out groupId))
									{
										var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);

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
								(await _departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true)).Select(x => x.UserId).ToList();
						}
					}
				}
				else if (notification.Type == EventTypes.CalendarEventUpdated)
				{
					var calEvent = await _calendarService.GetCalendarItemByIdAsync(notification.ItemId);

					if (calEvent != null)
					{
						if (calEvent.ItemType == 0 && !String.IsNullOrWhiteSpace(calEvent.Entities)) // NONE: Notify based on entities
						{
							var items = calEvent.Entities.Split(char.Parse(","));

							if (items.Any(x => x.StartsWith("D:")))
							{
								notification.Users =
									(await _departmentsService.GetAllUsersForDepartment(notification.DepartmentId, true)).Select(x => x.UserId).ToList();
							}
							else
							{
								foreach (var val in items)
								{
									int groupId = 0;
									if (int.TryParse(val.Replace("G:", ""), out groupId))
									{
										var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);

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

		public async Task<DepartmentGroup> GetGroupForEventAsync(ProcessedNotification notification)
		{
			//NotificationItem dynamicData = (NotificationItem)JsonConvert.DeserializeObject(data);
			NotificationItem dynamicData = ObjectSerialization.Deserialize<NotificationItem>(notification.Data);

			if (dynamicData != null)
			{
				if (notification.Type == EventTypes.UnitStatusChanged)
				{
					var unitEvent = await _unitsService.GetUnitStateByIdAsync(dynamicData.StateId);

					if (unitEvent != null)
					{
						return await _departmentGroupsService.GetGroupByIdAsync(unitEvent.Unit.StationGroupId.GetValueOrDefault());
					}
				}
				else if (notification.Type == EventTypes.PersonnelStatusChanged)
				{
					var userStaffing = await _userStateService.GetUserStateByIdAsync(dynamicData.StateId);

					if (userStaffing != null)
					{
						var group = await _departmentGroupsService.GetGroupForUserAsync(userStaffing.UserId, notification.DepartmentId);
						return group;
					}
				}
				else if (notification.Type == EventTypes.PersonnelStatusChanged)
				{
					var actionLog = await _actionLogsService.GetActionLogByIdAsync(dynamicData.StateId);

					if (actionLog != null)
					{
						var group = await _departmentGroupsService.GetGroupForUserAsync(actionLog.UserId, notification.DepartmentId);
						return group;
					}
				}
				else if (notification.Type == EventTypes.UserAssignedToGroup)
				{
					return await _departmentGroupsService.GetGroupByIdAsync(dynamicData.GroupId);
				}
			}

			return null;
		}

		public async Task<bool> ValidateNotificationForProcessingAsync(ProcessedNotification notification, DepartmentNotification setting)
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

						currentState = await _unitsService.GetUnitStateByIdAsync(dynamicData.StateId);

						if (currentState != null)
						{
							if (!beforeAny)
								beforeState = await _unitsService.GetLastUnitStateBeforeIdAsync(currentState.UnitId, currentState.UnitStateId);

							if ((currentAny || currentState.State == int.Parse(setting.CurrentData)) &&
								(beforeAny || beforeState.State == int.Parse(setting.BeforeData)))
								return true;
						}
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

						bool beforeAny = false;
						if (!string.IsNullOrWhiteSpace(setting.BeforeData))
							beforeAny = setting.BeforeData.Contains("-1");

						bool currentAny = false;
						if (!string.IsNullOrWhiteSpace(setting.CurrentData))
							currentAny = setting.CurrentData.Contains("-1");

						UserState beforeState = null;
						UserState currentState = null;

						currentState = await _userStateService.GetUserStateByIdAsync((int)dynamicData.StateId);

						if (currentState != null)
						{
							if (!beforeAny)
								beforeState = await _userStateService.GetPreviousUserStateAsync(currentState.UserId, currentState.UserStateId);

							if ((currentAny || currentState.State == int.Parse(setting.CurrentData)) &&
								(beforeAny || beforeState.State == int.Parse(setting.BeforeData)))
								return true;
						}

						return false;
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

						currentState = await _actionLogsService.GetActionLogByIdAsync((int)dynamicData.StateId);

						if (!beforeAny)
							beforeState = await _actionLogsService.GetPreviousActionLogAsync(currentState.UserId, currentState.ActionLogId);

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
						var userStateChanged = await _userStateService.GetUserStateByIdAsync((int)dynamicData.StateId);
						var usersInRole = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(setting.Data));
						var group = await _departmentGroupsService.GetGroupForUserAsync(userStateChanged.UserId, notification.DepartmentId);

						if (group == null || group.Members == null || !group.Members.Any())
							return false;

						var acceptableStaffingLevels = setting.CurrentData.Split(char.Parse(","));

						if (usersInRole != null && !usersInRole.Any())
							return false;

						var staffingLevels = await _userStateService.GetLatestStatesForDepartmentAsync(setting.DepartmentId);

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
						var usersInRole = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(setting.Data));
						var acceptableStaffingLevels = setting.CurrentData.Split(char.Parse(","));

						if (usersInRole != null && !usersInRole.Any())
							return false;

						var staffingLevels = await _userStateService.GetLatestStatesForDepartmentAsync(setting.DepartmentId);

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
						var currentUnitState = await _unitsService.GetUnitStateByIdAsync((int)dynamicData.StateId);
						var unitsForType = await _unitsService.GetAllUnitsForTypeAsync(setting.DepartmentId, setting.Data);
						var unitForEvent = await _unitsService.GetUnitByIdAsync(currentUnitState.UnitId);

						if (unitForEvent?.StationGroupId == null)
							return false;

						var acceptableUnitStates = setting.CurrentData.Split(char.Parse(","));
						var unitsInGroup = await _unitsService.GetAllUnitsForGroupAsync(unitForEvent.StationGroupId.Value);

						if (unitsForType != null && !unitsForType.Any())
							return false;

						var staffingLevels = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(setting.DepartmentId);

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
						var unitsForType = await _unitsService.GetAllUnitsForTypeAsync(setting.DepartmentId, setting.Data);
						var acceptableUnitStates = setting.CurrentData.Split(char.Parse(","));

						if (unitsForType != null && !unitsForType.Any())
							return false;

						var staffingLevels = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(setting.DepartmentId);

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

		public bool AllowToSendViaSms(EventTypes type)
		{
			switch (type)
			{
				case EventTypes.UnitStatusChanged:
					return false;
				case EventTypes.PersonnelStaffingChanged:
					return false;
				case EventTypes.PersonnelStatusChanged:
					return false;
				case EventTypes.UserCreated:
					return false;
				case EventTypes.UserAssignedToGroup:
					return false;
				case EventTypes.DocumentAdded:
					return false;
				case EventTypes.NoteAdded:
					return false;
				case EventTypes.UnitAdded:
					return false;
				case EventTypes.LogAdded:
					return false;
				case EventTypes.DepartmentSettingsChanged:
					return false;
				default:
					return true;
			}
		}

		public async Task<string> GetMessageForTypeAsync(ProcessedNotification notification)
		{
			try
			{
				NotificationItem data = ObjectSerialization.Deserialize<NotificationItem>(notification.Data);

				switch (notification.Type)
				{
					case EventTypes.UnitStatusChanged:
						var unitEvent = await _unitsService.GetUnitStateByIdAsync((int)data.StateId);
						var unitStatus = await _customStateService.GetCustomUnitStateAsync(unitEvent);

						if (unitEvent != null && unitEvent.Unit != null && unitStatus != null)
							return String.Format("Unit {0} is now {1}", unitEvent.Unit.Name, unitStatus.ButtonText);
						else if (unitEvent != null && unitStatus != null)
							return String.Format("A Unit's status is now {0}", unitStatus.ButtonText);
						else if (unitEvent != null)
							return String.Format("{0} status has changed", unitEvent.Unit.Name);
						else
							return String.Empty;
					case EventTypes.PersonnelStaffingChanged:
						var userStaffing = await _userStateService.GetUserStateByIdAsync((int)data.StateId);

						if (userStaffing != null)
						{
							var userProfile = await _userProfileService.GetProfileByUserIdAsync(userStaffing.UserId);
							var userStaffingText = await _customStateService.GetCustomPersonnelStaffingAsync(data.DepartmentId, userStaffing);

							if (userProfile != null && userStaffingText != null)
								return String.Format("{0} staffing is now {1}", userProfile.FullName.AsFirstNameLastName, userStaffingText.ButtonText);
							else
								return String.Empty;
						}
						else
							return String.Empty;
					case EventTypes.PersonnelStatusChanged:
						var actionLog = await _actionLogsService.GetActionLogByIdAsync(data.StateId);

						UserProfile profile = null;
						if (actionLog != null)
							profile = await _userProfileService.GetProfileByUserIdAsync(actionLog.UserId);
						else if (data.UserId != String.Empty)
							profile = await _userProfileService.GetProfileByUserIdAsync(data.UserId);

						var userStatusText = await _customStateService.GetCustomPersonnelStatusAsync(data.DepartmentId, actionLog);

						if (profile != null && userStatusText != null)
							return String.Format("{0} status is now {1}", profile.FullName.AsFirstNameLastName, userStatusText.ButtonText);
						else if (profile != null)
							return String.Format("{0} status has changed", profile.FullName.AsFirstNameLastName);

						return String.Empty;
					case EventTypes.UserCreated:
						var newUserprofile = await _userProfileService.GetProfileByUserIdAsync(data.UserId);

						if (newUserprofile != null)
							return String.Format("{0} has been added to your department", newUserprofile.FullName.AsFirstNameLastName);
						else
							return "A new user has been added to your department";
					case EventTypes.UserAssignedToGroup:

						UserProfile groupUserprofile = null;
						try
						{
							if (data.UserId != String.Empty)
								groupUserprofile = await _userProfileService.GetProfileByUserIdAsync(data.UserId);
						}
						catch { }

						DepartmentGroup newGroup = null;
						try
						{
							if (data.GroupId != 0)
								newGroup = await _departmentGroupsService.GetGroupByIdAsync((int)data.GroupId, false);
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
						var calandarItem = await _calendarService.GetCalendarItemByIdAsync((int)data.ItemId);
						return String.Format("Event {0} is upcoming", calandarItem.Title);
					case EventTypes.DocumentAdded:
						var document = await _documentsService.GetDocumentByIdAsync((int)data.ItemId);
						return String.Format("Document {0} has been added", document.Name);
					case EventTypes.NoteAdded:
						var note = await _notesService.GetNoteByIdAsync((int)data.ItemId);

						if (note != null)
							return String.Format("Message {0} has been added", note.Title);

						break;
					case EventTypes.UnitAdded:
						var unit = await _unitsService.GetUnitByIdAsync((int)data.UnitId);
						return String.Format("Unit {0} has been added", unit.Name);
					case EventTypes.LogAdded:
						var log = await _workLogsService.GetWorkLogByIdAsync((int)data.ItemId);

						if (log != null)
						{
							var logUserProfile = await _userProfileService.GetProfileByUserIdAsync(log.LoggedByUserId);
							return String.Format("{0} created log {1}", logUserProfile.FullName.AsFirstNameLastName, log.LogId);
						}
						else
						{
							return String.Format("A new log was created");
						}
					case EventTypes.DepartmentSettingsChanged:
						return String.Format("Settings have been updated for your department");
					case EventTypes.RolesInGroupAvailabilityAlert:

						var userStateChanged = await _userStateService.GetUserStateByIdAsync(int.Parse(notification.Value));
						var roleForGroup = await _personnelRolesService.GetRoleByIdAsync(notification.PersonnelRoleTargeted);
						var groupForRole = await _departmentGroupsService.GetGroupForUserAsync(userStateChanged.UserId, notification.DepartmentId);
						// TODO: Check this
						if (roleForGroup != null && groupForRole != null)
							return String.Format("Availability for role {0} in group {1} is at or below the lower limit", roleForGroup.Name, groupForRole.Name);

						return "Availability for a role is at or below the lower limit";
					case EventTypes.RolesInDepartmentAvailabilityAlert:
						if (notification != null)
						{
							var roleForDep = await _personnelRolesService.GetRoleByIdAsync(notification.PersonnelRoleTargeted);

							if (roleForDep != null)
								return String.Format("Availability for role {0} for the department is at or below the lower limit", roleForDep.Name);
						}
						break;
					case EventTypes.UnitTypesInGroupAvailabilityAlert:
						if (data.UnitId != 0)
						{
							var unitForGroup = await _unitsService.GetUnitByIdAsync(data.UnitId);

							if (unitForGroup != null && unitForGroup.StationGroupId.HasValue)
							{
								var groupForUnit = await _departmentGroupsService.GetGroupByIdAsync(unitForGroup.StationGroupId.Value);

								return String.Format("Availability for unit type {0} in group {1} is at or below the lower limit",
									unitForGroup.Type, groupForUnit.Name);
							}
						}
						return String.Empty;
					case EventTypes.UnitTypesInDepartmentAvailabilityAlert:
						return String.Format("Availability for unit type {0} for the department is at or below the lower limit", notification.UnitTypeTargeted);
					case EventTypes.CalendarEventAdded:
						var calEvent = await _calendarService.GetCalendarItemByIdAsync(notification.ItemId);
						var department = await _departmentsService.GetDepartmentByIdAsync(calEvent.DepartmentId);

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
						var calUpdatedEvent = await _calendarService.GetCalendarItemByIdAsync(notification.ItemId);
						var calUpdatedEventDepartment = await _departmentsService.GetDepartmentByIdAsync(calUpdatedEvent.DepartmentId);

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

		public async Task<bool> DeleteDepartmentNotificationByIdAsync(int notificationId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var notification = await
				_departmentNotificationRepository.GetByIdAsync(notificationId);

			if (notification != null)
			{
				return await _departmentNotificationRepository.DeleteAsync(notification, cancellationToken);
			}

			return false;
		}
	}
}
