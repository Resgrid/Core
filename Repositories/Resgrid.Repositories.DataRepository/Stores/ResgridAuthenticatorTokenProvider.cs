using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Repositories.DataRepository.Stores
{
	/// <summary>
	/// Replaces Identity's authenticator provider (registered under <c>TokenOptions.DefaultAuthenticatorProvider</c> in every
	/// host) so each TOTP time step is accepted once per user across all nodes (plan section 7.5 rule 8). Every existing
	/// <c>VerifyTwoFactorTokenAsync</c> call site and <c>SignInManager.TwoFactorAuthenticatorSignInAsync</c> go through it.
	/// </summary>
	public sealed class ResgridAuthenticatorTokenProvider : IUserTwoFactorTokenProvider<IdentityUser>
	{
		private readonly IUserMfaStateRepository _mfaState;

		public ResgridAuthenticatorTokenProvider(IUserMfaStateRepository mfaState)
		{
			_mfaState = mfaState;
		}

		public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<IdentityUser> manager, IdentityUser user)
			=> !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));

		// Authenticator codes come from the user's app; like Identity's provider, nothing is generated server-side.
		public Task<string> GenerateAsync(string purpose, UserManager<IdentityUser> manager, IdentityUser user)
			=> Task.FromResult(string.Empty);

		public async Task<bool> ValidateAsync(string purpose, string token, UserManager<IdentityUser> manager, IdentityUser user)
		{
			var key = await manager.GetAuthenticatorKeyAsync(user);
			return await ValidateAndConsumeAsync(_mfaState, user.Id, key, token, DateTime.UtcNow);
		}

		/// <summary>
		/// Verifies <paramref name="code"/> against <paramref name="base32Key"/> and consumes the matched time step. Also used
		/// to verify a staged (not yet active) authenticator key during setup or replacement. A replayed step, a malformed
		/// input and a storage fault all return false; storage faults never accept a code.
		/// </summary>
		public static async Task<bool> ValidateAndConsumeAsync(IUserMfaStateRepository mfaState, string userId, string base32Key,
			string code, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;

			var step = TotpCalculator.FindMatchingTimeStep(base32Key, code, utcNow);
			if (step == null)
				return false;

			try
			{
				return await mfaState.TryConsumeTotpTimeStepAsync(userId, step.Value, utcNow, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "TOTP time-step consumption failed; the code was rejected.");
				return false;
			}
		}
	}
}
