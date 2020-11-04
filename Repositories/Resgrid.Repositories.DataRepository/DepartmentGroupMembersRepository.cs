using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.DepartmentGroups;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentGroupMembersRepository : RepositoryBase<DepartmentGroupMember>, IDepartmentGroupMembersRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentGroupMembersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<DepartmentGroupMember>> GetAllGroupMembersByGroupIdAsync(int groupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroupMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("GroupId", groupId);

					var query = _queryFactory.GetQuery<SelectGroupMembersByGroupIdQuery>();

					return await x.QueryAsync<DepartmentGroupMember>(sql: query,
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

		public async Task<IEnumerable<DepartmentGroupMember>> GetAllGroupMembersByUserAndDepartmentAsync(string userId, int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<DepartmentGroupMember>>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectGroupMembersByUserDidQuery>();

					return await x.QueryAsync<DepartmentGroupMember, DepartmentGroup, DepartmentGroupMember>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (dgm, dg) => { dgm.DepartmentGroup = dg; return dgm; },
						splitOn: "DepartmentGroupId");
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

	}
}
