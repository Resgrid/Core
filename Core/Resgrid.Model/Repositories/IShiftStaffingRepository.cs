using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftStaffingRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftStaffing}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftStaffing}" />
	public interface IShiftStaffingRepository: IRepository<ShiftStaffing>
	{
		/// <summary>
		/// Gets the shift staffing by shift day asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <param name="shiftDay">The shift day.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftStaffing&gt;&gt;.</returns>
		Task<ShiftStaffing> GetShiftStaffingByShiftDayAsync(int shiftId, DateTime shiftDay);
	}
}
