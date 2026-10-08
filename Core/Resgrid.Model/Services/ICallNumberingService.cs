using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department call numbering (setting 115 plus the CallNumberSequences counters): the number pattern, what each sequence
	/// issues next, and the number every new call receives.
	/// </summary>
	public interface ICallNumberingService
	{
		/// <summary>Takes the number for a call logged at <paramref name="loggedOnUtc"/> from its sequence. Each number is issued once.</summary>
		Task<string> AllocateCallNumberAsync(int departmentId, DateTime loggedOnUtc, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// What the next call logged at <paramref name="utcDate"/> would receive under <paramref name="config"/> (the saved
		/// configuration when null), without taking it.
		/// </summary>
		Task<CallNumberSequenceStatus> GetNextAsync(int departmentId, CallNumberingConfig config, DateTime utcDate);

		/// <summary>
		/// Saves the pattern and digit width, then raises the current sequence's next number when one is requested. A next number may
		/// only rise: one below what the sequence would already issue is refused, so a department never re-issues a number.
		/// </summary>
		Task<CallNumberingSaveResult> SaveAsync(int departmentId, string userId, CallNumberingUpdate update, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Renumbers the department's calls logged in the year containing <paramref name="inYearUtc"/> in logged order: the numbering
		/// year (a fiscal year when the department set its start) for a yearly pattern, the local calendar year for one with {MM} or
		/// {DD}. Each sequence starts at its raised starting point and skips numbers deleted calls still hold. Counters are moved past
		/// the year's numbers before any call is rewritten, so a call created meanwhile never shares a number. False, and nothing
		/// renumbered, when the pattern never restarts (a year cannot be renumbered on its own).
		/// </summary>
		Task<bool> RenumberCallsForYearAsync(int departmentId, DateTime inYearUtc, CancellationToken cancellationToken = default(CancellationToken));
	}
}
