using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Create Deployment from External Order (RMS plan section 4.1 "external-order fill contract", RMS-1C, Preview).
	/// Manual entry and artifact snapshot only: no IROC, CIFFC or member-agency connector, no write-back, no inferred
	/// order updates. Every deployment is a Record on the mutual-aid deployment definition, so lifecycle, audit,
	/// revisions, retention and Workflow events are the ordinary Records ones.
	/// </summary>
	public interface IRecordDeploymentsService
	{
		Task<RecordDeploymentAggregate> CreateFromExternalOrderAsync(int departmentId, string userId, RecordDeploymentCreateInput input, CancellationToken cancellationToken = default);
		Task<RecordDeploymentAggregate> GetAsync(int departmentId, string userId, string orderId, bool includeArtifact = false);
		Task<RecordDeploymentAggregate> GetForRecordAsync(int departmentId, string userId, string recordId);
		Task<List<RmsExternalOrder>> ListAsync(int departmentId, string userId, bool includeClosed);
		/// <summary>
		/// A bounded page of deployments with their fills, loaded in one pass. The list shape only needs the order,
		/// its fills and the Record's number/state, so this deliberately skips the full Record hydrate and the
		/// jurisdiction profiles that <see cref="GetAsync"/> loads; use GetAsync for a single deployment.
		/// </summary>
		Task<List<RecordDeploymentAggregate>> ListAggregatesAsync(int departmentId, string userId, bool includeClosed, int take);
		Task<RmsExternalOrderFill> AddFillAsync(int departmentId, string userId, string orderId, RecordDeploymentFillInput input, CancellationToken cancellationToken = default);
		Task<RmsExternalOrderFill> TransitionFillAsync(int departmentId, string userId, string fillId, RecordDeploymentFillTransitionInput input, CancellationToken cancellationToken = default);
		/// <summary>Records a later snapshot of the same external order (a new versioned artifact); never overwrites signed history.</summary>
		Task<RmsExternalOrder> RecordSourceSnapshotAsync(int departmentId, string userId, string orderId, string sourceVersion, byte[] artifact, string fileName, string contentType, CancellationToken cancellationToken = default);
		/// <summary>Closeout requires every accepted fill to have actually returned to its home unit.</summary>
		Task<RmsExternalOrder> CloseoutAsync(int departmentId, string userId, string orderId, long expectedRowVersion, string notes, CancellationToken cancellationToken = default);
		/// <summary>The definition key a department's deployments use (provisioned from the pack on first use).</summary>
		Task<string> EnsureDeploymentDefinitionAsync(int departmentId, string userId, string profileKey, CancellationToken cancellationToken = default);
	}
}
