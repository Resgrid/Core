using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Member and reviewer notices of training records (round 9B; EventTypes 79-81): an hours submission received, accepted as self-reported,
	/// approved, denied (with the reason) or sent back for more information; a submission waiting for a reviewer (each, or the daily digest);
	/// a completion an instructor recorded for the member, and its void (the round-7B gap). Sent through the standard notification path
	/// (ICommunicationService.SendNotificationAsync: the member's notification settings choose push, e-mail and text), in the member's own
	/// language (Areas/User/TrainingReviews resx, 10 locales; this work overrides the older English-only outbound rule), to active members only
	/// (inactive-member policy), and never while the department may not transmit.
	/// </summary>
	public class TrainingRecordNoticeService : ITrainingRecordNoticeService
	{
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviews", typeof(SupportedLocales).Assembly);

		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentSettingsService _settings;

		public TrainingRecordNoticeService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IUserProfileService profiles,
			IDepartmentSettingsService settings)
		{
			_communication = communication;
			_departments = departments;
			_profiles = profiles;
			_settings = settings;
		}

		/// <summary>The push event code: "LS:" a member's submission, "LV:" a review waiting, "LC:" a recorded completion.</summary>
		public static string EventCode(TrainingRecordNoticeKinds kind, int id)
		{
			var prefix = kind == TrainingRecordNoticeKinds.ReviewWaiting || kind == TrainingRecordNoticeKinds.ReviewDigest ? "LV:"
				: kind == TrainingRecordNoticeKinds.CompletionRecorded || kind == TrainingRecordNoticeKinds.CompletionVoided ? "LC:" : "LS:";
			return prefix + id.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>The EventTypes value a notice kind belongs to (79, 80 or 81).</summary>
		public static EventTypes EventTypeOf(TrainingRecordNoticeKinds kind)
		{
			switch (kind)
			{
				case TrainingRecordNoticeKinds.ReviewWaiting:
				case TrainingRecordNoticeKinds.ReviewDigest:
					return EventTypes.TrainingReviewNotice;
				case TrainingRecordNoticeKinds.CompletionRecorded:
				case TrainingRecordNoticeKinds.CompletionVoided:
					return EventTypes.TrainingCompletionNotice;
				default:
					return EventTypes.TrainingSubmissionNotice;
			}
		}

		private static string BaseUrl => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/');

		public static string SubmissionLink(int learningRecordId) => BaseUrl + "/User/TrainingReviews/Submission/" + learningRecordId.ToString(CultureInfo.InvariantCulture);

		public static string ReviewLink(int learningRecordId) => BaseUrl + "/User/TrainingReviews/Review/" + learningRecordId.ToString(CultureInfo.InvariantCulture);

		public static string InboxLink() => BaseUrl + "/User/TrainingReviews";

		public static string TranscriptLink() => BaseUrl + "/User/TrainingRecords/Transcript";

		public static CultureInfo Culture(string language)
		{
			try
			{
				var culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en" : language);
				if (SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName))
					return culture;
			}
			catch (CultureNotFoundException)
			{
			}

			return CultureInfo.GetCultureInfo("en");
		}

		/// <summary>A notice string in the member's language (English when a key is missing).</summary>
		public static string Text(string key, CultureInfo culture)
		{
			var value = Strings.GetString(key, culture);
			return string.IsNullOrEmpty(value) ? Strings.GetString(key, CultureInfo.GetCultureInfo("en")) ?? key : value;
		}

		private static string Plain(string value, int max = 200)
		{
			var text = SmsContentHelper.StripDisallowedUrls(StringHelpers.HtmlToPlainText(value ?? string.Empty) ?? string.Empty, Array.Empty<string>())?.Trim();
			if (string.IsNullOrEmpty(text))
				return string.Empty;
			return text.Length > max ? text.Substring(0, max) + "..." : text;
		}

		private static string Hours(decimal hours, CultureInfo culture) => hours.ToString("0.##", culture);

		/// <summary>The title and message of a notice, in the member's language (public for the tests and the docs).</summary>
		public static (string Title, string Message) Compose(TrainingRecordNoticeKinds kind, CultureInfo culture, string subject, decimal hours, string note, string actorName,
			int count, DateTime? date, string link)
		{
			var title = Text("Notice." + kind + ".Title", culture);
			string body;
			switch (kind)
			{
				case TrainingRecordNoticeKinds.SubmissionDenied:
				case TrainingRecordNoticeKinds.SubmissionInfoRequested:
				case TrainingRecordNoticeKinds.CompletionVoided:
					body = string.Format(culture, Text("Notice." + kind + ".Body", culture), Plain(subject), Plain(note, 500));
					break;
				case TrainingRecordNoticeKinds.ReviewWaiting:
					body = string.Format(culture, Text("Notice." + kind + ".Body", culture), Plain(actorName, 100), Plain(subject), Hours(hours, culture));
					break;
				case TrainingRecordNoticeKinds.ReviewDigest:
					body = string.Format(culture, Text("Notice." + kind + ".Body", culture), count);
					break;
				case TrainingRecordNoticeKinds.CompletionRecorded:
					body = string.Format(culture, Text("Notice." + kind + ".Body", culture), Plain(actorName, 100), Plain(subject),
						date.HasValue ? date.Value.ToString("d", culture) : string.Empty);
					break;
				default:
					body = string.Format(culture, Text("Notice." + kind + ".Body", culture), Plain(subject), Hours(hours, culture));
					break;
			}

			return (title, string.IsNullOrWhiteSpace(link) ? body : body + " " + link);
		}

		public async Task<bool> NotifyMemberAsync(LearningRecord record, TrainingRecordNoticeKinds kind, string note, CancellationToken cancellationToken = default)
		{
			if (record == null || string.IsNullOrWhiteSpace(record.UserId))
				return false;
			var link = SubmissionLink(record.LearningRecordId);
			return await SendAsync(record.DepartmentId, record.UserId, kind, record.LearningRecordId,
				culture => Compose(kind, culture, record.Title, record.Hours, note, null, 0, null, link), cancellationToken);
		}

		public async Task<int> NotifyReviewersAsync(LearningRecord record, IEnumerable<string> reviewerUserIds, bool resubmitted, CancellationToken cancellationToken = default)
		{
			if (record == null)
				return 0;

			var names = await NamesAsync(record.DepartmentId);
			var member = names.TryGetValue(record.UserId ?? string.Empty, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
			var link = ReviewLink(record.LearningRecordId);
			var sent = 0;
			foreach (var reviewer in (reviewerUserIds ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				// Never the member themself, whatever the caller passed.
				if (string.Equals(reviewer, record.UserId, StringComparison.OrdinalIgnoreCase))
					continue;
				cancellationToken.ThrowIfCancellationRequested();
				if (await SendAsync(record.DepartmentId, reviewer, TrainingRecordNoticeKinds.ReviewWaiting, record.LearningRecordId, culture =>
					{
						var composed = Compose(TrainingRecordNoticeKinds.ReviewWaiting, culture, record.Title, record.Hours, null, member ?? Text("Notice.AMember", culture), 0, null, link);
						return resubmitted ? (Text("Notice.ReviewResubmitted.Title", culture), composed.Message) : composed;
					}, cancellationToken))
					sent++;
			}

			return sent;
		}

		public Task<bool> NotifyReviewerDigestAsync(int departmentId, string reviewerUserId, int waiting, CancellationToken cancellationToken = default)
		{
			if (waiting <= 0)
				return Task.FromResult(false);
			var link = InboxLink();
			return SendAsync(departmentId, reviewerUserId, TrainingRecordNoticeKinds.ReviewDigest, 0,
				culture => Compose(TrainingRecordNoticeKinds.ReviewDigest, culture, null, 0, null, null, waiting, null, link), cancellationToken);
		}

		public async Task<bool> NotifyCompletionAsync(TrainingCompletionRecording recording, bool voided, CancellationToken cancellationToken = default)
		{
			if (recording == null || string.IsNullOrWhiteSpace(recording.UserId) || recording.UserId.StartsWith(LearningRecordQuery.ExternalLearnerUserIdPrefix, StringComparison.OrdinalIgnoreCase))
				return false;

			var kind = voided ? TrainingRecordNoticeKinds.CompletionVoided : TrainingRecordNoticeKinds.CompletionRecorded;
			string recorder = null;
			if (!voided)
			{
				var names = await NamesAsync(recording.DepartmentId);
				recorder = names.TryGetValue(recording.RecordedByUserId ?? string.Empty, out var n) && !string.IsNullOrWhiteSpace(n) ? n : null;
			}

			var department = await _departments.GetDepartmentByIdAsync(recording.DepartmentId, false);
			DateTime? local = null;
			try { local = department == null ? recording.CompletedOn : recording.CompletedOn.TimeConverter(department); }
			catch (Exception) { local = recording.CompletedOn; }

			var link = TranscriptLink();
			return await SendAsync(recording.DepartmentId, recording.UserId, kind, recording.LearningRecordId ?? recording.TrainingCompletionRecordingId,
				culture => Compose(kind, culture, recording.TargetName, 0, recording.VoidReason, recorder ?? Text("Notice.AnInstructor", culture), 0, local, link),
				cancellationToken);
		}

		private async Task<Dictionary<string, string>> NamesAsync(int departmentId) =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private async Task<bool> SendAsync(int departmentId, string userId, TrainingRecordNoticeKinds kind, int id, Func<CultureInfo, (string Title, string Message)> compose,
			CancellationToken cancellationToken)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !ConfigHelper.CanTransmit(departmentId))
				return false;

			try
			{
				// Inactive-member policy: deleted, disabled and hidden members are never addressed by automation.
				var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (!active.Contains(userId))
					return false;

				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				if (department == null)
					return false;

				cancellationToken.ThrowIfCancellationRequested();
				var profile = await _profiles.GetProfileByUserIdAsync(userId, false);
				var (title, message) = compose(Culture(profile?.Language));
				var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
				// The standard path: the member's own notification settings choose push, e-mail and text.
				return await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, profile, false, EventCode(kind, id));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// A notice never undoes the action it reports.
				Logging.LogException(ex, $"Training record notice {kind} ({(int)EventTypeOf(kind)}) for department {departmentId} could not be sent.");
				return false;
			}
		}
	}
}
