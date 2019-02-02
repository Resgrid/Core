using System.Data.SqlClient;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;
using System.Configuration;
using Dapper;
using System.Data;

namespace Resgrid.Repositories.DataRepository
{
	public class UnitLocationRepository : RepositoryBase<UnitLocation>, IUnitLocationRepository
	{
		public string connectionString =
			ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>()
				.FirstOrDefault(x => x.Name == "ResgridContext")
				.ConnectionString;

		public UnitLocationRepository(DataContext context, IISolationLevel isolationLevel)
			: base(context, isolationLevel) { }

		public void SoftAddUnitLocation(UnitLocation location)
		{
			var query = $@"	DECLARE @UnitId INT = {location.UnitId}
								DECLARE @Latitude DECIMAL(18,2) = {location.Latitude}
								DECLARE @Longitude DECIMAL(18,2) = {location.Longitude}
								DECLARE @Timestamp DATETIME = '{location.Timestamp}'
								DECLARE @Accuracy DECIMAL(18,2) = {location.Accuracy}
								DECLARE @Altitude DECIMAL(18,2) = {location.Altitude}
								DECLARE @AltitudeAccuracy DECIMAL(18,2) = {location.AltitudeAccuracy}
								DECLARE @Speed DECIMAL(18,2) = {location.Speed}
								DECLARE @Heading DECIMAL(18,2) = {location.Heading}

								MERGE UnitLocations AS UL
								USING (SELECT @UnitId AS UnitId, @Latitude AS Latitude, @Longitude AS Longitude) AS S
									ON  UL.UnitId = S.UnitID AND UL.Latitude = S.Latitude AND UL.Longitude = S.Longitude
								  WHEN not matched THEN
									INSERT (UnitId, Timestamp, Latitude, Longitude, Accuracy, Altitude, AltitudeAccuracy, Speed, Heading) 
									VALUES(@UnitId, @Timestamp, @Latitude, @Longitude, @Accuracy, @Altitude, @AltitudeAccuracy, @Speed, @Heading);
								";

			using (IDbConnection db = new SqlConnection(connectionString))
			{
				db.Execute(query);
			}
		}
	}
}