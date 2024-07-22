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

		public CertificationService(IDepartmentCertificationTypeRepository departmentCertificationTypeRepository, IPersonnelCertificationRepository personnelCertificationRepository)
		{
			_departmentCertificationTypeRepository = departmentCertificationTypeRepository;
			_personnelCertificationRepository = personnelCertificationRepository;
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
			return await _personnelCertificationRepository.SaveOrUpdateAsync(certification, cancellationToken);
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
