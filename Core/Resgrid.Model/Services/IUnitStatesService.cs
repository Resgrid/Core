

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IUnitStatesService
	{
		Task<List<UnitState>> GetAllStatesForUnitInDateRangeAsync(int unitId, DateTime start, DateTime end);
	}
}
