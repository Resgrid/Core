using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftSignupRepository
	/// Implements the <see cref="ShiftSignup" />
	/// </summary>
	/// <seealso cref="ShiftSignup" />
	public interface IShiftSignupRepository: IRepository<ShiftSignup>
	{
		/// <summary>
		/// Gets all shift signups by shift identifier asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftSignup&gt;&gt;.</returns>
		Task<IEnumerable<ShiftSignup>> GetAllShiftSignupsByShiftIdAsync(int shiftId);

		/// <summary>
		/// Gets all shift signups by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftSignup&gt;&gt;.</returns>
		Task<IEnumerable<ShiftSignup>> GetAllShiftSignupsByUserIdAsync(string userId);

		/// <summary>
		/// Gets all shift signups by shift identifier and date asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <param name="shiftDayDate">The shift day date.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftSignup&gt;&gt;.</returns>
		Task<IEnumerable<ShiftSignup>> GetAllShiftSignupsByShiftIdAndDateAsync(int shiftId, DateTime shiftDayDate);

		Task<IEnumerable<ShiftSignup>> GetAllShiftSignupsByGroupIdAndDateAsync(int departmentGroupId, DateTime shiftDayDate);
	}
}
