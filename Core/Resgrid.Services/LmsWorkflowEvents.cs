using System;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;

namespace Resgrid.Services
{
	/// <summary>
	/// The LMS layer's builders of the round-7C workflow payloads (triggers 259-269): course, skill, enforcement, training request and
	/// add-on/export events from LMS entities. They live here (an Lms-named type) so baseline code never depends on a course type (§17.3).
	/// Ids, names of the content, dates, statuses and scores only: never an answer, a key, a note, a justification or contact details.
	/// </summary>
	public static class LmsWorkflowEvents
	{
		public static CourseWorkflowEvent VersionPublished(LmsCourse course, LmsCourseVersion version, string actorUserId, DateTime utcNow) => new CourseWorkflowEvent
		{
			DepartmentId = course.DepartmentId,
			Change = CourseWorkflowChanges.VersionPublished,
			CourseId = course.LmsCourseId,
			CourseVersionId = version?.LmsCourseVersionId,
			CourseCode = course.Code,
			CourseTitle = version?.Title ?? course.Title,
			VersionLabel = version?.Label,
			IsMajorVersion = version != null && version.Major > 0 && version.Minor == 0,
			IsAcademy = course.IsSystem,
			Hours = version?.Hours,
			ActorUserId = actorUserId,
			CertificationTypeId = version?.IssuesCertificationTypeId,
			OccurredOn = utcNow
		};

		/// <summary>A completed enrollment: the learner's department, the course and version it completed, the test-out flag.</summary>
		public static CourseWorkflowEvent Completed(LmsEnrollment enrollment, LmsCourse course, LmsCourseVersion version, DateTime utcNow) => new CourseWorkflowEvent
		{
			DepartmentId = enrollment.DepartmentId,
			Change = CourseWorkflowChanges.Completed,
			CourseId = enrollment.LmsCourseId,
			CourseVersionId = enrollment.LmsCourseVersionId,
			CourseCode = course?.Code,
			CourseTitle = version?.Title ?? course?.Title,
			VersionLabel = version?.Label,
			IsAcademy = course?.IsSystem ?? false,
			Hours = version?.Hours,
			UserId = enrollment.UserId,
			ActorUserId = enrollment.AssignedByUserId,
			EnrollmentId = enrollment.LmsEnrollmentId,
			TestedOut = enrollment.TestedOut,
			CertificationTypeId = course != null && !course.IsSystem ? version?.IssuesCertificationTypeId : null,
			CompletedOn = enrollment.CompletedOn,
			OccurredOn = utcNow
		};

		/// <summary>One member an officer assigned the course to directly (271): the course, the version, the due date.</summary>
		public static CourseWorkflowEvent Assigned(int departmentId, LmsCourse course, string userId, string actorUserId, DateTime? dueOnUtc, DateTime utcNow) => new CourseWorkflowEvent
		{
			// The assigning department (an Academy course belongs to the system publisher, department 0).
			DepartmentId = departmentId,
			Change = CourseWorkflowChanges.Assigned,
			CourseId = course.LmsCourseId,
			CourseVersionId = course.CurrentVersionId,
			CourseCode = course.Code,
			CourseTitle = course.Title,
			IsAcademy = course.IsSystem,
			UserId = userId,
			ActorUserId = actorUserId,
			DueOn = dueOnUtc,
			OccurredOn = utcNow
		};

		/// <summary>A course taken out of use (273): the course and the version that was in force.</summary>
		public static CourseWorkflowEvent Retired(LmsCourse course, LmsCourseVersion current, string actorUserId, DateTime utcNow) => new CourseWorkflowEvent
		{
			DepartmentId = course.DepartmentId,
			Change = CourseWorkflowChanges.Retired,
			CourseId = course.LmsCourseId,
			CourseVersionId = current?.LmsCourseVersionId,
			CourseCode = course.Code,
			CourseTitle = current?.Title ?? course.Title,
			VersionLabel = current?.Label,
			Hours = current?.Hours,
			ActorUserId = actorUserId,
			OccurredOn = utcNow
		};

