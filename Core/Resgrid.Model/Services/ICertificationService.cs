using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICertificationService
	{
		/// <summary>
		/// Gets all certification types by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DepartmentCertificationType&gt;&gt;.</returns>
		Task<List<DepartmentCertificationType>> GetAllCertificationTypesByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the certification type by identifier asynchronous.
		/// </summary>
		/// <param name="certificationTypeId">The certification type identifier.</param>
		/// <returns>Task&lt;DepartmentCertificationType&gt;.</returns>
		Task<DepartmentCertificationType> GetCertificationTypeByIdAsync(int certificationTypeId);

		/// <summary>
		/// Deletes the certification type by identifier asynchronous.
		/// </summary>
		/// <param name="certificationTypeId">The certification type identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCertificationTypeByIdAsync(int certificationTypeId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the new certification type asynchronous.
		/// </summary>
		/// <param name="certificationType">Type of the certification.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentCertificationType&gt;.</returns>
		Task<DepartmentCertificationType> SaveNewCertificationTypeAsync(string certificationType, int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the certifications by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelCertification&gt;&gt;.</returns>
		Task<List<PersonnelCertification>> GetCertificationsByUserIdAsync(string userId);

		/// <summary>
		/// Gets the department certification types asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;System.String&gt;&gt;.</returns>
		Task<List<string>> GetDepartmentCertificationTypesAsync(int departmentId);

		/// <summary>
		/// Saves the certification asynchronous.
		/// </summary>
		/// <param name="certification">The certification.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;PersonnelCertification&gt;.</returns>
		Task<PersonnelCertification> SaveCertificationAsync(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the certification by identifier asynchronous.
		/// </summary>
		/// <param name="certificationId">The certification identifier.</param>
		/// <returns>Task&lt;PersonnelCertification&gt;.</returns>
		Task<PersonnelCertification> GetCertificationByIdAsync(int certificationId);

		/// <summary>
		/// Deletes the certification.
		/// </summary>
		/// <param name="certification">The certification.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteCertification(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes all certifications for user.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteAllCertificationsForUser(string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DoesCertificationTypeAlreadyExistAsync(int departmentId, string certificationTypeText);
	}
}
