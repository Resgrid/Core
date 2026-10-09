using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Department document-number sequences for custom patterns (DocumentNumberSequences, M0268). Every write is one atomic statement per dialect.</summary>
	public interface IDocumentNumberSequencesRepository
	{
		Task<DocumentNumberSequence> GetSequenceAsync(int departmentId, string kind, string scopeKey);

		/// <summary>
		/// Takes the next sequence in a scope and returns it. A scope with no row yet starts at <paramref name="seed"/> + 1, so the
		/// caller seeds it with the highest sequence already written into a number of that scope.
		/// </summary>
		Task<int> TakeNextAsync(int departmentId, string kind, string scopeKey, int seed, CancellationToken cancellationToken = default);

		/// <summary>Makes the scope issue at least <paramref name="nextSequence"/> next and records it as the raised starting point. Never lowers either.</summary>
		Task RaiseFloorAsync(int departmentId, string kind, string scopeKey, int nextSequence, string userId, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>
		/// Highest sequence written as prefix + digits + suffix into the department's numbers of <paramref name="kind"/> (the
		/// kind's own number column; built-in and custom numbers alike, deleted rows included). The sequence is read as a number,
		/// so a change of digit width never restarts it. 0 for an unknown kind.
		/// </summary>
		Task<int> GetHighestIssuedAsync(int departmentId, string kind, string numberPrefix, string numberSuffix);
	}
}
