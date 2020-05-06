using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IShiftsRepository : IRepository<Shift>
	{
		List<Shift> GetAllShiftsAndDays();
	}
}
