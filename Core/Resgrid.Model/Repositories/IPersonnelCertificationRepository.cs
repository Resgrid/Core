using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPersonnelCertificationRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PersonnelCertification}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.PersonnelCertification}" />
	public interface IPersonnelCertificationRepository: IRepository<PersonnelCertification>
	{
		/// <summary>
		/// Gets the certifications by user asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;PersonnelCertification&gt;&gt;.</returns>
		Task<IEnumerable<PersonnelCertification>> GetCertificationsByUserAsync(string userId);
	}
}