		/// <summary>An attempt waiting for a grader (272): which attempt and lesson, never the answers.</summary>
		public static CourseWorkflowEvent AwaitingGrading(LmsAssessmentAttempt attempt, LmsLesson lesson, LmsCourseVersion version, DateTime utcNow) => new CourseWorkflowEvent
		{
			DepartmentId = attempt.DepartmentId,
			Change = CourseWorkflowChanges.AwaitingGrading,
			CourseId = version?.LmsCourseId ?? 0,
			CourseVersionId = attempt.LmsCourseVersionId,
			CourseTitle = version?.Title,
			VersionLabel = version?.Label,
			UserId = attempt.UserId,
			EnrollmentId = attempt.LmsEnrollmentId,
			AttemptId = attempt.LmsAssessmentAttemptId,
			LessonId = attempt.LmsLessonId,
			LessonTitle = lesson?.Title,
			CompletedOn = attempt.SubmittedOn,
			OccurredOn = utcNow
		};

		/// <summary>Seats enabled or disabled by an officer, or the packs a renewal changed (274): counts only, never which members.</summary>
		public static TrainingAdminWorkflowEvent SeatsChanged(int departmentId, int added, int removed, int? enabledSeats, int packs, int? previousPacks, string actorUserId,
			DateTime utcNow) => new TrainingAdminWorkflowEvent
		{
			DepartmentId = departmentId,
			Change = TrainingAdminWorkflowChanges.SeatsChanged,
			SeatsAdded = added,
			SeatsRemoved = removed,
			EnabledSeats = enabledSeats,
			Packs = packs,
			PreviousPacks = previousPacks,
			UserId = actorUserId,
			OccurredOn = utcNow
		};

		/// <summary>An attempt a grader finished: pass or fail and the score; never the answers, the key or the grader's notes.</summary>
		public static CourseWorkflowEvent AssessmentGraded(LmsAssessmentAttempt attempt, LmsLesson lesson, LmsCourseVersion version, int? courseId, DateTime utcNow) => new CourseWorkflowEvent
		{
			DepartmentId = attempt.DepartmentId,
			Change = CourseWorkflowChanges.AssessmentGraded,
			CourseId = courseId ?? version?.LmsCourseId ?? 0,
			CourseVersionId = attempt.LmsCourseVersionId,
			CourseTitle = version?.Title,
			VersionLabel = version?.Label,
			UserId = attempt.UserId,
			ActorUserId = attempt.GradedByUserId,
			EnrollmentId = attempt.LmsEnrollmentId,
			AttemptId = attempt.LmsAssessmentAttemptId,
			LessonId = attempt.LmsLessonId,
			LessonTitle = lesson?.Title,
			Passed = attempt.Passed,
			Score = attempt.Percent,
			CompletedOn = attempt.GradedOn,
			OccurredOn = utcNow
		};

		/// <summary>One evaluated member of a completed skill evaluation.</summary>
		public static SkillWorkflowEvent EvaluationCompleted(LmsSkillEvaluation evaluation, string userId, int memberCount, DateTime utcNow) => new SkillWorkflowEvent
		{
			DepartmentId = evaluation.DepartmentId,
			Change = SkillWorkflowChanges.EvaluationCompleted,
			EvaluationId = evaluation.LmsSkillEvaluationId,
			SkillSheetId = evaluation.LmsSkillSheetId,
			TaskBookId = evaluation.LmsTaskBookId,
			SkillCode = evaluation.Code,
			SkillTitle = evaluation.Title,
			UserId = userId,
			ActorUserId = evaluation.EvaluatorUserId,
			Result = Enum.IsDefined(typeof(LmsSkillEvaluationResults), evaluation.Result) ? ((LmsSkillEvaluationResults)evaluation.Result).ToString() : null,
			Score = evaluation.Score,
			CriticalFailed = evaluation.CriticalFailed,
			MemberCount = memberCount,
			CompletedOn = evaluation.CompletedOn,
			OccurredOn = utcNow
		};

		/// <summary>A task book that closed (certified, expired or cancelled); the cancellation reason stays in the book.</summary>
		public static SkillWorkflowEvent TaskBookClosed(LmsTaskBook book, string actorUserId, DateTime utcNow) => new SkillWorkflowEvent
		{
			DepartmentId = book.DepartmentId,
			Change = SkillWorkflowChanges.TaskBookClosed,
			TaskBookId = book.LmsTaskBookId,
			SkillCode = book.PositionCode,
			SkillTitle = book.Name,
			UserId = book.UserId,
			ActorUserId = actorUserId,
			Result = Enum.IsDefined(typeof(LmsTaskBookStatuses), book.Status) ? ((LmsTaskBookStatuses)book.Status).ToString() : null,
			MemberCount = 1,
			CertificationTypeId = book.CertificationTypeId,
			CompletedOn = book.ClosedOn ?? book.CertifiedOn,
			OccurredOn = utcNow
		};

