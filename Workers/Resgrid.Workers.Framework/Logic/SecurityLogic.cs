using Resgrid.Model.Services;
using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Linq;
using System.Collections.Generic;
using Resgrid.Model.Providers;
using Resgrid.Workers.Framework.Workers.Security;

namespace Resgrid.Workers.Framework.Logic
{
	public class SecurityLogic
	{
		private static TimeSpan Day30CacheLength = TimeSpan.FromDays(30);
		private static string WhoCanViewUnitsCacheKey = "ViewUnitsSecurityMaxtix_{0}";
		private static string WhoCanViewUnitLocationsCacheKey = "ViewUnitLocationsSecurityMaxtix_{0}";
		private static string WhoCanViewPersonnelCacheKey = "ViewUsersSecurityMaxtix_{0}";
		private static string WhoCanViewPersonnelLocationsCacheKey = "ViewUserLocationsSecurityMaxtix_{0}";

		private IDepartmentMembersRepository _departmentMembersRepository;
		private IUserProfileService _userProfileService;
		private IUsersService _usersService;
		private IDepartmentsService _departmentsService;
		private IScheduledTasksService _scheduledTasksService;
		private ICallsRepository _callsRepository;
		private IPermissionsService _permissionsService;
		private IUnitsService _unitsService;
		private IDepartmentGroupsService _departmentGroupsService;
		private IPersonnelRolesService _personnelRolesService;
		private ICacheProvider _cacheProvider;
		private IAuthorizationService _authorizationService;

		public SecurityLogic()
		{
			_departmentMembersRepository = Bootstrapper.GetKernel().Resolve<IDepartmentMembersRepository>();
			_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
			_usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();
			_departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
			_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
			_callsRepository = Bootstrapper.GetKernel().Resolve<ICallsRepository>();
			_permissionsService = Bootstrapper.GetKernel().Resolve<IPermissionsService>();
			_unitsService = Bootstrapper.GetKernel().Resolve<IUnitsService>();
			_departmentGroupsService = Bootstrapper.GetKernel().Resolve<IDepartmentGroupsService>();
			_personnelRolesService = Bootstrapper.GetKernel().Resolve<IPersonnelRolesService>();
			_cacheProvider = Bootstrapper.GetKernel().Resolve<ICacheProvider>();
			_authorizationService = Bootstrapper.GetKernel().Resolve<IAuthorizationService>();
		}

		public async Task<Tuple<bool, string>> Process(SecurityQueueItem item)
		{
			bool success = true;
			string result = String.Empty;

			var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId);

