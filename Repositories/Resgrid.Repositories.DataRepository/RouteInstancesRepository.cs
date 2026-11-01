using System;
using System.Collections.Generic;
using System.Data.Common;
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
				  "actualendon, endedbyuserid, totaldistancemeters, totaldurationseconds, stopscompleted, stopstotal, addedon " +
				  $"FROM {schema}.routeinstances WHERE departmentid = @DepartmentId AND status = ANY(@Statuses) ORDER BY actualstarton DESC"
				: "SELECT [RouteInstanceId], [RoutePlanId], [RouteScheduleId], [UnitId], [DepartmentId], [Status], [StartedByUserId], [ScheduledStartOn], " +
				  "[ActualStartOn], [ActualEndOn], [EndedByUserId], [TotalDistanceMeters], [TotalDurationSeconds], [StopsCompleted], [StopsTotal], [AddedOn] " +
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
	}
}
