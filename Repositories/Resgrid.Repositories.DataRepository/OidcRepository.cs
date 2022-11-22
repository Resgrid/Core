using System.Data;
using System.Data.SqlClient;
using Dapper;
using Resgrid.Model.Repositories;
using Resgrid.Config;
using System.IO;
using Resgrid.Providers.Migrations.Migrations;
using System;
using DnsClient;
using Serilog.Core;

namespace Resgrid.Repositories.DataRepository
{
	public class OidcRepository : IOidcRepository
	{
		public bool UpdateOidcDatabase()
		{
			try
			{
				var assembly = typeof(M0001_InitialMigration).Assembly;
				var resourceName = "Resgrid.Providers.Migrations.Sql.EF0001_PopulateOIDCDb.sql";

				using (Stream stream = assembly.GetManifestResourceStream(resourceName))
				using (StreamReader reader = new StreamReader(stream))
				{
					string migrationScript = reader.ReadToEnd();

					if (!string.IsNullOrWhiteSpace(migrationScript))
					{
						using (IDbConnection db = new SqlConnection(OidcConfig.ConnectionString))
						{
							var response = db.Execute(migrationScript);
						}
					}
				}

				//using (IDbConnection db = new SqlConnection(OidcConfig.ConnectionString))
				//{
				//	var response = db.Execute(@$"
				//		IF NOT EXISTS (SELECT * FROM [dbo].[OpenIddictApplications] WHERE ClientId = '${JwtConfig.EventsClientId}')
				//		BEGIN
				//		INSERT INTO [dbo].[OpenIddictApplications]
				//				   ([Id]
				//				   ,[ClientId]
				//				   ,[ClientSecret]
				//				   ,[ConcurrencyToken]
				//				   ,[ConsentType]
				//				   ,[DisplayName]
				//				   ,[DisplayNames]
				//				   ,[Permissions]
				//				   ,[PostLogoutRedirectUris]
				//				   ,[Properties]
				//				   ,[RedirectUris]
				//				   ,[Requirements]
				//				   ,[Type])
				//			 VALUES
				//				   ('8f59d88e-6f59-4059-8a84-ce2719a642d0'
				//				   ,'${JwtConfig.EventsClientId}'
				//				   ,'${JwtConfig.EventsClientSecret}'
				//				   ,'896f4a83-4adc-470d-8f0e-567eac416da4'
				//				   ,NULL
				//				   ,'Events Client'
				//				   ,'Events Client'
				//				   ,'ept:introspection'
				//				   ,NULL
				//				   ,NULL
				//				   ,NULL
				//				   ,NULL
				//				   ,NULL)
				//		END");
				//}
				
				return true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

	}
}
