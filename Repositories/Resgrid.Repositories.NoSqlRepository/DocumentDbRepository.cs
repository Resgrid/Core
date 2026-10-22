using MongoDB.Driver;
using Npgsql;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Resgrid.Repositories.NoSqlRepository
{
	public class DocumentDbRepository : IDocumentDbRepository
	{
		private readonly Lazy<IUnitLocationsMongoRepository> _unitLocationsMongoRepository;
		private readonly Lazy<IMongoRepository<PersonnelLocation>> _personnelLocationsMongoRepository;

		public DocumentDbRepository(Lazy<IUnitLocationsMongoRepository> unitLocationsMongoRepository,
			Lazy<IMongoRepository<PersonnelLocation>> personnelLocationsMongoRepository)
		{
			_unitLocationsMongoRepository = unitLocationsMongoRepository;
			_personnelLocationsMongoRepository = personnelLocationsMongoRepository;
		}

		public async Task<bool> UpdateDocumentDatabaseAsync()
		{
			try
			{
				if (DataConfig.DocDatabaseType == DatabaseTypes.MongoDb)
				{
					await _unitLocationsMongoRepository.Value.EnsureIndexesAsync();
					await _unitLocationsMongoRepository.Value.EnsureReadIndexesAsync();

					// Map visualization paths read a department's member locations for a time window.
					await _personnelLocationsMongoRepository.Value.GetCollection().Indexes.CreateOneAsync(new CreateIndexModel<PersonnelLocation>(
						Builders<PersonnelLocation>.IndexKeys.Ascending(location => location.DepartmentId).Descending(location => location.Timestamp),
						new CreateIndexOptions { Name = "ix_personnellocations_department_timestamp" }));
					return true;
				}

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
