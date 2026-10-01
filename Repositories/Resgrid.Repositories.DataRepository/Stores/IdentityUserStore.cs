using Microsoft.AspNetCore.Identity;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Security;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityRole = Resgrid.Model.Identity.IdentityRole;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Repositories.DataRepository.Stores
{
	public class IdentityUserStore :
	   IUserStore<IdentityUser>,
	   IUserLoginStore<IdentityUser>,
	   IUserRoleStore<IdentityUser>,
	   IUserClaimStore<IdentityUser>,
	   IUserPasswordStore<IdentityUser>,
	   IUserSecurityStampStore<IdentityUser>,
	   IUserEmailStore<IdentityUser>,
	   IUserLockoutStore<IdentityUser>,
	   IUserPhoneNumberStore<IdentityUser>,
	   IQueryableUserStore<IdentityUser>,
	   IUserTwoFactorStore<IdentityUser>,
	   IUserAuthenticationTokenStore<IdentityUser>,
	   IUserAuthenticatorKeyStore<IdentityUser>,
	   IUserTwoFactorRecoveryCodeStore<IdentityUser>
	{
		private readonly IUnitOfWork _unitOfWork;
		private readonly IConnectionProvider _connectionProvider;
		private readonly IIdentityUserRepository _userRepository;
		private readonly IUserMfaStateRepository _mfaStateRepository;

		public IdentityUserStore(IConnectionProvider connProv,
							   IIdentityUserRepository roleRepo,
							   IUnitOfWork uow,
							   IUserMfaStateRepository mfaStateRepository)
		{
			_userRepository = roleRepo;
			_connectionProvider = connProv;
			_unitOfWork = uow;
			_mfaStateRepository = mfaStateRepository;
		}

		public Task SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken)) => CommitTransactionAsync(cancellationToken);

		private Task CommitTransactionAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			if (cancellationToken != default(CancellationToken))
				cancellationToken.ThrowIfCancellationRequested();

			try
			{
				_unitOfWork.CommitChanges();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				_unitOfWork.DiscardChanges();
			}


			return Task.CompletedTask;
		}

		public void Dispose()
		{
			_unitOfWork?.Dispose();
		}

		public IQueryable<IdentityUser> Users
		{
			get
			{
				//Impossible to implement IQueryable with Dapper
				throw new NotImplementedException();
			}
		}

		public async Task AddClaimsAsync(IdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.InsertClaimsAsync(user.Id, claims, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task AddLoginAsync(IdentityUser user, UserLoginInfo login, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.InsertLoginInfoAsync(user.Id, login, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task AddToRoleAsync(IdentityUser user, string roleName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.AddToRoleAsync(user.Id, roleName, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.InsertAsync(user, cancellationToken);

				if (!result.Equals(default(string)))
				{
					user.Id = result;

					return IdentityResult.Success;
				}
				else
				{
					return IdentityResult.Failed();
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return IdentityResult.Failed(new IdentityError[]
				{
					new IdentityError{ Description = ex.Message }
				});
			}
		}

		public async Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.RemoveAsync(user.Id, cancellationToken);

				return result ? IdentityResult.Success : IdentityResult.Failed();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return IdentityResult.Failed(new IdentityError[]
				{
					new IdentityError{ Description = ex.Message }
				});
			}
		}

		public async Task<IdentityUser> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(normalizedEmail))
				throw new ArgumentNullException(nameof(normalizedEmail));

			try
			{
				var result = await _userRepository.GetByEmailAsync(normalizedEmail);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}

		}

		public async Task<IdentityUser> FindByIdAsync(string userId, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(userId))
				throw new ArgumentNullException(nameof(userId));

			try
			{
				var key = default(string);

				var converter = TypeDescriptor.GetConverter(typeof(string));
				if (converter != null && converter.CanConvertFrom(typeof(string)))
				{
					key = (string)converter.ConvertFromInvariantString(userId);
				}
				else
				{
					key = (string)Convert.ChangeType(userId, typeof(string));
				}

				var result = await _userRepository.GetByIdAsync(key);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IdentityUser> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				var result = await _userRepository.GetByUserLoginAsync(loginProvider, providerKey);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IdentityUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(normalizedUserName))
				throw new ArgumentNullException(nameof(normalizedUserName));

			try
			{
				var result = await _userRepository.GetByUserNameAsync(normalizedUserName);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public Task<int> GetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.AccessFailedCount);
		}

		public async Task<IList<Claim>> GetClaimsAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.GetClaimsByUserIdAsync(user.Id);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public Task<string> GetEmailAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.Email);
		}

		public Task<bool> GetEmailConfirmedAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.EmailConfirmed);
		}

		public Task<bool> GetLockoutEnabledAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.LockoutEnabled);
		}

		public Task<DateTimeOffset?> GetLockoutEndDateAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.LockoutEnd);
		}

		public async Task<IList<UserLoginInfo>> GetLoginsAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.GetUserLoginInfoByIdAsync(user.Id);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public Task<string> GetNormalizedEmailAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.Email);
		}

		public Task<string> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			// Prefer the stored normalized name; fall back to UserName.ToUpperInvariant() for legacy
			// rows whose NormalizedUserName was never populated (see SetNormalizedUserNameAsync).
			return Task.FromResult(user.NormalizedUserName ?? user.UserName?.ToUpperInvariant());
		}

		public Task<string> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.PasswordHash);
		}

		public Task<string> GetPhoneNumberAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.PhoneNumber);
		}

		public Task<bool> GetPhoneNumberConfirmedAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.PhoneNumberConfirmed);
		}

		public async Task<IList<string>> GetRolesAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.GetRolesByUserIdAsync(user.Id);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public Task<string> GetSecurityStampAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.SecurityStamp);
		}

		public async Task<string> GetTokenAsync(IdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null) throw new ArgumentNullException(nameof(user));
			var stored = await _userRepository.GetTokenAsync(user.Id, loginProvider, name);
			var use = SeedUse(loginProvider, name);
			return use == null || stored == null ? stored : await ReadSeedAsync(user.Id, loginProvider, name, use.Value, stored, cancellationToken);
		}

		public async Task SetTokenAsync(IdentityUser user, string loginProvider, string name, string value, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null) throw new ArgumentNullException(nameof(user));
			var use = SeedUse(loginProvider, name);
			if (use != null && value != null && TwoFactorConfig.AuthenticatorSeedEncryptionEnabled)
				value = AuthenticatorSeedProtector.Protect(user.Id, use.Value, value);
			await _userRepository.SetTokenAsync(user.Id, loginProvider, name, value, cancellationToken);
		}

		/// <summary>The authenticator seed tokens: the active key, and a replacement staged for setup (slice 14).</summary>
		private static AuthenticatorSeedUse? SeedUse(string loginProvider, string name) =>
			loginProvider == AuthenticatorKeyLoginProvider && name == AuthenticatorKeyTokenName ? AuthenticatorSeedUse.Active
			: loginProvider == StagedAuthenticatorKey.LoginProvider && name == StagedAuthenticatorKey.TokenName ? AuthenticatorSeedUse.Staged
			: null;

		/// <summary>
		/// A stored seed as the authenticator uses it. An unreadable one (unknown key, moved between accounts or uses, or
		/// tampered with) reads as no seed, so the code check fails closed; the account still has its recovery codes. With the
		/// gate on, a plaintext seed or one under a retired key is re-encrypted here, once, and only if nothing replaced it
		/// meanwhile.
		/// </summary>
		private async Task<string> ReadSeedAsync(string userId, string loginProvider, string name, AuthenticatorSeedUse use, string stored,
			CancellationToken cancellationToken)
		{
			var read = AuthenticatorSeedProtector.Unprotect(userId, use, stored);
			if (read.Failed)
			{
				Framework.Logging.LogError($"An authenticator seed could not be decrypted ({use}); the account's authenticator reads as not set up.");
				return null;
			}

			if (TwoFactorConfig.AuthenticatorSeedEncryptionEnabled && (read.IsPlaintext || read.NeedsRewrap))
			{
				try
				{
					await _userRepository.TryReplaceTokenAsync(userId, loginProvider, name, stored,
						AuthenticatorSeedProtector.Protect(userId, use, read.Value), cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// The seed still works as read; the next read or the migration command tries again.
					Framework.Logging.LogException(ex, "Authenticator seed re-encryption failed.");
				}
			}

			return read.Value;
		}

		public async Task RemoveTokenAsync(IdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null) throw new ArgumentNullException(nameof(user));
			await _userRepository.RemoveTokenAsync(user.Id, loginProvider, name, cancellationToken);
		}

		// ── IUserAuthenticatorKeyStore ─────────────────────────────────────────────

		private const string AuthenticatorKeyLoginProvider = AuthenticatorSeedProtector.ActiveLoginProvider;
		private const string AuthenticatorKeyTokenName = AuthenticatorSeedProtector.ActiveTokenName;
		private const string RecoveryCodeTokenName = "RecoveryCodes";

		public Task SetAuthenticatorKeyAsync(IdentityUser user, string key, CancellationToken cancellationToken)
			=> SetTokenAsync(user, AuthenticatorKeyLoginProvider, AuthenticatorKeyTokenName, key, cancellationToken);

		public Task<string> GetAuthenticatorKeyAsync(IdentityUser user, CancellationToken cancellationToken)
			=> GetTokenAsync(user, AuthenticatorKeyLoginProvider, AuthenticatorKeyTokenName, cancellationToken);

		// ── IUserTwoFactorRecoveryCodeStore ────────────────────────────────────────
		// Codes live as HMAC verifiers in UserRecoveryCodes (M0243), one row each, consumed by a single guarded UPDATE
		// so a code redeemed concurrently on two nodes succeeds once. The pre-M0243 plaintext ";"-joined token is
		// migrated on first use and then deleted.

		public async Task ReplaceCodesAsync(IdentityUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null) throw new ArgumentNullException(nameof(user));

			var hashes = (recoveryCodes ?? Enumerable.Empty<string>())
				.Where(code => !string.IsNullOrWhiteSpace(code))
				.Select(code => RecoveryCodeHasher.Hash(user.Id, code))
				.ToList();
			await _mfaStateRepository.ReplaceRecoveryCodesAsync(user.Id, hashes, RecoveryCodeHasher.CurrentVersion, DateTime.UtcNow, cancellationToken);
		}

		public async Task<bool> RedeemCodeAsync(IdentityUser user, string code, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null) throw new ArgumentNullException(nameof(user));
			if (string.IsNullOrWhiteSpace(code)) return false;

			await MigrateLegacyRecoveryCodesAsync(user, cancellationToken);
			return await _mfaStateRepository.TryRedeemRecoveryCodeAsync(user.Id, RecoveryCodeHasher.Hash(user.Id, code), DateTime.UtcNow, cancellationToken);
		}

		public async Task<int> CountCodesAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null) throw new ArgumentNullException(nameof(user));

			await MigrateLegacyRecoveryCodesAsync(user, cancellationToken);
			return await _mfaStateRepository.CountUnusedRecoveryCodesAsync(user.Id, cancellationToken);
		}

		private async Task MigrateLegacyRecoveryCodesAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			var legacy = await GetTokenAsync(user, AuthenticatorKeyLoginProvider, RecoveryCodeTokenName, cancellationToken);
			if (legacy == null)
				return;

			var hashes = legacy.Split(';', StringSplitOptions.RemoveEmptyEntries)
				.Select(code => RecoveryCodeHasher.Hash(user.Id, code))
				.ToList();

			// Returns false when another request already migrated (or regenerated) this user's codes; either way the
			// verifier rows are now authoritative.
			await _mfaStateRepository.ImportLegacyRecoveryCodesAsync(user.Id, legacy, hashes, RecoveryCodeHasher.CurrentVersion,
				DateTime.UtcNow, cancellationToken);
		}

		public Task<bool> GetTwoFactorEnabledAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.TwoFactorEnabled);
		}

		public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.Id.ToString());
		}

		public Task<string> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			return Task.FromResult(user.UserName);
		}

		public async Task<IList<IdentityUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (claim == null)
				throw new ArgumentNullException(nameof(claim));

			try
			{
				var result = await _userRepository.GetUsersByClaimAsync(claim);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IList<IdentityUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(roleName))
				throw new ArgumentNullException(nameof(roleName));

			try
			{
				var result = await _userRepository.GetUsersInRoleAsync(roleName);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult(user.PasswordHash != null);
		}

		public Task<int> IncrementAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.AccessFailedCount++;
			return Task.FromResult(user.AccessFailedCount);
		}

		public async Task<bool> IsInRoleAsync(IdentityUser user, string roleName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			if (string.IsNullOrEmpty(roleName))
				throw new ArgumentNullException(nameof(roleName));

			try
			{
				var result = await _userRepository.IsInRoleAsync(user.Id, roleName);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return false;
			}
		}

		public async Task RemoveClaimsAsync(IdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			if (claims == null)
				throw new ArgumentNullException(nameof(claims));

			try
			{
				var result = await _userRepository.RemoveClaimsAsync(user.Id, claims, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task RemoveFromRoleAsync(IdentityUser user, string roleName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			if (string.IsNullOrEmpty(roleName))
				throw new ArgumentNullException(nameof(roleName));

			try
			{
				var result = await _userRepository.RemoveFromRoleAsync(user.Id, roleName, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task RemoveLoginAsync(IdentityUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			if (string.IsNullOrEmpty(loginProvider))
				throw new ArgumentNullException(nameof(loginProvider));

			if (string.IsNullOrEmpty(providerKey))
				throw new ArgumentNullException(nameof(providerKey));

			try
			{
				var result = await _userRepository.RemoveLoginAsync(user.Id, loginProvider, providerKey, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task ReplaceClaimAsync(IdentityUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			if (claim == null)
				throw new ArgumentNullException(nameof(claim));

			if (newClaim == null)
				throw new ArgumentNullException(nameof(newClaim));

			try
			{
				var result = await _userRepository.UpdateClaimAsync(user.Id, claim, newClaim, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public Task ResetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.AccessFailedCount = 0;

			return Task.FromResult(0);
		}

		public Task SetEmailAsync(IdentityUser user, string email, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.Email = email;

			return Task.FromResult(0);
		}

		public Task SetEmailConfirmedAsync(IdentityUser user, bool confirmed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.EmailConfirmed = confirmed;

			return Task.FromResult(0);
		}

		public Task SetLockoutEnabledAsync(IdentityUser user, bool enabled, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.LockoutEnabled = enabled;

			return Task.FromResult(0);
		}

		public Task SetLockoutEndDateAsync(IdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.LockoutEnd = lockoutEnd;

			return Task.FromResult(0);
		}

		public Task SetNormalizedEmailAsync(IdentityUser user, string normalizedEmail, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.Email = normalizedEmail;

			return Task.FromResult(0);
		}

		public Task SetNormalizedUserNameAsync(IdentityUser user, string normalizedName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			// Persist the normalized name so new users (created via UserManager.CreateAsync during
			// department/account signup) get NormalizedUserName populated; otherwise it inserts as NULL
			// and lookups keyed on normalizedusername (= UserName.ToUpperInvariant()) can't find the user.
			user.NormalizedUserName = normalizedName ?? user.UserName?.ToUpperInvariant();

			return Task.FromResult(0);
		}

		public Task SetPasswordHashAsync(IdentityUser user, string passwordHash, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.PasswordHash = passwordHash;

			return Task.FromResult(0);
		}

		public Task SetPhoneNumberAsync(IdentityUser user, string phoneNumber, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.PhoneNumber = phoneNumber;

			return Task.FromResult(0);
		}

		public Task SetPhoneNumberConfirmedAsync(IdentityUser user, bool confirmed, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.PhoneNumberConfirmed = confirmed;

			return Task.FromResult(0);
		}

		public Task SetSecurityStampAsync(IdentityUser user, string stamp, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.SecurityStamp = stamp;

			return Task.FromResult(0);
		}

		public Task SetTwoFactorEnabledAsync(IdentityUser user, bool enabled, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.TwoFactorEnabled = enabled;

			return Task.FromResult(0);
		}

		public Task SetUserNameAsync(IdentityUser user, string userName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			user.UserName = userName;

			return Task.FromResult(0);
		}

		public async Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (user == null)
				throw new ArgumentNullException(nameof(user));

			try
			{
				var result = await _userRepository.UpdateAsync(user, cancellationToken);

				return result ? IdentityResult.Success : IdentityResult.Failed();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return IdentityResult.Failed(new IdentityError[]
				{
					new IdentityError{ Description = ex.Message }
				});
			}
		}
	}
}
