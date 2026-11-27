using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Protocol flashcards (ProtocolFlashcards, M0399).</summary>
	public interface IProtocolFlashcardsRepository : IRepository<ProtocolFlashcard>
	{
		/// <summary>The protocol's cards in their order (every status).</summary>
		Task<List<ProtocolFlashcard>> GetForProtocolAsync(int departmentId, int dispatchProtocolId);

		Task<ProtocolFlashcard> GetByIdForDepartmentAsync(int departmentId, int protocolFlashcardId);

		/// <summary>
		/// The supersede hook: every active card of the protocol drafted against a version other than <paramref name="versionInForceId"/> is
		/// flagged NeedsReview (reason <paramref name="reason"/>). Returns how many were flagged. One update; cards already flagged keep their first
		/// flag time.
		/// </summary>
		Task<int> FlagOtherVersionsAsync(int departmentId, int dispatchProtocolId, int versionInForceId, int reason, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>Flags the named active cards (their source is gone). Returns how many were flagged.</summary>
		Task<int> FlagCardsAsync(int departmentId, IEnumerable<int> protocolFlashcardIds, int reason, DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
