using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Department call-number sequences (CallNumberSequences, M0264). Every write is one atomic statement per dialect.</summary>
	public interface ICallNumberSequencesRepository
	{
		Task<CallNumberSequence> GetSequenceAsync(int departmentId, string scopeKey);

		/// <summary>
		/// Takes the next sequence in a scope and returns it. A scope with no row yet starts at <paramref name="seed"/> + 1, so the
		/// caller seeds it with the highest sequence already written into a call number of that scope.
		/// </summary>
		Task<int> TakeNextAsync(int departmentId, string scopeKey, int seed, CancellationToken cancellationToken = default);

		/// <summary>Makes the scope issue at least <paramref name="nextSequence"/> next and records it as the scope's raised starting point. Never lowers either.</summary>
		Task RaiseFloorAsync(int departmentId, string scopeKey, int nextSequence, string userId, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Sets the last sequence issued after a renumbering; the floor is left as it is.</summary>
		Task SetLastSequenceAsync(int departmentId, string scopeKey, int lastSequence, CancellationToken cancellationToken = default);

		/// <summary>
		/// Highest sequence written as prefix + digits + suffix into the department's call numbers, deleted calls included, limited to
		/// calls logged in [fromUtc, toUtc) when given. The sequence is read as a number, so a change of digit width never restarts it.
		/// </summary>
		Task<int> GetHighestIssuedAsync(int departmentId, string numberPrefix, string numberSuffix, DateTime? fromUtc, DateTime? toUtc);
	}
}
