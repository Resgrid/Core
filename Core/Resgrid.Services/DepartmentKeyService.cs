using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Department DEK version lifecycle. See <see cref="IDepartmentKeyService"/> for the contract.
	/// Metadata only — wrapped blobs come from the IKeyWrappingProvider and are stored verbatim;
	/// nothing here can produce plaintext key material.
	/// </summary>
	public class DepartmentKeyService : IDepartmentKeyService
	{
		private readonly IDepartmentDataProtectionKeyRepository _keyRepository;
		private readonly IKeyWrappingProvider _keyWrappingProvider;

		public DepartmentKeyService(IDepartmentDataProtectionKeyRepository keyRepository,
			IKeyWrappingProvider keyWrappingProvider)
		{
			_keyRepository = keyRepository;
			_keyWrappingProvider = keyWrappingProvider;
		}

		public Task<DepartmentDataProtectionKey> GetActiveKeyAsync(int departmentId) =>
			_keyRepository.GetActiveByDepartmentIdAsync(departmentId);

		public Task<DepartmentDataProtectionKey> GetKeyByVersionAsync(int departmentId, int version) =>
			_keyRepository.GetByDepartmentAndVersionAsync(departmentId, version);

		public async Task<DepartmentDataProtectionKey> ProvisionNextKeyVersionAsync(int departmentId,
			CancellationToken cancellationToken = default)
		{
			var existing = await _keyRepository.GetAllVersionsByDepartmentIdAsync(departmentId);

			// Idempotent resume: a Pending row is a previous provisioning attempt that crashed before
			// activation — activate it rather than minting another version; an already-Active newest
			// version means provisioning completed and this call is a re-run.
			var newest = existing.OrderByDescending(k => k.Version).FirstOrDefault();
			if (newest != null && newest.Status == (int)DepartmentDataProtectionKeyStatus.Active)
				return newest;
			if (newest != null && newest.Status == (int)DepartmentDataProtectionKeyStatus.Pending)
				return await ActivateAsync(newest, existing.Where(k => k.Version < newest.Version), cancellationToken);

			var nextVersion = (newest?.Version ?? 0) + 1;
			var wrapped = await _keyWrappingProvider.GenerateWrappedDataKeyAsync(departmentId, cancellationToken);

			var keyRow = new DepartmentDataProtectionKey
			{
				DepartmentId = departmentId,
				Version = nextVersion,
				WrappedKey = wrapped.WrappedKeyBase64,
				ProviderType = wrapped.ProviderType,
				ProviderKeyReference = wrapped.ProviderKeyReference,
				ProviderKeyVersion = wrapped.ProviderKeyVersion,
				Status = (int)DepartmentDataProtectionKeyStatus.Pending,
				CreatedOn = DateTime.UtcNow
			};

			// The unique (DepartmentId, Version) index turns a concurrent double-provision into a
			// database error on one side; that caller re-reads and resumes idempotently. Only
			// database exceptions enter the re-read path — anything else (KMS faults, cancellation)
			// propagates with its original cause intact.
			try
			{
				await _keyRepository.InsertAsync(keyRow, cancellationToken);
			}
			catch (System.Data.Common.DbException ex)
			{
				Logging.LogException(ex, $"ADP key provisioning insert collided for department {departmentId} version {nextVersion}; re-reading");
				var reread = await _keyRepository.GetByDepartmentAndVersionAsync(departmentId, nextVersion);
				if (reread == null)
					throw;
				keyRow = reread;
			}

			return await ActivateAsync(keyRow, existing, cancellationToken);
		}

		public async Task<bool> RetireKeyVersionAsync(int departmentId, int version, CancellationToken cancellationToken = default)
		{
			var keyRow = await _keyRepository.GetByDepartmentAndVersionAsync(departmentId, version);
			if (keyRow == null || keyRow.Status != (int)DepartmentDataProtectionKeyStatus.Retiring)
				return false;

			keyRow.Status = (int)DepartmentDataProtectionKeyStatus.Retired;
			keyRow.RetiredOn = DateTime.UtcNow;
			await _keyRepository.SaveOrUpdateAsync(keyRow, cancellationToken);
			return true;
		}

		private async Task<DepartmentDataProtectionKey> ActivateAsync(DepartmentDataProtectionKey keyRow,
			System.Collections.Generic.IEnumerable<DepartmentDataProtectionKey> olderVersions,
			CancellationToken cancellationToken)
		{
			// The NEW version becomes Active first so a concurrent GetActiveKeyAsync never observes
			// zero Active rows mid-activation (the lookup takes the highest Active version, so the
			// brief two-Active overlap resolves to the new key). Older versions then move to
			// Retiring; reads resolve older envelopes through Retiring versions until rotation
			// re-encryption retires them.
			if (keyRow.Status != (int)DepartmentDataProtectionKeyStatus.Active)
			{
				keyRow.Status = (int)DepartmentDataProtectionKeyStatus.Active;
				keyRow.ActivatedOn = DateTime.UtcNow;
				await _keyRepository.SaveOrUpdateAsync(keyRow, cancellationToken);
			}

			foreach (var older in olderVersions.Where(k => k.Status == (int)DepartmentDataProtectionKeyStatus.Active))
			{
				older.Status = (int)DepartmentDataProtectionKeyStatus.Retiring;
				await _keyRepository.SaveOrUpdateAsync(older, cancellationToken);
			}

			return keyRow;
		}
	}
}