			if (item.Type == SecurityCacheTypes.WhoCanViewUnits)
			{
				async Task<VisibilityPayloadUnits> getWhoCanViewUnits()
				{
					var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(item.DepartmentId, PermissionTypes.ViewGroupUnits);
					var unitsPayload = new VisibilityPayloadUnits();
					var units = await _unitsService.GetUnitsForDepartmentAsync(item.DepartmentId);

					if (permission == null || (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup))
					{
						unitsPayload.EveryoneNoGroupLock = true;
					}
					else
					{
						unitsPayload.Units = new Dictionary<int, List<string>>();

						if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly)
						{ // Department Admins only
							foreach (var unit in units)
							{
								unitsPayload.Units.Add(unit.UnitId, department.AdminUsers);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup)
						{ // Department and group Admins (not locked to group)
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);
								users.AddRange((await _departmentGroupsService.GetAllGroupAdminsByDepartmentIdAsync(item.DepartmentId)).Select(x => x.UserId));

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup)
						{ // Department and group Admins (locked to group)
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								if (unit.StationGroupId.HasValue)
								{
									users.AddRange((await _departmentGroupsService.GetAllAdminsForGroupAsync(unit.StationGroupId.Value)).Select(x => x.UserId));
								}

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !permission.LockToGroup)
						{ // Department admins selected roles (not locked to group)
							List<string> users = new List<string>();
							users.AddRange(department.AdminUsers);

							if (!String.IsNullOrWhiteSpace(permission.Data))
							{
								var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

								if (roleIds != null && roleIds.Any())
								{
									foreach (var roleId in roleIds)
									{
										var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

										if (rolePersons != null && rolePersons.Any())
											users.AddRange(rolePersons.Select(x => x.UserId));
									}
								}
							}

							foreach (var unit in units)
							{
								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && permission.LockToGroup)
						{
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								if (unit.StationGroupId.HasValue)
								{
									var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value);

									if (usersInGroup != null && usersInGroup.Any())
									{
										if (!String.IsNullOrWhiteSpace(permission.Data))
										{
											var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

											if (roleIds != null && roleIds.Any())
											{
												foreach (var roleId in roleIds)
												{
													var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

													if (rolePersons != null && rolePersons.Any())
													{
														foreach (var rolePerson in rolePersons)
														{
															if (usersInGroup.Any(x => x.UserId == rolePerson.UserId))
																users.Add(rolePerson.UserId);
														}
													}
												}
											}
										}
									}
								}

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
						{ // Everyone in the same group have access to locked to group
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								if (unit.StationGroupId.HasValue)
								{
									users.AddRange((await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value)).Select(x => x.UserId));
								}

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
					}

					return unitsPayload;
				}

				if (Config.SystemBehaviorConfig.CacheEnabled)
				{
					await _cacheProvider.RemoveAsync(string.Format(WhoCanViewUnitsCacheKey, item.DepartmentId));
					await _cacheProvider.RetrieveAsync(string.Format(WhoCanViewUnitsCacheKey, item.DepartmentId), getWhoCanViewUnits, Day30CacheLength);
				}
			}
			else if (item.Type == SecurityCacheTypes.WhoCanViewUnitLocations)
			{
				async Task<VisibilityPayloadUnits> getWhoCanViewUnitLocations()
				{
					var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(item.DepartmentId, PermissionTypes.CanSeeUnitLocations);
					var unitsPayload = new VisibilityPayloadUnits();
					var units = await _unitsService.GetUnitsForDepartmentAsync(item.DepartmentId);

					if (permission == null || (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup))
					{
						unitsPayload.EveryoneNoGroupLock = true;
					}
					else
					{
						unitsPayload.Units = new Dictionary<int, List<string>>();

						if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly)
						{ // Department Admins only
							foreach (var unit in units)
							{
								unitsPayload.Units.Add(unit.UnitId, department.AdminUsers);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup)
						{ // Department and group Admins (not locked to group)
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);
								users.AddRange((await _departmentGroupsService.GetAllGroupAdminsByDepartmentIdAsync(item.DepartmentId)).Select(x => x.UserId));

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup)
						{ // Department and group Admins (locked to group)
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								if (unit.StationGroupId.HasValue)
								{
									users.AddRange((await _departmentGroupsService.GetAllAdminsForGroupAsync(unit.StationGroupId.Value)).Select(x => x.UserId));
								}

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !permission.LockToGroup)
						{ // Department admins selected roles (not locked to group)
							List<string> users = new List<string>();
							users.AddRange(department.AdminUsers);

							if (!String.IsNullOrWhiteSpace(permission.Data))
							{
								var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

								if (roleIds != null && roleIds.Any())
								{
									foreach (var roleId in roleIds)
									{
										var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

										if (rolePersons != null && rolePersons.Any())
											users.AddRange(rolePersons.Select(x => x.UserId));
									}
								}
							}

							foreach (var unit in units)
							{
								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && permission.LockToGroup)
						{
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								if (unit.StationGroupId.HasValue)
								{
									var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value);

									if (usersInGroup != null && usersInGroup.Any())
									{
										if (!String.IsNullOrWhiteSpace(permission.Data))
										{
											var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

											if (roleIds != null && roleIds.Any())
											{
												foreach (var roleId in roleIds)
												{
													var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

													if (rolePersons != null && rolePersons.Any())
													{
														foreach (var rolePerson in rolePersons)
														{
															if (usersInGroup.Any(x => x.UserId == rolePerson.UserId))
																users.Add(rolePerson.UserId);
														}
													}
												}
											}
										}
									}
								}

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
						{ // Everyone in the same group have access to locked to group
							foreach (var unit in units)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								if (unit.StationGroupId.HasValue)
								{
									users.AddRange((await _departmentGroupsService.GetAllMembersForGroupAsync(unit.StationGroupId.Value)).Select(x => x.UserId));
								}

								unitsPayload.Units.Add(unit.UnitId, users);
							}
						}
					}

					return unitsPayload;
				}

