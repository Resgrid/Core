using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Academy
{
	/// <summary>One Resgrid Academy lesson or walkthrough (academy.resgrid.com).</summary>
	public sealed class AcademyLesson
	{
		public AcademyLesson(string id, string topic, double minutes, bool isWalkthrough)
		{
			Id = id;
			Topic = topic;
			Minutes = minutes;
			IsWalkthrough = isWalkthrough;
		}

		/// <summary>The Academy's own id, e.g. "05-calls". Stable: the docs embed it as &lt;AcademyVideo id="..."/&gt;.</summary>
		public string Id { get; }

		/// <summary>English topic name as the Academy lists it.</summary>
		public string Topic { get; }

		public double Minutes { get; }

		/// <summary>A walkthrough has its own page under /deep-dive/lessons/{id}/; a lesson opens on the home page.</summary>
		public bool IsWalkthrough { get; }
	}

	/// <summary>
	/// Academy phase A0 (protocols-training-knowledge-plan §15): a contextual "Learn" link per module page, keyed by Academy
	/// lesson id. Since A2 the lessons and the pages they teach come from the lesson manifests (<see cref="AcademyManifests"/>,
	/// <c>Academy/Manifests/Lessons/*.json</c>); the ids mirror the docs repository's <c>src/components/AcademyVideo/catalog.js</c>, so
	/// when the Academy adds a lesson, add its manifest and the docs entry.
	/// </summary>
	public static class AcademyCatalog
	{
		public static readonly IReadOnlyList<AcademyLesson> Lessons =
			AcademyManifests.Lessons.Select(m => new AcademyLesson(m.Id, m.Topic, m.Minutes, m.Walkthrough)).ToList();

		private static readonly Dictionary<string, AcademyLesson> ById =
			Lessons.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

		/// <summary>User-area controller name (without "Controller") to the lesson that teaches that module: each manifest's "pages".</summary>
		public static readonly IReadOnlyDictionary<string, string> ControllerLessons = AcademyManifests.Lessons
			.SelectMany(m => (m.Pages ?? new List<string>()).Select(page => (Page: page, Lesson: m.Id)))
			.ToDictionary(p => p.Page, p => p.Lesson, StringComparer.OrdinalIgnoreCase);

		public static AcademyLesson Find(string lessonId)
		{
			if (string.IsNullOrWhiteSpace(lessonId))
				return null;

			return ById.TryGetValue(lessonId.Trim(), out var lesson) ? lesson : null;
		}

		/// <summary>The lesson for a page: an explicit lesson id wins (a page can name a closer walkthrough), then the controller.</summary>
		public static AcademyLesson ForPage(string controller, string explicitLessonId = null)
		{
			var explicitLesson = Find(explicitLessonId);
			if (explicitLesson != null)
				return explicitLesson;

			if (!string.IsNullOrWhiteSpace(controller) && ControllerLessons.TryGetValue(controller, out var id))
				return Find(id);

			return null;
		}

		/// <summary>Absolute link to the lesson on the Academy (self-hosted installs can point AcademyUrl at a mirror).</summary>
		public static string LessonUrl(string academyUrl, AcademyLesson lesson)
		{
			var root = HomeUrl(academyUrl);
			if (lesson == null || root == null)
				return root;

			var id = Uri.EscapeDataString(lesson.Id);
			return lesson.IsWalkthrough ? root + "deep-dive/lessons/" + id + "/" : root + "#lesson=" + id;
		}

		/// <summary>The Academy home with a trailing slash, or null when it is switched off (empty setting).</summary>
		public static string HomeUrl(string academyUrl)
		{
			if (string.IsNullOrWhiteSpace(academyUrl))
				return null;

			var root = academyUrl.Trim();
			return root.EndsWith("/", StringComparison.Ordinal) ? root : root + "/";
		}
	}
}
