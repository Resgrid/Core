using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calls;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class CallDispatchUnitRepository : RepositoryBase<CallDispatchUnit>, ICallDispatchUnitRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CallDispatchUnitRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CallDispatchUnit>> GetCallUnitDispatchesByCallIdAsync(int callId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CallDispatchUnit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);

					var query = _queryFactory.GetQuery<SelectAllCallUnitDispsByCallIdQuery>();

					return await x.QueryAsync<CallDispatchUnit>(sql: query,
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

		public async Task<IEnumerable<int>> GetOpenCallIdsForUnitAsync(int departmentId, int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<int>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectOpenCallIdsForUnitQuery>();

					return await x.QueryAsync<int>(sql: query,
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

		public async Task<IEnumerable<CallDispatchUnit>> GetCallUnitDispatchesForCallsInRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CallDispatchUnit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);

					var query = _queryFactory.GetQuery<SelectCallUnitDispatchesForCallsInRangeQuery>();

					return await x.QueryAsync<CallDispatchUnit>(sql: query,
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

		public async Task<IEnumerable<CallDispatchWindow>> GetUnitDispatchWindowsAsync(int departmentId, DateTime startDate, DateTime endDate, DateTime loggedFrom)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CallDispatchWindow>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);
					dynamicParameters.Add("LoggedFrom", loggedFrom);

					var query = _queryFactory.GetQuery<SelectUnitDispatchWindowsQuery>();

					return await x.QueryAsync<CallDispatchWindow>(sql: query,
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
