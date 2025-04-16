using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;

namespace Resgrid.Console.Commands
{
	public sealed class HelpCommand(
		IConfiguration configuration,
		ILogger<HelpCommand> logger) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Resgrid Console Help");

			logger.LogInformation("--AddHosts :: Adds a host to the Resgrid Console");
			logger.LogInformation("--ClearCache -- --DepartmentId=1 :: Clears the cache for a department");
			logger.LogInformation("--DbUpdate || --UpdateDb :: Updates the Resgrid Database");
			logger.LogInformation("--GenOidcCerts :: Generates the OIDC Certificates");
			logger.LogInformation("--MigrateDocsDb :: Migrates the Resgrid Docs Database");
			logger.LogInformation("--OidcUpdate :: Updates the Resgrid OIDC Database");
			logger.LogInformation("--ResetPassword -- --UserId=[GUID] --Password=[PASSWORD] :: Resets the password for a user");
			logger.LogInformation("--SecurityRefresh :: Refreshes the Resgrid Security Matrix Cache");

			return ExitCode.Success;
		}
	}
}
