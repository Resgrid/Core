using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calls;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
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

		public async Task<IEnumerable<CallDispatchUnit>> GetOpenCallUnitDispatchesForUnitAsync(int departmentId, int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CallDispatchUnit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("UnitId", unitId);
					dynamicParameters.Add("Now", DateTime.UtcNow);

					var query = _queryFactory.GetQuery<SelectOpenCallUnitDispatchesForUnitQuery>();

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

		public async Task<IEnumerable<CallDispatchUnit>> GetOpenCallUnitDispatchesForDepartmentAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CallDispatchUnit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("Now", DateTime.UtcNow);

					var query = _queryFactory.GetQuery<SelectOpenCallUnitDispatchesForDepartmentQuery>();

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

		public async Task<bool> TryReleaseAsync(int callDispatchUnitId, DateTime releasedOnUtc, string releasedByUserId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// Conditional on the row still being live, so only one of two concurrent releases changes it (and runs the
			// release's status, notice and event); the other sees zero rows.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var sql = postgres
				? $"UPDATE {_sqlConfiguration.SchemaName}.calldispatchunits SET releasedon = @ReleasedOn, releasedbyuserid = @ReleasedByUserId " +
				  "WHERE calldispatchunitid = @CallDispatchUnitId AND releasedon IS NULL"
				: $"UPDATE {_sqlConfiguration.SchemaName}.[CallDispatchUnits] SET [ReleasedOn] = @ReleasedOn, [ReleasedByUserId] = @ReleasedByUserId " +
				  "WHERE [CallDispatchUnitId] = @CallDispatchUnitId AND [ReleasedOn] IS NULL";

			var parameters = new DynamicParameters(new { CallDispatchUnitId = callDispatchUnitId, ReleasedByUserId = releasedByUserId });
			// PostgreSQL timestamp columns carry no zone (Npgsql binds an Unspecified kind); SQL Server's column is datetime2.
			if (postgres)
				parameters.Add("ReleasedOn", DateTime.SpecifyKind(releasedOnUtc, DateTimeKind.Unspecified));
			else
				parameters.Add("ReleasedOn", releasedOnUtc, DbType.DateTime2);

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction,
						cancellationToken: cancellationToken)) == 1;

				await using var connection = _connectionProvider.Create();
				await connection.OpenAsync(cancellationToken);
				return await connection.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken)) == 1;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}
	}
}
