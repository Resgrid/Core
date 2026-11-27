using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The Resgrid starter course library (T5 item 4, §13a.4 point 2): Resgrid-authored starters (structure and original plain-language text, no
	/// standard's text, no accreditation) cloned into a department as a new DRAFT course the department adapts, submits and publishes through its
	/// normal approval. The clone keeps its attribution: the origin row (source, licence, revision) and an attribution line in the course overview
	/// that travels with the content. Partner entries are documented LTI 1.3 / SCORM / cmi5 slots (the standards work launches them) and are
	/// never cloned. No AI is involved; the LMS authoring gate applies (Manage Courses and the add-on).
	/// </summary>
	public class LmsStarterCourseService : ILmsStarterCourseService
	{
		private readonly ILmsCourseService _courses;
		private readonly ILmsQuestionBankService _banks;
		private readonly ILmsCourseOriginsRepository _origins;
		private readonly IEventAggregator _eventAggregator;

		public LmsStarterCourseService(ILmsCourseService courses, ILmsQuestionBankService banks, ILmsCourseOriginsRepository origins, IEventAggregator eventAggregator)
		{
			_courses = courses;
			_banks = banks;
			_origins = origins;
			_eventAggregator = eventAggregator;
		}

		public IReadOnlyList<LmsStarterCourseManifest> GetCatalog() => LmsStarterCourseCatalog.All;

		public IReadOnlyList<LmsStarterCourseManifest> GetCatalog(string culture) => LmsStarterCourseCatalog.AllFor(culture);

		/// <summary>The attribution line recorded on a clone and shown on its overview.</summary>
		public static string AttributionFor(LmsStarterCourseManifest manifest, string locale) =>
			string.Format(CultureInfo.InvariantCulture, LmsText.Get("Studio.StarterAttribution", locale), manifest.Title, manifest.Revision);

		/// <summary>A starter as a course plan: its modules and lessons, its check as a question bank and a closing assessment, the attribution kept.</summary>
		public static LmsCoursePlan PlanFor(LmsStarterCourseManifest manifest, string locale)
		{
			// Round 9C: the clone carries the starter's text in the author's language (machine-translated variants; English otherwise).
			manifest = LmsStarterCourseCatalog.Localized(manifest, locale);
			var attribution = AttributionFor(manifest, locale);
			var plan = new LmsCoursePlan
			{
				Title = manifest.Title,
				Code = manifest.Code,
				Summary = manifest.Summary,
				Tags = manifest.Tags,
				Hours = manifest.Hours,
				OverviewHtml = (manifest.OverviewHtml ?? string.Empty) + "<p><em>" + WebUtility.HtmlEncode(attribution) + "</em></p>",
				Modules = (manifest.Modules ?? new List<LmsStarterModule>()).Select(m => new LmsCoursePlanModule
				{
					Title = m.Title,
					Lessons = (m.Lessons ?? new List<LmsStarterLesson>()).Select(l => new LmsCoursePlanLesson { Title = l.Title, Html = l.Html }).ToList()
				}).ToList()
			};

			if (manifest.Check?.Questions?.Count > 0)
			{
				plan.BankName = string.Format(CultureInfo.InvariantCulture, LmsText.Get("Studio.StarterBankName", locale), manifest.Title);
				plan.BankDescription = attribution;
				plan.CheckModuleTitle = manifest.Check.Module;
				plan.CheckLessonTitle = manifest.Check.Title;
				plan.CheckPassMark = manifest.Check.PassMark;
				plan.Questions = manifest.Check.Questions.Select(q => new LmsCoursePlanQuestion
				{
					Text = q.Text,
					Options = q.Options.ToList(),
					CorrectIndex = q.Correct,
					Explanation = q.Explanation,
					Kind = q.IsTrueFalse ? (int)LmsQuestionKinds.TrueFalse : (int)LmsQuestionKinds.SingleChoice
				}).ToList();
			}
			return plan;
		}

		public async Task<LmsCourseDraftResult> CloneAsync(LmsActor actor, string starterKey, string locale, CancellationToken cancellationToken = default)
		{
			var manifest = LmsStarterCourseCatalog.Find(starterKey);
			if (manifest == null || manifest.IsPartnerSlot)
				return new LmsCourseDraftResult { Outcome = LmsStudioOutcomes.NotFound };
			if (actor == null || !actor.IsCurrentMember || actor.Gate?.CanAuthor != true)
				return new LmsCourseDraftResult { Outcome = LmsStudioOutcomes.NotEntitled };
			if (!actor.MayManage)
				return new LmsCourseDraftResult { Outcome = LmsStudioOutcomes.NotAllowed };

			var written = await LmsCoursePlanWriter.WriteAsync(_courses, _banks, actor, PlanFor(manifest, locale), cancellationToken);
			if (!written.Succeeded)
				return new LmsCourseDraftResult { Outcome = LmsStudioOutcomes.NotSaved };

			var origin = new LmsCourseOrigin
			{
				DepartmentId = actor.DepartmentId,
				LmsCourseId = written.Course.LmsCourseId,
				LmsCourseVersionId = written.Version.LmsCourseVersionId,
				OriginKind = (int)LmsCourseOriginKinds.Starter,
				SourceKey = manifest.SourceKey,
				SourceName = LmsStarterCourseCatalog.SourceName,
				SourceVersion = manifest.Revision.ToString(CultureInfo.InvariantCulture),
				License = manifest.License,
				Attribution = AttributionFor(manifest, locale),
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = actor.UserId
			};
			try
			{
				await _origins.InsertAsync(origin, cancellationToken, true);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// The overview carries the attribution line either way.
				Logging.LogException(ex, $"The attribution of cloned starter course {written.Course.LmsCourseId} could not be saved.");
			}

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsStarterCourseCloned,
				After = JsonConvert.SerializeObject(new { written.Course.LmsCourseId, written.Version.LmsCourseVersionId, origin.SourceKey, origin.SourceVersion, origin.License }),
				Successful = true,
				ServerName = Environment.MachineName
			});

			return new LmsCourseDraftResult
			{
				Outcome = LmsStudioOutcomes.Created,
				LmsCourseId = written.Course.LmsCourseId,
				LmsCourseVersionId = written.Version.LmsCourseVersionId,
				LmsQuestionBankId = written.LmsQuestionBankId,
				Modules = written.Modules,
				Lessons = written.Lessons,
				Questions = written.Questions
			};
		}
	}
}
