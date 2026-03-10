using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using Resgrid.Repositories.DataRepository.Queries.Udf;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class UdfFieldValueRepository : RepositoryBase<UdfFieldValue>, IUdfFieldValueRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UdfFieldValueRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<UdfFieldValue>> GetFieldValuesByEntityAsync(int entityType, string entityId, string definitionId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<UdfFieldValue>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("EntityType", entityType);
					dynamicParameters.Add("EntityId", entityId);
					dynamicParameters.Add("UdfDefinitionId", definitionId);

					var query = _queryFactory.GetQuery<SelectUdfFieldValuesByEntityQuery>();

					return await x.QueryAsync<UdfFieldValue>(sql: query,
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

		public async Task<IEnumerable<UdfFieldValue>> GetFieldValuesByEntitiesAsync(int entityType, IEnumerable<string> entityIds, string definitionId)
		{
			try
			{
				var idList = entityIds?.ToList() ?? new List<string>();
				if (idList.Count == 0)
					return Enumerable.Empty<UdfFieldValue>();

				var selectFunction = new Func<DbConnection, Task<IEnumerable<UdfFieldValue>>>(async x =>
				{
					var schema = _sqlConfiguration.SchemaName;
					var table = _sqlConfiguration.UdfFieldValuesTableName;

					// Build an inline SQL statement that leverages Dapper's native IN-list expansion.
					// The @EntityIds parameter is expanded by Dapper into the correct number of bind variables.
					var sql = $"SELECT * FROM {schema}.{table} WHERE EntityType = @EntityType AND EntityId IN @EntityIds AND UdfDefinitionId = @UdfDefinitionId";

					return await x.QueryAsync<UdfFieldValue>(sql: sql,
						param: new { EntityType = entityType, EntityIds = idList, UdfDefinitionId = definitionId },
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

		public async Task<bool> DeleteFieldValuesByEntityAndDefinitionAsync(int entityType, string entityId, string definitionId, CancellationToken cancellationToken)
		{
			try
			{
				var deleteFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("EntityType", entityType);
					dynamicParameters.Add("EntityId", entityId);
					dynamicParameters.Add("UdfDefinitionId", definitionId);

					var query = _queryFactory.GetDeleteQuery<DeleteUdfFieldValuesByEntityAndDefinitionQuery>();

					var result = await x.ExecuteAsync(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return result >= 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync();
						return await deleteFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await deleteFunction(conn);
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


