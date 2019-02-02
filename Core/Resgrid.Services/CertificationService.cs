using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CertificationService : ICertificationService
	{
		private readonly IGenericDataRepository<DepartmentCertificationType> _departmentCertificationTypeRepository;
		private readonly IGenericDataRepository<PersonnelCertification> _personnelCertificationRepository;

		public CertificationService(IGenericDataRepository<DepartmentCertificationType> departmentCertificationTypeRepository, IGenericDataRepository<PersonnelCertification> personnelCertificationRepository)
		{
			_departmentCertificationTypeRepository = departmentCertificationTypeRepository;
			_personnelCertificationRepository = personnelCertificationRepository;
		}

		public List<DepartmentCertificationType> GetAllCertificationTypesByDepartment(int departmentId)
		{
			return _departmentCertificationTypeRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public DepartmentCertificationType GetCertificationTypeById(int certificationTypeId)
		{
			return _departmentCertificationTypeRepository.GetAll().SingleOrDefault(x => x.DepartmentCertificationTypeId == certificationTypeId);
		}

		public void DeleteCertificationTypeById(int certificationTypeId)
		{
			var type = GetCertificationTypeById(certificationTypeId);

			if (type != null)
				_departmentCertificationTypeRepository.DeleteOnSubmit(type);
		}

		public DepartmentCertificationType SaveNewCertificationType(string certificationType, int departmentId)
		{
			DepartmentCertificationType newCertType = new DepartmentCertificationType();
			newCertType.DepartmentId = departmentId;
			newCertType.Type = certificationType;

			_departmentCertificationTypeRepository.SaveOrUpdate(newCertType);

			return GetCertificationTypeById(newCertType.DepartmentCertificationTypeId);
		}

		public List<PersonnelCertification> GetCertificationsByUserId(string userId)
		{
			return _personnelCertificationRepository.GetAll().Where(x => x.UserId == userId).ToList();
		}

		public List<string> GetDepartmentCertificationTypes(int departmentId)
		{
			var types = (from doc in _departmentCertificationTypeRepository.GetAll()
							  where doc.DepartmentId == departmentId
							  select doc.Type).Distinct().ToList();

			return types;
		}

		public PersonnelCertification SaveCertification(PersonnelCertification certification)
		{
			_personnelCertificationRepository.SaveOrUpdate(certification);

			return certification;
		}

		public PersonnelCertification GetCertificationById(int certificationId)
		{
			return _personnelCertificationRepository.GetAll().SingleOrDefault(x => x.PersonnelCertificationId == certificationId);
		}

		public void DeleteCertification(PersonnelCertification certification)
		{
			_personnelCertificationRepository.DeleteOnSubmit(certification);
		}

		public void DeleteAllCertificationsForUser(string userId)
		{
			var certs = GetCertificationsByUserId(userId);
			_personnelCertificationRepository.DeleteAll(certs);
		}
	}
}
