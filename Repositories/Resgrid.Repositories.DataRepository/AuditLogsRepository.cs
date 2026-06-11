using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.AuditLogs;

namespace Resgrid.Repositories.DataRepository
{
	public class AuditLogsRepository : RepositoryBase<AuditLog>, IAuditLogsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public AuditLogsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<AuditLog>> GetAuditLogsForDepartmentPagedAsync(int departmentId, DateTime startDate, DateTime endDate, int? logType, int page, int pageSize)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<AuditLog>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					// Bind the bounds as DateTime2 so out-of-SqlDateTime-range values (e.g. DateTime.MinValue
					// from an unset start filter) don't trigger "SqlDateTime overflow". datetime2 covers
					// 0001-9999 on SQL Server (vs datetime's 1753 floor) and maps to timestamp on PostgreSQL,
					// comparing correctly against the loggedon column either way.
					dynamicParameters.Add("StartDate", startDate, DbType.DateTime2);
					dynamicParameters.Add("EndDate", endDate, DbType.DateTime2);
					dynamicParameters.Add("Offset", (page - 1) * pageSize);
					dynamicParameters.Add("PageSize", pageSize);

					string query;
					if (logType.HasValue)
					{
						dynamicParameters.Add("LogType", logType.Value);
						query = _queryFactory.GetQuery<SelectAuditLogsForDepartmentByTypePagedQuery>();
					}
					else
					{
						query = _queryFactory.GetQuery<SelectAuditLogsForDepartmentPagedQuery>();
					}

					return await x.QueryAsync<AuditLog>(sql: query,
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
				Logging.LogException(ex, extraMessage: $"GetAuditLogsForDepartmentPagedAsync DepartmentId: {departmentId}");

				throw;
			}
		}
	}
}
