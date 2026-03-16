using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using Dapper;
using Resgrid.Framework;
using Resgrid.Repositories.DataRepository.Queries.CustomMaps;

namespace Resgrid.Repositories.DataRepository
{
	public class CustomMapTilesRepository : RepositoryBase<CustomMapTile>, ICustomMapTilesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CustomMapTilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<CustomMapTile> GetTileAsync(string layerId, int zoomLevel, int tileX, int tileY)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CustomMapTile>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapLayerId", layerId);
					dynamicParameters.Add("ZoomLevel", zoomLevel);
					dynamicParameters.Add("TileX", tileX);
					dynamicParameters.Add("TileY", tileY);

					var query = _queryFactory.GetQuery<SelectCustomMapTileQuery>();

					return await x.QueryFirstOrDefaultAsync<CustomMapTile>(sql: query,
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

		public async Task<IEnumerable<CustomMapTile>> GetTilesForLayerAsync(string layerId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CustomMapTile>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapLayerId", layerId);

					var query = _queryFactory.GetQuery<SelectCustomMapTilesForLayerQuery>();

					return await x.QueryAsync<CustomMapTile>(sql: query,
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

		public async Task<bool> DeleteTilesForLayerAsync(string layerId, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				var deleteFunction = new Func<DbConnection, Task<bool>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapLayerId", layerId);

					var query = _queryFactory.GetQuery<DeleteCustomMapTilesForLayerQuery>();

					await x.ExecuteAsync(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction);

					return true;
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
