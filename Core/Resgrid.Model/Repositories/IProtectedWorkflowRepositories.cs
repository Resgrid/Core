using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowProtectedReleaseRepository : IRepository<WorkflowProtectedRelease>
	{
		/// <summary>The workflow's current release: the most recently created row (a Revoked row stays current until superseded).</summary>
		Task<WorkflowProtectedRelease> GetLatestByWorkflowIdAsync(string workflowId);

		Task<IEnumerable<WorkflowProtectedRelease>> GetAllByWorkflowIdAsync(string workflowId);

		Task<IEnumerable<WorkflowProtectedRelease>> GetAllByDepartmentIdAsync(int departmentId);

		Task<IEnumerable<WorkflowProtectedRelease>> GetAllByCredentialIdAsync(string workflowCredentialId);

		/// <summary>Cross-department read for the daily sweep.</summary>
		Task<IEnumerable<WorkflowProtectedRelease>> GetAllByStatesAsync(IEnumerable<ProtectedReleaseState> states);

		/// <summary>
		/// Writes the release only if its stored Version still equals <c>release.Version</c> (the value it was read with), then
		/// increments it. False means someone else changed the release first and nothing was written.
		/// </summary>
		Task<bool> TryUpdateAsync(WorkflowProtectedRelease release, CancellationToken cancellationToken = default);
	}

	public interface IProtectedWorkflowDisclosureRepository : IRepository<ProtectedWorkflowDisclosure>
	{
		/// <summary>
		/// Appends one record to the department's hash chain: links it to the current tail (Sequence, PrevHash, Hash)
		/// and inserts it, retrying on a concurrent append that took the same sequence number. Never updates a row.
		/// </summary>
		Task<ProtectedWorkflowDisclosure> AppendAsync(ProtectedWorkflowDisclosure record, CancellationToken cancellationToken = default);

		Task<ProtectedWorkflowDisclosure> GetLatestForDepartmentAsync(int departmentId);

		/// <summary>Filtered, newest first.</summary>
		Task<IEnumerable<ProtectedWorkflowDisclosure>> GetForDepartmentAsync(int departmentId, ProtectedWorkflowDisclosureFilter filter);

		/// <summary>The whole chain in sequence order, for verification.</summary>
		Task<IEnumerable<ProtectedWorkflowDisclosure>> GetChainForDepartmentAsync(int departmentId);
	}
}
