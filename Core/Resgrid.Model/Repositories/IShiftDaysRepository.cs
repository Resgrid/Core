using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftDaysRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftDay}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftDay}" />
	public interface IShiftDaysRepository: IRepository<ShiftDay>
	{
		/// <summary>
		/// Gets all shift days by shift identifier asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftDay&gt;&gt;.</returns>
		Task<IEnumerable<ShiftDay>> GetAllShiftDaysByShiftIdAsync(int shiftId);

		/// <summary>
		/// Gets the shift day by identifier asynchronous.
		/// </summary>
		/// <param name="shiftDayId">The shift day identifier.</param>
		/// <returns>Task&lt;ShiftDay&gt;.</returns>
		Task<ShiftDay> GetShiftDayByIdAsync(int shiftDayId);
	}
}
