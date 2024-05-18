using Microsoft.AspNetCore.Identity;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
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
	   IUserAuthenticationTokenStore<IdentityUser>
	{
		private readonly IUnitOfWork _unitOfWork;
		private readonly IConnectionProvider _connectionProvider;
		private readonly IIdentityUserRepository _userRepository;

		public IdentityUserStore(IConnectionProvider connProv,
							   IIdentityUserRepository roleRepo,
							   IUnitOfWork uow)
		{
			_userRepository = roleRepo;
			_connectionProvider = connProv;
			_unitOfWork = uow;
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

			return Task.FromResult(user.UserName);
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

		public Task<string> GetTokenAsync(IdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
		{
			return Task.FromResult(string.Empty);
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

		public Task RemoveTokenAsync(IdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
		{
			return Task.FromResult(0);
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

		public Task SetTokenAsync(IdentityUser user, string loginProvider, string name, string value, CancellationToken cancellationToken)
		{
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
