using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Academy;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// A3 in-product guidance (plan §15): guided tours per role built on the walkthrough lessons, a "What's new" micro-lesson per release
	/// (both manifest-driven, dismissible and remembered per member) and the free Help-menu keyword search over the Academy and the Admin
	/// Assist reference corpus. No AI here; "how do I…" answers go through IAiKnowledgeService when the department has knowledge answers.
	/// </summary>
	public interface IAcademyGuidanceService
	{
		/// <summary>The dashboard card: the member's role tour while it is open, and the newest release lesson not yet dismissed.</summary>
		Task<AcademyGuidanceView> GetDashboardAsync(AcademyGuidanceActor actor, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The tour this member gets by default (administrator, dispatcher, training officer, member).</summary>
		Task<string> GetDefaultTourKeyAsync(AcademyGuidanceActor actor);

		/// <summary>Every tour in the member's language with their progress; the member's own role tour first.</summary>
		Task<System.Collections.Generic.List<AcademyTourView>> GetToursAsync(AcademyGuidanceActor actor, CancellationToken cancellationToken = default);

		/// <summary>Marks one task done (or not done); finishing the last task completes the tour. False for an unknown tour or task.</summary>
		Task<bool> SetTaskDoneAsync(AcademyGuidanceActor actor, string tourKey, string taskKey, bool done, CancellationToken cancellationToken = default);

		/// <summary>Closes a tour or a release lesson for this member ("tour:…" or "whatsnew:…"); it stays closed.</summary>
		Task<bool> DismissAsync(AcademyGuidanceActor actor, string guidanceKey, CancellationToken cancellationToken = default);

		/// <summary>Opens a dismissed or finished tour again, keeping the tasks done.</summary>
		Task<bool> ReopenTourAsync(AcademyGuidanceActor actor, string tourKey, CancellationToken cancellationToken = default);

		/// <summary>One release's lesson in the member's language (the newest when <paramref name="release"/> is empty).</summary>
		Task<AcademyWhatsNewView> GetWhatsNewAsync(AcademyGuidanceActor actor, string release, CancellationToken cancellationToken = default);

		/// <summary>Free keyword search over the lessons the department can see and the Admin Assist reference corpus.</summary>
		Task<AcademyHelpSearchResult> SearchAsync(AcademyGuidanceActor actor, string query, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Applies the policy of major Academy lesson revisions (A2): for each pending update, the completed learners of every department that
	/// assigned the lesson are notified or re-assigned (active members only), department by department with a stored cursor. Worker 88.
	/// </summary>
	public interface IAcademyManifestUpdateService
	{
		/// <summary>Processes pending updates; returns the number of learners notified or re-assigned.</summary>
		Task<int> ApplyPendingAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
