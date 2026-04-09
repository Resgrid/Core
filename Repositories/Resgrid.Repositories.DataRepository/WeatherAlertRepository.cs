using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.WeatherAlerts;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class WeatherAlertRepository : RepositoryBase<WeatherAlert>, IWeatherAlertRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public WeatherAlertRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<WeatherAlert>> GetActiveAlertsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WeatherAlert>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectActiveWeatherAlertsByDepartmentIdQuery>();

					return await x.QueryAsync<WeatherAlert>(sql: query,
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

		public async Task<WeatherAlert> GetByExternalIdAndSourceIdAsync(string externalId, Guid sourceId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<WeatherAlert>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ExternalId", externalId);
					dynamicParameters.Add("SourceId", sourceId);

					var query = _queryFactory.GetQuery<SelectWeatherAlertByExternalIdAndSourceIdQuery>();

					return await x.QueryFirstOrDefaultAsync<WeatherAlert>(sql: query,
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

		public async Task<IEnumerable<WeatherAlert>> GetAlertsByDepartmentAndSeverityAsync(int departmentId, int maxSeverity)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WeatherAlert>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("MaxSeverity", maxSeverity);

					var query = _queryFactory.GetQuery<SelectWeatherAlertsByDepartmentAndSeverityQuery>();

					return await x.QueryAsync<WeatherAlert>(sql: query,
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

		public async Task<IEnumerable<WeatherAlert>> GetAlertsByDepartmentAndCategoryAsync(int departmentId, int category)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WeatherAlert>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("Category", category);

					var query = _queryFactory.GetQuery<SelectWeatherAlertsByDepartmentAndCategoryQuery>();

					return await x.QueryAsync<WeatherAlert>(sql: query,
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

		public async Task<IEnumerable<WeatherAlert>> GetExpiredUnprocessedAlertsAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WeatherAlert>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					var query = _queryFactory.GetQuery<SelectExpiredUnprocessedWeatherAlertsQuery>();

					return await x.QueryAsync<WeatherAlert>(sql: query,
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

		public async Task<IEnumerable<WeatherAlert>> GetUnnotifiedAlertsAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WeatherAlert>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();

					var query = _queryFactory.GetQuery<SelectUnnotifiedWeatherAlertsQuery>();

					return await x.QueryAsync<WeatherAlert>(sql: query,
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

		public async Task<IEnumerable<WeatherAlert>> GetAlertHistoryByDepartmentAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<WeatherAlert>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);

					var query = _queryFactory.GetQuery<SelectWeatherAlertHistoryByDepartmentQuery>();

					return await x.QueryAsync<WeatherAlert>(sql: query,
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
