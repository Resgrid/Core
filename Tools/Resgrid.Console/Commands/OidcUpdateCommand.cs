using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using Resgrid.Workers.Framework;
using Resgrid.Model.Repositories;
using Autofac;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Console.Models;

namespace Resgrid.Console.Commands
{
	public sealed class OidcUpdateCommand(
		IConfiguration configuration,
		ILogger<OidcUpdateCommand> logger,
		IOidcRepository oidcRepository) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Starting the Resgrid OIDC DB Update Process");
			logger.LogInformation("Please Wait...");

			try
			{
				bool result = oidcRepository.UpdateOidcDatabaseAsync().Result;

				if (result)
					logger.LogInformation("Completed updating the Resgrid OIDC DB!");
				else
					logger.LogError("Process did not complete with a success code. OIDC DB may have been properly updated, please check it and ensure everything is there.");
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error trying to update the Resgrid OIDC DB, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}
	}
}
