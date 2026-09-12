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
			logger.LogInformation("--FeatureFlags :: Reads and sets feature toggles. Sub commands:");
			logger.LogInformation("    --FeatureFlags --List [--DepartmentId=1] [--IncludeArchived] [--Filter=text] :: Lists every flag in the database, and what a department resolves it to");
			logger.LogInformation("    --FeatureFlags --Keys :: Lists every flag key application code reads and whether a flag row has been seeded for it");
			logger.LogInformation("    --FeatureFlags --Show --Key=Chat.System [--DepartmentId=1] :: Shows one flag with its overrides, rules and prerequisites");
			logger.LogInformation("    --FeatureFlags --On|--Off --Key=Chat.System :: Sets the global default, which applies to EVERY department without an override");
			logger.LogInformation("    --FeatureFlags --On|--Off --Key=Chat.System --DepartmentId=1 [--Reason=\"text\"] [--ExpiresOn=2026-12-31] :: Sets one department's override");
			logger.LogInformation("    --FeatureFlags --Clear --Key=Chat.System --DepartmentId=1 :: Removes a department override so it follows the global default");
			logger.LogInformation("    --FeatureFlags --Rollout=25 --Key=Chat.System :: Sets the staged rollout percentage across departments");
			logger.LogInformation("    Add --UserId=[GUID] to any write to attribute it in the audit log");
			logger.LogInformation("--GenOidcCerts :: Generates the OIDC Certificates");
			logger.LogInformation("--MigrateDocsDb :: Migrates the Resgrid Docs Database");
			logger.LogInformation("--NormalizePhoneNumbers [--Apply] [--DepartmentId=1] :: Rewrites stored profile phone numbers to E.164. Dry run unless --Apply is passed");
			logger.LogInformation("--OidcUpdate :: Updates the Resgrid OIDC Database");
			logger.LogInformation("--ResetPassword -- --UserId=[GUID] --Password=[PASSWORD] :: Resets the password for a user");
			logger.LogInformation("--SecurityRefresh :: Refreshes the Resgrid Security Matrix Cache");

			return ExitCode.Success;
		}
	}
}
