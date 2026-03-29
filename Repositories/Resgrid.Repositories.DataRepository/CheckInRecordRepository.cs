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
	public class CheckInRecordRepository : RepositoryBase<CheckInRecord>, ICheckInRecordRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CheckInRecordRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CheckInRecord>> GetByCallIdAsync(int callId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CheckInRecord>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);

					var query = _queryFactory.GetQuery<SelectCheckInRecordsByCallIdQuery>();

					return await x.QueryAsync<CheckInRecord>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<CheckInRecord> GetLastCheckInForUserOnCallAsync(int callId, string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CheckInRecord>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectLastCheckInForUserOnCallQuery>();

					return await x.QueryAsync<CheckInRecord>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<CheckInRecord> GetLastCheckInForUnitOnCallAsync(int callId, int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CheckInRecord>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectLastCheckInForUnitOnCallQuery>();

					return await x.QueryAsync<CheckInRecord>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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

		public async Task<IEnumerable<CheckInRecord>> GetByDepartmentIdAndDateRangeAsync(int departmentId, DateTime start, DateTime end)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CheckInRecord>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", start);
					dynamicParameters.Add("EndDate", end);

					var query = _queryFactory.GetQuery<SelectCheckInRecordsByDepartmentIdAndDateRangeQuery>();

					return await x.QueryAsync<CheckInRecord>(sql: query, param: dynamicParameters, transaction: _unitOfWork.Transaction);
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
