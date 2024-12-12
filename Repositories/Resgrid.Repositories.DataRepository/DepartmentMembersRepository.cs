using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.DepartmentMembers;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentMembersRepository : RepositoryBase<DepartmentMember>, IDepartmentMembersRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentMembersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<DepartmentMember>> GetAllDepartmentMembersWithinLimitsAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectMembersWithinLimitsQuery>();

					return await x.QueryAsync<DepartmentMember, IdentityUser, DepartmentMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<IEnumerable<DepartmentMember>> GetAllDepartmentMembersUnlimitedAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectMembersUnlimitedQuery>();

					return await x.QueryAsync<DepartmentMember, IdentityUser, DepartmentMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<IEnumerable<DepartmentMember>> GetAllDepartmentMembersUnlimitedIncDelAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectMembersUnlimitedInclDelQuery>();

					return await x.QueryAsync<DepartmentMember, IdentityUser, DepartmentMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<DepartmentMember> GetDepartmentMemberByDepartmentIdAndUserIdAsync(int departmentId, string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentMember>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectMembersByDidUserIdQuery>();

					var result = await x.QueryAsync<DepartmentMember, IdentityUser, DepartmentMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);

					return result.FirstOrDefault();
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

		public async Task<IEnumerable<DepartmentMember>> GetAllDepartmentMemberByUserIdAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectMembersByUserIdQuery>();

					return await x.QueryAsync<DepartmentMember, Department, DepartmentMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (dm, d) => { dm.Department = d; return dm; },
						splitOn: "DepartmentId");
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

		//public List<UserProfileMaintenance> GetAllMissingUserProfiles()
		//{
		//	//var data = db.SqlQuery<UserProfileMaintenance>(@"SELECT d.UserId, d.DepartmentId FROM DepartmentMembers d
		//	//											INNER JOIN Profiles p on d.UserId = p.UserId
		//	//											WHERE d.UserId NOT IN (SELECT up.UserId FROM UserProfiles up INNER JOIN DepartmentMembers dm ON up.UserId = dm.UserId WHERE dm.DepartmentId = d.DepartmentId)");

		//	//return data.ToList();

		//	return null;
		//}

		//public List<UserProfileMaintenance> GetAllUserProfilesWithEmptyNames()
		//{
		//	//var data = db.SqlQuery<UserProfileMaintenance>(@"SELECT d.UserId, d.DepartmentId
		//	//												FROM DepartmentMembers d
		//	//												INNER JOIN UserProfiles up on d.UserId = up.UserId
		//	//												WHERE up.FirstName IS NULL OR up.FirstName = ''");

		//	//return data.ToList();

		//	return null;
		//}
	}
}
