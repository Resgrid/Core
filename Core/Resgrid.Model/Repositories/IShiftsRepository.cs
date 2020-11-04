using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Shift}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Shift}" />
	public interface IShiftsRepository: IRepository<Shift>
	{
		/// <summary>
		/// Gets the shift and days by shift identifier asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <returns>Task&lt;Shift&gt;.</returns>
		Task<Shift> GetShiftAndDaysByShiftIdAsync(int shiftId);

		/// <summary>
		/// Gets all shift and days asynchronous.
		/// </summary>
		/// <returns>Task&lt;IEnumerable&lt;Shift&gt;&gt;.</returns>
		Task<IEnumerable<Shift>> GetAllShiftAndDaysAsync();

		/// <summary>
		/// Gets the shift and days by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Shift&gt;&gt;.</returns>
		Task<IEnumerable<Shift>> GetShiftAndDaysByDepartmentIdAsync(int departmentId);
	}
}
