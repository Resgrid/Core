using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using Dapper;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentsRepository : RepositoryBase<Department>, IDepartmentsRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public DepartmentsRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public Department GetDepartmentById(int departmentId)
		{
			using (IDbConnection db = new SqlConnection(connectionString))
			{
				return db.Query<Department>($"SELECT * FROM Departments WHERE DepartmentId = @departmentId", new { departmentId = departmentId }).FirstOrDefault();
			}
		}

		public Department GetDepartmentWithMembersById(int departmentId)
		{
			Department department;
			Dictionary<int, Department> lookup = new Dictionary<int, Department>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT d.*, dm.*
							FROM Departments d
							INNER JOIN DepartmentMembers dm ON dm.DepartmentId = d.DepartmentId
							WHERE d.DepartmentId = @departmentId AND dm.IsDeleted = 0";

				//var multi = await db.QueryMultipleAsync(query, new { departmentId = departmentId });
				//var department = (await multi.ReadAsync<Department>()).FirstOrDefault();

				//if (department != null)
				//	department.Members = (await multi.ReadAsync<DepartmentMember>()).ToList();

				department = db.Query<Department, DepartmentMember, Department>(query, (possibleDupeDepartment, dm) =>
				{
					Department dep;

					if (!lookup.TryGetValue(possibleDupeDepartment.DepartmentId, out dep))
					{
						lookup.Add(possibleDupeDepartment.DepartmentId, possibleDupeDepartment);
						dep = possibleDupeDepartment;
					}

					if (dep.Members == null)
						dep.Members = new List<DepartmentMember>();

					if (!dep.Members.Contains(dm))
					{
						dep.Members.Add(dm);
						dm.Department = dep;
					}

					return dep;

				}, new { departmentId = departmentId }, splitOn: "DepartmentId").FirstOrDefault();
			}

			if (department != null && department.Members != null)
			{
				department.AdminUsers = new List<string>();
				foreach (var member in department.Members)
				{
					if (member.IsAdmin.GetValueOrDefault())
						department.AdminUsers.Add(member.UserId);
				}
			}

			if (department != null && !department.Use24HourTime.HasValue)
				department.Use24HourTime = false;

			return department;
		}

		public Department GetDepartmentWithMembersByUserId(string userId)
		{
			Department department;
			Dictionary<int, Department> lookup = new Dictionary<int, Department>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT d.*, dm.*
							FROM AspNetUsers u
							INNER JOIN DepartmentMembers dm1 ON dm1.UserId = u.Id
							INNER JOIN Departments d ON d.DepartmentId = dm1.DepartmentId
							INNER JOIN DepartmentMembers dm ON dm.DepartmentId = d.DepartmentId
							WHERE u.Id = @userId AND d.DepartmentId = dm.DepartmentId AND dm.IsDeleted = 0";

				//var multi = await db.QueryMultipleAsync(query, new { userId = userId });
				//var department = (await multi.ReadAsync<Department>()).FirstOrDefault();

				//if (department != null)
				//	department.Members = (await multi.ReadAsync<DepartmentMember>()).ToList();

				department = db.Query<Department, DepartmentMember, Department>(query, (possibleDupeDepartment, dm) =>
				{
					Department dep;

					if (!lookup.TryGetValue(possibleDupeDepartment.DepartmentId, out dep))
					{
						lookup.Add(possibleDupeDepartment.DepartmentId, possibleDupeDepartment);
						dep = possibleDupeDepartment;
					}

					if (dep.Members == null)
						dep.Members = new List<DepartmentMember>();

					if (!dep.Members.Contains(dm))
					{
						dep.Members.Add(dm);
						dm.Department = dep;
					}

					return dep;

				}, new { userId = userId }, splitOn: "DepartmentId").FirstOrDefault();
			}

			if (department != null && department.Members != null)
			{
				department.AdminUsers = new List<string>();
				foreach (var member in department.Members)
				{
					if (member.IsAdmin.GetValueOrDefault())
						department.AdminUsers.Add(member.UserId);
				}
			}

			if (department != null && !department.Use24HourTime.HasValue)
				department.Use24HourTime = false;

			return department;
		}

		public ValidateUserForDepartmentResult GetValidateUserForDepartmentData(string userName)
		{
			var data = db.SqlQuery<ValidateUserForDepartmentResult>(@"SELECT dm.UserId as 'UserId', dm.IsDisabled as 'IsDisabled', dm.IsDeleted as 'IsDeleted', d.DepartmentId as 'DepartmentId', d.Code as 'Code' 
																	FROM AspNetUsers u
																	INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
																	INNER JOIN Departments d ON dm.DepartmentId = d.DepartmentId
																	WHERE u.UserName = @userName AND dm.IsActive = 1",
												new SqlParameter("@userName", userName));

			return data.FirstOrDefault();
		}

		//public Department GetDepartmentForUserByUsername(string userName)
		//{
		//	var data = db.SqlQuery<Department>(@"SELECT TOP 1 d.*
		//															FROM AspNetUsers u
		//															INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
		//															INNER JOIN Departments d ON dm.DepartmentId = d.DepartmentId
		//															WHERE u.UserName = @userName AND dm.IsActive = 1",
		//										new SqlParameter("@userName", userName));

		//	return data.FirstOrDefault();
		//}

		public Department GetDepartmentForUserByUsername(string userName)
		{
			Department department;
			Dictionary<int, Department> lookup = new Dictionary<int, Department>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT d.*, dm.*
							FROM AspNetUsers u
							INNER JOIN DepartmentMembers dm ON dm.UserId = u.Id
							INNER JOIN Departments d ON d.DepartmentId = dm.DepartmentId
							WHERE u.UserName = @userName AND dm.IsDeleted = 0 AND (dm.IsActive = 1 OR dm.IsDefault = 1)";

				//var multi = (await db.QueryMultipleAsync(query, new { userName = userName }));
				//var department = multi.Read<Department>().FirstOrDefault();

				//if (department != null)
				//	department.Members = multi.Read<DepartmentMember>().ToList();

				var multi = db.QueryMultiple(query, new { userName = userName });
				var departments = multi.Read<Department, DepartmentMember, Department>((possibleDupeDepartment, dm) =>
				{
					Department dep;

					if (!lookup.TryGetValue(possibleDupeDepartment.DepartmentId, out dep))
					{
						lookup.Add(possibleDupeDepartment.DepartmentId, possibleDupeDepartment);
						dep = possibleDupeDepartment;
					}

					if (dep.Members == null)
						dep.Members = new List<DepartmentMember>();

					if (!dep.Members.Contains(dm))
					{
						dep.Members.Add(dm);
						dm.Department = dep;
					}

					return dep;

				}, splitOn: "DepartmentId").ToList();

				department = departments.FirstOrDefault(x => x.Members.Any(y => y.IsActive));

				if (department == null)
					department = departments.FirstOrDefault(x => x.Members.Any(y => y.IsDefault));
			}

			if (department != null && department.Members != null)
			{
				department.AdminUsers = new List<string>();
				foreach (var member in department.Members)
				{
					if (member.IsAdmin.GetValueOrDefault())
						department.AdminUsers.Add(member.UserId);
				}
			}

			if (department != null && !department.Use24HourTime.HasValue)
				department.Use24HourTime = false;

			return department;
		}

		public async Task<Department> GetDepartmentForUserByUsernameAsync(string userName)
		{
			Department department;
			Dictionary<int, Department> lookup = new Dictionary<int, Department>();

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				var query = @"SELECT d.*, dm.*
							FROM AspNetUsers u
							INNER JOIN DepartmentMembers dm1 ON dm1.UserId = u.Id
							INNER JOIN Departments d ON d.DepartmentId = dm1.DepartmentId
							INNER JOIN DepartmentMembers dm ON dm.DepartmentId = d.DepartmentId
							WHERE u.UserName = @userName AND d.DepartmentId = dm.DepartmentId AND dm.IsDeleted = 0 AND (dm.IsActive = 1 OR dm.IsDefault = 1)";

				//var multi = (await db.QueryMultipleAsync(query, new { userName = userName }));
				//var department = multi.Read<Department>().FirstOrDefault();

				//if (department != null)
				//	department.Members = multi.Read<DepartmentMember>().ToList();

				var multi = await db.QueryMultipleAsync(query, new { userName = userName });
				var departments = multi.Read<Department, DepartmentMember, Department>((possibleDupeDepartment, dm) =>
				{
					Department dep;

					if (!lookup.TryGetValue(possibleDupeDepartment.DepartmentId, out dep))
					{
						lookup.Add(possibleDupeDepartment.DepartmentId, possibleDupeDepartment);
						dep = possibleDupeDepartment;
					}

					if (dep.Members == null)
						dep.Members = new List<DepartmentMember>();

					if (!dep.Members.Contains(dm))
					{
						dep.Members.Add(dm);
						dm.Department = dep;
					}

					return dep;

				}, splitOn: "DepartmentId").ToList();

				department = departments.FirstOrDefault(x => x.Members.Any(y => y.IsActive));

				if (department == null)
					department = departments.FirstOrDefault(x => x.Members.Any(y => y.IsDefault));
			}

			if (department != null && department.Members != null)
			{
				department.AdminUsers = new List<string>();
				foreach (var member in department.Members)
				{
					if (member.IsAdmin.GetValueOrDefault())
						department.AdminUsers.Add(member.UserId);
				}
			}

			if (department != null && !department.Use24HourTime.HasValue)
				department.Use24HourTime = false;

			return department;
		}

		public List<PersonName> GetAllPersonnelNamesForDepartment(int departmentId)
		{
			var data = db.SqlQuery<PersonName>(@"
											DECLARE @profiles TABLE 
											( 
													UserId VARCHAR(MAX), 
													Name VARCHAR(MAX),
													FirstName VARCHAR(MAX),
													LastName VARCHAR(MAX)
											)

											INSERT INTO @profiles
											SELECT dm.UserId, up.FirstName + ' ' + up.LastName as 'Name', up.FirstName, up.LastName
											FROM UserProfiles up
											RIGHT OUTER JOIN DepartmentMembers dm ON dm.UserId = up.UserId
											WHERE dm.DepartmentId = @departmentId AND dm.IsDeleted = 0

											SELECT * FROM @profiles",
						new SqlParameter("@departmentId", departmentId));

			return data.ToList();
		}

		public DepartmentReport GetDepartmentReport(int departmentId)
		{
			var data = db.SqlQuery<DepartmentReport>(@"SELECT 
							d.DepartmentId,
							d.Name,
							d.CreatedOn,
							(SELECT COUNT (*) FROM DepartmentGroups dg WHERE dg.DepartmentId = d.DepartmentId) AS 'Groups',
							(SELECT COUNT (*) -1 FROM DepartmentMembers dm WHERE dm.DepartmentId = d.DepartmentId) AS 'Users',
							(SELECT COUNT (*) FROM Units u WHERE u.DepartmentId = d.DepartmentId) AS 'Units',
							(SELECT COUNT (*) FROM Calls c WHERE c.DepartmentId = d.DepartmentId) AS 'Calls',
							(SELECT COUNT (*) FROM PersonnelRoles pr WHERE pr.DepartmentId = d.DepartmentId) AS 'Roles',
							(SELECT COUNT (*) FROM DepartmentNotifications dn WHERE dn.DepartmentId = d.DepartmentId) AS 'Notifications',
							(SELECT COUNT (*) FROM UnitTypes ut WHERE ut.DepartmentId = d.DepartmentId) AS 'UnitTypes',
							(SELECT COUNT (*) FROM CallTypes ct WHERE ct.DepartmentId = d.DepartmentId) AS 'CallTypes',
							(SELECT COUNT (*) FROM DepartmentCertificationTypes dct WHERE dct.DepartmentId = d.DepartmentId) AS 'CertTypes',
								CASE 
									WHEN d.TimeZone IS NOT NULL OR d.Use24HourTime IS NOT NULL OR d.AddressId IS NOT NULL
										THEN 1 
									ELSE 0 
										END AS 'Settings'
						FROM Departments d
						WHERE d.DepartmentId = @departmentId",
				new SqlParameter("@departmentId", departmentId));

			return data.FirstOrDefault();
		}
	}
}
