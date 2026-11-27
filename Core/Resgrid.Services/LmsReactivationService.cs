using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Lapse rule 12 (§17.3): the reactivation re-base the requirement engine (T3) calls for paused due dates. Reads the department's
	/// recorded lapses (LmsAddonLapses, written by the gate) and applies <see cref="LmsLapseRebase"/> with
	/// <see cref="LmsAddonConfig.ReactivationGraceDays"/>. One read per call; never called for every department (R-L9).
	/// </summary>
	public class LmsReactivationService : ILmsReactivationService
	{
		private readonly ILmsAddonLapsesRepository _lapses;

		public LmsReactivationService(ILmsAddonLapsesRepository lapses)
		{
			_lapses = lapses;
		}

		public async Task<List<LmsAddonLapse>> GetLapsesAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<LmsAddonLapse>();

			return (await _lapses.GetForDepartmentAsync(departmentId) ?? new List<LmsAddonLapse>())
				.Where(l => l != null && l.DepartmentId == departmentId && l.EndedOn.HasValue)
				.OrderBy(l => l.StartedOn)
				.ToList();
		}

		public async Task<DateTime?> GetLastReactivationAsync(int departmentId)
		{
			var lapses = await GetLapsesAsync(departmentId);
			return lapses.Count == 0 ? null : lapses.Max(l => l.EndedOn);
		}

		public async Task<List<LmsRebasedDue>> RebaseAsync(int departmentId, IEnumerable<LmsDueItem> items)
		{
			var list = (items ?? Enumerable.Empty<LmsDueItem>()).Where(i => i != null).ToList();
			if (list.Count == 0)
				return new List<LmsRebasedDue>();

			var lapses = await GetLapsesAsync(departmentId);
			var grace = TimeSpan.FromDays(Math.Max(0, LmsAddonConfig.ReactivationGraceDays));
			return list.Select(item => LmsLapseRebase.Rebase(item, lapses, grace)).ToList();
		}
	}
}
