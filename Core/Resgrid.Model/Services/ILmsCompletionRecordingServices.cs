using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Round 7B (permission 135, LMS add-on for the course path): record that members completed an LMS course, from a course's enrolment list
	/// (manual, a note required) or a class roster (the session's linked LMS course or baseline training). Every check of the baseline path
	/// applies (permission and group scope, active member, never yourself, the attestation), plus the LMS seat (§17.4: a member without a
	/// seat cannot get an LMS course completion recorded; the baseline-training path still works) and the add-on. External learners only from
	/// a class roster that has them. The completion runs through the normal path (enrolment, every lesson, LmsCourseCompletion record,
	/// certification issue/renew) with the recorded-by metadata.
	/// </summary>
	public interface ILmsCompletionRecordingService
	{
		Task<TrainingCompletionRecordingOutcome> RecordCourseAsync(int departmentId, int lmsCourseId, TrainingCompletionRecordingRequest request,
			CancellationToken cancellationToken = default);

		/// <summary>From a class roster: the selected people must be on the roster; the session must have started and not be cancelled.</summary>
		Task<TrainingCompletionRecordingOutcome> RecordFromClassAsync(int departmentId, int lmsClassSessionId, TrainingCompletionRecordingRequest request,
			CancellationToken cancellationToken = default);

		/// <summary>The roster panel: each row's eligibility, the default selection (attended) and the recordings already made.</summary>
		Task<LmsClassRecordingView> GetClassRecordingViewAsync(int departmentId, int lmsClassSessionId, string viewerUserId);

		/// <summary>The record page of an LMS course: every active member (open enrolments first) with their eligibility and recordings.</summary>
		Task<TrainingCompletionRecordingView> GetCourseRecordingViewAsync(int departmentId, int lmsCourseId, string viewerUserId);
	}
}
