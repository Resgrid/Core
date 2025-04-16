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
				logger.LogInformation("Migrating Map Layers...");

				var layers = mapLayersRepository.AsQueryable().ToList();

				if (layers != null && layers.Any())
				{
					Parallel.ForEach(layers, layer =>
					{
						var existingLayer = mapLayersDocRepository.GetByOldIdAsync(layer.Id.ToString()).Result;

						if (existingLayer == null)
						{
							logger.LogInformation($"Migrating Map: {layer.Id.ToString()}");
							mapLayersDocRepository.InsertAsync(layer).
								ContinueWith(t => logger.LogError(t.Exception?.ToString()),
									TaskContinuationOptions.OnlyOnFaulted);
						}
					});
				}

				var unitLocations = unitsLocationRepository.AsQueryable().ToList();

				if (unitLocations != null && unitLocations.Any())
				{
					Parallel.ForEach(unitLocations, unitLocation =>
					{
						var existingLocation = unitsLocationsDocRepository.GetByOldIdAsync(unitLocation.Id.ToString()).Result;

						if (existingLocation == null)
						{
							logger.LogInformation($"Migrating Unit Location: {unitLocation.Id.ToString()}");
							unitsLocationsDocRepository.InsertAsync(unitLocation).
								ContinueWith(t => logger.LogError(t.Exception?.ToString()),
									TaskContinuationOptions.OnlyOnFaulted);
						}
					});
				}

				var personnelLocations = personnelLocationRepository.AsQueryable().ToList();

				if (personnelLocations != null && personnelLocations.Any())
				{
					Parallel.ForEach(personnelLocations, personLocation =>
					{
						var existingLocation = personnelLocationsDocRepository.GetByOldIdAsync(personLocation.Id.ToString()).Result;

						if (existingLocation == null)
						{
							logger.LogInformation($"Migrating Personnel Location: {personLocation.Id.ToString()}");
							personnelLocationsDocRepository.InsertAsync(personLocation).
								ContinueWith(t => logger.LogError(t.Exception?.ToString()),
									TaskContinuationOptions.OnlyOnFaulted);
						}
					});
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
