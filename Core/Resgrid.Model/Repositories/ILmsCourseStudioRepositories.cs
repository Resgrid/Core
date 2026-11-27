using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	/// <summary>Where a department course came from (LmsCourseOrigins, M0398): one row per drafted or cloned course.</summary>
	public interface ILmsCourseOriginsRepository : IRepository<LmsCourseOrigin>
	{
		Task<LmsCourseOrigin> GetForCourseAsync(int departmentId, int lmsCourseId);

		Task<List<LmsCourseOrigin>> GetForDepartmentAsync(int departmentId);
	}

	/// <summary>Course flashcards (LmsFlashcards, M0398).</summary>
	public interface ILmsFlashcardsRepository : IRepository<LmsFlashcard>
	{
		/// <summary>The course's cards in their order (every status).</summary>
		Task<List<LmsFlashcard>> GetForCourseAsync(int departmentId, int lmsCourseId);

		Task<LmsFlashcard> GetByIdForDepartmentAsync(int departmentId, int lmsFlashcardId);

		/// <summary>
		/// The supersede hook: every active card of the department drafted from <paramref name="sourceItemId"/> (of <paramref name="sourceKind"/>)
		/// against a version other than <paramref name="versionInForceId"/> is flagged NeedsReview (reason <paramref name="reason"/>). Returns
		/// how many were flagged. One update; cards already flagged keep their first flag time.
		/// </summary>
		Task<int> FlagOtherVersionsAsync(int departmentId, int sourceKind, int sourceItemId, int versionInForceId, int reason, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>Flags the named active cards (a learner read found their source gone). Returns how many were flagged.</summary>
		Task<int> FlagCardsAsync(int departmentId, IEnumerable<int> lmsFlashcardIds, int reason, DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
