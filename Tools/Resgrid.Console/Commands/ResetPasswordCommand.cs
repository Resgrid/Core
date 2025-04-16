using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Resgrid.Model.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Models;

namespace Resgrid.Console.Commands
{
	public sealed class ResetPasswordCommand(
		IConfiguration configuration,
		ILogger<ResetPasswordCommand> logger,
		UserManager<IdentityUser> userManager) : ICommandService
	{
		private string UserId => GetConfigurationValue("UserId");

		private string Password => GetConfigurationValue("Password");

		/// <summary>
		///     Executes the main functionality of the application.
		/// </summary>
		/// <param name="args">An array of command-line arguments passed to the application.</param>
		/// <param name="cancellationToken">A token that can be used to signal the operation should be canceled.</param>
		/// <returns>Returns an <see cref="ExitCode" /> indicating the result of the execution.</returns>
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			try
			{
				var user = await userManager.FindByIdAsync(UserId);

				if (user == null)
				{
					logger.LogInformation("Could not find the user with the Id of " + UserId);
					return ExitCode.Failed;
				}

				var token = await userManager.GeneratePasswordResetTokenAsync(user);
				var result = await userManager.ResetPasswordAsync(user, token, Password);

				if (result.Succeeded)
					logger.LogInformation("Successfully Reset the Password");
				else
				{
					logger.LogError("Failed to reset the Password: " + result.Errors.FirstOrDefault()?.Description);
					return ExitCode.Failed;
				}
			}
			catch (Exception ex)
			{
				logger.LogError("There was an error trying to reset the password, see the error output below:");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}


			return ExitCode.Success;
		}

		/// <summary>
		///     Retrieves the value of a specified configuration key.
		/// </summary>
		/// <param name="key">The configuration key whose value needs to be retrieved.</param>
		/// <returns>The value of the specified configuration key.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the configuration key is not specified or the value is empty.</exception>
		private string GetConfigurationValue(string key)
		{
			var value = configuration.GetValue<string>(key);

			if (string.IsNullOrEmpty(value))
				throw new InvalidOperationException(
					$"Configuration key '{key}' not specified or is empty. Please specify a value for '{key}'.");

			return value;
		}
	}
}
