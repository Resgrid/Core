using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Areas.User.Models.Training
{
	/// <summary>The member's My Training home (§18.3): every member, managers included.</summary>
	public class TrainingIndexModel
	{
		public TrainingManagementScope Scope { get; set; }

		public MyTrainingOverview MyTraining { get; set; } = new MyTrainingOverview();

		public Department Department { get; set; }

		public string Message { get; set; }

		public string Error { get; set; }
	}

	/// <summary>One row of the officer's training list.</summary>
	public class TrainingManageRow
	{
		public Model.Training Training { get; set; }

		public int Assigned { get; set; }

		public int Complete { get; set; }

		public bool CanAuthor { get; set; }

		public bool CanAssign { get; set; }

		public bool CanDelete { get; set; }

		/// <summary>Nobody completed it (as far as the list knows): Delete is offered, the server re-checks the history.</summary>
		public bool MayDeleteOutright => Complete == 0;

		public bool IsScheduled { get; set; }
	}

	/// <summary>The officer's list of trainings with KPI tiles and a status filter.</summary>
	public class TrainingManageModel
	{
		public TrainingManagementScope Scope { get; set; }

		public TrainingManagerSummary Summary { get; set; } = new TrainingManagerSummary();

		public List<TrainingManageRow> Rows { get; set; } = new List<TrainingManageRow>();

		/// <summary>active, scheduled, archived or all.</summary>
		public string Status { get; set; } = "active";

		public string Search { get; set; }

		public Department Department { get; set; }

		public string Message { get; set; }

		public string Error { get; set; }
	}

	/// <summary>One question as the editor posts and re-renders it (never the entity).</summary>
	public class TrainingQuestionEditor
	{
		public int Id { get; set; }

		public string Text { get; set; }

		public int Type { get; set; }

		public List<TrainingAnswerEditor> Answers { get; set; } = new List<TrainingAnswerEditor>();
	}

	public class TrainingAnswerEditor
	{
		public int Id { get; set; }

		public string Text { get; set; }

		public bool Correct { get; set; }
	}

	/// <summary>
	/// The New / Edit form. Only these fields bind: the training's stamps (Notified, CreatedOn, ArchivedOn, the creator...)
	/// are never posted (T-D23). Dates are department-local strings from native date inputs.
	/// </summary>
	public class TrainingEditorModel
	{
		public int TrainingId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public string TrainingText { get; set; }

		public double MinimumScore { get; set; }

		/// <summary>none, fixed or relative.</summary>
		public string DueMode { get; set; } = "none";

		/// <summary>yyyy-MM-dd, department-local.</summary>
		public string DueDate { get; set; }

		public int? DueDays { get; set; }

		/// <summary>yyyy-MM-ddTHH:mm, department-local; empty starts at once.</summary>
		public string StartsOn { get; set; }

		public bool ClosedToNewAssignees { get; set; }

		public bool OpenToAllMembers { get; set; }

		/// <summary>Never used for knowledge answers (K1, decision 14: licensed material whose license forbids processing).</summary>
		public bool ExcludeFromAi { get; set; }

		public bool AllowSelfEnroll { get; set; }

		public int? RepeatMonths { get; set; }

		public int? QuizAttemptLimit { get; set; }

		public bool OneQuestionPerPage { get; set; }

		/// <summary>Round 9A read and confirm: a training without a quiz completes only when the member confirms they read and reviewed it.</summary>
		public bool RequiresAcknowledgment { get; set; }

		public string ReminderLeadDaysCsv { get; set; }

		public bool SendOverdueNotices { get; set; } = true;

		public int? OverdueRepeatDays { get; set; }

		public string ReminderMessage { get; set; }

		public decimal? Hours { get; set; }

		public decimal? CeHours { get; set; }

		public string CeProvider { get; set; }

		public string CeCourseNumber { get; set; }

		public int? CategoryId { get; set; }

		/// <summary><see cref="MethodsOfTraining"/> (Vector parity, Training T1).</summary>
		public int? MethodOfTraining { get; set; }

		/// <summary>Plain text, at most 2000 characters.</summary>
		public string Objectives { get; set; }

		/// <summary>Comma separated, at most 500 characters.</summary>
		public string Tags { get; set; }

		/// <summary>The course code exports and the transcript carry, at most 64 characters.</summary>
		public string ExternalCode { get; set; }

		// Assignment (audience rules; ContentAudienceKinds)
		public bool AssignEveryone { get; set; }

		public List<string> UserIds { get; set; } = new List<string>();

		public List<int> GroupIds { get; set; } = new List<int>();

		public List<int> RoleIds { get; set; } = new List<int>();

		public List<int> CertificationTypeIds { get; set; } = new List<int>();

		public List<int> UnitTypeIds { get; set; } = new List<int>();

		public List<int> ShiftIds { get; set; } = new List<int>();

		/// <summary>Edit only: store the picks as audience rules so membership changes keep the assignment current.</summary>
		public bool MakeDynamic { get; set; }

		/// <summary>Edit only, asked when the quiz or pass mark changed and members completed it: material or keep.</summary>
		public string MaterialChange { get; set; }

		/// <summary>Save, or Preview (show who the audience would add without saving anything).</summary>
		public string Command { get; set; }

		// Display only (never read from a post)
		public List<TrainingQuestionEditor> Questions { get; set; } = new List<TrainingQuestionEditor>();

		public List<TrainingAttachment> Attachments { get; set; } = new List<TrainingAttachment>();

		public List<SelectListItem> Users { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Groups { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Roles { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> CertificationTypes { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> UnitTypes { get; set; } = new List<SelectListItem>();

		public List<SelectListItem> Shifts { get; set; } = new List<SelectListItem>();

		/// <summary>The department's training categories (a removed one only when it is the stored value).</summary>
		public List<SelectListItem> Categories { get; set; } = new List<SelectListItem>();

		public bool CanAuthor { get; set; } = true;

		public bool CanAssign { get; set; } = true;

		/// <summary>The training already stores audience rules (its assignment is dynamic).</summary>
		public bool IsDynamic { get; set; }

		public int AssignedCount { get; set; }

		public int CompletedCount { get; set; }

		public bool AskMaterialChange { get; set; }

		public TrainingAudiencePreview Preview { get; set; }

		/// <summary>Open "flag this question" feedback (authors only).</summary>
		public List<TrainingQuestionFlag> Flags { get; set; } = new List<TrainingQuestionFlag>();

		public string Message { get; set; }

		public string Error { get; set; }

		public bool IsNew => TrainingId <= 0;
	}

	public class NewTrainingModel : TrainingEditorModel { }

	public class EditTrainingModel : TrainingEditorModel { }

	/// <summary>Who an audience would add, and who is skipped and why (T-D16).</summary>
	public class TrainingAudiencePreview
	{
		public List<string> AddedNames { get; set; } = new List<string>();

		public int AlreadyAssigned { get; set; }

		public List<TrainingSkippedMember> Skipped { get; set; } = new List<TrainingSkippedMember>();

		public bool Closed { get; set; }
	}

	public class TrainingSkippedMember
	{
		public string Name { get; set; }

		/// <summary>Resource key of the reason (SkipReason*).</summary>
		public string ReasonKey { get; set; }
	}

	/// <summary>The training page: content, attachments, honest progress and the next step.</summary>
	public class ViewTrainingModel
	{
		public Model.Training Training { get; set; }

		public TrainingUser Assignment { get; set; }

		public TrainingAssignmentStates? State { get; set; }

		public DateTime? DueUtc { get; set; }

		public Department Department { get; set; }

		public string CreatorUserName { get; set; }

		public bool CanAuthor { get; set; }

		public bool CanAssign { get; set; }

		public bool CanDelete { get; set; }

		public bool HasQuiz { get; set; }

		/// <summary>Null when attempts are unlimited.</summary>
		public int? AttemptsLeft { get; set; }

		/// <summary>The member is assigned, has not passed, and may still submit.</summary>
		public bool CanTakeQuiz { get; set; }

		public bool CanEnroll { get; set; }

		public bool OnLeave { get; set; }

		public TrainingQuizAttempt LastAttempt { get; set; }

		public string Message { get; set; }

		public string Error { get; set; }
	}

	/// <summary>
	/// The quiz as the browser receives it: questions and options only (R-T11). The answer key never leaves the server; a
	/// guard test fails if a property here could carry it.
	/// </summary>
	public class TrainingQuizView
	{
		public int TrainingId { get; set; }

		public string Name { get; set; }

		public bool OneQuestionPerPage { get; set; }

		public double MinimumScore { get; set; }

		public int? AttemptsLeft { get; set; }

		/// <summary>A manager looking at the quiz without being assigned: nothing is submitted.</summary>
		public bool IsPreview { get; set; }

		public List<TrainingQuizQuestionView> Questions { get; set; } = new List<TrainingQuizQuestionView>();

		public static TrainingQuizView From(Model.Training training, int? attemptsLeft)
		{
			var view = new TrainingQuizView
			{
				TrainingId = training?.TrainingId ?? 0,
				Name = training?.Name,
				OneQuestionPerPage = training?.OneQuestionPerPage ?? false,
				MinimumScore = training?.MinimumScore ?? 0,
				AttemptsLeft = attemptsLeft
			};

			foreach (var question in (training?.Questions ?? Enumerable.Empty<TrainingQuestion>()).Where(q => q != null).OrderBy(q => q.TrainingQuestionId))
			{
				view.Questions.Add(new TrainingQuizQuestionView
				{
					QuestionId = question.TrainingQuestionId,
					Text = question.Question,
					Type = question.Type,
					Options = (question.Answers ?? Enumerable.Empty<TrainingQuestionAnswer>()).Where(a => a != null).OrderBy(a => a.TrainingQuestionAnswerId)
						.Select(a => new TrainingQuizOptionView { OptionId = a.TrainingQuestionAnswerId, Text = a.Answer }).ToList()
				});
			}

			return view;
		}
	}

	public class TrainingQuizQuestionView
	{
		public int QuestionId { get; set; }

		public string Text { get; set; }

		/// <summary>TrainingQuestionTypes: single, multiple (all-or-nothing) or true/false.</summary>
		public int Type { get; set; }

		public List<TrainingQuizOptionView> Options { get; set; } = new List<TrainingQuizOptionView>();
	}

	public class TrainingQuizOptionView
	{
		public int OptionId { get; set; }

		public string Text { get; set; }
	}

	/// <summary>The result of a submitted attempt, with the questions the member may flag.</summary>
	public class TrainingQuizResultView
	{
		public int TrainingId { get; set; }

		public string Name { get; set; }

		public TrainingQuizSubmissionStatuses Status { get; set; }

		public double Score { get; set; }

		public bool Passed { get; set; }

		public double MinimumScore { get; set; }

		public int CorrectCount { get; set; }

		public int QuestionCount { get; set; }

		public int? AttemptsLeft { get; set; }

		public bool CanRetake { get; set; }

		public List<TrainingQuizQuestionView> Questions { get; set; } = new List<TrainingQuizQuestionView>();
	}

	/// <summary>The officer's report of one training.</summary>
	public class TrainingReportView
	{
		public TrainingReport Report { get; set; }

		public Department Department { get; set; }

		public bool ShowFormer { get; set; }

		public bool HasQuiz { get; set; }

		public bool CanAuthor { get; set; }

		public bool CanDelete { get; set; }

		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<int, string> QuestionTexts { get; set; } = new Dictionary<int, string>();

		public string Message { get; set; }

		public string Error { get; set; }

		public IEnumerable<TrainingReportRow> VisibleRows =>
			(Report?.Rows ?? new List<TrainingReportRow>()).Where(r => ShowFormer || r.IsActiveMember);

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) ? name : userId;
	}

	/// <summary>Member leave of absence (R-C2), entered by whoever assigns training.</summary>
	public class TrainingLeaveView
	{
		public List<MemberLeavePeriod> Periods { get; set; } = new List<MemberLeavePeriod>();

		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public List<SelectListItem> Members { get; set; } = new List<SelectListItem>();

		public Department Department { get; set; }

		public string UserId { get; set; }

		public string StartDate { get; set; }

		public string EndDate { get; set; }

		public int Kind { get; set; }

		public string Note { get; set; }

		public string Message { get; set; }

		public string Error { get; set; }

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) ? name : userId;
	}

	/// <summary>
	/// How an assignment state shows (pill text, icon, colour) everywhere: My Training, the training page and the report.
	/// The Report's completion column reads <see cref="IsComplete"/>: a training without a quiz is Complete only when the
	/// member's state is Complete, never because the column guessed from the quiz (T-D12).
	/// </summary>
	public static class TrainingStatePresentation
	{
		public static string Key(TrainingAssignmentStates state) => "State" + state;

		public static string Icon(TrainingAssignmentStates state)
		{
			switch (state)
			{
				case TrainingAssignmentStates.Complete: return "fa-check-circle";
				case TrainingAssignmentStates.Overdue: return "fa-exclamation-circle";
				case TrainingAssignmentStates.Failed: return "fa-times-circle";
				case TrainingAssignmentStates.InProgress: return "fa-hourglass-half";
				case TrainingAssignmentStates.Exempt: return "fa-ban";
				case TrainingAssignmentStates.OnLeave: return "fa-pause-circle";
				case TrainingAssignmentStates.Scheduled: return "fa-clock-o";
				case TrainingAssignmentStates.Removed: return "fa-user-times";
				default: return "fa-circle-o";
			}
		}

		public static string LabelClass(TrainingAssignmentStates state)
		{
			switch (state)
			{
				case TrainingAssignmentStates.Complete: return "label-primary";
				case TrainingAssignmentStates.Overdue: return "label-danger";
				case TrainingAssignmentStates.Failed: return "label-warning";
				case TrainingAssignmentStates.InProgress: return "label-info";
				case TrainingAssignmentStates.Exempt:
				case TrainingAssignmentStates.OnLeave:
				case TrainingAssignmentStates.Removed:
					return "label-default";
				default: return "label-default";
			}
		}

		/// <summary>The Report's completion column (T-D12).</summary>
		public static bool IsComplete(TrainingReportRow row) => row != null && row.State == TrainingAssignmentStates.Complete;

		/// <summary>A UTC moment as the department reads it (short date), or null.</summary>
		public static string LocalDate(DateTime? utc, Department department) =>
			utc.HasValue ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(department).ToString("d", CultureInfo.CurrentCulture) : null;

		/// <summary>A UTC moment as the department reads it (date and time), or null.</summary>
		public static string LocalDateTime(DateTime? utc, Department department) =>
			utc.HasValue ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(department).ToString("g", CultureInfo.CurrentCulture) : null;

		/// <summary>Resource key of a skip reason (T-D16).</summary>
		public static string SkipReasonKey(ContentAudienceSkipReasons reason) => "SkipReason" + reason;
	}
}
