using System;
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
using Resgrid.Repositories.DataRepository.Queries.ResourceOrders;

namespace Resgrid.Repositories.DataRepository
{
	public class ResourceOrderFillRepository : RepositoryBase<ResourceOrderFill>, IResourceOrderFillRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ResourceOrderFillRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<bool> UpdateFillStatusAsync(int fillId, string userId, bool accepted, CancellationToken cancellationToken)
		{
			try
			{
				var updateFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("Accepted", accepted);
					dynamicParameters.Add("AcceptedUserId", userId);
					dynamicParameters.Add("AcceptedOn", DateTime.UtcNow);
					dynamicParameters.Add("ResourceOrderFillId", fillId);

					var entity = new ResourceOrderFill();
					var query = _queryFactory.GetUpdateQuery<UpdateOrderFillStatusQuery, ResourceOrderFill>(entity);

					var result = await x.ExecuteAsync(query, dynamicParameters, _unitOfWork.Transaction);

					return result > 0;
				});

				DbConnection conn = null;
				if (_unitOfWork?.Connection == null)
				{
					using (conn = _connectionProvider.Create())
					{
						await conn.OpenAsync(cancellationToken);
						return await updateFunction(conn);
					}
				}
				else
				{
					conn = _unitOfWork.CreateOrGetConnection();
					return await updateFunction(conn);
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
