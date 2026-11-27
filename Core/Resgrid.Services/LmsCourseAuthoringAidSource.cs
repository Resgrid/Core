using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// LMS course versions (T2) as an authoring aid source (K2), in the LMS layer so baseline code never sees a course type. The member must
	/// be able to edit the course (course rights) with the add-on's authoring gate open; system (Academy) courses are never drafted from.
	/// The grounding is the version's outline and lesson text (blocks, transcripts); a change summary gets only the diff of that text
	/// against the course's version in force.
	/// </summary>
	public sealed class LmsCourseAuthoringAidSource : IAuthoringAidSource
	{
		private readonly ILmsGateService _gate;
		private readonly ILmsCourseService _courses;

		public LmsCourseAuthoringAidSource(ILmsGateService gate, ILmsCourseService courses)
		{
			_gate = gate;
			_courses = courses;
		}

		public AuthoringAidSourceKinds Kind => AuthoringAidSourceKinds.CourseVersion;

		public async Task<AuthoringAidSourceResult> LoadAsync(AuthoringAidRequest request, CancellationToken cancellationToken = default)
		{
			var actor = await _gate.GetActorAsync(request.DepartmentId, request.UserId);
			if (actor == null || !actor.IsCurrentMember || actor.Gate == null || !actor.Gate.CanAuthor)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotAllowed);

			var outline = await _courses.GetOutlineAsync(request.DepartmentId, request.SourceId);
			if (outline?.Course == null || outline.Version == null || outline.Course.DepartmentId != request.DepartmentId || outline.Course.IsSystem)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotFound);
			var rights = await _courses.GetRightsAsync(actor, outline.Course);
			if (rights == null || !rights.Edit)
				return AuthoringAidSourceResult.Fail(AuthoringAidOutcomes.NotAllowed);

			var source = new AuthoringAidSource
			{
				Title = string.IsNullOrWhiteSpace(outline.Version.Title) ? outline.Course.Title : outline.Version.Title,
				Text = Text(outline),
				VersionLabel = outline.Version.Label,
				ItemKey = "course-version:" + outline.Version.LmsCourseVersionId.ToString(CultureInfo.InvariantCulture)
			};

			if (request.Kind == (int)AuthoringAidKinds.ChangeSummary)
			{
				var previous = (await _courses.GetVersionsAsync(request.DepartmentId, outline.Course.LmsCourseId) ?? new List<LmsCourseVersion>())
					.Where(v => v.LmsCourseVersionId != outline.Version.LmsCourseVersionId &&
						(v.Status == (int)ProtocolVersionStatuses.Published || v.Status == (int)ProtocolVersionStatuses.Superseded))
					.OrderByDescending(v => v.Status == (int)ProtocolVersionStatuses.Published).ThenByDescending(v => v.Major).ThenByDescending(v => v.Minor)
					.FirstOrDefault();
				if (previous != null)
				{
					var before = await _courses.GetOutlineAsync(request.DepartmentId, previous.LmsCourseVersionId);
					if (before?.Version != null)
					{
						source.PreviousVersionLabel = previous.Label;
						source.DiffText = AuthoringAidDiffText.From(ProtocolVersionDiff.Compare(AuthoringAidText.AsParagraphs(Text(before)), AuthoringAidText.AsParagraphs(source.Text)));
					}
				}
			}

			return AuthoringAidSourceResult.Ok(source);
		}

		/// <summary>The version as plain lines: the overview, then each module title and its lessons' titles and text.</summary>
		public static string Text(LmsCourseOutline outline)
		{
			var text = new StringBuilder();
			var overview = AuthoringAidText.FromHtml(outline.Version.ContentHtml);
			if (overview.Length > 0)
				text.Append(overview).Append('\n');
			foreach (var module in outline.Modules ?? new List<LmsOutlineModule>())
			{
				if (!string.IsNullOrWhiteSpace(module.Module?.Title))
					text.Append(module.Module.Title.Trim()).Append('\n');
				foreach (var lesson in module.Lessons ?? new List<LmsLesson>())
				{
					if (!string.IsNullOrWhiteSpace(lesson.Title))
						text.Append(lesson.Title.Trim()).Append('\n');
					var body = lesson.Kind == (int)LmsLessonKinds.Blocks ? AuthoringAidText.FromHtml(lesson.ContentHtml)
						: lesson.Kind == (int)LmsLessonKinds.Video ? (lesson.TranscriptText ?? string.Empty).Trim() : string.Empty;
					if (body.Length > 0)
						text.Append(body).Append('\n');
				}
			}
			return WebUtility.HtmlDecode(text.ToString()).Trim();
		}
	}
}
