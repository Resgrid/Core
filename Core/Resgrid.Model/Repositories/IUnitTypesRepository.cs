using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitTypesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitType}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitType}" />
	public interface IUnitTypesRepository: IRepository<UnitType>
	{
		/// <summary>
		/// Gets the unit by name department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;UnitType&gt;.</returns>
		Task<UnitType> GetUnitByNameDepartmentIdAsync(int departmentId, string name);
	}
}
