using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlServerCe;
using System.Linq;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentSettingsRepository : RepositoryBase<DepartmentSetting>, IDepartmentSettingsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentSettingsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public DepartmentSetting GetDepartmentSettingByUserIdType(string userId, DepartmentSettingTypes type)
		{
//			if (IsSqlCe)
//			{
//				var data = db.SqlQuery<DepartmentSetting>(@"SELECT ds.* FROM DepartmentSettings ds
//																	INNER JOIN DepartmentMembers dm ON ds.DepartmentId = dm.DepartmentId
//																	WHERE dm.UserId = @UserId AND ds.SettingType = @SettingType",
//					new SqlCeParameter("@UserId", userId),
//					new SqlCeParameter("@SettingType", (int)type));

//				return data.FirstOrDefault();
//			}
//			else
//			{
				var data = db.SqlQuery<DepartmentSetting>(@"SELECT ds.* FROM DepartmentSettings ds
																	INNER JOIN DepartmentMembers dm ON ds.DepartmentId = dm.DepartmentId
																	WHERE dm.UserId = @UserId AND ds.SettingType = @SettingType",
					new SqlParameter("@UserId", userId),
					new SqlParameter("@SettingType", (int) type));

				return data.FirstOrDefault();
			//}
		}

		public DepartmentSetting GetDepartmentSettingByIdType(int departmentId, DepartmentSettingTypes type)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<DepartmentSetting>(@"SELECT ds.* FROM DepartmentSettings ds
													WHERE ds.DepartmentId = @departmentId AND ds.SettingType = @settingType", 
						new { departmentId = departmentId, settingType = type }).FirstOrDefault();
			}
		}

		public DepartmentSetting GetDepartmentSettingBySettingType(string setting, DepartmentSettingTypes type)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<DepartmentSetting>(@"SELECT ds.* FROM DepartmentSettings ds
													WHERE ds.Setting = @setting AND ds.SettingType = @settingType",
						new { setting = setting, settingType = type }).FirstOrDefault();
			}
		}

		public List<DepartmentManagerInfo> GetAllDepartmentManagerInfo()
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<DepartmentManagerInfo>(@"SELECT d.DepartmentId, d.Name, up.FirstName, up.LastName, u.Email FROM Departments d
													 INNER JOIN AspNetUsers u ON u.Id = d.ManagingUserId
													 LEFT OUTER JOIN UserProfiles up ON up.UserId = d.ManagingUserId").ToList();
			}
		}

		public DepartmentManagerInfo GetDepartmentManagerInfoByEmail(string emailAddress)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<DepartmentManagerInfo>(@"SELECT d.DepartmentId, d.Name, up.FirstName, up.LastName, u.Email FROM Departments d
													 INNER JOIN AspNetUsers u ON u.Id = d.ManagingUserId
													 LEFT OUTER JOIN UserProfiles up ON up.UserId = d.ManagingUserId
													 WHERE u.Email = @emailAddress",
						new { emailAddress = emailAddress}).FirstOrDefault();
			}
		}
	}
}
