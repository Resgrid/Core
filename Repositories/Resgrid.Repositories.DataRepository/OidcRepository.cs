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

				// Initial Migration
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

				// Update to V5
				var resourceName2 = "Resgrid.Providers.Migrations.Sql.EF0003_MigrateOIDCDbToV5.sql";

				using (Stream stream = assembly.GetManifestResourceStream(resourceName2))
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
