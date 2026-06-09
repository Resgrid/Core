using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Dapper.Contrib.Extensions;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model;
using Resgrid.Config;
using Npgsql;

namespace Resgrid.Repositories.DataRepository
{
	public class IdentityRepository : IIdentityRepository
	{
		public List<IdentityUser> GetAll()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>($"SELECT * FROM aspnetusers").ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>($"SELECT * FROM AspNetUsers").ToList();
				}
			}

			return null;
		}

		public IdentityUser GetUserById(string userId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>($"SELECT * FROM aspnetusers WHERE id = @userId", new { userId = userId }).FirstOrDefault();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>($"SELECT * FROM AspNetUsers WHERE Id = @userId", new { userId = userId }).FirstOrDefault();
				}
			}

			return null;
		}

		public async Task<IdentityUser> GetUserByUserNameAsync(string userName)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					// aspnetusers columns are citext (case-insensitive), so match on the same key ASP.NET
					// Identity authenticated against -- normalizedusername -- so a row whose username and
					// normalizedusername have drifted apart is still found here.
					var result = await db.QueryAsync<IdentityUser>($"SELECT * FROM aspnetusers WHERE normalizedusername = @normalizedUserName", new { normalizedUserName = userName?.ToUpperInvariant() });

					return result.FirstOrDefault();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var result = await db.QueryAsync<IdentityUser>($"SELECT * FROM AspNetUsers WHERE UserName = @userName", new { userName = userName });

					return result.FirstOrDefault();
				}
			}

			return null;
		}

		public IdentityUser GetUserByEmail(string email)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					// email is citext (case-insensitive), so plain equality is already case-insensitive and can
					// use the index -- no LOWER() needed.
					return db.Query<IdentityUser>($"SELECT * FROM aspnetusers WHERE email = @email", new { email = email }).FirstOrDefault();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>($"SELECT * FROM AspNetUsers WHERE Email = @email", new { email = email }).FirstOrDefault();
				}
			}

			return null;
		}

		public void UpdateUsername(string oldUsername, string newUsername)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					// username is citext, so equality is already case-insensitive and index-friendly.
					db.Execute($"UPDATE public.aspnetusers SET username = @newUsername, normalizedusername = @newUsernameUpper WHERE username = @oldUsername", new { newUsername = newUsername, newUsernameUpper = newUsername.ToUpperInvariant(), oldUsername = oldUsername });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					db.Execute($"UPDATE [AspNetUsers] SET [UserName] = @newUsername, [NormalizedUserName] = @newUsernameUpper WHERE UserName = @oldUsername", new { newUsername = newUsername, newUsernameUpper = newUsername.ToUpperInvariant(), oldUsername = oldUsername });
				}
			}
		}

		public void UpdateEmail(string userId, string newEmail)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					// Keep normalizedemail in sync (ASP.NET Identity's FindByEmailAsync looks up by it); the
					// SQL Server branch already does this. Without it, email lookups go stale after a change.
					db.Execute($"UPDATE public.aspnetusers SET email = @newEmail, normalizedemail = @newEmailUpper WHERE id = @userId", new { userId = userId, newEmail = newEmail, newEmailUpper = newEmail?.ToUpperInvariant() });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					db.Execute($"UPDATE [AspNetUsers] SET [Email] = @newEmail, [NormalizedEmail] = @newEmailUpper WHERE Id = @userId", new { userId = userId, newEmail = newEmail, newEmailUpper = newEmail.ToUpperInvariant() });
				}
			}
		}

		public void AddUserToRole(string userId, string roleId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					db.Execute($"INSERT INTO public.aspnetuserroles (\"userid\" ,\"roleid\") VALUES (@userId, @roleId)", new { userId = userId, roleId = roleId });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					db.Execute($"INSERT INTO AspNetUserRoles ([UserId] ,[RoleId]) VALUES (@userId, @roleId)", new { userId = userId, roleId = roleId });
				}
			}
		}

		public void InitUserExtInfo(string userId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					db.Execute($"INSERT INTO public.aspnetusersext (\"userid\" ,\"createdate\" ,\"lastactivitydate\") VALUES (@userId,@dateTimeNow,@dateTimeNow)", new { userId = userId, dateTimeNow = DateTime.UtcNow });
					db.Execute($"UPDATE public.aspnetusers SET emailconfirmed = true WHERE id = @userId", new { userId = userId });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					db.Execute($"INSERT INTO AspNetUsersExt ([UserId] ,[CreateDate] ,[LastActivityDate]) VALUES (@userId,@dateTimeNow,@dateTimeNow)", new { userId = userId, dateTimeNow = DateTime.UtcNow });
					db.Execute($"UPDATE [dbo].[AspNetUsers] SET [EmailConfirmed] = 1 WHERE Id = @userId", new { userId = userId });
				}
			}
		}

		public IdentityUserRole GetRoleForUserRole(string userId, string roleId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUserRole>($"SELECT * FROM aspnetuserroles WHERE userid = @userId AND roleid = @roleId", new { userId = userId, roleId = roleId }).FirstOrDefault();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUserRole>($"SELECT * FROM AspNetUserRoles WHERE UserId = @userId AND RoleId = @roleId", new { userId = userId, roleId = roleId }).FirstOrDefault();
				}
			}

			return null;
		}

		public IdentityUser Update(IdentityUser user)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					db.Update(user);
				}
				return user;
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					db.Update(user);
				}
				return user;
			}

			return null;
		}

		public List<IdentityUser> GetAllMembershipsForDepartment(int departmentId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT m.* FROM aspnetusers m
																						INNER JOIN departmentmembers dm ON dm.userid = m.id
																						WHERE dm.departmentid = @departmentId AND dm.isdeleted = false", new { departmentId = departmentId }).ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT m.* FROM AspNetUsers m
																						INNER JOIN DepartmentMembers dm ON dm.UserId = m.Id
																						WHERE dm.DepartmentId = @departmentId AND dm.IsDeleted = 0", new { departmentId = departmentId }).ToList();
				}
			}

			return null;
		}

		public List<IdentityUser> GetAllUsersForDepartment(int departmentId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.* FROM aspnetusers u
																				INNER JOIN departmentmembers dm ON dm.userid = u.id
																				WHERE dm.departmentid = @departmentId AND dm.isdeleted = false", new { departmentId = departmentId }).ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.* FROM AspNetUsers u
																				INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
																				WHERE dm.DepartmentId = @departmentId AND dm.IsDeleted = 0", new { departmentId = departmentId }).ToList();
				}
			}

			return null;
		}

		public List<IdentityUser> GetAllUsersForGroup(int groupId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.*
                                                FROM aspnetusers u
                                                INNER JOIN departmentgroupmembers dgm ON u.id = dgm.userid
                                                INNER JOIN departmentmembers dm ON u.id = dm.userid
                                                WHERE dgm.departmentgroupid = @groupId AND dm.isdeleted = false",
								new { groupId = groupId }).ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.*
												FROM AspNetUsers u
												INNER JOIN DepartmentGroupMembers dgm ON u.Id = dgm.UserId
												INNER JOIN DepartmentMembers dm ON u.Id = dm.UserId
												WHERE dgm.DepartmentGroupId = @groupId AND dm.IsDeleted = 0", new { groupId = groupId }).ToList();
				}
			}

			return null;
		}

		public List<IdentityUser> GetAllUsersForDepartmentWithinLimits(int departmentId, bool retrieveHidden)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.* FROM aspnetusers u
												INNER JOIN departmentmembers dm ON dm.userid = u.id
												WHERE dm.departmentid = @departmentId AND dm.isdeleted = false AND (@retrieveHidden = true OR (dm.ishidden = false OR dm.ishidden IS NULL)) AND (dm.isdisabled = false OR dm.isdisabled IS NULL)",
								new { departmentId = departmentId, retrieveHidden = retrieveHidden }).ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.* FROM AspNetUsers u
												INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
												WHERE dm.DepartmentId = @departmentId AND dm.IsDeleted = 0 AND (@retrieveHidden = 1 OR (dm.IsHidden = 0 OR dm.IsHidden IS NULL)) AND (dm.IsDisabled = 0 OR dm.IsDisabled IS NULL)",
								new { departmentId = departmentId, retrieveHidden = retrieveHidden }).ToList();
				}
			}

			return null;
		}

		public async Task<List<IdentityUser>> GetAllUsersForDepartmentWithinLimitsAsync(int departmentId, bool retrieveHidden)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					var result = await db.QueryAsync<IdentityUser>(@"SELECT u.* FROM aspnetusers u
												INNER JOIN departmentmembers dm ON dm.userid = u.id
												WHERE dm.departmentid = @departmentId AND dm.isdeleted = false AND (@retrieveHidden = true OR (dm.ishidden = false OR dm.ishidden IS NULL)) AND (dm.isdisabled = false OR dm.isdisabled IS NULL)",
						new { departmentId = departmentId, retrieveHidden = retrieveHidden });

					return result.ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var result = await db.QueryAsync<IdentityUser>(@"SELECT u.* FROM AspNetUsers u
												INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
												WHERE dm.DepartmentId = @departmentId AND dm.IsDeleted = 0 AND (@retrieveHidden = 1 OR (dm.IsHidden = 0 OR dm.IsHidden IS NULL)) AND (dm.IsDisabled = 0 OR dm.IsDisabled IS NULL)",
						new { departmentId = departmentId, retrieveHidden = retrieveHidden });

					return result.ToList();
				}
			}

			return null;
		}

		public List<IdentityUser> GetAllUsersCreatedAfterTimestamp(DateTime timestamp)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.*, ue.* FROM aspnetusers u
																				INNER JOIN aspnetusersext ue ON u.id = ue.userid
																				WHERE ue.createdate > @timestamp",
									new { timestamp = timestamp }).ToList();
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					return db.Query<IdentityUser>(@"SELECT u.*, ue.* FROM AspNetUsers u
																				INNER JOIN AspNetUsersExt ue ON u.Id = ue.UserId
																				WHERE ue.CreateDate > @timestamp",
									new { timestamp = timestamp }).ToList();
				}
			}

			return null;
		}

		public async Task<List<UserGroupRole>> GetAllUsersGroupsAndRolesAsync(int departmentId, bool retrieveHidden, bool retrieveDisabled, bool retrieveDeleted)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					var query = await db.QueryAsync<UserGroupRole>(@"SELECT dgm.departmentGroupid, u.id as userid,
												(SELECT STRING_AGG(pru.personnelroleid::text, ',') FROM public.personnelroleusers pru WHERE pru.userid = u.id AND pru.departmentid = @departmentId) as roles,
												(SELECT STRING_AGG(pr.name, ', ') FROM public.personnelroleusers pru INNER JOIN public.personnelroles pr ON pr.personnelroleid = pru.personnelroleid WHERE pru.userid = u.id AND pru.departmentid = @departmentId) as rolenames,
												dg.name as departmentgroupname,
												up.firstname AS firstname,
												up.lastname AS lastname
												FROM public.departmentmembers dm
												INNER JOIN public.aspnetusers u ON u.id = dm.userid
												LEFT OUTER JOIN public.departmentgroupmembers dgm ON u.id = dgm.userid AND dgm.departmentid = @departmentId
												LEFT OUTER JOIN public.departmentgroups dg ON dg.departmentgroupid = dgm.departmentgroupid
												INNER JOIN public.userprofiles up ON up.userid = u.id
												WHERE dm.departmentid = @departmentId AND (@retrieveHidden = true OR (dm.ishidden = false OR dm.ishidden IS NULL)) AND (@retrieveDisabled = true OR (dm.isdisabled = false OR dm.isdisabled IS NULL)) AND (@retrieveDeleted = true OR (dm.isdeleted = false OR dm.isdeleted IS NULL))
												GROUP BY dgm.departmentgroupid, u.id, dg.name, up.firstname, up.lastname",
									new { departmentId = departmentId, retrieveHidden = retrieveHidden, retrieveDisabled = retrieveDisabled, retrieveDeleted = retrieveDeleted });

					return query.ToList();
				}
			}
			else
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var query = await db.QueryAsync<UserGroupRole>(@"SELECT dgm.DepartmentGroupId, u.Id as 'UserId',
												(SELECT STUFF((
														SELECT ',' +  CONVERT(varchar, pru.PersonnelRoleId)
														FROM PersonnelRoleUsers pru
														WHERE pru.UserId = u.Id AND pru.DepartmentId = @departmentId
														FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 1, '')) as 'Roles',
												(SELECT STUFF((
														SELECT ', ' +  CONVERT(varchar, pr.Name)
														FROM PersonnelRoleUsers pru
														INNER JOIN PersonnelRoles pr ON pr.PersonnelRoleId = pru.PersonnelRoleId
														WHERE pru.UserId = u.Id AND pru.DepartmentId = @departmentId
														FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 1, '')) as 'RoleNames',
												dg.Name as 'DepartmentGroupName',
												up.FirstName AS 'FirstName',
												up.LastName AS 'LastName'
												FROM DepartmentMembers dm
												INNER JOIN AspNetUsers u ON u.Id = dm.UserId
												LEFT OUTER JOIN DepartmentGroupMembers dgm ON u.Id = dgm.UserId AND dgm.DepartmentId = @departmentId
												LEFT OUTER JOIN DepartmentGroups dg ON dg.DepartmentGroupId = dgm.DepartmentGroupId
												INNER JOIN UserProfiles up ON up.UserId = u.Id
												WHERE dm.DepartmentId = @departmentId AND (@retrieveHidden = 1 OR (dm.IsHidden = 0 OR dm.IsHidden IS NULL)) AND (@retrieveDisabled = 1 OR (dm.IsDisabled = 0 OR dm.IsDisabled IS NULL)) AND (@retrieveDeleted = 1 OR (dm.IsDeleted = 0 OR dm.IsDeleted IS NULL))",
									new { departmentId = departmentId, retrieveHidden = retrieveHidden, retrieveDisabled = retrieveDisabled, retrieveDeleted = retrieveDeleted });

					return query.ToList();
				}
		}

		public async Task<bool> CleanUpOIDCTokensAsync(DateTime timestamp)
		{
			if (OidcConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(OidcConfig.ConnectionString))
				{
					var result = await db.ExecuteAsync(@"DELETE FROM ""OpenIddictTokens"" WHERE ""ExpirationDate"" < @timestamp",
									new { timestamp = timestamp });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(OidcConfig.ConnectionString))
				{
					var result = await db.ExecuteAsync(@"DELETE FROM OpenIddictTokens
													 WHERE ExpirationDate < @timestamp",
									new { timestamp = timestamp });
				}
			}

			return false;
		}

		public async Task<bool> ClearOutUserLoginAsync(string userId)
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(DataConfig.CoreConnectionString))
				{
					var deleteId = Guid.NewGuid().ToString();
					var maskedEmail = deleteId + "@resgrid.del";
					// Full de-provisioning: mask the normalized columns too (so ASP.NET Identity's normalized
					// lookups can't find the row), null the password hash, rotate the security stamp, and lock
					// the account so a deleted user can no longer authenticate.
					var result = await db.ExecuteAsync(@"UPDATE public.aspnetusers
													 SET username = @deleteId,
													 normalizedusername = @normalizedDeleteId,
													 email = @maskedEmail,
													 normalizedemail = @normalizedMaskedEmail,
													 passwordhash = NULL,
													 securitystamp = @securityStamp,
													 emailconfirmed = false,
													 lockoutenabled = true,
													 lockoutend = @lockoutEnd
													 WHERE id = @userId",
									new { userId = userId, deleteId = deleteId, normalizedDeleteId = deleteId.ToUpperInvariant(), maskedEmail = maskedEmail, normalizedMaskedEmail = maskedEmail.ToUpperInvariant(), securityStamp = Guid.NewGuid().ToString(), lockoutEnd = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero) });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(DataConfig.CoreConnectionString))
				{
					var deleteId = Guid.NewGuid().ToString();
					var maskedEmail = deleteId + "@resgrid.del";
					var result = await db.ExecuteAsync(@"UPDATE AspNetUsers
													 SET UserName = @deleteId,
													 NormalizedUserName = @normalizedDeleteId,
													 Email = @maskedEmail,
													 NormalizedEmail = @normalizedMaskedEmail,
													 PasswordHash = NULL,
													 SecurityStamp = @securityStamp,
													 EmailConfirmed = 0,
													 LockoutEnabled = 1,
													 LockoutEnd = @lockoutEnd
													 WHERE Id = @userId",
									new { userId = userId, deleteId = deleteId, normalizedDeleteId = deleteId.ToUpperInvariant(), maskedEmail = maskedEmail, normalizedMaskedEmail = maskedEmail.ToUpperInvariant(), securityStamp = Guid.NewGuid().ToString(), lockoutEnd = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero) });
				}
			}

			return false;
		}

		public async Task<bool> CleanUpOIDCTokensByUserAsync(string userId)
		{
			if (OidcConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				using (IDbConnection db = new NpgsqlConnection(OidcConfig.ConnectionString))
				{
					var result = await db.ExecuteAsync(@"DELETE FROM ""OpenIddictTokens"" WHERE ""Subject"" = @userId",
									new { userId = userId });
				}
			}
			else
			{
				using (IDbConnection db = new SqlConnection(OidcConfig.ConnectionString))
				{
					var result = await db.ExecuteAsync(@"DELETE FROM OpenIddictTokens
													 WHERE Subject = @userId",
									new { userId = userId });
				}
			}

			return false;
		}
	}
}
