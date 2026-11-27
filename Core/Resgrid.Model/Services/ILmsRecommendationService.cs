using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Learning path and course recommendations (K3, protocols-training-knowledge-plan §13a "path recommendations from role, requirements and
	/// gaps", LMS): a deterministic ranking from the member's open learning requirements (T3), the learning paths they are on, the competency
	/// gap report (T3 programs) and the courses offered to their roles, with an optional AI explanation written only from those reasons.
	/// A member sees their own; LMS managers, department-wide Assign Training holders and group admins (their group) see the members in their
	/// scope. Needs the LMS add-on (flag and an active add-on); the explanation also needs Enhanced AI with Ai.Knowledge. Fails closed.
	/// </summary>
	public interface ILmsRecommendationService
	{
		/// <summary>The member's recommendations, best first, or an error (NotAuthorized outside the actor's scope, AddonRequired).</summary>
		Task<LmsRecommendationSet> GetForMemberAsync(LmsActor actor, TrainingManagementScope scope, string memberUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>The members the actor may see recommendations for (themselves first, then their scope; active members only).</summary>
		Task<List<LmsRecommendationMember>> GetMembersInScopeAsync(LmsActor actor, TrainingManagementScope scope);

		/// <summary>The optional AI explanation of the member's recommendations (logged to AiGenerations; nothing is saved).</summary>
		Task<LmsRecommendationExplanation> ExplainAsync(LmsActor actor, TrainingManagementScope scope, string memberUserId, string locale, DateTime utcNow,
			CancellationToken cancellationToken = default);
	}
}
