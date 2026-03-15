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
	public class CustomMapFloorsRepository : RepositoryBase<CustomMapFloor>, ICustomMapFloorsRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public CustomMapFloorsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration,
			IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CustomMapFloor>> GetFloorsByMapIdAsync(string customMapId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<IEnumerable<CustomMapFloor>>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapId", customMapId);

					var query = _queryFactory.GetQuery<SelectCustomMapFloorsByMapIdQuery>();

					var dictionary = new Dictionary<string, CustomMapFloor>();
					var result = await x.QueryAsync<CustomMapFloor, CustomMapZone, CustomMapFloor>(
						sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: FloorsMapping(dictionary),
						splitOn: "CustomMapZoneId");

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

		public async Task<CustomMapFloor> GetFloorByIdWithZonesAsync(string customMapFloorId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<CustomMapFloor>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("CustomMapFloorId", customMapFloorId);

					var query = _queryFactory.GetQuery<SelectCustomMapFloorsByMapIdQuery>();

					var dictionary = new Dictionary<string, CustomMapFloor>();
					var result = await x.QueryAsync<CustomMapFloor, CustomMapZone, CustomMapFloor>(
						sql: query,
						param: dynamicParameters,
						transaction: _unitOfWork.Transaction,
						map: FloorsMapping(dictionary),
						splitOn: "CustomMapZoneId");

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

		private static Func<CustomMapFloor, CustomMapZone, CustomMapFloor> FloorsMapping(Dictionary<string, CustomMapFloor> dictionary)
		{
			return (floor, zone) =>
			{
				if (!dictionary.TryGetValue(floor.CustomMapFloorId, out var dictionaryFloor))
				{
					floor.Zones = new List<CustomMapZone>();
					dictionary.Add(floor.CustomMapFloorId, floor);
					dictionaryFloor = floor;
				}

				if (zone != null && dictionaryFloor.Zones.All(z => z.CustomMapZoneId != zone.CustomMapZoneId))
					dictionaryFloor.Zones.Add(zone);

				return dictionaryFloor;
			};
		}
	}
}



