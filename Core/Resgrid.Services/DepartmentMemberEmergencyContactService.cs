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
	/// <summary>See <see cref="IDepartmentMemberEmergencyContactService"/>.</summary>
	public class DepartmentMemberEmergencyContactService : IDepartmentMemberEmergencyContactService
	{
		private readonly IDepartmentMemberEmergencyContactRepository _repository;

		// Lazy: defers the protected-write graph (broker client) until a save actually needs it.
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;

		public DepartmentMemberEmergencyContactService(IDepartmentMemberEmergencyContactRepository repository,
			Lazy<IProtectedWriteService> protectedWriteService)
		{
			_repository = repository;
			_protectedWriteService = protectedWriteService;
		}

		public async Task<List<DepartmentMemberEmergencyContact>> GetAllForMemberAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<DepartmentMemberEmergencyContact>();

			var contacts = await _repository.GetAllByDepartmentAndUserAsync(departmentId, userId);

			return contacts?.ToList() ?? new List<DepartmentMemberEmergencyContact>();
		}

		public async Task<DepartmentMemberEmergencyContact> SaveAsync(DepartmentMemberEmergencyContact contact,
			CancellationToken cancellationToken = default)
		{
			if (contact == null)
				return null;

			if (contact.DepartmentMemberEmergencyContactId == 0)
				contact.CreatedOn = DateTime.UtcNow;
			else
				contact.UpdatedOn = DateTime.UtcNow;

			var saved = await _repository.SaveOrUpdateAsync(contact, cancellationToken);

			// ADP write safety net (plan 4.2/19.2). Runs AFTER the save so the identity pk exists —
			// it is the AAD row key — then re-persists the enveloped row. Fails closed by throwing
			// rather than leaving next-of-kin details in plaintext.
			var protectedWrite = await _protectedWriteService.Value.PrepareMemberEmergencyContactWriteAsync(
				saved.DepartmentId, saved, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); emergency contact {saved.DepartmentMemberEmergencyContactId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				saved = await _repository.SaveOrUpdateAsync(saved, cancellationToken);

			// Exactly one primary per member. "Who do we call first" has to have a single answer, and
			// nothing in the schema enforces it. Demotion runs after the save so the new row has its
			// id and can exclude itself. The other rows are re-saved through the repository rather
			// than this method: only a bool changes, and their cataloged values are already
			// enveloped, so putting them through the write net again would be pointless work.
			if (saved.IsPrimary)
			{
				var siblings = await _repository.GetAllByDepartmentAndUserAsync(saved.DepartmentId, saved.UserId);

				foreach (var other in (siblings ?? Enumerable.Empty<DepartmentMemberEmergencyContact>())
					.Where(x => x != null && x.IsPrimary &&
								x.DepartmentMemberEmergencyContactId != saved.DepartmentMemberEmergencyContactId))
				{
					other.IsPrimary = false;
					other.UpdatedOn = DateTime.UtcNow;
					other.UpdatedByUserId = saved.UpdatedByUserId;
					await _repository.SaveOrUpdateAsync(other, cancellationToken);
				}
			}

			return saved;
		}

		public async Task<int> DeleteAllForMemberAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return 0;

			// Hard delete, unlike the per-contact soft delete above: an account deletion must not
			// leave a third party's name and phone number sitting in the table under an IsDeleted
			// flag. Envelopes go with the rows, so there is nothing to decrypt first.
			return await _repository.DeleteAllByDepartmentAndUserAsync(departmentId, userId);
		}

		public async Task<bool> DeleteAsync(int departmentMemberEmergencyContactId, int departmentId, string userId,
			string deletingUserId, CancellationToken cancellationToken = default)
		{
			// Scoped by department AND user: an id alone must never be enough to remove another
			// member's contact.
			var contacts = await _repository.GetAllByDepartmentAndUserAsync(departmentId, userId);
			var contact = contacts?.FirstOrDefault(x => x.DepartmentMemberEmergencyContactId == departmentMemberEmergencyContactId);

			if (contact == null)
				return false;

			contact.IsDeleted = true;
			contact.UpdatedOn = DateTime.UtcNow;
			contact.UpdatedByUserId = deletingUserId;

			// Soft delete only — the row keeps its envelopes, so no decrypt is needed and the
			// migration engine's residue counts stay consistent.
			await _repository.SaveOrUpdateAsync(contact, cancellationToken);

			return true;
		}
	}
}
