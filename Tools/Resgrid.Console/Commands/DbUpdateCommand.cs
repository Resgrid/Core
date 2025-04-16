using Resgrid.Console.Args;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Consolas2.Core;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Providers.Migrations.Migrations;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;
using Resgrid.Model.Repositories;
using Resgrid.Providers.MigrationsPg.Migrations;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace Resgrid.Console.Commands
{
	public sealed class DbUpdateCommand(
		IConfiguration configuration,
		ILogger<DbUpdateCommand> logger,
		IMigrationRunner migrationRunner) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Starting the Resgrid Database Update Process");
			logger.LogInformation("Please Wait...");

			try
			{
				migrationRunner.MigrateUp();

				logger.LogInformation("Completed updating the Resgrid Database!");
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error trying to update the Resgrid Database, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}
	}
}
