using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface ICertificationService
	{
		List<DepartmentCertificationType> GetAllCertificationTypesByDepartment(int departmentId);
		void DeleteCertificationTypeById(int certificationTypeId);
		DepartmentCertificationType GetCertificationTypeById(int certificationTypeId);
		DepartmentCertificationType SaveNewCertificationType(string certificationType, int departmentId);
		List<PersonnelCertification> GetCertificationsByUserId(string userId);
		List<string> GetDepartmentCertificationTypes(int departmentId);
		PersonnelCertification SaveCertification(PersonnelCertification certification);
		PersonnelCertification GetCertificationById(int certificationId);
		void DeleteCertification(PersonnelCertification certification);
		void DeleteAllCertificationsForUser(string userId);
	}
}