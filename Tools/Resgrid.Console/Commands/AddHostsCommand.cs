using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;

namespace Resgrid.Console.Commands
{
	public sealed class AddHostsCommand(
		IConfiguration configuration,
		ILogger<AddHostsCommand> logger) : ICommandService
	{
		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			logger.LogInformation("Adding Resgrid local Urls to the hostfile");
			logger.LogInformation("Please Wait...");

			string webUrl = "127.0.0.1 resgrid.local";
			string apiUrl = "127.0.0.1 resgridapi.local";

			try
			{
				WriteRedirectToHostFile(webUrl);
				WriteRedirectToHostFile(apiUrl);

				logger.LogInformation("Successfully updated the Hostfile with the Resgrid Loopback urls!");
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error trying to update the hostfile, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}

			return ExitCode.Success;
		}

		private void WriteRedirectToHostFile(string entry)
		{
			string text = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers/etc/hosts"), Encoding.UTF8);
			if (!text.Contains(entry))
			{
				using (StreamWriter w = File.AppendText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers/etc/hosts")))
				{
					w.WriteLine(entry);
				}
			}
		}
	}
}
