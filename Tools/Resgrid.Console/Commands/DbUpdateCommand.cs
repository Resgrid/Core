using Consolas.Core;
using Resgrid.Console.Args;
using Resgrid.Repositories.DataRepository.Migrations;
using System;
using System.Data.Entity;
using System.Data.Entity.Migrations;

namespace Resgrid.Console.Commands
{
    public class DbUpdateCommand : Command
    {
	    private readonly IConsole _console;

	    public DbUpdateCommand(IConsole console)
	    {
		    _console = console;
	    }

		public string Execute(DbUpdateArgs args)
        {
			_console.WriteLine("Starting the Resgrid Database Update Process");
			_console.WriteLine("Please Wait...");

			try
			{
				Database.SetInitializer(new MigrateDatabaseToLatestVersion<Repositories.DataRepository.Contexts.DataContext, Configuration>());
				var migrator = new DbMigrator(new Repositories.DataRepository.Migrations.Configuration());
				migrator.Update();

				_console.WriteLine("Completed updating the Resgrid Database!");
			}
			catch (Exception ex)
			{
				_console.WriteLine("There was an error trying to update the Resgrid Database, see the error output below:");
				_console.WriteLine(ex.ToString());
			}


			return "";
		}
    }
}
