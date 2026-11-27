using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>The attribution a set copy keeps (P5 attribution, round 10 A3): the set and publisher it came from and the publisher's version.</summary>
	public static class ContentSetAttribution
	{
		public static ContentAttribution For(ContentSetCopyContext context, ContentAttributionSubjects subject, int subjectId) => new ContentAttribution
		{
			DepartmentId = context.SubscriberDepartmentId,
			SubjectKind = (int)subject,
			SubjectId = subjectId,
			SourceKind = (int)ContentAttributionSources.ContentSet,
			SourceRef = context.Set.ContentSetId.ToString(CultureInfo.InvariantCulture),
			SourceName = string.IsNullOrWhiteSpace(context.PublisherName) ? context.Set.Name : context.PublisherName + ": " + context.Set.Name,
			Author = context.PublisherName,
			SourceVersion = context.Source?.VersionLabel,
			CreatedOn = DateTime.UtcNow,
			CreatedByUserId = context.UserId
		}.Normalize();

		/// <summary>Writes the copy's attribution; a failure is logged, never fails the copy (attribution is provenance, not content).</summary>
		public static async Task KeepAsync(IContentAttributionsRepository attributions, ContentSetCopyContext context, ContentAttributionSubjects subject, int subjectId,
			CancellationToken cancellationToken)
		{
			if (attributions == null || context?.Set == null || subjectId <= 0)
				return;
			try
			{
				await attributions.SaveForSubjectAsync(For(context, subject, subjectId), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A set copy's attribution could not be written.");
			}
		}
	}

	/// <summary>
	/// LMS courses in a course set (Training T5, round 10 A3). LMS-type: a subscriber receives them only with its own LMS add-on or the
	/// publisher's sponsorship (§17.2 sharing row, T5 item 2a). The copy is the subscriber's own department course, built and republished by
	/// <see cref="ILmsCourseService.CreateSetCopyAsync"/> from the publisher's version. Hosted video and SCORM/cmi5 packages are shared with
	/// the subscriber's own rows over the publisher's stored files; an LTI lesson needs the subscriber's own registration with the tool, which
	/// the copy prepares (inactive) and the subscriber's managers are told to finish. Each copy keeps a ContentSet attribution (subject LmsCourse).
	/// </summary>
	public class LmsCourseSetItemSource : IContentSetItemSource
	{
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCourseModulesRepository _modules;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsCourseService _courseService;
		private readonly IContentAttributionsRepository _attributions;
		private readonly IContentSetNotifier _notifier;

		public LmsCourseSetItemSource(ILmsCoursesRepository courses, ILmsCourseVersionsRepository versions, ILmsCourseModulesRepository modules, ILmsLessonsRepository lessons,
			ILmsCourseService courseService, IContentAttributionsRepository attributions, IContentSetNotifier notifier)
		{
			_courses = courses;
			_versions = versions;
			_modules = modules;
			_lessons = lessons;
			_courseService = courseService;
			_attributions = attributions;
			_notifier = notifier;
		}

		public ContentSetItemKinds Kind => ContentSetItemKinds.LmsCourse;

		public bool NeedsLms => true;

		public ContentSetKinds SetKind => ContentSetKinds.Training;

		public async Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId)
		{
			var result = new List<ContentSetCandidate>();
			foreach (var course in (await _courses.GetForDepartmentAsync(publisherDepartmentId) ?? new List<LmsCourse>())
				.Where(c => c.DepartmentId == publisherDepartmentId && !c.IsSystem && !c.IsDeleted).OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
			{
				var version = course.CurrentVersionId.HasValue ? await _versions.GetVisibleByIdAsync(publisherDepartmentId, course.CurrentVersionId.Value) : null;
				result.Add(new ContentSetCandidate
				{
					Kind = Kind,
					ItemId = course.LmsCourseId,
					Title = course.Title,
					VersionLabel = version?.Label,
					Problem = version == null ? LmsCourseService.SetProblemNotPublished : await _courseService.GetSetCopyProblemAsync(publisherDepartmentId, version.LmsCourseVersionId),
					NeedsLms = true
				});
			}

			return result;
		}

		public async Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId)
		{
			var course = await _courses.GetVisibleByIdAsync(publisherDepartmentId, itemId);
			if (course == null || course.DepartmentId != publisherDepartmentId || course.IsSystem)
				return null;
			return await DescribeVersionAsync(publisherDepartmentId, itemId, course.CurrentVersionId ?? 0);
		}

		public async Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId)
		{
			var course = await _courses.GetVisibleByIdAsync(publisherDepartmentId, itemId);
			if (course == null || course.DepartmentId != publisherDepartmentId || course.IsSystem)
				return null;
			var version = versionId > 0 ? await _versions.GetVisibleByIdAsync(publisherDepartmentId, versionId) : null;
			if (version == null || version.LmsCourseId != course.LmsCourseId)
				version = course.CurrentVersionId.HasValue ? await _versions.GetVisibleByIdAsync(publisherDepartmentId, course.CurrentVersionId.Value) : null;

			var item = new ContentSetSourceItem { Kind = Kind, ItemId = course.LmsCourseId, Title = course.Title, Problem = LmsCourseService.SetProblemNotPublished };
			if (version == null)
				return item;
			item.VersionId = version.LmsCourseVersionId;
			item.VersionLabel = version.Label;
			item.Major = version.Major;
			item.EffectiveOn = version.EffectiveOn ?? version.PublishedOn;
			item.ChangeSummary = version.ChangeSummary;
			item.Locale = version.Locale;
			// Only the version in force is checked for copying; an older version is described for the inbox redline and history.
			item.Problem = course.IsDeleted ? LmsCourseService.SetProblemNotPublished
				: version.LmsCourseVersionId != course.CurrentVersionId ? null
				: await _courseService.GetSetCopyProblemAsync(publisherDepartmentId, version.LmsCourseVersionId);
			return item;
		}

		private static string Summary(ContentSetCopyContext context)
		{
			var label = string.IsNullOrWhiteSpace(context.Source.VersionLabel) ? string.Empty : " " + context.Source.VersionLabel;
			var from = string.IsNullOrWhiteSpace(context.PublisherName) ? context.Set.Name : context.PublisherName;
			return ("[" + from + label + "] " + (context.Source.ChangeSummary ?? string.Empty)).Trim();
		}

		public async Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var result = await _courseService.CreateSetCopyAsync(new LmsSetCopyRequest
			{
				SubscriberDepartmentId = context.SubscriberDepartmentId,
				PublisherDepartmentId = context.Set.DepartmentId,
				SourceVersionId = context.Source.VersionId,
				UserId = context.UserId,
				ChangeSummary = Summary(context)
			}, cancellationToken);
			if (!result.Succeeded)
				return ContentSetCopyOutcome.Fail(result.Value as string ?? result.Error.ToString().ToLowerInvariant());
			await AfterCopyAsync(context, result.Course.LmsCourseId, result.Value as LmsSetCopyNotes, cancellationToken);
			return new ContentSetCopyOutcome { Succeeded = true, LocalItemId = result.Course.LmsCourseId, LocalVersionId = result.Version?.LmsCourseVersionId };
		}

		/// <summary>Attribution, then a notice for each LTI tool the subscriber must still register (its lessons stay closed until it does).</summary>
		private async Task AfterCopyAsync(ContentSetCopyContext context, int localCourseId, LmsSetCopyNotes notes, CancellationToken cancellationToken)
		{
			await ContentSetAttribution.KeepAsync(_attributions, context, ContentAttributionSubjects.LmsCourse, localCourseId, cancellationToken);
			foreach (var tool in notes?.ToolsToRegister ?? new List<string>())
			{
				try
				{
					await _notifier.ToolRegistrationNeededAsync(context.SubscriberDepartmentId, context.Set.Name, tool, context.PublisherName, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An LTI registration notice could not be sent.");
				}
			}
		}

		public async Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var result = await _courseService.PublishSetVersionAsync(new LmsSetCopyRequest
			{
				SubscriberDepartmentId = context.SubscriberDepartmentId,
				PublisherDepartmentId = context.Set.DepartmentId,
				SourceVersionId = context.Source.VersionId,
				LocalCourseId = context.Copy.LocalItemId,
				UserId = context.UserId,
				ChangeSummary = Summary(context),
				Major = context.Major
			}, cancellationToken);
			if (!result.Succeeded)
				return ContentSetCopyOutcome.Fail(result.Value as string ?? result.Error.ToString().ToLowerInvariant());
			await AfterCopyAsync(context, context.Copy.LocalItemId, result.Value as LmsSetCopyNotes, cancellationToken);
			return new ContentSetCopyOutcome { Succeeded = true, LocalItemId = context.Copy.LocalItemId, LocalVersionId = result.Version?.LmsCourseVersionId };
		}

		/// <summary>A course's outline as HTML (its overview, then each module and lesson title), the text the inbox redline compares.</summary>
		private async Task<string> OutlineAsync(int departmentId, LmsCourseVersion version)
		{
			if (version == null)
				return string.Empty;
			var html = new StringBuilder();
			html.Append("<h2>").Append(WebUtility.HtmlEncode(version.Title ?? string.Empty)).Append("</h2>");
			if (!string.IsNullOrWhiteSpace(version.ContentHtml))
				html.Append(version.ContentHtml);
			var lessons = (await _lessons.GetForVersionAsync(departmentId, version.LmsCourseVersionId) ?? new List<LmsLesson>()).ToLookup(l => l.LmsCourseModuleId);
			foreach (var module in (await _modules.GetForVersionAsync(departmentId, version.LmsCourseVersionId) ?? new List<LmsCourseModule>()).OrderBy(m => m.SortOrder))
			{
				html.Append("<h3>").Append(WebUtility.HtmlEncode(module.Title ?? string.Empty)).Append("</h3><ul>");
				foreach (var lesson in lessons[module.LmsCourseModuleId].OrderBy(l => l.SortOrder))
					html.Append("<li>").Append(WebUtility.HtmlEncode(lesson.Title ?? string.Empty)).Append("</li>");
				html.Append("</ul>");
			}

			return html.ToString();
		}

		public async Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId)
		{
			var course = await _courses.GetVisibleByIdAsync(subscriberDepartmentId, localItemId);
			if (course == null || course.DepartmentId != subscriberDepartmentId || !course.CurrentVersionId.HasValue)
				return string.Empty;
			return await OutlineAsync(subscriberDepartmentId, await _versions.GetVisibleByIdAsync(subscriberDepartmentId, course.CurrentVersionId.Value));
		}

		public async Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context)
		{
			if (context?.Source == null || context.Source.VersionId <= 0)
				return (string.Empty, new List<string>());
			return (await OutlineAsync(context.Set.DepartmentId, await _versions.GetVisibleByIdAsync(context.Set.DepartmentId, context.Source.VersionId)), new List<string>());
		}

		public async Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId)
		{
			var course = await _courses.GetVisibleByIdAsync(subscriberDepartmentId, localItemId);
			return course != null && course.DepartmentId == subscriberDepartmentId && !course.IsDeleted;
		}
	}

	/// <summary>
	/// Baseline trainings in a course set (Training T5, round 10 A3). Baseline-type: free for every subscriber (§17.2). A training has no
	/// versions, so its "version" is a hash of what a subscriber receives (name, description, text, hours, CE fields and the quiz with its
	/// pass mark, <see cref="TrainingQuizScorer.RevisionHash"/>): when the publisher edits any of it, worker 100 queues the change like a new
	/// version, and the redline shows the quiz too. The copy is the subscriber's own training, assigned on its own and locked in the Trainings
	/// editor. An accepted update that changes the quiz replaces the copy's quiz; members who already completed the training keep their
	/// completion (a set update is never treated as a material change that re-assigns them; the department can re-assign from the report).
	/// Categories and attachments are the publisher's and are not carried. Each copy keeps a ContentSet attribution (subject Training).
	/// </summary>
	public class TrainingSetItemSource : IContentSetItemSource
	{
		public const string ProblemArchived = "archived";

		private readonly ITrainingService _trainings;
		private readonly IContentAttributionsRepository _attributions;

		public TrainingSetItemSource(ITrainingService trainings, IContentAttributionsRepository attributions)
		{
			_trainings = trainings;
			_attributions = attributions;
		}

		public ContentSetItemKinds Kind => ContentSetItemKinds.Training;

		public bool NeedsLms => false;

		public ContentSetKinds SetKind => ContentSetKinds.Training;

		/// <summary>A positive, stable "version" of a training's shareable text and quiz.</summary>
		public static int ContentVersion(Training training)
		{
			if (training == null)
				return 0;
			var canonical = string.Join("\u001f", training.Name ?? string.Empty, training.Description ?? string.Empty, training.TrainingText ?? string.Empty,
				training.MinimumScore.ToString(CultureInfo.InvariantCulture), training.Hours?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
				training.CeHours?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, training.CeProvider ?? string.Empty, training.CeCourseNumber ?? string.Empty,
				TrainingQuizScorer.RevisionHash(training));
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
			var value = BitConverter.ToInt32(bytes, 0) & int.MaxValue;
			return value == 0 ? 1 : value;
		}

		public async Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId) =>
			(await _trainings.GetAllTrainingsForDepartmentAsync(publisherDepartmentId) ?? new List<Training>())
				.Where(t => t != null && t.DepartmentId == publisherDepartmentId).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(t => new ContentSetCandidate { Kind = Kind, ItemId = t.TrainingId, Title = t.Name, Problem = t.ArchivedOn.HasValue ? ProblemArchived : null })
				.ToList();

		public async Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId)
		{
			var training = await _trainings.GetTrainingByIdAsync(itemId);
			if (training == null || training.DepartmentId != publisherDepartmentId || publisherDepartmentId <= 0)
				return null;
			return new ContentSetSourceItem
			{
				Kind = Kind,
				ItemId = training.TrainingId,
				Title = training.Name,
				VersionId = ContentVersion(training),
				EffectiveOn = training.UpdatedOn ?? training.CreatedOn,
				Problem = training.ArchivedOn.HasValue ? ProblemArchived : null
			};
		}

		public Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId) => DescribeAsync(publisherDepartmentId, itemId);

		public async Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var source = await _trainings.GetTrainingByIdAsync(context.Source.ItemId);
			if (source == null || source.DepartmentId != context.Set.DepartmentId || source.ArchivedOn.HasValue)
				return ContentSetCopyOutcome.Fail(ProblemArchived);

			var now = DateTime.UtcNow;
			var copy = new Training
			{
				DepartmentId = context.SubscriberDepartmentId,
				CreatedByUserId = context.UserId,
				CreatedOn = now,
				Name = source.Name,
				Description = source.Description,
				TrainingText = source.TrainingText,
				MinimumScore = source.MinimumScore,
				ExcludeFromAi = source.ExcludeFromAi,
				RequiresAcknowledgment = source.RequiresAcknowledgment,
				QuizAttemptLimit = source.QuizAttemptLimit,
				OneQuestionPerPage = source.OneQuestionPerPage,
				Hours = source.Hours,
				CeHours = source.CeHours,
				CeProvider = source.CeProvider,
				CeCourseNumber = source.CeCourseNumber,
				Objectives = source.Objectives,
				Tags = source.Tags,
				Questions = CopyQuiz(source)
			};

			var saved = await _trainings.SaveAsync(copy, cancellationToken);
			if (saved == null)
				return ContentSetCopyOutcome.Fail("save");
			await ContentSetAttribution.KeepAsync(_attributions, context, ContentAttributionSubjects.Training, saved.TrainingId, cancellationToken);
			return new ContentSetCopyOutcome { Succeeded = true, LocalItemId = saved.TrainingId };
		}

		/// <summary>The publisher's quiz as new rows (no ids), in the order the scorer and the editor show it.</summary>
		public static Collection<TrainingQuestion> CopyQuiz(Training source)
		{
			var questions = new Collection<TrainingQuestion>();
			foreach (var question in (source?.Questions ?? Enumerable.Empty<TrainingQuestion>()).Where(q => q != null)
				.OrderBy(q => q.TrainingQuestionId <= 0 ? int.MaxValue : q.TrainingQuestionId))
				questions.Add(new TrainingQuestion
				{
					Question = question.Question,
					Type = question.Type,
					Answers = (question.Answers ?? Enumerable.Empty<TrainingQuestionAnswer>()).Where(a => a != null)
						.OrderBy(a => a.TrainingQuestionAnswerId <= 0 ? int.MaxValue : a.TrainingQuestionAnswerId)
						.Select(a => new TrainingQuestionAnswer { Answer = a.Answer, Correct = a.Correct }).ToList()
				});
			return questions;
		}

		public async Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var source = await _trainings.GetTrainingByIdAsync(context.Source.ItemId);
			var local = await _trainings.GetTrainingByIdAsync(context.Copy.LocalItemId);
			if (source == null || source.DepartmentId != context.Set.DepartmentId || local == null || local.DepartmentId != context.SubscriberDepartmentId)
				return ContentSetCopyOutcome.Fail(ProblemArchived);

			local.Name = source.Name;
			local.Description = source.Description;
			local.TrainingText = source.TrainingText;
			local.MinimumScore = source.MinimumScore;
			local.Hours = source.Hours;
			local.CeHours = source.CeHours;
			local.CeProvider = source.CeProvider;
			local.CeCourseNumber = source.CeCourseNumber;
			// The quiz flows down by version: a changed question, option, key or pass mark replaces the copy's quiz (the old rows go with
			// their flags, as when an author rewrites it); an unchanged quiz keeps its rows and ids.
			if (!string.Equals(TrainingQuizScorer.RevisionHash(source), TrainingQuizScorer.RevisionHash(local), StringComparison.Ordinal))
				local.Questions = CopyQuiz(source);
			local.UpdatedOn = DateTime.UtcNow;
			local.UpdatedByUserId = context.UserId;
			var saved = await _trainings.SaveAsync(local, cancellationToken);
			if (saved == null)
				return ContentSetCopyOutcome.Fail("save");
			await ContentSetAttribution.KeepAsync(_attributions, context, ContentAttributionSubjects.Training, saved.TrainingId, cancellationToken);
			return new ContentSetCopyOutcome { Succeeded = true, LocalItemId = saved.TrainingId };
		}

		/// <summary>The training's text followed by its quiz (questions, options, the key marked), the HTML the inbox redline compares.</summary>
		public static string Html(Training training)
		{
			if (training == null)
				return string.Empty;
			var html = new StringBuilder(training.TrainingText ?? string.Empty);
			var questions = CopyQuiz(training);
			if (questions.Count == 0)
				return html.ToString();
			html.Append("<hr /><ol>");
			foreach (var question in questions)
			{
				html.Append("<li><p>").Append(WebUtility.HtmlEncode(question.Question ?? string.Empty)).Append("</p><ul>");
				foreach (var answer in question.Answers)
					html.Append("<li>").Append(answer.Correct ? "&#10003; " : string.Empty).Append(WebUtility.HtmlEncode(answer.Answer ?? string.Empty)).Append("</li>");
				html.Append("</ul></li>");
			}
			html.Append("</ol><p>&#10003; ").Append(training.MinimumScore.ToString("0.##", CultureInfo.InvariantCulture)).Append("%</p>");
			return html.ToString();
		}

		public async Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId)
		{
			var local = await _trainings.GetTrainingByIdAsync(localItemId);
			return local == null || local.DepartmentId != subscriberDepartmentId ? string.Empty : Html(local);
		}

		public async Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context)
		{
			var source = context?.Source == null ? null : await _trainings.GetTrainingByIdAsync(context.Source.ItemId);
			return (source == null || source.DepartmentId != context.Set.DepartmentId ? string.Empty : Html(source), new List<string>());
		}

		public async Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId)
		{
			var local = await _trainings.GetTrainingByIdAsync(localItemId);
			return local != null && local.DepartmentId == subscriberDepartmentId;
		}
	}
}
