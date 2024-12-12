using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.ResourceOrders;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class ResourceOrderFillUnitRepository : RepositoryBase<ResourceOrderFillUnit>, IResourceOrderFillUnitRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public ResourceOrderFillUnitRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<ResourceOrderFillUnit>> GetAllResourceOrderFillUnitsByFillIdAsync(int resourceOrderFillId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<ResourceOrderFillUnit>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("ResourceOrderFillId", resourceOrderFillId);

					var query = _queryFactory.GetQuery<SelectOrderFillUnitsByFillIdQuery>();

					return await x.QueryAsync<ResourceOrderFillUnit, Unit, ResourceOrderFillUnit>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: (rofu, u) => { rofu.Unit = u; return rofu; },
						splitOn: "UnitId");
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
