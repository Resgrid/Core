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
using Resgrid.Repositories.DataRepository.Queries.CustomMaps;

namespace Resgrid.Repositories.DataRepository
{
	public class CustomMapImportsRepository : RepositoryBase<CustomMapImport>, ICustomMapImportsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CustomMapImportsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CustomMapImport>> GetImportsForMapAsync(string mapId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CustomMapImport>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapId", mapId);

					var query = _queryFactory.GetQuery<SelectCustomMapImportsForMapQuery>();

					return await x.QueryAsync<CustomMapImport>(sql: query,
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

		public async Task<IEnumerable<CustomMapImport>> GetPendingImportsAsync()
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CustomMapImport>>>(async x =>
				{
					var query = _queryFactory.GetQuery<SelectPendingCustomMapImportsQuery>();

					return await x.QueryAsync<CustomMapImport>(sql: query,
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
