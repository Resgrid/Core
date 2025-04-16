using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Console.Commands
{
	public sealed class SecurityRefreshCommand(
		IConfiguration configuration,
		ILogger<SecurityRefreshCommand> logger) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Starting the Security Refresh Process");
			logger.LogInformation(DateTime.Now.ToString("MM/dd/yy H:mm:ss zzz"));
			logger.LogInformation("Please Wait...");

			try
			{
				Stopwatch timer = new Stopwatch();
				var security = new SecurityLogic();
				timer.Start();

				var result = await security.UpdatedCachedSecurityForAllDepartments();

				timer.Stop();

				logger.LogInformation(DateTime.Now.ToString("MM/dd/yy H:mm:ss zzz"));
				logger.LogInformation($"Completed the Security Refresh Process in {timer.Elapsed.ToString("mm\\:ss\\.ff")}!");
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error trying to run the Security Refresh Process, see the error output below:");
				logger.LogError(DateTime.Now.ToString("MM/dd/yy H:mm:ss zzz"));
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}
	}
}
