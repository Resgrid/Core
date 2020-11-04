using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftSignupTradeRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftSignupTrade}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftSignupTrade}" />
	public interface IShiftSignupTradeRepository: IRepository<ShiftSignupTrade>
	{
		/// <summary>
		/// Gets the shift signup trade by source shift signup identifier asynchronous.
		/// </summary>
		/// <param name="shiftSignupId">The shift signup identifier.</param>
		/// <returns>Task&lt;ShiftSignupTrade&gt;.</returns>
		Task<ShiftSignupTrade> GetShiftSignupTradeBySourceShiftSignupIdAsync(int shiftSignupId);

		/// <summary>
		/// Gets the shift signup trade by target shift signup identifier asynchronous.
		/// </summary>
		/// <param name="shiftSignupId">The shift signup identifier.</param>
		/// <returns>Task&lt;ShiftSignupTrade&gt;.</returns>
		Task<ShiftSignupTrade> GetShiftSignupTradeByTargetShiftSignupIdAsync(int shiftSignupId);

		/// <summary>
		/// Gets all open trade requests by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftSignupTrade&gt;&gt;.</returns>
		Task<IEnumerable<ShiftSignupTrade>> GetAllOpenTradeRequestsByUserIdAsync(string userId);

		/// <summary>
		/// Gets the trade requests and source shifts by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftSignupTrade&gt;&gt;.</returns>
		Task<IEnumerable<ShiftSignupTrade>> GetTradeRequestsAndSourceShiftsByUserIdAsync(string userId);
	}
}
