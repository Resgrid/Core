using Resgrid.Console.Args;
using System;
using System.Configuration;
using System.Data.SqlClient;
using Consolas2.Core;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Providers.Migrations.Migrations;
using FluentMigrator.Runner;

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
			WriteConnectionString();

			_console.WriteLine("Please Wait...");

			try
			{
				var serviceProvider = CreateServices();

				// Put the database update into a scope to ensure
				// that all resources will be disposed.
				using (var scope = serviceProvider.CreateScope())
				{
					UpdateDatabase(scope.ServiceProvider);
				}

				_console.WriteLine("Completed updating the Resgrid Database!");
			}
			catch (Exception ex)
			{
				_console.WriteLine("There was an error trying to update the Resgrid Database, see the error output below:");
				_console.WriteLine(ex.ToString());
			}


			return "";
		}

		/// <summary>
		/// Configure the dependency injection services
		/// </summary>
		private static IServiceProvider CreateServices()
		{
			return new ServiceCollection()
				// Add common FluentMigrator services
				.AddFluentMigratorCore()
				.ConfigureRunner(rb => rb
					// Add SQL Server support to FluentMigrator
					.AddSqlServer()
					// Set the timeout
					.WithGlobalCommandTimeout(TimeSpan.Zero)
					// Set the connection string
					.WithGlobalConnectionString(ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString)
					// Define the assembly containing the migrations
					.ScanIn(typeof(M0001_InitialMigration).Assembly).For.Migrations().For.EmbeddedResources())
				// Enable logging to console in the FluentMigrator way
				.AddLogging(lb => lb.AddFluentMigratorConsole())
				// Build the service provider
				.BuildServiceProvider(false);
		}

		/// <summary>
		/// Update the database
		/// </summary>
		private static void UpdateDatabase(IServiceProvider serviceProvider)
		{
			// Instantiate the runner
			var runner = serviceProvider.GetRequiredService<IMigrationRunner>();
			
			// Execute the migrations
			runner.MigrateUp();
		}

		private void WriteConnectionString()
		{
			var connection = ConfigurationManager.ConnectionStrings["ResgridContext"].ConnectionString;

			if (!String.IsNullOrWhiteSpace(connection))
			{

				var csb = new SqlConnectionStringBuilder(connection);
				_console.WriteLine($"SQL Server: {csb.DataSource}");
				_console.WriteLine($"Database Name: {csb.InitialCatalog}");
			}
		}
	}
}
