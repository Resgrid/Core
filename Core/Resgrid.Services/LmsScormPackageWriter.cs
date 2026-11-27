using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.LmsExports;
using Resgrid.Model.Lms;

namespace Resgrid.Services
{
	/// <summary>
	/// A SCORM 1.2 package of one course version (the data exit, R-C12): a single SCO (index.html) holding every module and lesson in order,
	/// with a small runtime script that finds the LMS's SCORM API, marks the course incomplete when opened and completed when the learner
	/// presses the button at the end, and reports session time. Only versions whose lessons are all blocks, videos or documents are packaged
	/// (<see cref="LmsDataExitRules.ScormRefusal"/>); assessments, surveys and protocol lessons have no faithful single-SCO form.
	/// Hosted video goes into the package when the export copied it; YouTube/Vimeo stay embeds; documents stay links.
	/// </summary>
	public static class LmsScormPackageWriter
	{
		public const string RuntimeScript = @"(function () {
  'use strict';
  var api = null, started = new Date().getTime(), finished = false;
  function find(win) {
    var tries = 0;
    while (win && !win.API && win.parent && win.parent !== win && tries < 10) { win = win.parent; tries++; }
    return win && win.API ? win.API : null;
  }
  function pad(n) { return (n < 10 ? '0' : '') + n; }
  function sessionTime() {
    var s = Math.floor((new Date().getTime() - started) / 1000);
    var h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60);
    return pad(Math.min(h, 9999)) + ':' + pad(m) + ':' + pad(s % 60);
  }
  function finish() {
    if (!api || finished) { return; }
    finished = true;
    api.LMSSetValue('cmi.core.session_time', sessionTime());
    api.LMSCommit('');
    api.LMSFinish('');
  }
  window.resgridScorm = {
    start: function () {
      api = find(window) || (window.opener ? find(window.opener) : null);
      if (!api) { return; }
      api.LMSInitialize('');
      var status = api.LMSGetValue('cmi.core.lesson_status');
      if (status === 'not attempted' || status === '') { api.LMSSetValue('cmi.core.lesson_status', 'incomplete'); }
      api.LMSCommit('');
    },
    complete: function () {
      if (api) { api.LMSSetValue('cmi.core.lesson_status', 'completed'); api.LMSCommit(''); }
      var done = document.getElementById('rg-done');
      if (done) { done.hidden = false; }
      finish();
    },
    finish: finish
  };
  window.addEventListener('load', function () {
    window.resgridScorm.start();
    var button = document.getElementById('rg-complete');
    if (button) { button.addEventListener('click', window.resgridScorm.complete); }
  });
  window.addEventListener('pagehide', finish);
  window.addEventListener('beforeunload', finish);
})();
";

		private const string Stylesheet = @"body{font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;line-height:1.5;margin:0;color:#1f2933;background:#fff}
