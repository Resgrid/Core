using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Calendar;

namespace Resgrid.Repositories.DataRepository
{
	public class CalendarItemCheckInRepository : RepositoryBase<CalendarItemCheckIn>, ICalendarItemCheckInRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CalendarItemCheckInRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<CalendarItemCheckIn> GetCheckInByCalendarItemAndUserAsync(int calendarItemId, string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CalendarItemCheckIn>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CalendarItemId", calendarItemId);
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectCalendarItemCheckInByItemAndUserQuery>();

					return await x.QueryFirstOrDefaultAsync<CalendarItemCheckIn>(sql: query,
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

		public async Task<IEnumerable<CalendarItemCheckIn>> GetCheckInsByCalendarItemIdAsync(int calendarItemId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CalendarItemCheckIn>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CalendarItemId", calendarItemId);

					var query = _queryFactory.GetQuery<SelectCalendarItemCheckInsByItemIdQuery>();

					return await x.QueryAsync<CalendarItemCheckIn>(sql: query,
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

		public async Task<IEnumerable<CalendarItemCheckIn>> GetCheckInsByDepartmentAndDateRangeAsync(int departmentId, DateTime start, DateTime end)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CalendarItemCheckIn>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", start);
					dynamicParameters.Add("EndDate", end);

					var query = _queryFactory.GetQuery<SelectCalendarItemCheckInsByDeptDateRangeQuery>();

					return await x.QueryAsync<CalendarItemCheckIn>(sql: query,
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

		public async Task<IEnumerable<CalendarItemCheckIn>> GetCheckInsByUserAndDateRangeAsync(string userId, int departmentId, DateTime start, DateTime end)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CalendarItemCheckIn>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", start);
					dynamicParameters.Add("EndDate", end);

					var query = _queryFactory.GetQuery<SelectCalendarItemCheckInsByUserDateRangeQuery>();

					return await x.QueryAsync<CalendarItemCheckIn>(sql: query,
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
