using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IShiftSignupTradeUserShiftsRepository: IRepository<ShiftSignupTradeUserShift>
	{
		/// <summary>
		/// Gets the trade offers that put up the given signup as a swap-back day.
		/// </summary>
		Task<IEnumerable<ShiftSignupTradeUserShift>> GetShiftSignupTradeUserShiftsBySignupIdAsync(int shiftSignupId);
	}
}
