using Microsoft.AspNetCore.Identity;
using Resgrid.Framework;
using Resgrid.Model.Identity;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityRole = Resgrid.Model.Identity.IdentityRole;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Repositories.DataRepository.Stores
{
	public class IdentityRoleStore : IRoleStore<IdentityRole>, IRoleClaimStore<IdentityRole>
	{
		private DbConnection _connection;

		private readonly IUnitOfWork _unitOfWork;
		private readonly IConnectionProvider _connectionProvider;
		private readonly IIdentityRoleRepository _roleRepository;

		public IdentityRoleStore(IConnectionProvider connProv, IIdentityRoleRepository roleRepo, IUnitOfWork uow)
		{
			_roleRepository = roleRepo;
			_connectionProvider = connProv;
			_unitOfWork = uow;
		}

		private async Task CreateTransactionIfNotExistsAsync(CancellationToken cancellationToken)
		{
			_connection = _unitOfWork.CreateOrGetConnection();

			if (_connection.State == System.Data.ConnectionState.Closed)
				await _connection.OpenAsync(cancellationToken);
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

		public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			try
			{
				var result = await _roleRepository.InsertAsync(role, cancellationToken);

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

		public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			try
			{
				var result = await _roleRepository.RemoveAsync(role.Id, cancellationToken);

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

		public virtual string ConvertIdFromString(string id)
		{
			if (id == null)
				return default(string);

			return (string)TypeDescriptor.GetConverter(typeof(string)).ConvertFromInvariantString(id);
		}

		public async Task<IdentityRole> FindByIdAsync(string roleId, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (string.IsNullOrEmpty(roleId))
				throw new ArgumentNullException(nameof(roleId));

			try
			{
				var result = await _roleRepository.GetByIdAsync(ConvertIdFromString(roleId));

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<IdentityRole> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (string.IsNullOrEmpty(normalizedRoleName))
				throw new ArgumentNullException(nameof(normalizedRoleName));

			try
			{
				var result = await _roleRepository.GetByNameAsync(normalizedRoleName);

				return result;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task<string> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			return role.Name;
		}

		public async Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			if (role.Id.Equals(default(string)))
				return null;

			return role.Id.ToString();
		}

		public async Task<string> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			return role.Name;
		}

		public async Task SetNormalizedRoleNameAsync(IdentityRole role, string normalizedName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			role.Name = normalizedName;
		}

		public async Task SetRoleNameAsync(IdentityRole role, string roleName, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			role.Name = roleName;
		}

		public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			try
			{
				var result = await _roleRepository.UpdateAsync(role, cancellationToken);

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

		public async Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken = default(CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			try
			{
				var result = await _roleRepository.GetClaimsByRole(role, cancellationToken);

				return result?.Select(roleClaim => new Claim(roleClaim.ClaimType, roleClaim.ClaimValue))
							  .ToList();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				return null;
			}
		}

		public async Task AddClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default(CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			try
			{
				var result = await _roleRepository.InsertClaimAsync(role, claim, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		public async Task RemoveClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default(CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CreateTransactionIfNotExistsAsync(cancellationToken);

			if (role == null)
				throw new ArgumentNullException(nameof(role));

			try
			{
				var result = await _roleRepository.RemoveClaimAsync(role, claim, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}
