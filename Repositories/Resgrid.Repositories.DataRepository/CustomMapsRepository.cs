using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Mapping;

namespace Resgrid.Repositories.DataRepository
{
	public class CustomMapsRepository : RepositoryBase<CustomMap>, ICustomMapsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CustomMapsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CustomMap>> GetCustomMapsByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CustomMap>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectCustomMapsByDIdQuery>();

					var dictionary = new Dictionary<string, CustomMap>();
					var result = await x.QueryAsync<CustomMap, CustomMapFloor, CustomMap>(
						sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: CustomMapsMapping(dictionary),
						splitOn: "CustomMapFloorId");

					return dictionary.Count > 0 ? dictionary.Select(y => y.Value) : result;
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
				return null;
			}
		}

		public async Task<CustomMap> GetCustomMapByIdWithFloorsAsync(string customMapId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CustomMap>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapId", customMapId);

					var query = _queryFactory.GetQuery<SelectCustomMapByIdQuery>();

					var dictionary = new Dictionary<string, CustomMap>();
					var result = await x.QueryAsync<CustomMap, CustomMapFloor, CustomMap>(
						sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: CustomMapsMapping(dictionary),
						splitOn: "CustomMapFloorId");

					return dictionary.Count > 0
						? dictionary.Select(y => y.Value).FirstOrDefault()
						: result.FirstOrDefault();
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
				return null;
			}
		}

		private static Func<CustomMap, CustomMapFloor, CustomMap> CustomMapsMapping(Dictionary<string, CustomMap> dictionary)
		{
			return (customMap, floor) =>
			{
				if (!dictionary.TryGetValue(customMap.CustomMapId, out var dictionaryMap))
				{
					customMap.Floors = new List<CustomMapFloor>();
					dictionary.Add(customMap.CustomMapId, customMap);
					dictionaryMap = customMap;
				}

				if (floor != null && dictionaryMap.Floors.All(f => f.CustomMapFloorId != floor.CustomMapFloorId))
					dictionaryMap.Floors.Add(floor);

				return dictionaryMap;
			};
		}
	}
}


