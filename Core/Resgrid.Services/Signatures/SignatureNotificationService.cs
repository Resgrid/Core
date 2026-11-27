using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// Direct member notifications for multi-signer requests (S3, EventTypes 66 SignatureStepAssigned and 67
	/// SignatureRequestClosed): push + email through ICommunicationService with the push event code "SG:{requestId}" and the web
	/// link in the text (the older apps open it in the browser). Active members only; nothing goes out where the department cannot
	/// transmit. The text names the document and the content code, never an outside signer's name or contact. The text comes from
	/// ContentNotices in each recipient's language (round 9C).
	/// </summary>
	public class SignatureNotificationService : ISignatureNotificationService
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;

		public SignatureNotificationService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings,
			IUserProfileService profiles)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
			_profiles = profiles;
		}

		/// <summary>The member's signing page for a request.</summary>
		public static string StepLink(int signatureRequestId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Signatures/Step?id=" + signatureRequestId.ToString(CultureInfo.InvariantCulture);

		/// <summary>The push event code the apps route on ("SG:{requestId}").</summary>
		public static string EventCode(int signatureRequestId) => "SG:" + signatureRequestId.ToString(CultureInfo.InvariantCulture);

		/// <summary>"Your signature is needed on ..." in <paramref name="culture"/> (English when none is given).</summary>
		public static string StepReadyMessage(SignatureRequestRecord request, bool reminder, CultureInfo culture = null) =>
			ContentNoticeText.Format(reminder ? "Signature.Reminder.Message" : "Signature.Needed.Message", culture, request.Title,
				SignaturePolicy.ShortCode(request.ArtifactSha256), StepLink(request.SignatureRequestId));

		/// <summary>How the request ended, in <paramref name="culture"/> (English when none is given).</summary>
		public static string ClosedMessage(SignatureRequestRecord request, CultureInfo culture = null)
		{
			var key = (SignatureRequestStatuses)request.Status switch
			{
				SignatureRequestStatuses.Completed => "Signature.Closed.Completed",
				SignatureRequestStatuses.Declined => "Signature.Closed.Declined",
				SignatureRequestStatuses.Expired => "Signature.Closed.Expired",
				SignatureRequestStatuses.Voided => "Signature.Closed.Voided",
				_ => "Signature.Closed.Other"
			};
			return ContentNoticeText.Format(key, culture, request.Title, StepLink(request.SignatureRequestId));
		}

		public Task<int> NotifyStepReadyAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, bool reminder, CancellationToken cancellationToken = default) =>
			request == null ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, userIds, culture => new ContentNoticeDelivery.Notice(
					ContentNoticeText.Get(reminder ? "Signature.Reminder.Title" : "Signature.Needed.Title", culture), StepReadyMessage(request, reminder, culture)),
					EventCode(request.SignatureRequestId), cancellationToken);

		public Task<int> NotifyClosedAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
			request == null ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, userIds, culture => new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Signature.Closed.Title", culture),
					ClosedMessage(request, culture)), EventCode(request.SignatureRequestId), cancellationToken);

		/// <summary>Active members only, each in their own language (<see cref="ContentNoticeDelivery"/>).</summary>
		private Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, Func<CultureInfo, ContentNoticeDelivery.Notice> compose, string eventCode,
			CancellationToken cancellationToken) =>
			ContentNoticeDelivery.SendAsync(_communication.Value, _departments, _settings, _profiles, departmentId, userIds, (culture, _) => compose(culture), eventCode,
				"Signature notification", cancellationToken);
	}
}
