using System;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Repositories.DataRepository.Stores
{
	/// <summary>
	/// Authenticator (TOTP) setup and replacement, shared by the API and Web sign-in, setup and recovery (passkey plan section 6.2;
	/// Web's TwoFactorController account page keeps the same steps and order): a new key is staged apart from the active one, it
	/// becomes active only after a code from it verifies (and that time step is spent), and replacing a factor retires everything
	/// the old one authorized.
	/// </summary>
	public static class AuthenticatorSetup
	{
		/// <summary>Stages a new key for this user; returns it formatted for typing and as an otpauth:// URI.</summary>
		public static async Task<(string SharedKey, string AuthenticatorUri)> StageAsync(UserManager<IdentityUser> userManager, IdentityUser user)
		{
			var key = userManager.GenerateNewAuthenticatorKey();
			await userManager.SetAuthenticationTokenAsync(user, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName,
				StagedAuthenticatorKey.Serialize(key, DateTime.UtcNow));
			return (FormatKey(key), await UriAsync(userManager, user, key));
		}

		/// <summary>The otpauth:// URI an authenticator app scans for <paramref name="key"/>.</summary>
		public static async Task<string> UriAsync(UserManager<IdentityUser> userManager, IdentityUser user, string key)
		{
			var issuer = UrlEncoder.Default.Encode(TwoFactorConfig.TotpIssuerName);
			var account = UrlEncoder.Default.Encode(await userManager.GetEmailAsync(user) ?? user.UserName ?? "");
			return $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6";
		}

		/// <summary>The staged key while it is within its lifetime; null otherwise.</summary>
		public static async Task<string> GetStagedKeyAsync(UserManager<IdentityUser> userManager, IdentityUser user) =>
			StagedAuthenticatorKey.ReadUsableKey(
				await userManager.GetAuthenticationTokenAsync(user, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName),
				DateTime.UtcNow, TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.StagedAuthenticatorLifetimeMinutes)));

		/// <summary>Whether <paramref name="code"/> comes from the staged key; the time step is spent either way it is used.</summary>
		public static Task<bool> VerifyStagedCodeAsync(IUserMfaStateRepository mfaState, IdentityUser user, string stagedKey, string code,
			CancellationToken cancellationToken) =>
			string.IsNullOrWhiteSpace(stagedKey) || string.IsNullOrWhiteSpace(code)
				? Task.FromResult(false)
				: ResgridAuthenticatorTokenProvider.ValidateAndConsumeAsync(mfaState, user.Id, stagedKey,
					code.Replace(" ", string.Empty).Replace("-", string.Empty), DateTime.UtcNow, cancellationToken);

		/// <summary>
		/// Makes the staged key the active one and turns TOTP on, recording where it was set up (plan section 6.5): the app, the
		/// installation label, and whether it was a shared installation.
		/// </summary>
		public static async Task PromoteAsync(UserManager<IdentityUser> userManager, IUserStore<IdentityUser> userStore, IUserMfaStateRepository mfaState,
			IdentityUser user, string stagedKey, TotpEnrollmentContext context, CancellationToken cancellationToken)
		{
			if (userStore is not IUserAuthenticatorKeyStore<IdentityUser> keyStore)
				throw new InvalidOperationException("The user store does not support authenticator keys.");

			await keyStore.SetAuthenticatorKeyAsync(user, stagedKey, cancellationToken);
			await userManager.RemoveAuthenticationTokenAsync(user, StagedAuthenticatorKey.LoginProvider, StagedAuthenticatorKey.TokenName);
			await mfaState.RecordTotpEnrollmentAsync(user.Id, DateTime.UtcNow, context, cancellationToken);
			if (!await userManager.GetTwoFactorEnabledAsync(user))
				await userManager.SetTwoFactorEnabledAsync(user, true);
		}

		public static async Task<string[]> NewRecoveryCodesAsync(UserManager<IdentityUser> userManager, IdentityUser user) =>
			(await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorConfig.DefaultRecoveryCodeCount)).ToArray();

		/// <summary>
		/// After a replacement: the old seed, remembered state (security stamp), every session and every piece of evidence
		/// end, and fresh recovery codes replace the old ones. Returns the new codes, shown once.
		/// </summary>
		public static async Task<string[]> RetireOldAuthorityAsync(UserManager<IdentityUser> userManager, IUserSessionService sessions,
			IMfaEvidenceService evidence, IdentityUser user, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			await userManager.UpdateSecurityStampAsync(user);
			var codes = await NewRecoveryCodesAsync(userManager, user);
			await sessions.RevokeAllAfterCredentialChangeAsync(user.Id, user.Id, UserSessionRevocationReason.MfaChanged, now, cancellationToken);
			await evidence.RevokeForUserAsync(user.Id, cancellationToken);
			return codes;
		}

		/// <summary>The key in groups of four, lower case, for typing into an authenticator app.</summary>
		public static string FormatKey(string key)
		{
			var result = new StringBuilder();
			for (var i = 0; i < key.Length; i += 4)
				result.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(i + 4 < key.Length ? " " : "");
			return result.ToString().ToLowerInvariant();
		}
	}
}
