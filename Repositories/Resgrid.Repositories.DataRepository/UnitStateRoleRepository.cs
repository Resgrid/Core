using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Units;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitStateRoleRepository : RepositoryBase<UnitStateRole>, IUnitStateRoleRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitStateRoleRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<UnitStateRole>> GetCurrentRolesForUnitAsync(int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitStateRole>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectCurrentRolesByUnitIdQuery>();

					return await x.QueryAsync<UnitStateRole>(sql: query,
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

		public async Task<IEnumerable<UnitStateRole>> GetRolesForUnitStatesAsync(IReadOnlyCollection<int> unitStateIds)
		{
			var ids = (unitStateIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToArray();
			if (ids.Length == 0)
				return Enumerable.Empty<UnitStateRole>();

			try
			{
				var isPostgres = DataConfig.DatabaseType == DatabaseTypes.Postgres;
				// Postgres does not expand IN @p; = ANY(@p) binds the array (see the Dapper IN-list note in the repo docs).
				var query = isPostgres
					? $"SELECT * FROM {_sqlConfiguration.SchemaName}.{_sqlConfiguration.UnitStateRolesTable} WHERE UnitStateId = ANY(@Ids)"
					: $"SELECT * FROM {_sqlConfiguration.SchemaName}.[{_sqlConfiguration.UnitStateRolesTable}] WHERE [UnitStateId] IN @Ids";

				var result = new List<UnitStateRole>();
				foreach (var batch in ids.Chunk(1000))
				{
					var selectFunction = new Func<DbConnection, Task<IEnumerable<UnitStateRole>>>(async x =>
						await x.QueryAsync<UnitStateRole>(sql: query, param: new { Ids = batch }, transaction: _unitOfWork.Transaction));

					if (_unitOfWork?.Connection == null)
					{
						using (var conn = _connectionProvider.Create())
						{
							await conn.OpenAsync();
							result.AddRange(await selectFunction(conn));
						}
					}
					else
					{
						result.AddRange(await selectFunction(_unitOfWork.CreateOrGetConnection()));
					}
				}

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw;
			}
		}
	}
}
