using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UnitStatesService: IUnitStatesService
	{
		private readonly IUnitStatesRepository _unitStatesRepository;

		public UnitStatesService(IUnitStatesRepository unitStatesRepository)
		{
			_unitStatesRepository = unitStatesRepository;
		}

		public async Task<List<UnitState>> GetAllStatesForUnitInDateRangeAsync(int unitId, DateTime start, DateTime end)
		{
			var states = await _unitStatesRepository.GetAllUnitStatesForUnitInDateRangeAsync(unitId, start, end);

			return states?.ToList();
		}
	}
}
