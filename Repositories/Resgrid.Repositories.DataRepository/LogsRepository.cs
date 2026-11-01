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
using Resgrid.Repositories.DataRepository.Queries.Logs;

namespace Resgrid.Repositories.DataRepository
{
	public class LogsRepository : RepositoryBase<Log>, ILogsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public LogsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<WorkLogHeader>> GetLogHeadersSinceAsync(int departmentId, DateTime sinceUtc, int take)
		{
			if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
			var postgres = Resgrid.Config.DataConfig.DatabaseType == Resgrid.Config.DatabaseTypes.Postgres;
			// Header columns only: the narrative, contact and cause columns are never read for a count.
			string Col(string name) => postgres ? name.ToLowerInvariant() : $"[{name}]";
			var table = postgres ? $"{_sqlConfiguration.SchemaName}.{_sqlConfiguration.LogsTable.ToLowerInvariant()}" : $"{_sqlConfiguration.SchemaName}.[{_sqlConfiguration.LogsTable}]";
			var p = _sqlConfiguration.ParameterNotation;
			var sql = $"SELECT {Col("LogId")}, {Col("LogType")}, {Col("StationGroupId")}, {Col("CallId")}, {Col("StartedOn")}, {Col("LoggedOn")} FROM {table} " +
				$"WHERE {Col("DepartmentId")} = {p}DepartmentId AND {Col("LoggedOn")} >= {p}Since ORDER BY {Col("LoggedOn")} DESC, {Col("LogId")} DESC " +
				(postgres ? $"LIMIT {p}Take" : $"OFFSET 0 ROWS FETCH NEXT {p}Take ROWS ONLY");

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			// PostgreSQL stores these as timestamp without time zone; Npgsql wants an Unspecified kind for them.
			parameters.Add("Since", postgres ? DateTime.SpecifyKind(sinceUtc, DateTimeKind.Unspecified) : sinceUtc);
			parameters.Add("Take", take);

			try
			{
				if (_unitOfWork?.Connection == null)
				{
					using (var conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await conn.QueryAsync<WorkLogHeader>(sql, parameters);
					}
				}

				return await _unitOfWork.CreateOrGetConnection().QueryAsync<WorkLogHeader>(sql, parameters, _unitOfWork.Transaction);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				throw;
			}
		}

		public async Task<IEnumerable<Log>> GetLogsForUserAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Log>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectLogsByUserIdQuery>();

					return await x.QueryAsync<Log>(sql: query,
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

		public async Task<IEnumerable<Log>> GetLogsForCallAsync(int callId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Log>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);

					var query = _queryFactory.GetQuery<SelectLogsByCallIdQuery>();

					return await x.QueryAsync<Log>(sql: query,
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

		public async Task<IEnumerable<Log>> GetLogsForGroupAsync(int groupId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Log>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("GroupId", groupId);

					var query = _queryFactory.GetQuery<SelectLogsByGroupIdQuery>();

					return await x.QueryAsync<Log>(sql: query,
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

		public async Task<IEnumerable<string>> SelectLogYearsByDeptAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<string>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectLogYearsByDeptQuery>();

					return await x.QueryAsync<string>(sql: query,
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

		public async Task<IEnumerable<Log>> GetAllLogsByDepartmentIdYearAsync(int departmentId, string year)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<Log>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("Year", int.TryParse(year, out var parsedYear) ? parsedYear : DateTime.UtcNow.Year);

					var query = _queryFactory.GetQuery<SelecAllLogsByDidYearQuery>();

					return await x.QueryAsync<Log>(sql: query,
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
