using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.ActionLogs;

namespace Resgrid.Repositories.DataRepository
{
	public class ActionLogsRepository : RepositoryBase<ActionLog>, IActionLogsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ActionLogsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
		: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ActionLog>> GetLastActionLogsForDepartmentAsync(int departmentId, bool disableAutoAvailable, DateTime timeStamp)
		{
			try
			{
				var latestTimestamp = DateTime.UtcNow.AddYears(-1);

				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("DisableAutoAvailable", disableAutoAvailable);
					dynamicParameters.Add("Timestamp", timeStamp);
					dynamicParameters.Add("LatestTimestamp", latestTimestamp);

					var query = _queryFactory.GetQuery<SelectLastActionLogsForDepartmentQuery>();

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<IEnumerable<ActionLog>> GetAllActionLogsForUser(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectActionLogsByUserIdQuery>();

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<IEnumerable<ActionLog>> GetAllActionLogsForUserInDateRangeAsync(string userId, DateTime startDate, DateTime endDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);

					var query = _queryFactory.GetQuery<SelectALogsByUserInDateRangQuery>();

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

				return null;
			}
		}

		public async Task<IEnumerable<ActionLog>> GetAllActionLogsInDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);

					var query = _queryFactory.GetQuery<SelectALogsByDateRangeQuery>();

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

				return null;
			}
		}

		public async Task<IEnumerable<ActionLog>> GetAllActionLogsForDepartmentAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectALogsByDidQuery>();

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

				return null;
			}
		}

		public async Task<ActionLog> GetLastActionLogsForUserAsync(string userId, bool disableAutoAvailable, DateTime timeStamp)
		{
			try
			{
				var latestTimestamp = DateTime.UtcNow.AddYears(-1);

				var selectFunction = new Func<DbConnection, Task<ActionLog>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("DisableAutoAvailable", disableAutoAvailable);
					dynamicParameters.Add("Timestamp", timeStamp);
					dynamicParameters.Add("LatestTimestamp", latestTimestamp);

					var query = _queryFactory.GetQuery<SelectLastActionLogForUserQuery>();

					var result = await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);

					return result.FirstOrDefault();
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

		public async Task<IEnumerable<ActionLog>> GetActionLogsForCallAsync(int callId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", callId);

					var query = _queryFactory.GetQuery<SelectActionLogsByCallIdQuery>();

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<IEnumerable<ActionLog>> GetActionLogsForCallAndTypesAsync(int destinationId, List<int> types)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ActionLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CallId", destinationId);

					var usersToQuery = String.Join(",", types.Select(p => $"{p.ToString()}").ToArray());
					//dynamicParameters.Add("Types", usersToQuery);

					var query = _queryFactory.GetQuery<SelectActionLogsByCallIdTypeQuery>();
					query = query.Replace("%TYPES%", usersToQuery, StringComparison.InvariantCultureIgnoreCase);

					return await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);
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

		public async Task<ActionLog> GetPreviousActionLogAsync(string userId, int actionLogId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ActionLog>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("ActionLogId", actionLogId);

					var query = _queryFactory.GetQuery<SelectPerviousActionLogsByUserQuery>();

					var items = await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);

					return items.FirstOrDefault();
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

		public async Task<ActionLog> GetLastActionLogForUserAsync(string userId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<ActionLog>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);

					var query = _queryFactory.GetQuery<SelectLastActionLogByUserIdQuery>();

					var items = await x.QueryAsync<ActionLog, IdentityUser, ActionLog>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (up, u) => { up.User = u; return up; }/*,
						splitOn: "Id"*/);

					return items.FirstOrDefault();
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
