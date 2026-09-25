using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>A saved call supplies route identifiers, never historical recipient evidence.</summary>
	public sealed record DispatchImpactRequest(string ExpectedRevision, int CallId, DateTime SimulationTimeUtc,
		bool ShiftInsteadOfGroup, bool UnitCrew, bool UnitGroup);
	public interface IDispatchImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, DispatchImpactRequest request, CancellationToken cancellationToken = default);
	}
}
