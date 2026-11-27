using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Writes a <see cref="LmsCoursePlan"/> (an AI draft or a starter clone) as a new DRAFT course through the course and question bank services,
	/// so every rule they hold applies: Manage Courses and the add-on's authoring gate, lesson validation per kind, html sanitized on save, the
	/// draft's hash following its content. Nothing is submitted or published. When a step is refused after the course exists, the draft (and
	/// the bank it made) is discarded so no half-written course is left behind.
	/// </summary>
	public static class LmsCoursePlanWriter
	{
		public const int MaxModules = 20;
		public const int MaxLessonsPerModule = 20;

		public static async Task<LmsCoursePlanResult> WriteAsync(ILmsCourseService courses, ILmsQuestionBankService banks, LmsActor actor, LmsCoursePlan plan,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayManage)
				return new LmsCoursePlanResult { Error = LmsErrors.NotAuthorized };
			if (plan == null || string.IsNullOrWhiteSpace(plan.Title) || plan.Modules == null || plan.Modules.Count == 0 ||
				plan.Modules.Any(m => m == null || string.IsNullOrWhiteSpace(m.Title) || m.Lessons == null || m.Lessons.Count == 0))
				return new LmsCoursePlanResult { Error = LmsErrors.InvalidInput };

			var created = await courses.CreateAsync(actor, new LmsCourseInput { Title = plan.Title, Code = plan.Code, Summary = plan.Summary, Tags = plan.Tags }, cancellationToken);
			if (!created.Succeeded || created.Course == null || created.Version == null)
				return new LmsCoursePlanResult { Error = created.Error == LmsErrors.None ? LmsErrors.InvalidInput : created.Error };

			var result = new LmsCoursePlanResult { Course = created.Course, Version = created.Version };
			int? bankId = null;
			try
			{
				var error = await WriteContentAsync(courses, banks, actor, plan, result, b => bankId = b, cancellationToken);
				if (error != LmsErrors.None)
				{
					await RollBackAsync(courses, banks, actor, result.Version.LmsCourseVersionId, bankId);
					return new LmsCoursePlanResult { Error = error };
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"A planned course could not be written for department {actor.DepartmentId}.");
				await RollBackAsync(courses, banks, actor, result.Version.LmsCourseVersionId, bankId);
				return new LmsCoursePlanResult { Error = LmsErrors.InvalidInput };
			}

			result.LmsQuestionBankId = bankId;
			return result;
		}

		private static async Task<LmsErrors> WriteContentAsync(ILmsCourseService courses, ILmsQuestionBankService banks, LmsActor actor, LmsCoursePlan plan,
			LmsCoursePlanResult result, Action<int> bankMade, CancellationToken cancellationToken)
		{
			var versionId = result.Version.LmsCourseVersionId;

			// Settings first, while the version's RowVersion is the one CreateAsync returned.
			var settings = await courses.SaveVersionSettingsAsync(actor, versionId, new LmsVersionSettingsInput
			{
				Title = plan.Title,
				OverviewHtml = plan.OverviewHtml ?? string.Empty,
				Hours = Math.Max(0, Math.Min(1000, plan.Hours)),
				ExpectedRowVersion = result.Version.RowVersion
			}, cancellationToken);
			if (!settings.Succeeded)
				return settings.Error == LmsErrors.None ? LmsErrors.InvalidInput : settings.Error;
			result.Version = settings.Version ?? result.Version;

			// CreateAsync made one untitled module: it becomes the first planned module.
			var outline = await courses.GetOutlineAsync(actor.DepartmentId, versionId);
			var firstModuleId = outline?.Modules?.FirstOrDefault()?.Module?.LmsCourseModuleId ?? 0;

			var modules = plan.Modules.Take(MaxModules).ToList();
			for (var m = 0; m < modules.Count; m++)
			{
				var saved = await courses.SaveModuleAsync(actor, versionId, m == 0 ? firstModuleId : 0, modules[m].Title, cancellationToken);
				if (!saved.Succeeded || saved.Value is not LmsCourseModule module)
					return saved.Error == LmsErrors.None ? LmsErrors.InvalidInput : saved.Error;
				result.Modules++;

				foreach (var lesson in modules[m].Lessons.Take(MaxLessonsPerModule))
				{
					var lessonResult = await courses.SaveLessonAsync(actor, new LmsLessonInput
					{
						LmsCourseVersionId = versionId,
						LmsCourseModuleId = module.LmsCourseModuleId,
						Kind = (int)LmsLessonKinds.Blocks,
						Title = lesson.Title,
						ContentHtml = lesson.Html ?? string.Empty,
						IsRequired = true
					}, cancellationToken);
					if (!lessonResult.Succeeded || lessonResult.Value is not LmsLesson savedLesson)
						return lessonResult.Error == LmsErrors.None ? LmsErrors.InvalidInput : lessonResult.Error;
					result.LessonIds.Add(savedLesson.LmsLessonId);
					result.Lessons++;
				}
			}

			var questions = (plan.Questions ?? new List<LmsCoursePlanQuestion>()).Where(q => q != null && !string.IsNullOrWhiteSpace(q.Text)).ToList();
			if (questions.Count == 0 || string.IsNullOrWhiteSpace(plan.BankName))
				return LmsErrors.None;

			var bankResult = await banks.SaveBankAsync(actor, new LmsQuestionBank { Name = plan.BankName, Description = plan.BankDescription }, cancellationToken);
			if (!bankResult.Succeeded || bankResult.Value is not LmsQuestionBank bank)
				return bankResult.Error == LmsErrors.None ? LmsErrors.InvalidInput : bankResult.Error;
			bankMade(bank.LmsQuestionBankId);

			var keys = new[] { "a", "b", "c", "d", "e", "f" };
			foreach (var question in questions)
			{
				var options = (question.Options ?? new List<string>()).Take(keys.Length).ToList();
				var saved = await banks.SaveQuestionAsync(actor, new LmsQuestion
				{
					LmsQuestionBankId = bank.LmsQuestionBankId,
					Kind = question.Kind == (int)LmsQuestionKinds.TrueFalse ? (int)LmsQuestionKinds.TrueFalse : (int)LmsQuestionKinds.SingleChoice,
					Text = question.Text,
					Explanation = question.Explanation,
					Points = 1,
					Options = options.Select((o, i) => new LmsQuestionOption { Key = keys[i], Text = o, IsCorrect = i == question.CorrectIndex }).ToList()
				}, cancellationToken);
				if (!saved.Succeeded)
					return saved.Error == LmsErrors.None ? LmsErrors.InvalidInput : saved.Error;
				result.Questions++;
			}

			if (string.IsNullOrWhiteSpace(plan.CheckModuleTitle) || string.IsNullOrWhiteSpace(plan.CheckLessonTitle))
				return LmsErrors.None;

			var checkModule = await courses.SaveModuleAsync(actor, versionId, 0, plan.CheckModuleTitle, cancellationToken);
			if (!checkModule.Succeeded || checkModule.Value is not LmsCourseModule check)
				return checkModule.Error == LmsErrors.None ? LmsErrors.InvalidInput : checkModule.Error;
			result.Modules++;
			var assessment = await courses.SaveLessonAsync(actor, new LmsLessonInput
			{
				LmsCourseVersionId = versionId,
				LmsCourseModuleId = check.LmsCourseModuleId,
				Kind = (int)LmsLessonKinds.Assessment,
				Title = plan.CheckLessonTitle,
				IsRequired = true,
				LmsQuestionBankId = bank.LmsQuestionBankId,
				DrawCount = 0,
				PassMark = Math.Clamp(plan.CheckPassMark, 1, 100),
				ShowAnswersPolicy = (int)LmsShowAnswersPolicies.AfterPassing,
				RandomizeAnswers = true
			}, cancellationToken);
			if (!assessment.Succeeded || assessment.Value is not LmsLesson savedAssessment)
				return assessment.Error == LmsErrors.None ? LmsErrors.InvalidInput : assessment.Error;
			result.LessonIds.Add(savedAssessment.LmsLessonId);
			result.Lessons++;
			return LmsErrors.None;
		}

		/// <summary>Discards the half-written draft (the course goes with its only draft) and the bank it made. Never throws.</summary>
		private static async Task RollBackAsync(ILmsCourseService courses, ILmsQuestionBankService banks, LmsActor actor, int versionId, int? bankId)
		{
			try
			{
				await courses.DiscardDraftAsync(actor, versionId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"A half-written course draft {versionId} could not be discarded.");
			}

			if (!bankId.HasValue)
				return;
			try
			{
				await banks.DeleteBankAsync(actor, bankId.Value, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The question bank {bankId} of a half-written course draft could not be removed.");
			}
		}
	}
}
