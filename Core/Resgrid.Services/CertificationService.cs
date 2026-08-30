using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Services
{
	public class CertificationService : ICertificationService
	{
		private readonly IDepartmentCertificationTypeRepository _departmentCertificationTypeRepository;
		private readonly IPersonnelCertificationRepository _personnelCertificationRepository;

		// Lazy: defers the protected graph (broker client) until a save actually needs it.
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;

		public CertificationService(IDepartmentCertificationTypeRepository departmentCertificationTypeRepository,
			IPersonnelCertificationRepository personnelCertificationRepository,
			Lazy<IProtectedWriteService> protectedWriteService)
		{
			_departmentCertificationTypeRepository = departmentCertificationTypeRepository;
			_personnelCertificationRepository = personnelCertificationRepository;
			_protectedWriteService = protectedWriteService;
		}

		public async Task<List<DepartmentCertificationType>> GetAllCertificationTypesByDepartmentAsync(int departmentId)
		{
			var items = await _departmentCertificationTypeRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentCertificationType>();
		}

		public async Task<DepartmentCertificationType> GetCertificationTypeByIdAsync(int certificationTypeId)
		{
			return await _departmentCertificationTypeRepository.GetByIdAsync(certificationTypeId);
		}

		public async Task<bool> DeleteCertificationTypeByIdAsync(int certificationTypeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = await GetCertificationTypeByIdAsync(certificationTypeId);

			if (type != null)
				return await _departmentCertificationTypeRepository.DeleteAsync(type, cancellationToken);

			return false;
		}

		public async Task<DepartmentCertificationType> SaveNewCertificationTypeAsync(string certificationType, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			DepartmentCertificationType newCertType = new DepartmentCertificationType();
			newCertType.DepartmentId = departmentId;
			newCertType.Type = certificationType;

			return await _departmentCertificationTypeRepository.SaveOrUpdateAsync(newCertType, cancellationToken);
		}

		public async Task<bool> DoesCertificationTypeAlreadyExistAsync(int departmentId, string certificationTypeText)
		{
			var categories = await _departmentCertificationTypeRepository.GetAllByDepartmentIdAsync(departmentId);

			if (categories == null)
				return false;

			return categories.Any(x => x.Type == certificationTypeText.Trim());
		}

		public async Task<List<PersonnelCertification>> GetCertificationsByUserIdAsync(string userId)
		{
			var items = await _personnelCertificationRepository.GetCertificationsByUserAsync(userId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<PersonnelCertification>();
		}

		public async Task<List<string>> GetDepartmentCertificationTypesAsync(int departmentId)
		{
			var types = (from doc in await GetAllCertificationTypesByDepartmentAsync(departmentId)
							  select doc.Type).Distinct().ToList();

			return types;
		}

		public async Task<PersonnelCertification> SaveCertificationAsync(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The stored row backs REDACTED-sentinel restoration: an admin who edits a member's
			// certification without a grant posts back placeholders, and those must not be written
			// over the real values. Fetched before the save, while the id still identifies the
			// stored row rather than the incoming one.
			PersonnelCertification existing = null;
			if (certification != null && certification.PersonnelCertificationId > 0)
				existing = await _personnelCertificationRepository.GetByIdAsync(certification.PersonnelCertificationId);

			// ADP write safety net (plan 4.2/19.2). The AAD row key is the identity pk, so a NEW row
			// must be inserted before it can be enveloped, and that insert is a transient plaintext
			// write. An UPDATE already has its id, so it is enveloped BEFORE the save and no
			// plaintext ever reaches the table - which is the common path here, since a member
			// edits this data far more often than they first fill it in. Fails closed either way.
			var isExistingRow = certification != null && certification.PersonnelCertificationId > 0;

			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareCertificationWriteAsync(
					certification.DepartmentId, certification, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); certification {certification.PersonnelCertificationId} was NOT saved.");
			}

			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(certification, cancellationToken);

			if (!isExistingRow)
			{
				var protectedWrite = await _protectedWriteService.Value.PrepareCertificationWriteAsync(
					saved.DepartmentId, saved, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); certification {saved.PersonnelCertificationId} has transient plaintext pending re-encryption.");
				if (protectedWrite.Changed)
					saved = await _personnelCertificationRepository.SaveOrUpdateAsync(saved, cancellationToken);
			}

			return saved;
		}

		public async Task<PersonnelCertification> GetCertificationByIdAsync(int certificationId)
		{
			return await _personnelCertificationRepository.GetByIdAsync(certificationId);
		}

		public async Task<bool> DeleteCertification(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _personnelCertificationRepository.DeleteAsync(certification, cancellationToken);
		}

		public async Task<bool> DeleteAllCertificationsForUser(string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var certs = await GetCertificationsByUserIdAsync(userId);

			foreach (var cert in certs)
			{
				await _personnelCertificationRepository.DeleteAsync(cert, cancellationToken);
			}

			return true;
		}
	}
}
