using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Providers.Claims
{
	public class ResgridIdentity : ClaimsIdentity
	{
		public string UserId { get; set; }
		public int DepartmentId { get; set; }
		public string UserName { get; set; }
		public string FullName { get; set; }
		public string DepartmentName { get; set; }
		public string Role { get; set; }

		public void AddSystemAdminClaims(string username, string userId, string fullName)
		{
			AddClaim(new Claim(ClaimTypes.Name, username));
			AddClaim(new Claim(ClaimTypes.PrimarySid, userId.ToString()));
			AddClaim(new Claim(ClaimTypes.GivenName, fullName));
			AddClaim(new Claim(ClaimTypes.Role, "Admins"));
			Role = "Admins";
		}

		public void AddAffiliteClaims(string username, string userId, string fullName)
		{
			AddClaim(new Claim(ClaimTypes.Name, username));
			AddClaim(new Claim(ClaimTypes.PrimarySid, userId.ToString()));
			AddClaim(new Claim(ClaimTypes.GivenName, fullName));
			AddClaim(new Claim(ClaimTypes.Role, "Affilites"));
			Role = "Affilites";
		}

		public void AddGeneralClaims(string username, string userId, string fullName, int departmentId, string departmentName, string emailAddress, DateTime createdOn)
		{
			AddClaim(new Claim(ClaimTypes.Name, username));
			AddClaim(new Claim(ClaimTypes.PrimarySid, userId.ToString()));
			AddClaim(new Claim(ClaimTypes.GivenName, fullName));
			AddClaim(new Claim(ClaimTypes.Actor, departmentName));
			AddClaim(new Claim(ClaimTypes.PrimaryGroupSid, departmentId.ToString()));
			AddClaim(new Claim(ClaimTypes.Email, emailAddress));
			AddClaim(new Claim(ClaimTypes.OtherPhone, createdOn.ToString()));
			AddClaim(new Claim(ClaimTypes.Role, "Users"));
			Role = "Users";
		}

		public void AddDepartmentClaim(int departmentId, bool isAdmin)
		{
			AddClaim(new Claim(ResgridClaimTypes.Memberships.Departments, departmentId.ToString()));
			AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId),
				ResgridClaimTypes.Actions.View));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId),
					ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId),
					ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.CreateDepartmentClaimTypeString(departmentId),
					ResgridClaimTypes.Actions.Delete));

				AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddGroupClaim(int groupId, bool isAdmin)
		{
			AddClaim(new Claim(ResgridClaimTypes.Memberships.Groups, groupId.ToString()));
			AddClaim(new Claim(string.Format("{0}/{1}", ResgridClaimTypes.Resources.Group, groupId),
				ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				AddClaim(new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId),
					ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId),
					ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId),
					ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddCallClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateCall))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateCall);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddActionClaims()
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.View));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Update));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Create));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Delete));
		}

		public void AddLogClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateLog))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateLog);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddShiftClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateShift))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateShift);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));

				if (isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));
				}
			}
		}

		public void AddStaffingClaims()
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.View));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.Update));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.Create));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Staffing, ResgridClaimTypes.Actions.Delete));
		}

		public void AddPersonnelClaims(bool isAdmin)
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddRoleClaims(bool isAdmin)
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddUnitClaims(bool isAdmin)
		{
			if (isAdmin)
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Delete));
			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.View));
			}
		}

		public void AddUnitLogClaims()
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.View));
			AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Update));
			AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Create));
			AddClaim(new Claim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Delete));
		}

		public void AddMessageClaims()
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));
		}

		public void AddProfileClaims()
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Profile, ResgridClaimTypes.Actions.View));
			AddClaim(new Claim(ResgridClaimTypes.Resources.Profile, ResgridClaimTypes.Actions.Update));
		}

		public void AddReportsClaims()
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.Reports, ResgridClaimTypes.Actions.View));
		}

		public void AddGenericGroupClaims(bool isAdmin)
		{
			AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.View));

			if (isAdmin)
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddRoleClaims(IEnumerable<IdentityRole> roles)
		{
			foreach (var role in roles)
			{
				AddClaim(new Claim(ClaimTypes.Role, role.Name));
			}
		}

		public void AddDocumentsClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateDocument))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateDocument);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddNotesClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateNote))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateNote);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddScheduleClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateCalendarEntry))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateCalendarEntry);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddTrainingClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.CreateTraining))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.CreateTraining);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				if (isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));
				}

				AddClaim(new Claim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
			}
		}

		public List<int> GroupMemberships
		{
			get
			{
				int outValue = 0;
				return (from claim in FindAll(ResgridClaimTypes.Memberships.Groups)
								where int.TryParse(claim.Value, out outValue)
								select outValue).ToList();
			}
		}

		public List<int> DepartmentMemberships
		{
			get
			{
				int outValue = 0;
				return (from claim in FindAll(ResgridClaimTypes.Memberships.Departments)
								where int.TryParse(claim.Value, out outValue)
								select outValue).ToList();
			}
		}

		public void AddPIIClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ViewPersonalInfo))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ViewPersonalInfo);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin && !String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
					}
				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
				AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
				AddClaim(new Claim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));
			}
		}

		public void AddInventoryClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.AdjustInventory))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.AdjustInventory);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && !isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && !isAdmin && !isGroupAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
										 where roleIds.Contains(r.PersonnelRoleId)
										 select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
					}
					else
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}

			}
			else
			{
				if (isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));
				}

				AddClaim(new Claim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
			}
		}

		public void AddContactsClaims(bool isAdmin, List<Permission> permissions, bool isGroupAdmin, List<PersonnelRole> roles)
		{
			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactView))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactView);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				}
			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
			}

			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactEdit))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactEdit);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
						AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				}

			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
				AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
			}

			if (permissions != null && permissions.Any(x => x.PermissionType == (int)PermissionTypes.ContactDelete))
			{
				var permission = permissions.First(x => x.PermissionType == (int)PermissionTypes.ContactDelete);

				if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin))
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isAdmin)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isAdmin)
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
					}

				}
				else if (permission.Action == (int)PermissionActions.Everyone)
				{
					AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
				}
			}
			else
			{
				AddClaim(new Claim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
			}
		}
	}
}
