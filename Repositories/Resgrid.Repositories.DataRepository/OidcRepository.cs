using System.Data;
using System.Data.SqlClient;
using Dapper;
using Resgrid.Model.Repositories;
using Resgrid.Config;
using System.IO;
using Resgrid.Providers.Migrations.Migrations;
using System;
using Resgrid.Providers.MigrationsPg.Migrations;
using Npgsql;
using System.Threading.Tasks;

namespace Resgrid.Repositories.DataRepository
{
	public class OidcRepository : IOidcRepository
	{
		public async Task<bool> UpdateOidcDatabaseAsync()
		{
			try
			{
				if (Config.DataConfig.DatabaseType == DatabaseTypes.Postgres)
				{
					var assembly = typeof(M0001_InitialMigrationPg).Assembly;

					// Initial Migration
					var resourceName = "Resgrid.Providers.MigrationsPg.Sql.EF0001_PopulateOIDCDb.sql";

					using (Stream stream = assembly.GetManifestResourceStream(resourceName))
					using (StreamReader reader = new StreamReader(stream))
					{
						string migrationScript = reader.ReadToEnd();

						if (!string.IsNullOrWhiteSpace(migrationScript))
						{
							using (var conn = new NpgsqlConnection(OidcConfig.ConnectionString))
							using (var cmd = conn.CreateCommand())
							{
								{
									await conn.OpenAsync();
									using (var tran = conn.BeginTransaction())
									{
										try
										{
											cmd.Transaction = tran;
											cmd.CommandText = migrationScript;
											await cmd.ExecuteNonQueryAsync();
											await tran.CommitAsync();
										}
										catch
										{
											await tran.RollbackAsync();
											throw;
										}
									}
								}
							}
						}
					}
				}
				else
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
								var response = await db.ExecuteAsync(migrationScript);
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
								var response = await db.ExecuteAsync(migrationScript);
							}
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
