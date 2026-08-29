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

			var saved = await _repository.SaveOrUpdateAsync(data, cancellationToken);

			// ADP write safety net (plan 4.2/19.2). Runs AFTER the save so the identity pk exists —
			// it is the AAD row key — then re-persists the enveloped row. Fails closed by throwing
			// rather than leaving a member's identification number in plaintext.
			var protectedWrite = await _protectedWriteService.Value.PrepareMemberSensitiveDataWriteAsync(
				saved.DepartmentId, saved, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); member sensitive data {saved.DepartmentMemberSensitiveDataId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				saved = await _repository.SaveOrUpdateAsync(saved, cancellationToken);

			return saved;
		}
	}
}
