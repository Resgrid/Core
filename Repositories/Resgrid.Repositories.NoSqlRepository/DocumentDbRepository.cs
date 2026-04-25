using Npgsql;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Resgrid.Repositories.NoSqlRepository
{
	public class DocumentDbRepository : IDocumentDbRepository
	{
		public async Task<bool> UpdateDocumentDatabaseAsync()
		{
			try
			{
				if (DataConfig.DocDatabaseType != DatabaseTypes.Postgres)
					return true;

				if (string.IsNullOrWhiteSpace(DataConfig.DocumentConnectionString))
					throw new InvalidOperationException("DocumentConnectionString is required when DocDatabaseType is Postgres.");

				var assembly = Assembly.Load("Resgrid.Providers.MigrationsPg");
				const string resourceName = "Resgrid.Providers.MigrationsPg.Sql.EF0003_PopulateDocDb.sql";

				using Stream stream = assembly.GetManifestResourceStream(resourceName)
					?? throw new InvalidOperationException($"Unable to find document database migration resource '{resourceName}'.");
				using StreamReader reader = new StreamReader(stream);

				string migrationScript = await reader.ReadToEndAsync();

				if (string.IsNullOrWhiteSpace(migrationScript))
					throw new InvalidOperationException("Document database migration script is empty.");

				await using var conn = new NpgsqlConnection(DataConfig.DocumentConnectionString);
				await using var cmd = conn.CreateCommand();
				await conn.OpenAsync();
				await using var tran = await conn.BeginTransactionAsync();

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
