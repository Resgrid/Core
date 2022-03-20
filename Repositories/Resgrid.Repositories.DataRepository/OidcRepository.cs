using System.Data;
using System.Data.SqlClient;
using Dapper;
using Resgrid.Model.Repositories;
using Resgrid.Config;
using System.IO;
using Resgrid.Providers.Migrations.Migrations;

namespace Resgrid.Repositories.DataRepository
{
	public class OidcRepository : IOidcRepository
	{
		public bool UpdateOidcDatabase()
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

						return true;
					}
				}
			}

			return false;
		}

	}
}
