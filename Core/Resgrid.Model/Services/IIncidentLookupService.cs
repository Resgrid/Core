using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>Pick lists and drill-through labels for the Incident Back Office desks (activation-scoped, department catalogs included).</summary>
	public interface IIncidentLookupService
	{
		Task<IReadOnlyList<IncidentLookupRow>> GetOptionsAsync(IncidentActor actor, string table, IReadOnlyList<string> activationIds, CancellationToken cancellationToken = default);
		Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(IncidentActor actor, string table, IEnumerable<string> ids, CancellationToken cancellationToken = default);
	}
}
