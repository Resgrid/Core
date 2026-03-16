using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using System;
using Dapper;
using Resgrid.Framework;
using Resgrid.Repositories.DataRepository.Queries.IndoorMaps;

namespace Resgrid.Repositories.DataRepository
{
	public class IndoorMapZonesRepository : RepositoryBase<IndoorMapZone>, IIndoorMapZonesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public IndoorMapZonesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<IndoorMapZone>> GetZonesByFloorIdAsync(string indoorMapFloorId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<IndoorMapZone>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("IndoorMapFloorId", indoorMapFloorId);

					var query = _queryFactory.GetQuery<SelectIndoorMapZonesByFloorIdQuery>();

					return await x.QueryAsync<IndoorMapZone>(sql: query,
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

		public async Task<IEnumerable<IndoorMapZone>> SearchZonesAsync(int departmentId, string searchTerm)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<IndoorMapZone>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);
					dynamicParameters.Add("SearchTerm", searchTerm);

					var query = _queryFactory.GetQuery<SearchIndoorMapZonesQuery>();

					return await x.QueryAsync<IndoorMapZone>(sql: query,
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
