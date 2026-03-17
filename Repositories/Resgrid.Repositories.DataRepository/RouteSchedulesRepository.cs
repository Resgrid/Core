using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
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
	public class RouteSchedulesRepository : RepositoryBase<RouteSchedule>, IRouteSchedulesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public RouteSchedulesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<RouteSchedule>> GetSchedulesByRoutePlanIdAsync(string routePlanId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<RouteSchedule>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("RoutePlanId", routePlanId);

					var query = _queryFactory.GetQuery<SelectRouteSchedulesByRoutePlanIdQuery>();

					return await x.QueryAsync<RouteSchedule>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<RouteSchedule>> GetActiveSchedulesDueAsync(DateTime asOfDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<RouteSchedule>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("AsOfDate", asOfDate);

					var query = _queryFactory.GetQuery<SelectActiveSchedulesDueQuery>();

					return await x.QueryAsync<RouteSchedule>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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
