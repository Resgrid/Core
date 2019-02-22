using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Repositories.DataRepository
{
	public class UserProfilesRepository : RepositoryBase<UserProfile>, IUserProfilesRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public UserProfilesRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public UserProfile GetProfileByUserId(string userId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = $@"SELECT up.*, u.Email as 'MembershipEmail', u.* 
								FROM UserProfiles up
								INNER JOIN AspNetusers u ON u.Id = up.UserId
								WHERE up.UserId = @userId";


				var multi = db.QueryMultiple(query, new { userId = userId });
				var profile = multi.Read<UserProfile, IdentityUser, UserProfile>((up, u) => { up.User = u; return up; }).FirstOrDefault();

				return profile;
			}
		}

		public async Task<UserProfile> GetProfileByUserIdAsync(string userId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = $@"SELECT up.*, u.Email as 'MembershipEmail', u.* 
								FROM UserProfiles up
								INNER JOIN AspNetusers u ON u.Id = up.UserId
								WHERE up.UserId = @userId";


				var multi = await db.QueryMultipleAsync(query, new { userId = userId });
				var profile = multi.Read<UserProfile, IdentityUser, UserProfile>((up, u) => { up.User = u; return up; }).FirstOrDefault();

				return profile;
			}
		}


		public List<UserProfile> GetAllUserProfilesForDepartment(int departmentId)
		{
			try
			{
				using (IDbConnection db = new SqlConnection(connectionString))
				{
					return db.Query<UserProfile>($@"SELECT up.*, u.Email as 'MembershipEmail' FROM UserProfiles up
												INNER JOIN DepartmentMembers dm ON dm.UserId = up.UserId
												INNER JOIN AspNetusers u ON u.Id = up.UserId
												WHERE dm.DepartmentId = @departmentId AND dm.IsDeleted = 0 AND dm.IsDisabled = 0", new { departmentId = departmentId }).ToList();
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogError(string.Format("GetAllUserProfilesForDepartment Exception DepartmentId:{0} Error: {1}", departmentId, ex.ToString()));

				throw;
			}
		}

		public List<UserProfile> GetAllUserProfilesForDepartmentIncDisabledDeleted(int departmentId)
		{
			try
			{
				using (IDbConnection db = new SqlConnection(connectionString))
				{
					return db.Query<UserProfile>($@"SELECT up.*, u.Email as 'MembershipEmail' FROM UserProfiles up
												INNER JOIN DepartmentMembers dm ON dm.UserId = up.UserId
												INNER JOIN AspNetusers u ON u.Id = up.UserId
												WHERE dm.DepartmentId = @departmentId", new { departmentId = departmentId }).ToList();
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogError(string.Format("GetAllUserProfilesForDepartment Exception DepartmentId:{0} Error: {1}", departmentId, ex.ToString()));

				throw;
			}
		}

		public List<UserProfile> GetSelectedUserProfiles(List<string> userIds)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var usersToQuery = String.Join(",", userIds.Select(p => $"'{p.ToString()}'").ToArray());
				var query = $@"SELECT up.*, u.Email as 'MembershipEmail', u.* FROM UserProfiles up
												INNER JOIN AspNetusers u ON u.Id = up.UserId
												WHERE u.Id IN ({usersToQuery})";

				//return db.Query<UserProfile>($@"SELECT up.*, u.Email as 'MembershipEmail' FROM UserProfiles up
				//								INNER JOIN AspNetusers u ON u.Id = up.UserId
				//								WHERE u.Id IN ({usersToQuery})").ToList();

				var multi = db.QueryMultiple(query);
				var profiles = multi.Read<UserProfile, IdentityUser, UserProfile>((up, u) => { up.User = u; return up; }).ToList();

				return profiles;
			}
		}

		public async Task<List<UserProfile>> GetSelectedUserProfilesAsync(List<string> userIds)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var usersToQuery = String.Join(",", userIds.Select(p => $"'{p.ToString()}'").ToArray());
				var query = $@"SELECT up.*, u.Email as 'MembershipEmail', u.* FROM UserProfiles up
												INNER JOIN AspNetusers u ON u.Id = up.UserId
												WHERE u.Id IN ({usersToQuery})";

				var multi = await db.QueryMultipleAsync(query);
				var profiles = multi.Read<UserProfile, IdentityUser, UserProfile>((up, u) => { up.User = u; return up; }).ToList();

				return profiles;
			}
		}
	}
}
