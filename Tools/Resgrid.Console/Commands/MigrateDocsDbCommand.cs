using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using Resgrid.Workers.Framework;
using Autofac;
using Resgrid.Model.Repositories;
using Resgrid.Model;
using Stripe.Identity;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Console.Commands
{
	public class MigrateDocsDbCommand : Command
    {
	    private readonly IConsole _console;

	    public MigrateDocsDbCommand(IConsole console)
	    {
		    _console = console;
	    }

		public string Execute(MigrateDocsDbArgs args)
        {
			_console.WriteLine("Migrating Documents from Mongo to Postgres");
			_console.WriteLine("Please Wait...");

			try
			{
				var mapLayersRepository = Bootstrapper.GetKernel().Resolve<IMongoRepository<MapLayer>>();
				var unitsLocationRepository = Bootstrapper.GetKernel().Resolve<IMongoRepository<UnitsLocation>>();
				var personnelLocationRepository = Bootstrapper.GetKernel().Resolve<IMongoRepository<PersonnelLocation>>();

				var mapLayersDocRepository = Bootstrapper.GetKernel().Resolve<IMapLayersDocRepository>();
				var unitsLocationsDocRepository = Bootstrapper.GetKernel().Resolve<IUnitLocationsDocRepository>();
				var personnelLocationsDocRepository = Bootstrapper.GetKernel().Resolve<IPersonnelLocationsDocRepository>();

				_console.WriteLine("Migrating Map Layers...");

				var layers = mapLayersRepository.AsQueryable().ToList();

				if (layers != null && layers.Any())
				{
					Parallel.ForEach(layers, layer =>
					{
						var existingLayer = mapLayersDocRepository.GetByOldIdAsync(layer.Id.ToString()).Result;

						if (existingLayer == null)
						{
							_console.WriteLine($"Migrating Map: {layer.Id.ToString()}");
							mapLayersDocRepository.InsertAsync(layer).
								ContinueWith(t => _console.WriteLine(t.Exception),
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
							_console.WriteLine($"Migrating Unit Location: {unitLocation.Id.ToString()}");
							unitsLocationsDocRepository.InsertAsync(unitLocation).
								ContinueWith(t => _console.WriteLine(t.Exception),
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
							_console.WriteLine($"Migrating Personnel Location: {personLocation.Id.ToString()}");
							personnelLocationsDocRepository.InsertAsync(personLocation).
								ContinueWith(t => _console.WriteLine(t.Exception),
								TaskContinuationOptions.OnlyOnFaulted);
						}
					});
				}

				_console.WriteLine("Finished Migrating Documents.");
			}
			catch (Exception ex)
			{
				_console.WriteLine(ex.ToString());
			}


			return "";
		}
    }
}
