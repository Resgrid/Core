using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Framework;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using Resgrid.Repositories.DataRepository.Queries.Routes;

namespace Resgrid.Repositories.DataRepository
{
	public class RouteInstancesRepository : RepositoryBase<RouteInstance>, IRouteInstancesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public RouteInstancesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<RouteInstance>> GetInstancesByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<RouteInstance>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectRouteInstancesByDepartmentIdQuery>();

					return await x.QueryAsync<RouteInstance>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<RouteInstance>> GetActiveInstancesByUnitIdAsync(int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<RouteInstance>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectActiveRouteInstancesByUnitIdQuery>();

					return await x.QueryAsync<RouteInstance>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<RouteInstance>> GetInstancesByRoutePlanIdAsync(string routePlanId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<RouteInstance>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("RoutePlanId", routePlanId);

					var query = _queryFactory.GetQuery<SelectRouteInstancesByRoutePlanIdQuery>();

					return await x.QueryAsync<RouteInstance>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<RouteInstance>> GetInstancesByDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<RouteInstance>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);

					var query = _queryFactory.GetQuery<SelectRouteInstancesByDateRangeQuery>();

					return await x.QueryAsync<RouteInstance>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<RouteInstance>> GetActiveInstancesByDepartmentIdAsync(int departmentId)
		{
			// In progress (1) and paused (5) only, without the recorded route geometry and notes: a summary read for the home
			// dashboard, where loading every instance the department ever ran (GetInstancesByDepartmentIdAsync) would not scale.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var schema = _sqlConfiguration.SchemaName;
			var sql = postgres
				? "SELECT routeinstanceid, routeplanid, routescheduleid, unitid, departmentid, status, startedbyuserid, scheduledstarton, actualstarton, " +
				  "actualendon, endedbyuserid, totaldistancemeters, totaldurationseconds, stopscompleted, stopstotal, addedon, callid, startreachedon " +
				  $"FROM {schema}.routeinstances WHERE departmentid = @DepartmentId AND status = ANY(@Statuses) ORDER BY actualstarton DESC"
				: "SELECT [RouteInstanceId], [RoutePlanId], [RouteScheduleId], [UnitId], [DepartmentId], [Status], [StartedByUserId], [ScheduledStartOn], " +
				  "[ActualStartOn], [ActualEndOn], [EndedByUserId], [TotalDistanceMeters], [TotalDurationSeconds], [StopsCompleted], [StopsTotal], [AddedOn], [CallId], [StartReachedOn] " +
				  $"FROM {schema}.[RouteInstances] WHERE [DepartmentId] = @DepartmentId AND [Status] IN @Statuses ORDER BY [ActualStartOn] DESC";

			var parameters = new { DepartmentId = departmentId, Statuses = new[] { (int)RouteInstanceStatus.InProgress, (int)RouteInstanceStatus.Paused } };

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().QueryAsync<RouteInstance>(sql, parameters, _unitOfWork.Transaction);

				using (var conn = _connectionProvider.Create())
				{
					await conn.OpenAsync();
					return await conn.QueryAsync<RouteInstance>(sql, parameters);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<RouteInstance>> GetInstancesByCallIdAsync(int departmentId, int callId)
		{
			// Every run started for the call (M0322), newest first; the filtered IX_RouteInstances_CallId index serves it.
			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var schema = _sqlConfiguration.SchemaName;
			var sql = postgres
				? $"SELECT * FROM {schema}.routeinstances WHERE callid = @CallId AND departmentid = @DepartmentId ORDER BY addedon DESC"
				: $"SELECT * FROM {schema}.[RouteInstances] WHERE [CallId] = @CallId AND [DepartmentId] = @DepartmentId ORDER BY [AddedOn] DESC";

			var parameters = new { DepartmentId = departmentId, CallId = callId };

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().QueryAsync<RouteInstance>(sql, parameters, _unitOfWork.Transaction);

				using (var conn = _connectionProvider.Create())
				{
					await conn.OpenAsync();
					return await conn.QueryAsync<RouteInstance>(sql, parameters);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		/// <summary>How long a route start waits for another start of the same unit to finish.</summary>
		private const int RouteStartLockTimeoutMs = 10000;

		/// <summary>The first key of the PostgreSQL two-key advisory lock for route starts ("RTST"); the second is the unit id.</summary>
		private const int RouteStartLockSpace = 0x52545354;

		public async Task LockUnitForRouteStartAsync(int unitId, CancellationToken cancellationToken = default)
		{
			if (_unitOfWork?.Transaction == null)
				throw new InvalidOperationException("Locking a unit for a route start needs the unit of work's transaction.");

			var connection = _unitOfWork.Connection;
			var transaction = _unitOfWork.Transaction;

			try
			{
				if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					// Transaction-scoped: released when the start commits or rolls back. Polled, so a stuck start cannot hold
					// another one forever.
					var waitedMs = 0;
					while (true)
					{
						if (await connection.ExecuteScalarAsync<bool>(new Dapper.CommandDefinition("SELECT pg_try_advisory_xact_lock(@Space, @UnitId)",
								new { Space = RouteStartLockSpace, UnitId = unitId }, transaction, cancellationToken: cancellationToken)))
							return;

						if (waitedMs >= RouteStartLockTimeoutMs)
							throw new TimeoutException($"Another route start for unit {unitId} is still running.");

						await Task.Delay(250, cancellationToken);
						waitedMs += 250;
					}
				}

				// sp_getapplock answers 0 or 1 when granted and a negative code on timeout, cancellation or deadlock.
				var result = await connection.ExecuteScalarAsync<int>(new Dapper.CommandDefinition(
					"DECLARE @Result int; EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @TimeoutMs; SELECT @Result;",
					new { Resource = "Resgrid.RouteStart.Unit." + unitId.ToString(System.Globalization.CultureInfo.InvariantCulture), TimeoutMs = RouteStartLockTimeoutMs },
					transaction, cancellationToken: cancellationToken));
				if (result < 0)
					throw new TimeoutException($"Another route start for unit {unitId} is still running.");
			}
			catch (Exception ex) when (ex is not TimeoutException && ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"Locking unit {unitId} for a route start");
				throw;
			}
		}

		public async Task<bool> SetStartReachedOnAsync(string routeInstanceId, DateTime reachedOn, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(routeInstanceId))
				return false;

			var postgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
			var schema = _sqlConfiguration.SchemaName;
			var sql = postgres
				? $"UPDATE {schema}.routeinstances SET startreachedon = @ReachedOn WHERE routeinstanceid = @RouteInstanceId AND startreachedon IS NULL"
				: $"UPDATE {schema}.[RouteInstances] SET [StartReachedOn] = @ReachedOn WHERE [RouteInstanceId] = @RouteInstanceId AND [StartReachedOn] IS NULL";

			var parameters = new { RouteInstanceId = routeInstanceId, ReachedOn = reachedOn };

			try
			{
				if (_unitOfWork?.Connection != null)
					return await _unitOfWork.CreateOrGetConnection().ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, _unitOfWork.Transaction,
						cancellationToken: cancellationToken)) == 1;

				using (var conn = _connectionProvider.Create())
				{
					await conn.OpenAsync(cancellationToken);
					return await conn.ExecuteAsync(new Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken)) == 1;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recording the start reached of route instance {routeInstanceId}");
				throw;
			}
		}
	}
}
