using Resgrid.Console.Args;
using System;
using System.IO;
using Consolas2.Core;
using Resgrid.Workers.Framework;
using Autofac;
using Resgrid.Model.Repositories;
using Resgrid.Model;
using Stripe.Identity;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;
using File = Resgrid.Model.File;

namespace Resgrid.Console.Commands
{
	public sealed class MigrateDocsDbCommand(
		IConfiguration configuration,
		ILogger<MigrateDocsDbCommand> logger,
		IDocumentDbRepository documentDbRepository,
		IMongoRepository<MapLayer> mapLayersRepository,
		IMongoRepository<UnitsLocation> unitsLocationRepository,
		IMongoRepository<PersonnelLocation> personnelLocationRepository,
		IMapLayersDocRepository mapLayersDocRepository,
		IUnitLocationsDocRepository unitsLocationsDocRepository,
		IPersonnelLocationsDocRepository personnelLocationsDocRepository) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Migrating Documents from Mongo to Postgres");
			logger.LogInformation("Please Wait...");

			try
			{
				if (Config.DataConfig.DocDatabaseType == Config.DatabaseTypes.Postgres)
				{
					logger.LogInformation("Ensuring Postgres document tables exist...");

					var schemaUpdated = await documentDbRepository.UpdateDocumentDatabaseAsync();

					if (!schemaUpdated)
					{
						logger.LogError("Failed to update the Postgres document database schema.");
						return ExitCode.Failed;
					}
				}

				// A failed insert is logged and the rest still migrate (a re-run skips what already moved), but the command
				// must not report success while documents are missing.
				var failedInserts = 0;

				logger.LogInformation("Migrating Map Layers...");

				var layers = mapLayersRepository.AsQueryable().ToList();

				if (layers != null && layers.Any())
				{
					await Parallel.ForEachAsync(layers, cancellationToken, async (layer, _) =>
					{
						var existingLayer = await mapLayersDocRepository.GetByOldIdAsync(layer.Id.ToString());

						if (existingLayer == null)
						{
							logger.LogInformation($"Migrating Map: {layer.Id.ToString()}");
							try { await mapLayersDocRepository.InsertAsync(layer); }
							catch (Exception ex) { Interlocked.Increment(ref failedInserts); logger.LogError(ex.ToString()); }
						}
					});
				}

				var unitLocations = unitsLocationRepository.AsQueryable().ToList();

				if (unitLocations != null && unitLocations.Any())
				{
					await Parallel.ForEachAsync(unitLocations, cancellationToken, async (unitLocation, _) =>
					{
						var existingLocation = await unitsLocationsDocRepository.GetByOldIdAsync(unitLocation.Id.ToString());

						if (existingLocation == null)
						{
							logger.LogInformation($"Migrating Unit Location: {unitLocation.Id.ToString()}");
							try { await unitsLocationsDocRepository.InsertAsync(unitLocation); }
							catch (Exception ex) { Interlocked.Increment(ref failedInserts); logger.LogError(ex.ToString()); }
						}
					});
				}

				var personnelLocations = personnelLocationRepository.AsQueryable().ToList();

				if (personnelLocations != null && personnelLocations.Any())
				{
					await Parallel.ForEachAsync(personnelLocations, cancellationToken, async (personLocation, _) =>
					{
						var existingLocation = await personnelLocationsDocRepository.GetByOldIdAsync(personLocation.Id.ToString());

						if (existingLocation == null)
						{
							logger.LogInformation($"Migrating Personnel Location: {personLocation.Id.ToString()}");
							try { await personnelLocationsDocRepository.InsertAsync(personLocation); }
							catch (Exception ex) { Interlocked.Increment(ref failedInserts); logger.LogError(ex.ToString()); }
						}
					});
				}

				if (failedInserts > 0)
				{
					logger.LogError($"Finished Migrating Documents, but {failedInserts} document(s) could not be inserted; see the errors above and run the migration again.");
					return ExitCode.Failed;
				}

				logger.LogInformation("Finished Migrating Documents.");
			}
			catch (Exception ex)
			{
				logger.LogError("Failed to migrate the document database, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}
	}
}
