using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
	/// Hosted video (decision 9, T2 item 3): uploads go to the LMS bucket on RustFS, worker 87 transcodes them to HLS (plus one
	/// progressive MP4 rendition) with ffmpeg, and the player streams through signed URLs: the playlist is served by Resgrid with every
	/// segment rewritten to a short-lived signed URL of the store. Captions are uploaded WebVTT (AI captions are an Ai.Voice hook, not
	/// built). Storage is reported per department against the included 100 GB (no overage billing). Hosted video is read-only for 90
	/// days after the add-on lapses and then archived (lapse rule 6). The offline manifest is what Omni would use (R-C9; no Omni code).
	/// </summary>
	public class LmsMediaService : ILmsMediaService
	{
		private static readonly HashSet<string> VideoTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"video/mp4", "video/quicktime", "video/webm", "video/x-matroska", "video/x-msvideo", "video/mpeg"
		};

		private readonly ILmsMediaStore _store;
		private readonly ILmsVideoTranscoder _transcoder;
		private readonly ILmsMediaAssetsRepository _assets;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly ILmsDepartmentStatesRepository _states;
		private readonly ILmsGateService _gate;
		private readonly IEventAggregator _eventAggregator;

		public LmsMediaService(ILmsMediaStore store, ILmsVideoTranscoder transcoder, ILmsMediaAssetsRepository assets, ILmsLessonsRepository lessons,
			ILmsEnrollmentsRepository enrollments, ILmsCourseVersionsRepository versions, ILmsCoursesRepository courses, ILmsCourseOwnersRepository owners,
			ILmsDepartmentStatesRepository states, ILmsGateService gate, IEventAggregator eventAggregator)
		{
			_store = store;
			_transcoder = transcoder;
			_assets = assets;
			_lessons = lessons;
			_enrollments = enrollments;
			_versions = versions;
			_courses = courses;
			_owners = owners;
			_states = states;
			_gate = gate;
			_eventAggregator = eventAggregator;
		}

		public bool IsStoreConfigured => _store?.IsConfigured == true;

		private async Task<bool> MayAuthorMediaAsync(LmsActor actor)
		{
			if (actor == null || !actor.IsCurrentMember)
				return false;
			if (actor.MayManage)
				return true;
			return (await _owners.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsCourseOwner>()).Any(o => o.CanEdit);
		}

		public async Task<LmsResult> UploadVideoAsync(LmsActor actor, string fileName, string contentType, Stream content, long length, CancellationToken cancellationToken = default)
		{
			if (!await MayAuthorMediaAsync(actor))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanUploadMedia(actor.Gate))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			if (!IsStoreConfigured)
				return LmsResult.Fail(LmsErrors.MediaUnavailable);
			if (content == null || length <= 0)
				return LmsResult.Fail(LmsErrors.InvalidInput);
			if (length > Config.LmsConfig.MaxVideoUploadBytes)
				return LmsResult.Fail(LmsErrors.FileTooLarge);
			if (string.IsNullOrWhiteSpace(contentType) || !VideoTypes.Contains(contentType.Trim()))
				return LmsResult.Fail(LmsErrors.UnsupportedFile);

			var now = DateTime.UtcNow;
			var asset = new LmsMediaAsset
			{
				DepartmentId = actor.DepartmentId,
				Kind = (int)LmsMediaKinds.Video,
				FileName = LmsCourseService.Truncate(Path.GetFileName(fileName ?? "video"), 260),
				ContentType = contentType.Trim(),
				Status = (int)LmsMediaStatuses.Uploading,
				CreatedOn = now,
				CreatedByUserId = actor.UserId
			};
			await _assets.InsertAsync(asset, cancellationToken, true);

			try
			{
				asset.SourceKey = _store.KeyFor(actor.DepartmentId, asset.LmsMediaAssetId, "source" + SafeExtension(asset.FileName));
				await _store.PutAsync(asset.SourceKey, content, asset.ContentType, cancellationToken);
				asset.SourceBytes = await _store.GetSizeAsync(asset.SourceKey, cancellationToken) ?? length;
				asset.Status = (int)LmsMediaStatuses.Queued;
				await _assets.TryMoveAsync(asset, LmsMediaStatuses.Uploading, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An LMS video upload failed.");
				asset.Status = (int)LmsMediaStatuses.Failed;
				asset.LastError = "upload-failed";
				await _assets.TryMoveAsync(asset, LmsMediaStatuses.Uploading, cancellationToken);
				return new LmsResult { Error = LmsErrors.MediaUnavailable, Value = asset };
			}

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsMediaUploaded,
				After = JsonConvert.SerializeObject(new { asset.LmsMediaAssetId, asset.FileName, asset.SourceBytes }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return new LmsResult { Value = asset };
		}

		private static string SafeExtension(string fileName)
		{
			var extension = Path.GetExtension(fileName ?? string.Empty)?.ToLowerInvariant();
			return string.IsNullOrEmpty(extension) || extension.Length > 6 || !extension.Skip(1).All(char.IsLetterOrDigit) ? ".bin" : extension;
		}

		public async Task<LmsResult> UploadCaptionsAsync(LmsActor actor, int lmsLessonId, string locale, string label, byte[] webVtt, CancellationToken cancellationToken = default)
		{
			if (!await MayAuthorMediaAsync(actor))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanUploadMedia(actor.Gate))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			if (!IsStoreConfigured)
				return LmsResult.Fail(LmsErrors.MediaUnavailable);
			if (webVtt == null || webVtt.Length == 0)
				return LmsResult.Fail(LmsErrors.InvalidInput);
			if (webVtt.LongLength > Config.LmsConfig.MaxCaptionBytes)
				return LmsResult.Fail(LmsErrors.FileTooLarge);
			if (!IsWebVtt(webVtt))
				return LmsResult.Fail(LmsErrors.UnsupportedFile);
			var language = NormalizeLocale(locale);
			if (language == null)
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var lesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, lmsLessonId);
			if (lesson == null || lesson.DepartmentId != actor.DepartmentId || lesson.Kind != (int)LmsLessonKinds.Video)
				return LmsResult.Fail(LmsErrors.NotFound);
			var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, lesson.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
			if (course == null || !actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId)).Edit)
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!ProtocolLifecycle.IsContentEditable(version.Status))
				return LmsResult.Fail(LmsErrors.NotDraft);

			var key = _store.KeyFor(actor.DepartmentId, 0, "captions/" + lesson.LmsLessonId.ToString(CultureInfo.InvariantCulture) + "-" + language + "-" + Guid.NewGuid().ToString("N") + ".vtt");
			using (var stream = new MemoryStream(webVtt, writable: false))
				await _store.PutAsync(key, stream, "text/vtt", cancellationToken);

			var tracks = LmsCaptionTrack.Parse(lesson.CaptionTracksJson).Where(t => !string.Equals(t.Locale, language, StringComparison.OrdinalIgnoreCase)).ToList();
			tracks.Add(new LmsCaptionTrack { Locale = language, Label = LmsCourseService.Truncate(string.IsNullOrWhiteSpace(label) ? language : label.Trim(), 60), ObjectKey = key });
			lesson.CaptionTracksJson = JsonConvert.SerializeObject(tracks);
			await _lessons.UpdateAsync(lesson, cancellationToken, true);
			return new LmsResult { Value = lesson };
		}

		/// <summary>A WebVTT file starts with "WEBVTT" (after an optional byte order mark).</summary>
		public static bool IsWebVtt(byte[] content)
		{
			if (content == null || content.Length < 6)
				return false;
			var text = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 32)).TrimStart((char)0xFEFF);
			return text.StartsWith("WEBVTT", StringComparison.Ordinal);
		}

		private static string NormalizeLocale(string locale)
		{
			if (string.IsNullOrWhiteSpace(locale))
				return null;
			var value = locale.Trim();
			if (value.Length > 16 || !value.All(c => char.IsLetterOrDigit(c) || c == '-'))
				return null;
			return value.ToLowerInvariant();
		}

		public Task<LmsMediaAsset> GetAssetAsync(int departmentId, int lmsMediaAssetId) => _assets.GetByIdForDepartmentAsync(departmentId, lmsMediaAssetId);

		public async Task<List<LmsMediaAsset>> GetAssetsAsync(int departmentId) => await _assets.GetForDepartmentAsync(departmentId) ?? new List<LmsMediaAsset>();

		private static bool Playable(LmsDepartmentGate gate, LmsMediaAsset asset, DateTime utcNow) =>
			asset != null && asset.Status == (int)LmsMediaStatuses.Ready && LmsLapseRules.CanStreamHostedVideo(gate, utcNow);

		public async Task<string> GetSignedPlaylistAsync(LmsDepartmentGate gate, LmsMediaAsset asset, DateTime utcNow)
		{
			if (!Playable(gate, asset, utcNow) || !IsStoreConfigured || string.IsNullOrWhiteSpace(asset.HlsPrefix))
				return null;

			var playlist = Encoding.UTF8.GetString(await _store.ReadAllAsync(asset.HlsPrefix + "index.m3u8"));
			return RewritePlaylist(playlist, asset.HlsPrefix, key => _store.GetSignedUrl(key, utcNow.AddMinutes(Math.Max(5, Config.LmsConfig.SignedUrlMinutes))));
		}

		/// <summary>Rewrites every segment line of an HLS media playlist to a signed URL (tags and comments stay as they are).</summary>
		public static string RewritePlaylist(string playlist, string prefix, Func<string, string> sign)
		{
			var output = new StringBuilder();
			foreach (var raw in (playlist ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
			{
				var line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
				{
					output.Append(raw).Append('\n');
					continue;
				}
				// Only plain relative segment names (what worker 87 writes); anything else is dropped rather than followed.
				if (line.Contains("..") || line.Contains("://") || line.StartsWith("/", StringComparison.Ordinal))
					continue;
				output.Append(sign(prefix + line)).Append('\n');
			}
			return output.ToString();
		}

		public Task<string> GetSignedMp4UrlAsync(LmsDepartmentGate gate, LmsMediaAsset asset, DateTime utcNow)
		{
			if (!Playable(gate, asset, utcNow) || !IsStoreConfigured || string.IsNullOrWhiteSpace(asset.Mp4Key))
				return Task.FromResult<string>(null);
			return Task.FromResult(_store.GetSignedUrl(asset.Mp4Key, utcNow.AddMinutes(Math.Max(5, Config.LmsConfig.SignedUrlMinutes))));
		}

		public async Task<string> GetCaptionsAsync(int departmentId, LmsLesson lesson, string locale)
		{
			var track = LmsCaptionTrack.Parse(lesson?.CaptionTracksJson).FirstOrDefault(t => string.Equals(t.Locale, locale, StringComparison.OrdinalIgnoreCase));
			if (track == null || string.IsNullOrWhiteSpace(track.ObjectKey) || !IsStoreConfigured || lesson.DepartmentId != departmentId)
				return null;
			var bytes = await _store.ReadAllAsync(track.ObjectKey);
			return bytes == null ? null : Encoding.UTF8.GetString(bytes);
		}

		public async Task<LmsStorageReport> GetStorageReportAsync(int departmentId)
		{
			var assets = await GetAssetsAsync(departmentId);
			return new LmsStorageReport
			{
				IncludedBytes = Config.LmsConfig.IncludedStorageBytes,
				UsedBytes = await _assets.GetStoredBytesAsync(departmentId),
				Assets = assets.Count,
				Largest = assets.OrderByDescending(a => a.StoredBytes).Take(10).ToList()
			};
		}

		public async Task<List<LmsOfflineVideo>> GetOfflineManifestAsync(int departmentId, string userId, DateTime utcNow)
		{
			var result = new List<LmsOfflineVideo>();
			var gate = await _gate.GetGateAsync(departmentId);
			if (!IsStoreConfigured || !LmsLapseRules.CanStreamHostedVideo(gate, utcNow))
				return result;

			foreach (var enrollment in (await _enrollments.GetForUserAsync(departmentId, userId) ?? new List<LmsEnrollment>()).Where(e => e.IsOpen))
			{
				var course = await _courses.GetVisibleByIdAsync(departmentId, enrollment.LmsCourseId);
				if (course == null || course.IsSystem || !LmsLapseRules.CanRun(gate, false, enrollment, utcNow))
					continue;
				foreach (var lesson in (await _lessons.GetForVersionAsync(departmentId, enrollment.LmsCourseVersionId) ?? new List<LmsLesson>())
					.Where(l => l.Kind == (int)LmsLessonKinds.Video && l.VideoSource == (int)LmsVideoSources.Hosted && l.AvailableOffline && l.LmsMediaAssetId.HasValue))
				{
					var asset = await _assets.GetByIdForDepartmentAsync(departmentId, lesson.LmsMediaAssetId.Value);
					var maxBytes = lesson.OfflineMaxBytes ?? Config.LmsConfig.DefaultOfflineMaxBytes;
					if (asset == null || string.IsNullOrWhiteSpace(asset.Mp4Key))
						continue;
					var size = await _store.GetSizeAsync(asset.Mp4Key) ?? 0;
					// One rendition per device, never more than the lesson's cap (R-C9): a larger rendition is not offered offline.
					if (size <= 0 || size > maxBytes)
						continue;
					var urlExpires = utcNow.AddMinutes(Math.Max(5, Config.LmsConfig.SignedUrlMinutes));
					result.Add(new LmsOfflineVideo
					{
						LmsEnrollmentId = enrollment.LmsEnrollmentId,
						LmsLessonId = lesson.LmsLessonId,
						Title = lesson.Title,
						Url = await GetSignedMp4UrlAsync(gate, asset, utcNow),
						Bytes = size,
						MaxBytes = maxBytes,
						UrlExpiresOn = urlExpires,
						CacheExpiresOn = utcNow.AddDays(lesson.OfflineExpiryDays ?? Config.LmsConfig.DefaultOfflineExpiryDays)
					});
				}
			}
			return result.Where(v => v.Url != null).ToList();
		}

		#region Workers 87 and 88

		public async Task<int> ProcessTranscodeQueueAsync(int maxItems, CancellationToken cancellationToken = default)
		{
			if (!IsStoreConfigured)
				return 0;

			var finished = 0;
			for (var i = 0; i < Math.Max(1, maxItems); i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var now = DateTime.UtcNow;
				var asset = await _assets.ClaimNextAsync(now, now - LmsMediaStateMachine.StaleClaim, cancellationToken);
				if (asset == null)
					break;
				await TranscodeAsync(asset, cancellationToken);
				finished++;
			}
			return finished;
		}

		/// <summary>One claimed asset: download the source, transcode, upload the renditions, Ready; or back to the queue / Failed.</summary>
		internal async Task TranscodeAsync(LmsMediaAsset asset, CancellationToken cancellationToken)
		{
			var work = Path.Combine(Path.GetTempPath(), "resgrid-lms-" + asset.LmsMediaAssetId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(work);
				var source = Path.Combine(work, "source" + SafeExtension(asset.FileName));
				using (var input = await _store.OpenReadAsync(asset.SourceKey, cancellationToken))
				using (var file = System.IO.File.Create(source))
					await input.CopyToAsync(file, cancellationToken);

				var output = await _transcoder.TranscodeAsync(source, Path.Combine(work, "out"), cancellationToken);
				if (output == null || output.Files == null || !output.Files.Any(f => f.EndsWith("index.m3u8", StringComparison.OrdinalIgnoreCase)))
					throw new InvalidOperationException("The transcoder produced no HLS playlist.");

				long bytes = 0;
				var prefix = _store.KeyFor(asset.DepartmentId, asset.LmsMediaAssetId, "hls/");
				foreach (var relative in output.Files)
				{
					var path = Path.Combine(output.Directory, relative.Replace('/', Path.DirectorySeparatorChar));
					var key = relative.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && !relative.Contains('/')
						? _store.KeyFor(asset.DepartmentId, asset.LmsMediaAssetId, "video.mp4")
						: prefix + relative;
					using (var stream = System.IO.File.OpenRead(path))
					{
						bytes += stream.Length;
						await _store.PutAsync(key, stream, ContentTypeFor(relative), cancellationToken);
					}
					if (key.EndsWith("video.mp4", StringComparison.OrdinalIgnoreCase))
						asset.Mp4Key = key;
				}

				asset.HlsPrefix = prefix;
				asset.OutputBytes = bytes;
				asset.DurationSeconds = output.DurationSeconds;
				asset.LastError = null;
				asset.ReadyOn = DateTime.UtcNow;
				asset.Status = (int)LmsMediaStatuses.Ready;
				await _assets.TryMoveAsync(asset, LmsMediaStatuses.Transcoding, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// A stopping worker leaves the claim; it goes back to the queue once stale.
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Transcoding LMS media {asset.LmsMediaAssetId} failed (attempt {asset.Attempts}).");
				asset.LastError = LmsCourseService.Truncate(ex.Message, 2000);
				asset.Status = (int)LmsMediaStateMachine.AfterFailure(asset.Attempts);
				await _assets.TryMoveAsync(asset, LmsMediaStatuses.Transcoding, CancellationToken.None);
			}
			finally
			{
				try
				{
					if (Directory.Exists(work))
						Directory.Delete(work, true);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An LMS transcode work folder could not be removed.");
				}
			}
		}

		private static string ContentTypeFor(string path)
		{
			if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) return "application/vnd.apple.mpegurl";
			if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)) return "video/mp2t";
			if (path.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase)) return "video/iso.segment";
			if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return "video/mp4";
			return "application/octet-stream";
		}

		public async Task<int> ArchiveLapsedMediaAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var archived = 0;
			// R-L9: departments with due work first (ready media, add-on last seen more than 90 days ago), then one gate answer each.
			foreach (var departmentId in await _states.GetLapsedWithReadyMediaAsync(utcNow - LmsLapseRules.HostedVideoReadOnlyWindow) ?? new List<int>())
			{
				try
				{
					var gate = await _gate.GetGateAsync(departmentId);
					if (!LmsLapseRules.ShouldArchiveHostedMedia(gate, utcNow))
						continue;
					foreach (var asset in (await _assets.GetForDepartmentAsync(departmentId) ?? new List<LmsMediaAsset>()).Where(a => a.Status == (int)LmsMediaStatuses.Ready))
					{
						asset.Status = (int)LmsMediaStatuses.Archived;
						asset.ArchivedOn = utcNow;
						if (await _assets.TryMoveAsync(asset, LmsMediaStatuses.Ready, cancellationToken))
							archived++;
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Archiving lapsed LMS media failed for department {departmentId}.");
				}
			}
			return archived;
		}

		#endregion Workers 87 and 88
	}
}
