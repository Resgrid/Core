using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Providers.Claims
{
	public static class ClaimsLogic
	{
		public static void AddSystemAdminClaims(ClaimsIdentity identity, string username, string userId, string fullName)
		{
			identity.AddClaim(new Claim(ClaimTypes.Name, username));
			identity.AddClaim(new Claim(ClaimTypes.PrimarySid, userId.ToString()));
			identity.AddClaim(new Claim(ClaimTypes.GivenName, fullName));
			identity.AddClaim(new Claim(ClaimTypes.Role, "Admins"));
			//Role = "Admins";
		}

		public static void AddAffiliteClaims(ClaimsIdentity identity, string username, string userId, string fullName)
		{
			identity.AddClaim(new Claim(ClaimTypes.Name, username));
			identity.AddClaim(new Claim(ClaimTypes.PrimarySid, userId.ToString()));
			identity.AddClaim(new Claim(ClaimTypes.GivenName, fullName));
			identity.AddClaim(new Claim(ClaimTypes.Role, "Affilites"));
			//Role = "Affilites";
		}

		public static void AddGeneralClaims(ClaimsIdentity identity, string username, string userId, string fullName, int departmentId, string departmentName, string emailAddress, DateTime createdOn)
		{
			identity.AddClaim(new Claim(ClaimTypes.Name, username));
			identity.AddClaim(new Claim(ClaimTypes.PrimarySid, userId.ToString()));
			identity.AddClaim(new Claim(ClaimTypes.GivenName, fullName));
			identity.AddClaim(new Claim(ClaimTypes.Actor, departmentName));
			identity.AddClaim(new Claim(ClaimTypes.PrimaryGroupSid, departmentId.ToString()));
			identity.AddClaim(new Claim(ClaimTypes.Email, emailAddress));
			identity.AddClaim(new Claim(ClaimTypes.OtherPhone, createdOn.ToString()));
			identity.AddClaim(new Claim(ClaimTypes.Role, "Users"));
			//Role = "Users";
		}

		public static void AddDepartmentClaim(ClaimsIdentity identity, int departmentId, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Memberships.Departments, departmentId.ToString()));
			identity.AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId), ResgridClaimTypes.Actions.View));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId), ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId), ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId), ResgridClaimTypes.Actions.Delete));

				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddGroupClaim(ClaimsIdentity identity, int groupId, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Memberships.Groups, groupId.ToString()));
			identity.AddClaim(new Claim(string.Format("{0}/{1}", ResgridClaimTypes.Resources.Group, groupId), ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId), ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId), ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId), ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddCallClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateCall))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateCall);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddActionClaims(ClaimsIdentity identity)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.View));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Update));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Create));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Delete));
		}

		public static void AddLogClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateLog))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateLog);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddShiftClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateShift))
			{
				var permission = permissions.FirstOrDefault(x => x.PermissionType == (int)PermissionTypes.CreateShift);

				if (permission != null)
				{
					if (permission.Action == (int) PermissionActions.DepartmentAdminsOnly && isAdmin)
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
					}
					else if (permission.Action == (int) PermissionActions.DepartmentAdminsOnly && !isAdmin)
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					}
					else if (permission.Action == (int) PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
					}
					else if (permission.Action == (int) PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					}
					else if (permission.Action == (int) PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
					}
					else if (permission.Action == (int) PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
					{
						if (!String.IsNullOrWhiteSpace(permission.Data))
						{
							var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
							var role = from r in roles
								where roleIds.Contains(r.PersonnelRoleId)
								select r;

							if (role.Any())
							{
								identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
								identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
								identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
								identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
							}
							else
							{
								identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
							}
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
						}
					}
					else if (permission.Action == (int) PermissionActions.Everyone)
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
					}
				}
				else if (isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}
				else
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
				}
			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));

				if (isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}
			}
		}

		public static void AddStaffingClaims(ClaimsIdentity identity)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.View));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.Update));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.Create));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.Delete));
		}

		public static void AddPersonnelClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.View));

			var permission = permissions.FirstOrDefault(x => x.PermissionType == (int)PermissionTypes.AddPersonnel);

			if (permission != null)
			{
				if (permission.Action == (int) PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int) PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Delete));
				}
			}
			else if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddRoleClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddUnitClaims(ClaimsIdentity identity, bool isAdmin)
		{
			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Delete));
			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.View));
			}
		}

		public static void AddUnitLogClaims(ClaimsIdentity identity)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.View));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Update));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Create));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Delete));
		}

		public static void AddMessageClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateMessage))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateMessage);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
				}
			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddProfileClaims(ClaimsIdentity identity)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Profile, ResgridClaimTypes.Actions.View));
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Profile, ResgridClaimTypes.Actions.Update));
		}

		public static void AddReportsClaims(ClaimsIdentity identity)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Reports, ResgridClaimTypes.Actions.View));
		}

		public static void AddGenericGroupClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddRoleClaims(ClaimsIdentity identity, IEnumerable<IdentityRole> roles)
		{
			foreach (var role in roles)
			{
				identity.AddClaim(new Claim(ClaimTypes.Role, role.Name));
			}
		}

		public static void AddDocumentsClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateDocument))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateDocument);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddNotesClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateNote))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateNote);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddScheduleClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateCalendarEntry))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateCalendarEntry);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddTrainingClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateTraining))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateTraining);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				if (isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}

				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
			}
		}

		//public List<int> GroupMemberships
		//{
		//	get
		//	{
		//		int outValue = 0;
		//		return (from claim in FindAll(ResgridClaimTypes.Memberships.Groups)
		//						where int.TryParse(claim.Value, out outValue)
		//						select outValue).ToList();
		//	}
		//}

		//public List<int> DepartmentMemberships
		//{
		//	get
		//	{
		//		int outValue = 0;
		//		return (from claim in FindAll(ResgridClaimTypes.Memberships.Departments)
		//						where int.TryParse(claim.Value, out outValue)
		//						select outValue).ToList();
		//	}
		//}

		public static void AddPIIClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ViewPersonalInfo))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewPersonalInfo);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin && !String.IsNullOrWhiteSpace(permission.Data))
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
						}
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddInventoryClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.AdjustInventory))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.AdjustInventory);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					if (!String.IsNullOrWhiteSpace(permission.Data))
					{
						var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
						var role = from r in roles
											 where roleIds.Contains(r.PersonnelRoleId)
											 select r;

						if (role.Any())
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
						}
						else
						{
							identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
						}
					}
					else
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				if (isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}

				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
			}
		}

		public static void AddCommandClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddConnectClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddProtocolClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddFormsClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddVoiceClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddCustomStateClaims(ClaimsIdentity identity, bool isAdmin)
		{
			identity.AddClaim(new Claim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.Create));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.Delete));
			}
		}

		public static void AddContactsClaims(ClaimsIdentity identity, bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactView))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactView);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
			}

			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactEdit))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactEdit);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}

			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
			}

			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactDelete))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactDelete);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
			}
			else
			{
				identity.AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
			}
		}
	}
}