				if (Config.SystemBehaviorConfig.CacheEnabled)
				{
					await _cacheProvider.RemoveAsync(string.Format(WhoCanViewUnitLocationsCacheKey, item.DepartmentId));
					await _cacheProvider.RetrieveAsync(string.Format(WhoCanViewUnitLocationsCacheKey, item.DepartmentId), getWhoCanViewUnitLocations, Day30CacheLength);
				}
			}
			else if (item.Type == SecurityCacheTypes.WhoCanViewPersonnel)
			{
				async Task<VisibilityPayloadUsers> getWhoCanViewUsers()
				{
					var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(item.DepartmentId, PermissionTypes.ViewGroupUsers);
					var usersPayload = new VisibilityPayloadUsers();
					var allUsers = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(item.DepartmentId);

					if (permission == null || (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup))
					{
						usersPayload.EveryoneNoGroupLock = true;
					}
					else
					{
						usersPayload.Users = new Dictionary<string, List<string>>();

						if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly)
						{ // Department Admins only
							foreach (var user in allUsers)
							{
								usersPayload.Users.Add(user.UserId, department.AdminUsers);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup)
						{ // Department and group Admins (not locked to group)
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);
								users.AddRange((await _departmentGroupsService.GetAllGroupAdminsByDepartmentIdAsync(item.DepartmentId)).Select(x => x.UserId));

								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup)
						{ // Department and group Admins (locked to group)
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, item.DepartmentId);

								if (group != null)
								{
									users.AddRange((await _departmentGroupsService.GetAllAdminsForGroupAsync(group.DepartmentGroupId)).Select(x => x.UserId));
								}

								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !permission.LockToGroup)
						{ // Department admins selected roles (not locked to group)
							List<string> users = new List<string>();
							users.AddRange(department.AdminUsers);

							if (!String.IsNullOrWhiteSpace(permission.Data))
							{
								var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

								if (roleIds != null && roleIds.Any())
								{
									foreach (var roleId in roleIds)
									{
										var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

										if (rolePersons != null && rolePersons.Any())
											users.AddRange(rolePersons.Select(x => x.UserId));
									}
								}
							}

							foreach (var user in allUsers)
							{
								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && permission.LockToGroup)
						{
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, item.DepartmentId);

								if (group != null)
								{
									var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId);

									if (usersInGroup != null && usersInGroup.Any())
									{
										if (!String.IsNullOrWhiteSpace(permission.Data))
										{
											var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

											if (roleIds != null && roleIds.Any())
											{
												foreach (var roleId in roleIds)
												{
													var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

													if (rolePersons != null && rolePersons.Any())
													{
														foreach (var rolePerson in rolePersons)
														{
															if (usersInGroup.Any(x => x.UserId == rolePerson.UserId))
																users.Add(rolePerson.UserId);
														}
													}
												}
											}
										}
									}
								}

								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
						{ // Everyone in the same group have access to locked to group
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, item.DepartmentId);

								if (group != null)
								{
									users.AddRange((await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId)).Select(x => x.UserId));
								}

