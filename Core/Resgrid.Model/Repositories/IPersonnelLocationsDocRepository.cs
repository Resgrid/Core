using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IPersonnelLocationsDocRepository
	{
		Task<List<PersonnelLocation>> GetAllLocationsByUnitIdAsync(string userId);
		Task<PersonnelLocation> GetLatestLocationsByUnitIdAsync(string userId);
		Task<List<PersonnelLocation>> GetLatestLocationsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// The department's member location history in [startUtc, endUtc), newest first, at most <paramref name="maxRows"/> rows.
		/// <paramref name="userIds"/> null or empty reads every member.
		/// </summary>
		Task<List<PersonnelLocation>> GetLocationsForDepartmentInRangeAsync(int departmentId, System.DateTime startUtc, System.DateTime endUtc, IReadOnlyCollection<string> userIds, int maxRows, System.Threading.CancellationToken cancellationToken = default);
		Task<PersonnelLocation> GetByIdAsync(string id);
		Task<PersonnelLocation> GetByOldIdAsync(string id);
		Task<PersonnelLocation> InsertAsync(PersonnelLocation location, System.Threading.CancellationToken cancellationToken = default);
		Task<PersonnelLocation> UpdateAsync(PersonnelLocation location, System.Threading.CancellationToken cancellationToken = default);
	}
}
