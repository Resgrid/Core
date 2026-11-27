using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The K2 authoring aids (plan §16.4, §24 K2): quiz questions with explanations, a course outline, and a change summary from a version
	/// diff, drafted on the operator's model from the given content only. Gated like K1's knowledge answers (Enhanced AI with Ai.Knowledge,
	/// fails closed: the free tier sees nothing), refused for "no AI" content and while Advanced Data Protection keeps content local, and
	/// validated so no number appears that the source does not contain. Every draft is logged to AiGenerations. Nothing is persisted to
	/// the content: the author accepts what they keep, and the editor saves it.
	/// </summary>
	public interface IAuthoringAidService
	{
		/// <summary>Whether the member's department has the aids right now (the panel stays hidden otherwise).</summary>
		Task<AuthoringAidStatus> GetStatusAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		Task<AuthoringAidDraft> DraftAsync(AuthoringAidRequest request, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// One kind of content an aid can be drafted from (protocol versions, trainings, LMS course versions). The source decides whether the
	/// member may author it, whether it is excluded from AI, and what plain text (or diff) grounds the draft. A kind no source claims is
	/// never drafted from (fail closed).
	/// </summary>
	public interface IAuthoringAidSource
	{
		AuthoringAidSourceKinds Kind { get; }

		Task<AuthoringAidSourceResult> LoadAsync(AuthoringAidRequest request, CancellationToken cancellationToken = default);
	}
}
