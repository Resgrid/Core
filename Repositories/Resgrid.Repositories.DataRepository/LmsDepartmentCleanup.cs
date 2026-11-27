using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// The department's LMS rows (Training T2, M0349/M0350) in the caller's transaction, children first, for both databases. Every
	/// LMS table carries DepartmentId; system-published content (the Academy) has DepartmentId 0 and is never touched. A department's
	/// enrollments, progress and attempts on Academy courses go with it. LMS rows reference learning records, certifications and
	/// protocol versions by id only, so this runs before or after those cleanups alike.
	/// </summary>
	public static class LmsDepartmentCleanup
	{
		/// <summary>The tables this cleanup clears, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"LmsCrewViewingAttendees", "LmsLessonProgress", "LmsAssessmentAttempts", "LmsLessonFiles", "LmsSurveyResponses", "LmsCrewViewings",
			"LmsEnrollments", "LmsPathAssignments", "LmsDepartmentStates", "LmsLessons", "LmsCourseModules", "LmsCourseVersionApprovals",
			"LmsCourseVersions", "LmsCourseAudiences", "LmsCourseOwners", "LmsCourses", "LmsQuestions", "LmsQuestionBanks", "LmsMediaAssets",
			// Training T3 (M0364-M0366): requirement members, actions and audiences before their requirements, steps before their paths.
			// System (Academy) paths and their steps have DepartmentId 0 and are never touched.
			"LmsRequirementEnforcements", "LmsRequirementMembers", "LmsRequirementAudiences", "LmsRequirements", "LmsRequirementDepartmentStates",
			"LmsPathSteps", "LmsPaths",
			// Round 5F (M0387): protocol learning settings, acknowledgment quiz attempts and the weekly micro-learning questions and clock.
			"LmsProtocolQuizAttempts", "LmsMicroLearningItems", "LmsMicroLearningRuns", "LmsProtocolLearnings",
			// Round 6F (M0398): course flashcards and course origins (AI draft provenance, starter attribution).
			"LmsFlashcards", "LmsCourseOrigins"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "LMS");
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
