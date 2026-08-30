using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>See <see cref="IDepartmentMemberSensitiveDataService"/>.</summary>
	public class DepartmentMemberSensitiveDataService : IDepartmentMemberSensitiveDataService
	{
		private readonly IDepartmentMemberSensitiveDataRepository _repository;

		// Lazy: defers the protected graph (broker client) until a save or resolve actually needs it.
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		private readonly Lazy<IProtectedReadService> _protectedReadService;

		public DepartmentMemberSensitiveDataService(IDepartmentMemberSensitiveDataRepository repository,
			Lazy<IProtectedWriteService> protectedWriteService, Lazy<IProtectedReadService> protectedReadService)
		{
			_repository = repository;
			_protectedWriteService = protectedWriteService;
			_protectedReadService = protectedReadService;
		}

		public async Task<IReadOnlyDictionary<string, DepartmentMemberSensitiveData>> GetResolvedForDepartmentAsync(
			int departmentId, string grantToken, string actingUserId)
		{
			var rows = (await _repository.GetAllByDepartmentIdAsync(departmentId))?.ToList()
				?? new List<DepartmentMemberSensitiveData>();

			await _protectedReadService.Value.ResolveMemberSensitiveDataForReadAsync(departmentId, rows,
				grantToken, actingUserId);

			return rows
				.Where(r => !string.IsNullOrWhiteSpace(r.UserId))
				.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
		}

		public async Task ApplyIdentificationNumbersAsync(int departmentId, IEnumerable<UserProfile> profiles,
			string grantToken, string actingUserId)
		{
			var list = profiles?.Where(p => p != null).ToList();
			if (list == null || list.Count == 0)
				return;

			var byUser = await GetResolvedForDepartmentAsync(departmentId, grantToken, actingUserId);

			foreach (var profile in list)
			{
				// A member with no row for THIS department has no number here, even if the legacy
				// global profile column still holds one for a different department.
				profile.IdentificationNumber = profile.UserId != null && byUser.TryGetValue(profile.UserId, out var row)
					? row.IdentificationNumber
					: null;
			}
		}

		public async Task<DepartmentMemberSensitiveData> GetByDepartmentAndUserAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return null;

			return await _repository.GetByDepartmentAndUserAsync(departmentId, userId);
		}

		public async Task<bool> DeleteForMemberAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default)
		{
			var row = await GetByDepartmentAndUserAsync(departmentId, userId);
			if (row == null)
				return false;

			return await _repository.DeleteAsync(row, cancellationToken);
		}

		public async Task<DepartmentMemberSensitiveData> SaveAsync(DepartmentMemberSensitiveData data,
			CancellationToken cancellationToken = default)
		{
			if (data == null)
				return null;

			if (data.CreatedOn == default)
				data.CreatedOn = DateTime.UtcNow;
			else
				data.UpdatedOn = DateTime.UtcNow;

			// ProtectionId is NOT NULL and has no database default, so a first save has to assign it
			// or the insert fails outright.
			if (string.IsNullOrWhiteSpace(data.ProtectionId))
				data.ProtectionId = Guid.NewGuid().ToString("N");

			// ADP write safety net (plan 4.2/19.2). The AAD row key is the identity pk, so a NEW row
			// must be inserted before it can be enveloped, and that insert is a transient plaintext
			// write. An UPDATE already has its id, so it is enveloped BEFORE the save and no
			// plaintext ever reaches the table - which is the common path here, since a member
			// edits this data far more often than they first fill it in. Fails closed either way.
			var isExistingRow = data.DepartmentMemberSensitiveDataId > 0;

			// The stored row backs REDACTED-sentinel restoration. The profile page renders every one
			// of these values as a placeholder while protection is enforced, so a save made without a
			// grant posts placeholders back for fields the editor never touched; with no stored row
			// to restore from, the identification number and both addresses would be nulled instead.
			DepartmentMemberSensitiveData existing = null;
			if (isExistingRow)
				existing = await _repository.GetByDepartmentAndUserAsync(data.DepartmentId, data.UserId);

			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareMemberSensitiveDataWriteAsync(
					data.DepartmentId, data, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); member sensitive data {data.DepartmentMemberSensitiveDataId} was NOT saved.");
			}

			var saved = await _repository.SaveOrUpdateAsync(data, cancellationToken);

			if (!isExistingRow)
			{
				var protectedWrite = await _protectedWriteService.Value.PrepareMemberSensitiveDataWriteAsync(
					saved.DepartmentId, saved, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); member sensitive data {saved.DepartmentMemberSensitiveDataId} has transient plaintext pending re-encryption.");
				if (protectedWrite.Changed)
					saved = await _repository.SaveOrUpdateAsync(saved, cancellationToken);
			}

			return saved;
		}
	}
}
