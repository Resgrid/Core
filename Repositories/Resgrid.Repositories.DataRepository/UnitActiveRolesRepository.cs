using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Units;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitActiveRolesRepository : RepositoryBase<UnitActiveRole>, IUnitActiveRolesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitActiveRolesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<UnitActiveRole>> GetActiveRolesByUnitIdAsync(int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitActiveRole>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectUnitActiveRolesByUnitIdQuery>();

					return await x.QueryAsync<UnitActiveRole>(sql: query,
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

		public async Task<IEnumerable<UnitActiveRole>> GetAllActiveRolesForUnitsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitActiveRole>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectActiveRolesForUnitsByDidQuery>();

					return await x.QueryAsync<UnitActiveRole>(sql: query,
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

		public async Task<bool> DeleteActiveRolesByUnitIdAsync(int unitId, CancellationToken cancellationToken)
		{
			try
			{
				var removeFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					try
					{
						var dynamicParameters = new DynamicParametersExtension();
						dynamicParameters.Add("UnitId", unitId);

						var query = _queryFactory.GetDeleteQuery<DeleteUnitActiveRolesByUnitIdQuery>();
						query = query.Replace("%UNITID%", unitId.ToString(), StringComparison.InvariantCultureIgnoreCase);

						var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

						return result > 0;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);

						throw;
					}
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);

						return await removeFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();

					return await removeFunction(conn);
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
