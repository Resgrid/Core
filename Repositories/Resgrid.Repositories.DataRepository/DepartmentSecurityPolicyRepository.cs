using System;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Sso;

namespace Resgrid.Repositories.DataRepository
{
	public class DepartmentSecurityPolicyRepository : RepositoryBase<DepartmentSecurityPolicy>, IDepartmentSecurityPolicyRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public DepartmentSecurityPolicyRepository(
			IConnectionProvider connectionProvider,
			SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork,
			IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<DepartmentSecurityPolicy> GetByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<DepartmentSecurityPolicy>>(async x =>
				{
					var dp = new DynamicParametersExtension();
					dp.Add("DepartmentId", departmentId);
					var query = _queryFactory.GetQuery<SelectSecurityPolicyByDepartmentIdQuery>();
					return await x.QueryFirstOrDefaultAsync<DepartmentSecurityPolicy>(sql: query, param: dp, transaction: _unitOfWork?.Transaction);
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

				conn = _unitOfWork.CreateOrGetConnection();
				return await selectFunction(conn);
			}
			catch (Exception ex) { Logging.LogException(ex); throw; }
		}
	}
}

