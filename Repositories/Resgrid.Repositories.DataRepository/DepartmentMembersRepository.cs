using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using Dapper;
using Resgrid.Model.Identity;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentMembersRepository : RepositoryBase<DepartmentMember>, IDepartmentMembersRepository
	{
		public string connectionString = ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>().FirstOrDefault(x => x.Name == "ResgridContext").ConnectionString;

		public DepartmentMembersRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public List<DepartmentMember> GetAllDepartmentMembersWithinLimits(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"DECLARE @limit INT
							SET @limit = (SELECT TOP 1 pl.LimitValue FROM Payments p
							INNER JOIN PlanLimits pl ON pl.PlanId = p.PlanId
							WHERE DepartmentId = @departmentId AND pl.LimitType = 1 AND p.EffectiveOn <= GETUTCDATE() AND p.EndingOn >= GETUTCDATE()
							ORDER BY PaymentId DESC)

							SELECT TOP (@limit) dm.*, u.*
							FROM DepartmentMembers dm
							INNER JOIN AspNetUsers u ON u.Id = dm.UserId
							WHERE DepartmentId = @departmentId AND IsDeleted = 0";

				var multi = db.QueryMultiple(query, new { departmentId = departmentId });
				var members = multi.Read<DepartmentMember, IdentityUser, DepartmentMember>((dm, u) => { dm.User = u; return dm; }).ToList();

				return members;
			}
		}

		public List<DepartmentMember> GetAllDepartmentMembersUnlimited(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{

				var query = @"SELECT dm.*, u.*
							FROM DepartmentMembers dm
							INNER JOIN AspNetUsers u ON u.Id = dm.UserId
							WHERE DepartmentId = @departmentId AND IsDeleted = 0";

				var multi = db.QueryMultiple(query, new { departmentId = departmentId });
				var members = multi.Read<DepartmentMember, IdentityUser, DepartmentMember>((dm, u) => { dm.User = u; return dm; }).ToList();

				return members;
			}
		}

		public List<UserProfileMaintenance> GetAllMissingUserProfiles()
		{
			var data = db.SqlQuery<UserProfileMaintenance>(@"SELECT d.UserId, d.DepartmentId FROM DepartmentMembers d
														INNER JOIN Profiles p on d.UserId = p.UserId
														WHERE d.UserId NOT IN (SELECT up.UserId FROM UserProfiles up INNER JOIN DepartmentMembers dm ON up.UserId = dm.UserId WHERE dm.DepartmentId = d.DepartmentId)");

			return data.ToList();
		}

		public List<UserProfileMaintenance> GetAllUserProfilesWithEmptyNames()
		{
			var data = db.SqlQuery<UserProfileMaintenance>(@"SELECT d.UserId, d.DepartmentId
															FROM DepartmentMembers d
															INNER JOIN UserProfiles up on d.UserId = up.UserId
															WHERE up.FirstName IS NULL OR up.FirstName = ''");

			return data.ToList();
		}
	}
}
