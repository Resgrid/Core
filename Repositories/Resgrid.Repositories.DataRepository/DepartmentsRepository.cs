using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Custom;
using Resgrid.Model.Repositories;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Departments;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentsRepository : RepositoryBase<Department>, IDepartmentsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<Department> GetDepartmentWithMembersByIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Department>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectDepartmentByIdQuery>();

					var dictionary = new Dictionary<int, Department>();
					var result = await x.QueryAsync<Department, DepartmentMember, Department>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DepartmentMemberMapping(dictionary),
						splitOn: "DepartmentMemberId");

					Department department = null;
					if (dictionary.Count > 0)
						department = dictionary.Select(y => y.Value).FirstOrDefault();
					else
						department = result.FirstOrDefault();


					if (department != null && department.Members != null)
					{
						department.AdminUsers = new List<string>();
						department.AdminUsers.Add(department.ManagingUserId);
						foreach (var member in department.Members)
						{
							if (member.IsAdmin.GetValueOrDefault())
								department.AdminUsers.Add(member.UserId);
						}
					}

					if (department != null && !department.Use24HourTime.HasValue)
						department.Use24HourTime = false;

					return department;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<Department> GetDepartmentWithMembersByNameAsync(string name)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Department>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Name", name);

					var query = _queryFactory.GetQuery<SelectDepartmentByNameQuery>();

					var dictionary = new Dictionary<int, Department>();
					var result = await x.QueryAsync<Department, DepartmentMember, Department>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DepartmentMemberMapping(dictionary),
						splitOn: "DepartmentMemberId");

					Department department = null;
					if (dictionary.Count > 0)
						department = dictionary.Select(y => y.Value).FirstOrDefault();
					else
						department = result.FirstOrDefault();


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
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<ValidateUserForDepartmentResult> GetValidateUserForDepartmentDataAsync(string userName)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ValidateUserForDepartmentResult>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Username", userName);

					var query = _queryFactory.GetQuery<SelectValidDepartmentByUsernameQuery>();

					return await x.QueryFirstOrDefaultAsync<ValidateUserForDepartmentResult>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<Department> GetDepartmentForUserByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Department>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectDepartmentByUserIdQuery>();

					var dictionary = new Dictionary<int, Department>();
					var result = await x.QueryAsync<Department, DepartmentMember, Department>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DepartmentMemberMapping(dictionary),
						splitOn: "DepartmentMemberId");

					Department department = null;
					if (dictionary.Count > 0)
						department = dictionary.Select(y => y.Value).FirstOrDefault();
					else
						department = result.FirstOrDefault();


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
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}


		public async Task<Department> GetDepartmentForUserByUsernameAsync(string userName)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Department>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Username", userName);

					var query = _queryFactory.GetQuery<SelectDepartmentByUsernameQuery>();

					var dictionary = new Dictionary<int, Department>();
					var result = await x.QueryAsync<Department, DepartmentMember, Department>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DepartmentMemberMapping(dictionary),
						splitOn: "DepartmentMemberId");

					Department department = null;
					if (dictionary.Count > 0)
						department = dictionary.Select(y => y.Value).FirstOrDefault();
					else
						department = result.FirstOrDefault();


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
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<DepartmentReport> GetDepartmentReportAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentReport>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectDepartmentReportByDidQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentReport>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		public async Task<Department> GetByLinkCodeAsync(string code)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Department>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Code", code);

					var query = _queryFactory.GetQuery<SelectDepartmentByLinkCodeQuery>();

					var dictionary = new Dictionary<int, Department>();
					var result = await x.QueryAsync<Department, DepartmentMember, Department>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: DepartmentMemberMapping(dictionary),
						splitOn: "DepartmentMemberId");

					Department department = null;
					if (dictionary.Count > 0)
						department = dictionary.Select(y => y.Value).FirstOrDefault();
					else
						department = result.FirstOrDefault();


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
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<DepartmentStats> GetDepartmentStatsByDepartmentUserIdAsync(int departmentId, string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentStats>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectDepartmentStatsByUserDidQuery>();

					return await x.QueryFirstOrDefaultAsync<DepartmentStats>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();

						return await selectFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await selectFunction(conn);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}

		private static Func<Department, DepartmentMember, Department> DepartmentMemberMapping(Dictionary<int, Department> dictionary)
		{
			return new Func<Department, DepartmentMember, Department>((department, departmentMember) =>
			{
				var dictionaryDepartment = default(Department);

				if (departmentMember != null)
				{
					if (dictionary.TryGetValue(department.DepartmentId, out dictionaryDepartment))
					{
						if (dictionaryDepartment.Members.All(x => x.DepartmentMemberId != departmentMember.DepartmentMemberId))
							dictionaryDepartment.Members.Add(departmentMember);
					}
					else
					{
						if (department.Members == null)
							department.Members = new List<DepartmentMember>();

						department.Members.Add(departmentMember);
						dictionary.Add(department.DepartmentId, department);

						dictionaryDepartment = department;
					}
				}
				else
				{
					department.Members = new List<DepartmentMember>();
					dictionaryDepartment = department;
					dictionary.Add(department.DepartmentId, department);
				}

				return dictionaryDepartment;
			});
		}
	}
}
