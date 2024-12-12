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
	public class PoiTypesRepository : RepositoryBase<PoiType>, IPoiTypesRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public PoiTypesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<PoiType>> GetPoiTypesByDepartmentIdAsync(int departmentId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<PoiType>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("DepartmentId", departmentId);

					var query = _queryFactory.GetQuery<SelectPoiTypesByDIdQuery>();

					var dictionary = new Dictionary<int, PoiType>();
					var result = await x.QueryAsync<PoiType, Poi, PoiType>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: PoiTypesMapping(dictionary),
						splitOn: "PoiId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value);

					return result;
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

		public async Task<PoiType> GetPoiTypeByTypeIdAsync(int poiTypeId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<PoiType>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("PoiTypeId", poiTypeId);

					var query = _queryFactory.GetQuery<SelectPoiTypeByIdQuery>();

					var dictionary = new Dictionary<int, PoiType>();
					var result = await x.QueryAsync<PoiType, Poi, PoiType>(sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: PoiTypesMapping(dictionary),
						splitOn: "PoiId");

					if (dictionary.Count > 0)
						return dictionary.Select(y => y.Value).FirstOrDefault();

					return result.FirstOrDefault();
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

		private static Func<PoiType, Poi, PoiType> PoiTypesMapping(Dictionary<int, PoiType> dictionary)
		{
			return new Func<PoiType, Poi, PoiType>((poiType, poi) =>
			{
				var dictionaryPoiType = default(PoiType);

				if (poi != null)
				{
					if (dictionary.TryGetValue(poiType.PoiTypeId, out dictionaryPoiType))
					{
						if (dictionaryPoiType.Pois.All(x => x.PoiId != poi.PoiId))
							dictionaryPoiType.Pois.Add(poi);
					}
					else
					{
						if (poiType.Pois == null)
							poiType.Pois = new List<Poi>();

						poiType.Pois.Add(poi);
						dictionary.Add(poiType.PoiTypeId, poiType);

						dictionaryPoiType = poiType;
					}
				}
				else
				{
					poiType.Pois = new List<Poi>();
					dictionaryPoiType = poiType;
					dictionary.Add(poiType.PoiTypeId, poiType);
				}

				return dictionaryPoiType;
			});
		}
	}
}
