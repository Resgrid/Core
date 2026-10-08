using System;
using System.Collections.Generic;
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

		/// <summary>
		/// Moves the scope's last sequence up to at least <paramref name="lastSequence"/> (never down; the floor is left as it is)
		/// and returns the value it now holds. Renumbering calls this before rewriting any call, so a call created meanwhile
		/// takes a sequence above every number the renumbering hands out.
		/// </summary>
		Task<int> RaiseLastSequenceAsync(int departmentId, string scopeKey, int lastSequence, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the scope's last sequence only while it still holds <paramref name="expectedLastSequence"/>, so a sequence taken
		/// by a concurrent call is never handed out again. True when the row was updated.
		/// </summary>
		Task<bool> TrySetLastSequenceAsync(int departmentId, string scopeKey, int lastSequence, int expectedLastSequence, CancellationToken cancellationToken = default);

		/// <summary>Numbers of the department's deleted calls logged in [fromUtc, toUtc). Deleted calls keep their numbers, so renumbering never reuses them.</summary>
		Task<List<string>> GetDeletedCallNumbersAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>
		/// Numbers of every department call logged in [fromUtc, toUtc), deleted calls included. Renumbering a numbering year holds the
		/// numbers written around it, which can share its text when the department changed its year start.
		/// </summary>
		Task<List<string>> GetCallNumbersAsync(int departmentId, DateTime fromUtc, DateTime toUtc);

		/// <summary>
		/// Highest sequence written as prefix + digits + suffix into the department's call numbers, deleted calls included, limited to
		/// calls logged in [fromUtc, toUtc) when given. The sequence is read as a number, so a change of digit width never restarts it.
		/// </summary>
		Task<int> GetHighestIssuedAsync(int departmentId, string numberPrefix, string numberSuffix, DateTime? fromUtc, DateTime? toUtc);
	}
}
