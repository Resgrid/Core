using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Repositories.DataRepository.Stores
{
	public enum AuthenticatorSeedMigration
	{
		/// <summary>Encrypt plaintext seeds, and re-encrypt seeds under a retired key with the active one.</summary>
		Encrypt = 1,

		/// <summary>Write every seed back as plaintext, before rolling back below the build that encrypts them.</summary>
		Decrypt = 2
	}

	public sealed class AuthenticatorSeedMigrationResult
	{
		public int Scanned { get; set; }
		public int Rewritten { get; set; }

		/// <summary>Already in the requested form.</summary>
		public int Current { get; set; }

		/// <summary>Encrypted under a key that is not configured, or damaged. Left as is and reported.</summary>
		public int Unreadable { get; set; }

		/// <summary>Replaced by the user while this ran; the new value was written in the current form.</summary>
		public int ChangedMeanwhile { get; set; }
	}

	/// <summary>
	/// Rewrites every stored authenticator seed, active and staged, in pages, with the same guarded replace the lazy path
	/// uses (slice 14). Encrypting needs the gate on, which is the operator's statement that every host reads encrypted
	/// seeds; decrypting needs it off, or reads would re-encrypt behind it. Safe to run again: finished rows are skipped.
	/// </summary>
	public static class AuthenticatorSeedMigrator
	{
		public static async Task<AuthenticatorSeedMigrationResult> RunAsync(IIdentityUserRepository users, AuthenticatorSeedMigration direction,
			int batchSize, CancellationToken cancellationToken = default)
		{
			if (direction == AuthenticatorSeedMigration.Encrypt && !TwoFactorConfig.AuthenticatorSeedEncryptionEnabled)
				throw new InvalidOperationException("Turn TwoFactorConfig.AuthenticatorSeedEncryptionEnabled on, on every host, before encrypting seeds.");
			if (direction == AuthenticatorSeedMigration.Decrypt && TwoFactorConfig.AuthenticatorSeedEncryptionEnabled)
				throw new InvalidOperationException("Turn TwoFactorConfig.AuthenticatorSeedEncryptionEnabled off, on every host, before decrypting seeds.");
			if (direction == AuthenticatorSeedMigration.Encrypt && AuthenticatorSeedProtector.Readiness().Count > 0)
				throw new InvalidOperationException("The authenticator seed keys are not usable: " + string.Join(" ", AuthenticatorSeedProtector.Readiness()));

			var result = new AuthenticatorSeedMigrationResult();
			await RunAsync(users, direction, AuthenticatorSeedUse.Active, AuthenticatorSeedProtector.ActiveLoginProvider,
				AuthenticatorSeedProtector.ActiveTokenName, Math.Max(1, batchSize), result, cancellationToken);
			await RunAsync(users, direction, AuthenticatorSeedUse.Staged, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName,
				Math.Max(1, batchSize), result, cancellationToken);
			return result;
		}

		private static async Task RunAsync(IIdentityUserRepository users, AuthenticatorSeedMigration direction, AuthenticatorSeedUse use,
			string loginProvider, string name, int batchSize, AuthenticatorSeedMigrationResult result, CancellationToken cancellationToken)
		{
			string after = null;
			while (true)
			{
				var page = await users.GetTokensPageAsync(loginProvider, name, after, batchSize, cancellationToken);
				foreach (var row in page)
				{
					cancellationToken.ThrowIfCancellationRequested();
					result.Scanned++;
					after = row.UserId;
					if (string.IsNullOrEmpty(row.Value))
					{
						result.Current++;
						continue;
					}

					var read = AuthenticatorSeedProtector.Unprotect(row.UserId, use, row.Value);
					if (read.Failed)
					{
						result.Unreadable++;
						continue;
					}

					var target = direction == AuthenticatorSeedMigration.Encrypt
						? read.IsPlaintext || read.NeedsRewrap ? AuthenticatorSeedProtector.Protect(row.UserId, use, read.Value) : null
						: read.IsPlaintext ? null : read.Value;
					if (target == null)
					{
						result.Current++;
						continue;
					}

					if (await users.TryReplaceTokenAsync(row.UserId, loginProvider, name, row.Value, target, cancellationToken))
						result.Rewritten++;
					else
						result.ChangedMeanwhile++;
				}

				if (page.Count < batchSize)
					return;
			}
		}
	}
}
