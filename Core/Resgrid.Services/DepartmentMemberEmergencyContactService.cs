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

			// ADP write safety net (plan 4.2/19.2). The AAD row key is the identity pk, so a NEW row
			// must be inserted before it can be enveloped, and that insert is a transient plaintext
			// write. An UPDATE already has its id, so it is enveloped BEFORE the save and no
			// plaintext ever reaches the table - which is the common path here, since a member
			// edits this data far more often than they first fill it in. Fails closed either way.
			var isExistingRow = contact.DepartmentMemberEmergencyContactId > 0;

			// The stored row backs REDACTED-sentinel restoration. The contacts table renders every
			// cataloged value as a placeholder while protection is enforced, so an edit saved without
			// a grant posts placeholders back; with no stored row to restore from, the member's
			// next-of-kin name, relationship, both phone numbers and email would be nulled. Loaded
			// through the member-scoped accessor so an id from another member cannot be reached.
			DepartmentMemberEmergencyContact existing = null;
			if (isExistingRow)
				existing = (await _repository.GetAllByDepartmentAndUserAsync(contact.DepartmentId, contact.UserId))
					?.FirstOrDefault(x => x != null
						&& x.DepartmentMemberEmergencyContactId == contact.DepartmentMemberEmergencyContactId);

			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareMemberEmergencyContactWriteAsync(
					contact.DepartmentId, contact, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); emergency contact {contact.DepartmentMemberEmergencyContactId} was NOT saved.");
			}

			var saved = await _repository.SaveOrUpdateAsync(contact, cancellationToken);

			if (!isExistingRow)
			{
				var protectedWrite = await _protectedWriteService.Value.PrepareMemberEmergencyContactWriteAsync(
					saved.DepartmentId, saved, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); emergency contact {saved.DepartmentMemberEmergencyContactId} has transient plaintext pending re-encryption.");
				if (protectedWrite.Changed)
					saved = await _repository.SaveOrUpdateAsync(saved, cancellationToken);
			}

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
