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
using Resgrid.Repositories.DataRepository.Queries.Units;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitsRepository : RepositoryBase<Unit>, IUnitsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<Unit> GetUnitByNameDepartmentIdAsync(int departmentId, string name)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<Unit>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("UnitName", name);

					var query = _queryFactory.GetQuery<SelectUnitByDIdNameQuery>();

					return await x.QueryFirstOrDefaultAsync<Unit>(sql: query,
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

		public async Task<IEnumerable<Unit>> GetAllUnitsByGroupIdAsync(int groupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Unit>>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("GroupId", groupId);

					var query = _queryFactory.GetQuery<SelectUnitsByGroupIdQuery>();

					return await x.QueryAsync<Unit>(sql: query,
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

		public async Task<IEnumerable<Unit>> GetAllUnitsForTypeAsync(int departmentId, string type)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Unit>>>(async x =>
				{
					var dynamicParameters = new DynamicParameters();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("Type", type);

					var query = _queryFactory.GetQuery<SelectUnitByDIdTypeQuery>();

					return await x.QueryAsync<Unit>(sql: query,
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
	}
}