		public static RequirementEnforcementWorkflowEvent EnforcementUndone(LmsRequirementEnforcement action, LmsRequirement requirement, LmsEnforcementUndoReasons reason,
			int restoredRoles, string actorUserId, DateTime utcNow) => new RequirementEnforcementWorkflowEvent
		{
			DepartmentId = action.DepartmentId,
			EnforcementId = action.LmsRequirementEnforcementId,
			RequirementId = action.LmsRequirementId,
			RequirementName = requirement?.Name,
			UserId = action.UserId,
			CertificationId = action.PersonnelCertificationId,
			CertificationTypeId = action.DepartmentCertificationTypeId,
			UndoReason = reason.ToString(),
			RestoredRoleCount = restoredRoles,
			EnforcedOn = action.ActedOn,
			ActorUserId = actorUserId,
			OccurredOn = utcNow
		};

		/// <summary>A training request as stored: never the justification, the location detail or an approver's note.</summary>
		public static TrainingRequestWorkflowEvent TrainingRequest(LmsTrainingRequest request, TrainingRequestWorkflowChanges change, DateTime utcNow) => new TrainingRequestWorkflowEvent
		{
			DepartmentId = request.DepartmentId,
			Change = change,
			RequestId = request.LmsTrainingRequestId,
			UserId = request.UserId,
			RequestTitle = request.Title,
			Provider = request.Provider,
			CourseCode = request.CourseCode,
			CategoryId = request.CategoryId,
			StartsOn = request.StartsOn,
			EndsOn = request.EndsOn,
			Hours = request.Hours,
			TotalCost = request.CostTuition + request.CostTravel + request.CostOther,
			Currency = request.Currency,
			Status = Enum.IsDefined(typeof(LmsTrainingRequestStatuses), request.Status) ? ((LmsTrainingRequestStatuses)request.Status).ToString() : null,
			AwaitingStep = request.CurrentStep.HasValue && Enum.IsDefined(typeof(LmsTrainingRequestSteps), request.CurrentStep.Value)
				? ((LmsTrainingRequestSteps)request.CurrentStep.Value).ToString() : null,
			DecidedByUserId = request.DecidedByUserId,
			SubmittedOn = request.SubmittedOn,
			DecidedOn = request.DecidedOn,
			OccurredOn = utcNow
		};

		public static TrainingAdminWorkflowEvent AddonLapsed(int departmentId, LmsLapseReasons reason, DateTime lapsedSince, DateTime? paidThrough, int packs, DateTime utcNow) =>
			new TrainingAdminWorkflowEvent
			{
				DepartmentId = departmentId,
				Change = TrainingAdminWorkflowChanges.AddonLapsed,
				LapseReason = reason.ToString(),
				LapsedSince = lapsedSince,
				PaidThrough = paidThrough,
				Packs = packs,
				OccurredOn = utcNow
			};

		public static TrainingAdminWorkflowEvent AddonReactivated(int departmentId, LmsAddonLapse closed, DateTime? paidThrough, int packs, DateTime utcNow) =>
			new TrainingAdminWorkflowEvent
			{
				DepartmentId = departmentId,
				Change = TrainingAdminWorkflowChanges.AddonReactivated,
				LapseReason = closed != null && Enum.IsDefined(typeof(LmsLapseReasons), closed.Reason) ? ((LmsLapseReasons)closed.Reason).ToString() : null,
				LapsedSince = closed?.StartedOn,
				PaidThrough = paidThrough,
				Packs = packs,
				OccurredOn = utcNow
			};

		public static TrainingAdminWorkflowEvent DataExportReady(LmsDataExport export, DateTime utcNow) => new TrainingAdminWorkflowEvent
		{
			DepartmentId = export.DepartmentId,
			Change = TrainingAdminWorkflowChanges.DataExportReady,
			ExportId = export.LmsDataExportId,
			ExportScope = Enum.IsDefined(typeof(LmsDataExportScopes), export.Scope) ? ((LmsDataExportScopes)export.Scope).ToString() : null,
			IncludesMedia = export.IncludeMedia,
			SizeBytes = export.SizeBytes,
			ExpiresOn = export.ExpiresOn,
			UserId = export.RequestedByUserId,
			OccurredOn = utcNow
		};
	}
}
