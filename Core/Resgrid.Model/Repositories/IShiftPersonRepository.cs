using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftPersonRepository
	/// Implements the <see cref="ShiftPerson" />
	/// </summary>
	/// <seealso cref="ShiftPerson" />
	public interface IShiftPersonRepository: IRepository<ShiftPerson>
	{
		/// <summary>
		/// Gets all shift persons by shift identifier asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftPerson&gt;&gt;.</returns>
		Task<IEnumerable<ShiftPerson>> GetAllShiftPersonsByShiftIdAsync(int shiftId);
	}
}
