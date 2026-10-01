using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Console.Models;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Stores;

namespace Resgrid.Console.Commands
{
	/// <summary>
	///     Encrypts or decrypts every stored authenticator seed (passkey workbook section 12, slice 14).
	///     <para>
	///     --Encrypt encrypts plaintext seeds and re-encrypts seeds under a retired key with the active one. Run it after
	///     turning TwoFactorConfig.AuthenticatorSeedEncryptionEnabled on everywhere, and after every key rotation, before the
	///     old key leaves the ring. --Decrypt writes seeds back as plaintext, for rolling back below the build that encrypts
	///     them; turn the gate off everywhere first. Both are safe to run again and never print a seed.
	///     </para>
	/// </summary>
	public sealed class AuthenticatorSeedsCommand(
		ILogger<AuthenticatorSeedsCommand> logger,
		IIdentityUserRepository users) : ICommandService
	{
		public async Task<ExitCode> ExecuteMainAsync(string[] args, CancellationToken cancellationToken)
		{
			var encrypt = args.Contains("--Encrypt", StringComparer.OrdinalIgnoreCase);
			var decrypt = args.Contains("--Decrypt", StringComparer.OrdinalIgnoreCase);
			if (encrypt == decrypt)
			{
				logger.LogError("Pass exactly one of --Encrypt or --Decrypt.");
				return ExitCode.Failed;
			}

			var batch = 500;
			var batchArg = args.FirstOrDefault(a => a.StartsWith("--BatchSize=", StringComparison.OrdinalIgnoreCase));
			if (batchArg != null && (!int.TryParse(batchArg.Substring("--BatchSize=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out batch) ||
				batch < 1))
			{
				logger.LogError("--BatchSize must be a positive whole number.");
				return ExitCode.Failed;
			}

			try
			{
				logger.LogInformation(encrypt
					? $"Encrypting authenticator seeds with key {AuthenticatorSeedProtector.ActiveKeyId}..."
					: "Decrypting authenticator seeds to plaintext...");
				var result = await AuthenticatorSeedMigrator.RunAsync(users,
					encrypt ? AuthenticatorSeedMigration.Encrypt : AuthenticatorSeedMigration.Decrypt, batch, cancellationToken);

				logger.LogInformation($"Scanned {result.Scanned}; rewritten {result.Rewritten}; already done {result.Current}; " +
					$"changed by their users meanwhile {result.ChangedMeanwhile}; unreadable {result.Unreadable}.");
				if (result.Unreadable > 0)
				{
					logger.LogWarning("Unreadable seeds are under a key that is not configured, or damaged. Those users replace their authenticator " +
						"or use a recovery code. Do not remove a key from the ring while seeds still use it.");
					return ExitCode.Failed;
				}

				return ExitCode.Success;
			}
			catch (InvalidOperationException ex)
			{
				logger.LogError(ex.Message);
				return ExitCode.Failed;
			}
			catch (Exception ex)
			{
				logger.LogError("Authenticator seed migration stopped. Finished rows are kept; run it again to continue.");
				logger.LogError(ex.ToString());
				return ExitCode.Failed;
			}
		}
	}
}
