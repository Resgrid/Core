using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentMembersRepository : RepositoryBase<DepartmentMember>, IDepartmentMembersRepository
	{
		public string connectionString = ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>().FirstOrDefault(x => x.Name == "ResgridContext").ConnectionString;

		public DepartmentMembersRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		//public List<DepartmentMember> GetAllDepartmentMembersWithinLimits(int departmentId)
		//{
		//	var data = db.SqlQuery<DepartmentMember>(@"DECLARE @limit INT
		//		SET @limit = (SELECT TOP 1 pl.LimitValue FROM Payments p
		//		INNER JOIN PlanLimits pl ON pl.PlanId = p.PlanId
		//		WHERE DepartmentId = @departmentId AND pl.LimitType = 1 AND p.EffectiveOn <= GETUTCDATE() AND p.EndingOn >= GETUTCDATE()
		//		ORDER BY PaymentId DESC)

		//		SELECT TOP (@limit) * FROM DepartmentMembers
		//		WHERE DepartmentId = @departmentId AND IsDeleted = 0",
		//		new SqlParameter("@departmentId", departmentId));

		//	return data.ToList();
		//}

		public List<DepartmentMember> GetAllDepartmentMembersWithinLimits(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				//var orderResult = await db.QueryAsync<ResourceOrder>($"SELECT * FROM ResourceOrders WHERE ResourceOrderId = @id", new { id = id });

				//return await db.GetAsync<ResourceOrder>(id);

				//return orderResult.FirstOrDefault();

				//var query = @"SELECT ro.*,
				//				   d.*,
				//				   roi.*,
				//				   rof.*,
				//				   rou.*
				//			FROM ResourceOrders ro
				//			JOIN Departments d ON ro.DepartmentId = d.DepartmentId
				//			LEFT OUTER JOIN ResourceOrderItems roi ON ro.ResourceOrderId = roi.ResourceOrderId
				//			LEFT OUTER JOIN ResourceOrderFills rof ON roi.ResourceOrderItemId = rof.ResourceOrderItemId
				//			LEFT OUTER JOIN ResourceOrderFillUnits rou ON rou.ResourceOrderFillId = rof.ResourceOrderFillId
				//			WHERE ro.ResourceOrderId = @id";




				//var orderResult = await db.QueryAsync<ResourceOrder, Department, ResourceOrderItem, ResourceOrderFill, ResourceOrderFillUnit, ResourceOrder>(query, (order, d, item, fill, unit) =>
				//{
				//	order.Department = d;
				//	order.Items = item.T
				//	return order;
				//}, new { id = id });



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