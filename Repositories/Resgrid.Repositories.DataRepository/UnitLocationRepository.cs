using System;
using System.Data.Common;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Dapper;
using Resgrid.Framework;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries.Units;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitLocationRepository : RepositoryBase<UnitLocation>, IUnitLocationRepository
	{
		private readonly IConnectionProvider _connectionProvider;
		private readonly SqlConfiguration _sqlConfiguration;
		private readonly IQueryFactory _queryFactory;
		private readonly IUnitOfWork _unitOfWork;

		public UnitLocationRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_connectionProvider = connectionProvider;
			_sqlConfiguration = sqlConfiguration;
			_queryFactory = queryFactory;
			_unitOfWork = unitOfWork;
		}

		public void SoftAddUnitLocation(UnitLocation location)
		{
			//var query = $@"	DECLARE @UnitId INT = {location.UnitId}
			//					DECLARE @Latitude DECIMAL(18,2) = {location.Latitude}
			//					DECLARE @Longitude DECIMAL(18,2) = {location.Longitude}
			//					DECLARE @Timestamp DATETIME = '{location.Timestamp}'
			//					DECLARE @Accuracy DECIMAL(18,2) = {location.Accuracy}
			//					DECLARE @Altitude DECIMAL(18,2) = {location.Altitude}
			//					DECLARE @AltitudeAccuracy DECIMAL(18,2) = {location.AltitudeAccuracy}
			//					DECLARE @Speed DECIMAL(18,2) = {location.Speed}
			//					DECLARE @Heading DECIMAL(18,2) = {location.Heading}

			//					MERGE UnitLocations AS UL
			//					USING (SELECT @UnitId AS UnitId, @Latitude AS Latitude, @Longitude AS Longitude) AS S
			//						ON  UL.UnitId = S.UnitID AND UL.Latitude = S.Latitude AND UL.Longitude = S.Longitude
			//					  WHEN not matched THEN
			//						INSERT (UnitId, Timestamp, Latitude, Longitude, Accuracy, Altitude, AltitudeAccuracy, Speed, Heading) 
			//						VALUES(@UnitId, @Timestamp, @Latitude, @Longitude, @Accuracy, @Altitude, @AltitudeAccuracy, @Speed, @Heading);
			//					";

			//using (IDbConnection db = new SqlConnection(connectionString))
			//{
			//	db.Execute(query);
			//}
		}

		public async Task<UnitLocation> GetLastUnitLocationByUnitIdAsync(int unitId)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UnitLocation>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);

					var query = _queryFactory.GetQuery<SelectLatestUnitLocationByUnitId>();

					return await x.QueryFirstOrDefaultAsync<UnitLocation>(sql: query,
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

		public async Task<UnitLocation> GetLastUnitLocationByUnitIdTimestampAsync(int unitId, DateTime timestamp)
		{
			try
			{
				var selectFunction = new Func<DbConnection, Task<UnitLocation>>(async x =>
				{
					var dynamicParameters = new DynamicParametersExtension();
					dynamicParameters.Add("UnitId", unitId);
					dynamicParameters.Add("Timestamp", timestamp);

					var query = _queryFactory.GetQuery<SelectLatestUnitLocationByUnitIdTimeQuery>();

					return await x.QueryFirstOrDefaultAsync<UnitLocation>(sql: query,
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
