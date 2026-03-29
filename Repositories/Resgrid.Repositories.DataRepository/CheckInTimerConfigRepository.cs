using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Framework;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using Resgrid.Repositories.DataRepository.Queries.CheckIns;

namespace Resgrid.Repositories.DataRepository
{
	public class CheckInTimerConfigRepository : RepositoryBase<CheckInTimerConfig>, ICheckInTimerConfigRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CheckInTimerConfigRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CheckInTimerConfig>> GetByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CheckInTimerConfig>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectCheckInTimerConfigsByDepartmentIdQuery>();

					return await x.QueryAsync<CheckInTimerConfig>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<CheckInTimerConfig> GetByDepartmentAndTargetAsync(int departmentId, int timerTargetType, int? unitTypeId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CheckInTimerConfig>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("TimerTargetType", timerTargetType);
					dynamicParameters.Add("UnitTypeId", unitTypeId);

					var query = _queryFactory.GetQuery<SelectCheckInTimerConfigByDepartmentAndTargetQuery>();

					return await x.QueryAsync<CheckInTimerConfig>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return (await selectFunction(conn)).FirstOrDefault();
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return (await selectFunction(conn)).FirstOrDefault();
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