								usersPayload.Users.Add(user.UserId, users);
							}
						}
					}

					return usersPayload;
				}

				if (Config.SystemBehaviorConfig.CacheEnabled)
				{
					await _cacheProvider.RemoveAsync(string.Format(WhoCanViewPersonnelCacheKey, item.DepartmentId));
					await _cacheProvider.RetrieveAsync(string.Format(WhoCanViewPersonnelCacheKey, item.DepartmentId), getWhoCanViewUsers, Day30CacheLength);
				}
			}
			else if (item.Type == SecurityCacheTypes.WhoCanViewPersonnelLocations)
			{
				async Task<VisibilityPayloadUsers> getWhoCanViewUserLocations()
				{
					var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(item.DepartmentId, PermissionTypes.CanSeePersonnelLocations);
					var usersPayload = new VisibilityPayloadUsers();
					var allUsers = await _departmentMembersRepository.GetAllDepartmentMembersUnlimitedAsync(item.DepartmentId);

					if (permission == null || (permission.Action == (int)PermissionActions.Everyone && !permission.LockToGroup))
					{
						usersPayload.EveryoneNoGroupLock = true;
					}
					else
					{
						usersPayload.Users = new Dictionary<string, List<string>>();

						if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly)
						{ // Department Admins only
							foreach (var user in allUsers)
							{
								usersPayload.Users.Add(user.UserId, department.AdminUsers);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !permission.LockToGroup)
						{ // Department and group Admins (not locked to group)
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);
								users.AddRange((await _departmentGroupsService.GetAllGroupAdminsByDepartmentIdAsync(item.DepartmentId)).Select(x => x.UserId));

								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && permission.LockToGroup)
						{ // Department and group Admins (locked to group)
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, item.DepartmentId);

								if (group != null)
								{
									users.AddRange((await _departmentGroupsService.GetAllAdminsForGroupAsync(group.DepartmentGroupId)).Select(x => x.UserId));
								}

								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !permission.LockToGroup)
						{ // Department admins selected roles (not locked to group)
							List<string> users = new List<string>();
							users.AddRange(department.AdminUsers);

							if (!String.IsNullOrWhiteSpace(permission.Data))
							{
								var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

								if (roleIds != null && roleIds.Any())
								{
									foreach (var roleId in roleIds)
									{
										var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

										if (rolePersons != null && rolePersons.Any())
											users.AddRange(rolePersons.Select(x => x.UserId));
									}
								}
							}

							foreach (var user in allUsers)
							{
								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && permission.LockToGroup)
						{
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, item.DepartmentId);

								if (group != null)
								{
									var usersInGroup = await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId);

									if (usersInGroup != null && usersInGroup.Any())
									{
										if (!String.IsNullOrWhiteSpace(permission.Data))
										{
											var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);

											if (roleIds != null && roleIds.Any())
											{
												foreach (var roleId in roleIds)
												{
													var rolePersons = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);

													if (rolePersons != null && rolePersons.Any())
													{
														foreach (var rolePerson in rolePersons)
														{
															if (usersInGroup.Any(x => x.UserId == rolePerson.UserId))
																users.Add(rolePerson.UserId);
														}
													}
												}
											}
										}
									}
								}

								usersPayload.Users.Add(user.UserId, users);
							}
						}
						else if (permission.Action == (int)PermissionActions.Everyone && permission.LockToGroup)
						{ // Everyone in the same group have access to locked to group
							foreach (var user in allUsers)
							{
								List<string> users = new List<string>();
								users.AddRange(department.AdminUsers);

								var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, item.DepartmentId);

								if (group != null)
								{
									users.AddRange((await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId)).Select(x => x.UserId));
								}

								usersPayload.Users.Add(user.UserId, users);
							}
						}
					}

					return usersPayload;
				}

				if (Config.SystemBehaviorConfig.CacheEnabled)
				{
					await _cacheProvider.RemoveAsync(string.Format(WhoCanViewPersonnelLocationsCacheKey, item.DepartmentId));
					await _cacheProvider.RetrieveAsync(string.Format(WhoCanViewPersonnelLocationsCacheKey, item.DepartmentId), getWhoCanViewUserLocations, Day30CacheLength);
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public async Task<Tuple<bool, string>> UpdatedCachedSecurityForAllDepartments()
		{
			bool success = true;
			string result = String.Empty;

			var departments = await _departmentsService.GetAllAsync();

			foreach (var department in departments)
			{
				await Process(new SecurityQueueItem() { DepartmentId = department.DepartmentId, Type = SecurityCacheTypes.WhoCanViewUnits });
				await Process(new SecurityQueueItem() { DepartmentId = department.DepartmentId, Type = SecurityCacheTypes.WhoCanViewUnitLocations });
				await Process(new SecurityQueueItem() { DepartmentId = department.DepartmentId, Type = SecurityCacheTypes.WhoCanViewPersonnel });
				await Process(new SecurityQueueItem() { DepartmentId = department.DepartmentId, Type = SecurityCacheTypes.WhoCanViewPersonnelLocations });
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
