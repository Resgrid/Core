using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The LMS part of a member's GDPR export (round 5F, "lms_learning.json"), registered as an <see cref="IGdprExportSection"/> so the baseline
	/// export service never references LMS types (§17.3): the member's course enrollments, assessment attempts (their answers and which were
	/// right, never the drawn questions' keys: the snapshot is not even read), learning requirement status rows (T3), training requests (T3
	/// programs), protocol acknowledgment quiz attempts and weekly micro-learning answers. Only the member's own rows in the department.
	/// Unified Search keeps no projection of requirement rows on purpose: they are personal records, found through the matrix and the
	/// member's own page, never through search.
	/// </summary>
	public class LmsGdprExportSection : IGdprExportSection
	{
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsAssessmentAttemptsRepository _attempts;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsRequirementsRepository _requirements;
		private readonly ILmsRequirementMembersRepository _requirementMembers;
		private readonly ILmsTrainingRequestsRepository _requests;
		private readonly ILmsProtocolQuizAttemptsRepository _quizAttempts;
		private readonly ILmsMicroLearningRepository _micro;

		public LmsGdprExportSection(ILmsEnrollmentsRepository enrollments, ILmsAssessmentAttemptsRepository attempts, ILmsCoursesRepository courses,
			ILmsRequirementsRepository requirements, ILmsRequirementMembersRepository requirementMembers, ILmsTrainingRequestsRepository requests,
			ILmsProtocolQuizAttemptsRepository quizAttempts, ILmsMicroLearningRepository micro)
		{
			_enrollments = enrollments;
			_attempts = attempts;
			_courses = courses;
			_requirements = requirements;
			_requirementMembers = requirementMembers;
			_requests = requests;
			_quizAttempts = quizAttempts;
			_micro = micro;
		}

		public string EntryName => "lms_learning.json";

		private static bool Mine(string rowUserId, string userId) => string.Equals(rowUserId, userId, StringComparison.OrdinalIgnoreCase);

		private static string Name<TEnum>(int value) where TEnum : struct, Enum =>
			Enum.IsDefined(typeof(TEnum), value) ? ((TEnum)(object)value).ToString() : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

		public async Task<object> BuildAsync(string userId, int departmentId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new { };

			var enrollments = (await _enrollments.GetForUserAsync(departmentId, userId) ?? new List<LmsEnrollment>())
				.Where(e => e.DepartmentId == departmentId && Mine(e.UserId, userId)).ToList();
			var courseIds = enrollments.Select(e => e.LmsCourseId).Distinct().ToList();
			var courses = courseIds.Count == 0 ? new List<LmsCourse>() : await _courses.GetVisibleByIdsAsync(departmentId, courseIds) ?? new List<LmsCourse>();
			var titles = courses.GroupBy(c => c.LmsCourseId).ToDictionary(g => g.Key, g => g.First().Title);

			var attempts = (await _attempts.GetForUserWithoutSnapshotAsync(departmentId, userId) ?? new List<LmsAssessmentAttempt>())
				.Where(a => a.DepartmentId == departmentId && Mine(a.UserId, userId)).ToList();

			var requirements = (await _requirements.GetForDepartmentAsync(departmentId, true) ?? new List<LmsRequirement>()).ToDictionary(r => r.LmsRequirementId);
			var requirementRows = (await _requirementMembers.GetOpenForUserAsync(departmentId, userId) ?? new List<LmsRequirementMember>())
				.Where(m => m.DepartmentId == departmentId && Mine(m.UserId, userId)).ToList();

			var requests = (await _requests.GetForUserAsync(departmentId, userId) ?? new List<LmsTrainingRequest>())
				.Where(r => r.DepartmentId == departmentId && Mine(r.UserId, userId)).ToList();

			var quizAttempts = (await _quizAttempts.GetForUserAsync(departmentId, userId) ?? new List<LmsProtocolQuizAttempt>())
				.Where(a => a.DepartmentId == departmentId && Mine(a.UserId, userId)).ToList();

			var micro = (await _micro.GetForUserAsync(departmentId, userId, 0) ?? new List<LmsMicroLearningItem>())
				.Where(i => i.DepartmentId == departmentId && Mine(i.UserId, userId)).ToList();

			return new
			{
				Enrollments = enrollments.Select(e => new
				{
					e.LmsEnrollmentId,
					e.LmsCourseId,
					Course = titles.TryGetValue(e.LmsCourseId, out var title) ? title : null,
					e.LmsCourseVersionId,
					Source = Name<LmsEnrollmentSources>(e.Source),
					Status = Name<LmsEnrollmentStatuses>(e.Status),
					e.AssignedOn,
					e.DueOn,
					e.StartedOn,
					e.CompletedOn,
					e.TestedOut,
					e.ProgressPercent,
					e.SeatSeconds,
					e.WithdrawnOn
				}).ToList(),
				AssessmentAttempts = attempts.Select(a => new
				{
					a.LmsAssessmentAttemptId,
					a.LmsCourseVersionId,
					a.LmsLessonId,
					a.AttemptNumber,
					Status = Name<LmsAttemptStatuses>(a.Status),
					a.StartedOn,
					a.SubmittedOn,
					a.Score,
					a.MaxScore,
					a.Percent,
					a.Passed,
					a.PassMark,
					a.IsPreTest,
					// The member's own answers and which were right; the questions' keys are not part of the member's data.
					Answers = LmsAttemptAnswer.Parse(a.AnswersJson).Select(x => new { x.QuestionId, x.Keys, x.Text }).ToList(),
					Results = LmsQuestionResult.Parse(a.ResultsJson).Select(r => new { r.QuestionId, r.Correct, r.Awarded, r.Points }).ToList(),
					a.GradedOn,
					a.GradeNotes
				}).ToList(),
				RequirementStatus = requirementRows.Select(m => new
				{
					m.LmsRequirementMemberId,
					m.LmsRequirementId,
					Requirement = requirements.TryGetValue(m.LmsRequirementId, out var requirement) ? requirement.Name : null,
					State = Name<LmsRequirementStates>(m.State),
					m.EnteredOn,
					m.LeftOn,
					m.NotRequired,
					m.StateChangedOn,
					m.DueOn,
					m.LastCompletedOn,
					m.ExemptOn,
					m.ExemptReason,
					m.ExemptUntil,
					m.ExtendedDueOn,
					m.ExtensionReason
				}).ToList(),
				TrainingRequests = requests.Select(r => new
				{
					r.LmsTrainingRequestId,
					r.Title,
					r.Provider,
					r.Location,
					r.CourseCode,
					r.StartsOn,
					r.EndsOn,
					r.Hours,
					r.CostTuition,
					r.CostTravel,
					r.CostOther,
					r.Currency,
					r.Justification,
					Status = Name<LmsTrainingRequestStatuses>(r.Status),
					r.SubmittedOn,
					r.DecidedOn,
					r.CompletedOn,
					r.ActualCost
				}).ToList(),
				ProtocolQuizAttempts = quizAttempts.Select(a => new
				{
					a.LmsProtocolQuizAttemptId,
					a.DispatchProtocolId,
					a.ProtocolVersionId,
					a.AttemptNumber,
					Status = Name<LmsProtocolQuizAttemptStatuses>(a.Status),
					a.StartedOn,
					a.SubmittedOn,
					a.Percent,
					a.Passed,
					a.PassMark,
					Answers = LmsAttemptAnswer.Parse(a.AnswersJson).Select(x => new { x.QuestionId, x.Keys }).ToList(),
					Results = LmsQuestionResult.Parse(a.ResultsJson).Select(r => new { r.QuestionId, r.Correct }).ToList()
				}).ToList(),
				MicroLearning = micro.Select(i => new
				{
					i.LmsMicroLearningItemId,
					i.DispatchProtocolId,
					i.WeekKey,
					Question = LmsAttemptSnapshot.Parse(i.SnapshotJson).Questions.Select(q => q.Text).FirstOrDefault(),
					i.SentOn,
					i.AnsweredOn,
					Answer = ParseKeys(i.AnswersJson),
					i.Correct
				}).ToList()
			};
		}

		private static List<string> ParseKeys(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<string>();
			try
			{
				return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
			}
			catch (JsonException)
			{
				return new List<string>();
			}
		}
	}
}
