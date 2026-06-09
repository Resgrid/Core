using System;
using System.Threading;
using System.Threading.Tasks;
using Consolas2.Core;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;
using Resgrid.Model.Repositories;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Console.Commands
{
	/// <summary>
	///     On-demand UTF-8 data cleanup. Runs the same sweep as the nightly worker so the database can
	///     be made migration-clean ad hoc (e.g. immediately before a SQL Server -> PostgreSQL cutover).
	/// </summary>
	public sealed class CleanUtf8Command(
		ILogger<CleanUtf8Command> logger,
		IUtf8MaintenanceRepository utf8MaintenanceRepository) : ICommandService
	{
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Starting the Resgrid UTF-8 Data Cleanup (pre-migration sweep)");
			logger.LogInformation("Please Wait...");

			try
			{
				var logic = new Utf8CleanupLogic(utf8MaintenanceRepository);
				var result = await logic.Process(cancellationToken);

				if (!result.Item1)
				{
					logger.LogError("UTF-8 data cleanup did not complete successfully: " + result.Item2);
					return ExitCode.Failed;
				}

				logger.LogInformation(result.Item2);
				logger.LogInformation("Completed the Resgrid UTF-8 Data Cleanup!");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "There was an error running the UTF-8 data cleanup");
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}
	}
}