main,header,footer{max-width:960px;margin:0 auto;padding:16px}
h1{font-size:1.6em}h2{font-size:1.3em;border-bottom:1px solid #d9e2ec;padding-bottom:4px;margin-top:2em}h3{font-size:1.1em}
article{margin:1.5em 0}video,iframe{max-width:100%;width:100%;aspect-ratio:16/9;border:0;background:#000}
img{max-width:100%;height:auto}table{border-collapse:collapse}td,th{border:1px solid #d9e2ec;padding:4px 8px}
button{font-size:1em;padding:10px 18px;border:0;border-radius:4px;background:#1a73e8;color:#fff;cursor:pointer}
.rg-note{color:#52606d;font-style:italic}";

		/// <summary>A file the package carries besides its pages (an MP4 rendition or a caption track): its name in the package and its store key.</summary>
		public sealed class PackageMedia
		{
			public string PackagePath { get; set; }
			public string ObjectKey { get; set; }
		}

		private static string Text(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		private static string Absolute(string url, string baseUrl)
		{
			if (string.IsNullOrWhiteSpace(url))
				return null;
			var trimmed = url.Trim();
			if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
				return absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp ? absolute.ToString() : null;
			return trimmed.StartsWith("/", StringComparison.Ordinal) ? LmsXapiStatements.HomePage(baseUrl) + trimmed : null;
		}

		/// <summary>The package's index.html. <paramref name="mediaPaths"/> maps a hosted asset id to its MP4 inside the package.</summary>
		public static string BuildPage(LmsCourse course, LmsCourseVersion version, IReadOnlyList<LmsCourseModule> modules, IReadOnlyList<LmsLesson> lessons,
			IReadOnlyDictionary<int, string> mediaPaths, IReadOnlyDictionary<int, List<(string Path, LmsCaptionTrack Track)>> captionPaths, string baseUrl)
		{
			var locale = string.IsNullOrWhiteSpace(version.Locale) ? "en" : version.Locale.Trim();
			var title = string.IsNullOrWhiteSpace(version.Title) ? course.Title : version.Title;
			var page = new StringBuilder();
			page.Append("<!DOCTYPE html>\n<html lang=\"").Append(Text(locale)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
				.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>").Append(Text(title)).Append("</title>\n")
				.Append("<link rel=\"stylesheet\" href=\"course.css\">\n<script src=\"scorm.js\"></script>\n</head>\n<body>\n<header>\n<h1>").Append(Text(title)).Append("</h1>\n");
			if (!string.IsNullOrWhiteSpace(course.Summary))
				page.Append("<p>").Append(Text(course.Summary)).Append("</p>\n");
			if (!string.IsNullOrWhiteSpace(version.ContentHtml))
				page.Append("<div class=\"rg-overview\">").Append(StringHelpers.SanitizeHtmlForStorage(version.ContentHtml)).Append("</div>\n");
			page.Append("</header>\n<main>\n");

			foreach (var module in modules.OrderBy(m => m.SortOrder).ThenBy(m => m.LmsCourseModuleId))
			{
				page.Append("<section id=\"module-").Append(module.LmsCourseModuleId.ToString(CultureInfo.InvariantCulture)).Append("\">\n<h2>").Append(Text(module.Title)).Append("</h2>\n");
				foreach (var lesson in lessons.Where(l => l.LmsCourseModuleId == module.LmsCourseModuleId).OrderBy(l => l.SortOrder).ThenBy(l => l.LmsLessonId))
				{
					page.Append("<article id=\"lesson-").Append(lesson.LmsLessonId.ToString(CultureInfo.InvariantCulture)).Append("\">\n<h3>").Append(Text(lesson.Title)).Append("</h3>\n");
					switch ((LmsLessonKinds)lesson.Kind)
					{
						case LmsLessonKinds.Blocks:
							page.Append(StringHelpers.SanitizeHtmlForStorage(lesson.ContentHtml ?? string.Empty)).Append('\n');
							if (!string.IsNullOrWhiteSpace(lesson.FreeResponsePrompt))
								page.Append("<p class=\"rg-note\">").Append(Text(lesson.FreeResponsePrompt)).Append("</p>\n");
							break;
						case LmsLessonKinds.Video:
							AppendVideo(page, lesson, mediaPaths, captionPaths, locale, baseUrl);
							break;
						case LmsLessonKinds.Document:
							var link = Absolute(lesson.DocumentUrl, baseUrl);
							if (link != null)
								page.Append("<p><a href=\"").Append(Text(link)).Append("\" target=\"_blank\" rel=\"noopener\">")
									.Append(Text(string.IsNullOrWhiteSpace(lesson.DocumentName) ? link : lesson.DocumentName)).Append("</a></p>\n");
							break;
					}

					page.Append("</article>\n");
				}

				page.Append("</section>\n");
			}

			page.Append("</main>\n<footer>\n<button type=\"button\" id=\"rg-complete\">").Append(Text(LmsExportTexts.Get("Scorm.Complete", locale))).Append("</button>\n")
				.Append("<p id=\"rg-done\" hidden>").Append(Text(LmsExportTexts.Get("Scorm.Completed", locale))).Append("</p>\n</footer>\n</body>\n</html>\n");
			return page.ToString();
		}

		private static void AppendVideo(StringBuilder page, LmsLesson lesson, IReadOnlyDictionary<int, string> mediaPaths,
			IReadOnlyDictionary<int, List<(string Path, LmsCaptionTrack Track)>> captionPaths, string locale, string baseUrl)
		{
			switch ((LmsVideoSources)lesson.VideoSource)
			{
				case LmsVideoSources.Hosted:
					if (lesson.LmsMediaAssetId.HasValue && mediaPaths != null && mediaPaths.TryGetValue(lesson.LmsMediaAssetId.Value, out var path))
					{
						page.Append("<video controls preload=\"metadata\" src=\"").Append(Text(path)).Append("\">");
						if (captionPaths != null && captionPaths.TryGetValue(lesson.LmsLessonId, out var tracks))
							foreach (var (trackPath, track) in tracks)
								page.Append("<track kind=\"captions\" src=\"").Append(Text(trackPath)).Append("\" srclang=\"").Append(Text(track.Locale ?? "en"))
									.Append("\" label=\"").Append(Text(track.Label ?? track.Locale ?? "Captions")).Append("\">");
						page.Append("</video>\n");
					}
					else
					{
						page.Append("<p class=\"rg-note\">").Append(Text(LmsExportTexts.Get("Scorm.VideoNotIncluded", locale))).Append("</p>\n");
					}
					break;
				case LmsVideoSources.Embed:
					var embed = Absolute(lesson.VideoUrl, baseUrl);
					if (embed != null)
						page.Append("<iframe src=\"").Append(Text(embed)).Append("\" title=\"").Append(Text(lesson.Title))
							.Append("\" allow=\"fullscreen; picture-in-picture\" allowfullscreen></iframe>\n");
					break;
				case LmsVideoSources.Academy:
					var academy = Absolute(lesson.VideoUrl, baseUrl);
					if (academy != null)
						page.Append("<video controls preload=\"metadata\" src=\"").Append(Text(academy)).Append("\"></video>\n");
					break;
			}

			if (!string.IsNullOrWhiteSpace(lesson.TranscriptText))
				page.Append("<details><summary>").Append(Text(LmsExportTexts.Get("Scorm.Transcript", locale))).Append("</summary><p>")
					.Append(Text(lesson.TranscriptText).Replace("\n", "<br>")).Append("</p></details>\n");
		}

		/// <summary>imsmanifest.xml of a single-SCO SCORM 1.2 package listing every file it carries.</summary>
		public static string BuildManifest(LmsCourse course, LmsCourseVersion version, IEnumerable<string> files)
		{
			XNamespace cp = "http://www.imsproject.org/xsd/imscp_rootv1p1p2";
			XNamespace adlcp = "http://www.adlnet.org/xsd/adlcp_rootv1p2";
			XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
			var title = string.IsNullOrWhiteSpace(version.Title) ? course.Title : version.Title;
			var identifier = "resgrid-course-" + course.LmsCourseId.ToString(CultureInfo.InvariantCulture) + "-v" + version.LmsCourseVersionId.ToString(CultureInfo.InvariantCulture);

			var resource = new XElement(cp + "resource",
				new XAttribute("identifier", "resource-1"), new XAttribute("type", "webcontent"), new XAttribute(adlcp + "scormtype", "sco"), new XAttribute("href", "index.html"));
			foreach (var file in files)
				resource.Add(new XElement(cp + "file", new XAttribute("href", file)));

			var manifest = new XElement(cp + "manifest",
				new XAttribute("identifier", identifier), new XAttribute("version", version.Label ?? "1.0"),
				new XAttribute(XNamespace.Xmlns + "adlcp", adlcp), new XAttribute(XNamespace.Xmlns + "xsi", xsi),
				new XAttribute(xsi + "schemaLocation", "http://www.imsproject.org/xsd/imscp_rootv1p1p2 imscp_rootv1p1p2.xsd http://www.imsglobal.org/xsd/imsmd_rootv1p2p1 imsmd_rootv1p2p1.xsd http://www.adlnet.org/xsd/adlcp_rootv1p2 adlcp_rootv1p2.xsd"),
				new XElement(cp + "metadata", new XElement(cp + "schema", "ADL SCORM"), new XElement(cp + "schemaversion", "1.2")),
				new XElement(cp + "organizations", new XAttribute("default", "organization-1"),
					new XElement(cp + "organization", new XAttribute("identifier", "organization-1"),
						new XElement(cp + "title", title),
						new XElement(cp + "item", new XAttribute("identifier", "item-1"), new XAttribute("identifierref", "resource-1"), new XAttribute("isvisible", "true"),
							new XElement(cp + "title", title)))),
				new XElement(cp + "resources", resource));

			return new XDeclaration("1.0", "UTF-8", null) + "\n" + manifest.ToString();
		}

		/// <summary>
		/// Writes the package (a zip) to <paramref name="output"/>. <paramref name="media"/> lists the files to copy from the store through
		/// <paramref name="openMedia"/>; a file that cannot be read is left out and the page says the video is not included.
		/// </summary>
		public static async Task WriteAsync(Stream output, LmsCourse course, LmsCourseVersion version, IReadOnlyList<LmsCourseModule> modules, IReadOnlyList<LmsLesson> lessons,
			IReadOnlyDictionary<int, PackageMedia> videos, IReadOnlyDictionary<int, List<(PackageMedia Media, LmsCaptionTrack Track)>> captions,
			Func<string, CancellationToken, Task<Stream>> openMedia, string baseUrl, CancellationToken cancellationToken)
		{
			using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
			var files = new List<string> { "index.html", "scorm.js", "course.css" };
			var mediaPaths = new Dictionary<int, string>();
			var captionPaths = new Dictionary<int, List<(string, LmsCaptionTrack)>>();

			async Task<bool> CopyAsync(PackageMedia media)
			{
				if (media == null || openMedia == null || string.IsNullOrWhiteSpace(media.ObjectKey))
					return false;
				try
				{
					await using var source = await openMedia(media.ObjectKey, cancellationToken);
					if (source == null)
						return false;
					var entry = archive.CreateEntry(media.PackagePath, CompressionLevel.NoCompression);
					await using var target = entry.Open();
					await source.CopyToAsync(target, cancellationToken);
					files.Add(media.PackagePath);
					return true;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "SCORM package media " + media.ObjectKey + " could not be read; the package leaves it out.");
					return false;
				}
			}

			foreach (var (assetId, media) in videos ?? new Dictionary<int, PackageMedia>())
				if (await CopyAsync(media))
					mediaPaths[assetId] = media.PackagePath;

			foreach (var (lessonId, tracks) in captions ?? new Dictionary<int, List<(PackageMedia, LmsCaptionTrack)>>())
				foreach (var (media, track) in tracks)
					if (await CopyAsync(media))
					{
						if (!captionPaths.TryGetValue(lessonId, out var list))
							captionPaths[lessonId] = list = new List<(string, LmsCaptionTrack)>();
						list.Add((media.PackagePath, track));
					}

			WriteText(archive, "index.html", BuildPage(course, version, modules, lessons, mediaPaths, captionPaths, baseUrl));
			WriteText(archive, "scorm.js", RuntimeScript);
			WriteText(archive, "course.css", Stylesheet);
			WriteText(archive, "imsmanifest.xml", BuildManifest(course, version, files));
		}

		private static void WriteText(ZipArchive archive, string path, string text)
		{
			var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
			using var stream = entry.Open();
			var bytes = new UTF8Encoding(false).GetBytes(text);
			stream.Write(bytes, 0, bytes.Length);
		}
	}
}
