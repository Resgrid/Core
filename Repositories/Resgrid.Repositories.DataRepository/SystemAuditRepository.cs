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
using Resgrid.Repositories.DataRepository.Queries.SystemAudits;

namespace Resgrid.Repositories.DataRepository
{
	public class SystemAuditsRepository : RepositoryBase<SystemAudit>, ISystemAuditsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public SystemAuditsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<SystemAudit>> GetByUserIdPagedAsync(string userId, DateTime startDate, DateTime endDate, int page, int pageSize)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<SystemAudit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UserId", userId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);
					dynamicParameters.Add("Offset", Math.Max(0, (page - 1) * pageSize));
					dynamicParameters.Add("PageSize", pageSize);

					var query = _queryFactory.GetQuery<SelectSystemAuditsByUserIdPagedQuery>();

					return await x.QueryAsync<SystemAudit>(sql: query,
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
				Logging.LogException(ex, extraMessage: $"GetByUserIdPagedAsync UserId: {userId}");

				throw;
			}
		}

		public async Task<IEnumerable<SystemAudit>> GetByDepartmentIdPagedAsync(int departmentId, DateTime startDate, DateTime endDate, int page, int pageSize)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<SystemAudit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("StartDate", startDate);
					dynamicParameters.Add("EndDate", endDate);
					dynamicParameters.Add("Offset", Math.Max(0, (page - 1) * pageSize));
					dynamicParameters.Add("PageSize", pageSize);

					var query = _queryFactory.GetQuery<SelectSystemAuditsByDepartmentIdPagedQuery>();

					return await x.QueryAsync<SystemAudit>(sql: query,
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
				Logging.LogException(ex, extraMessage: $"GetByDepartmentIdPagedAsync DepartmentId: {departmentId}");

				throw;
			}
		}
	}
}
