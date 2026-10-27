using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	public class IncidentLookupService : IIncidentLookupService
	{
		private readonly IIncidentLookupRepository _lookups;

		public IncidentLookupService(IIncidentLookupRepository lookups)
		{
			_lookups = lookups;
		}

		public Task<IReadOnlyList<IncidentLookupRow>> GetOptionsAsync(IncidentActor actor, string table, IReadOnlyList<string> activationIds, CancellationToken cancellationToken = default)
			=> _lookups.GetOptionsAsync(table, actor.DepartmentId, activationIds, true, 1000, cancellationToken);

		public async Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(IncidentActor actor, string table, IEnumerable<string> ids, CancellationToken cancellationToken = default)
		{
			var rows = await _lookups.GetLabelsAsync(table, actor.DepartmentId, ids?.ToList() ?? new List<string>(), cancellationToken);
			return rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
		}
	}
}
