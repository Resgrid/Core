using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallProtocolsRepository
	/// Implements the <see cref="CallProtocol" />
	/// </summary>
	/// <seealso cref="CallProtocol" />
	public interface ICallProtocolsRepository: IRepository<CallProtocol>
	{
		/// <summary>
		/// Gets the call protocols by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallProtocol&gt;&gt;.</returns>
		Task<IEnumerable<CallProtocol>> GetCallProtocolsByCallIdAsync(int callId);

		/// <summary>
		/// How many of the department's calls logged since <paramref name="sinceUtc"/> carry each protocol (removed rows not
		/// counted).
		/// </summary>
		Task<IEnumerable<ProtocolUsageCount>> GetUsageCountsAsync(int departmentId, System.DateTime sinceUtc);

		/// <summary>One call protocol row (step log included) when the call is the department's; null otherwise.</summary>
		Task<CallProtocol> GetForDepartmentCallAsync(int departmentId, int callId, int callProtocolId);

		/// <summary>
		/// Writes a call protocol's step log (P3, M0379) when its revision is still <paramref name="expectedRevision"/>, bumping it; false
		/// when someone wrote it in between (the caller re-reads and re-applies). Touches only the step-log columns.
		/// </summary>
		Task<bool> TryUpdateStepLogAsync(int callProtocolId, string stepLogJson, int expectedRevision, System.Threading.CancellationToken cancellationToken = default);

		/// <summary>
		/// P4: writes a call protocol's case (determinant in force, override reason, case record) alone, never the rest of the row. False
		/// when the row is not on one of the department's calls.
		/// </summary>
		Task<bool> TryUpdateCaseAsync(int departmentId, int callProtocolId, string determinant, string overrideReason, string caseJson,
			System.Threading.CancellationToken cancellationToken = default);
	}
}
