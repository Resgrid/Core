using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftSignupTradeUserRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftSignupTradeUser}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftSignupTradeUser}" />
	public interface IShiftSignupTradeUserRepository: IRepository<ShiftSignupTradeUser>
	{
		/// <summary>
		/// Gets the shift signup trade users by trade identifier asynchronous.
		/// </summary>
		/// <param name="shiftTradeId">The shift trade identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftSignupTradeUser&gt;&gt;.</returns>
		Task<IEnumerable<ShiftSignupTradeUser>> GetShiftSignupTradeUsersByTradeIdAsync(int shiftTradeId);
	}
}
