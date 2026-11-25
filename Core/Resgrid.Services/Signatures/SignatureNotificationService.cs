using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// Direct member notifications for multi-signer requests (S3, EventTypes 66 SignatureStepAssigned and 67
	/// SignatureRequestClosed): push + email through ICommunicationService with the push event code "SG:{requestId}" and the web
	/// link in the text (the older apps open it in the browser). Active members only; nothing goes out where the department cannot
	/// transmit. The text names the document and the content code, never an outside signer's name or contact. Outbound text is
	/// English, like the rest of Resgrid's outbound messages.
	/// </summary>
	public class SignatureNotificationService : ISignatureNotificationService
	{
		private readonly Lazy<ICommunicationService> _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;

		public SignatureNotificationService(Lazy<ICommunicationService> communication, IDepartmentsService departments, IDepartmentSettingsService settings)
		{
			_communication = communication;
			_departments = departments;
			_settings = settings;
		}

		/// <summary>The member's signing page for a request.</summary>
		public static string StepLink(int signatureRequestId) =>
			(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/User/Signatures/Step?id=" + signatureRequestId.ToString(CultureInfo.InvariantCulture);

		/// <summary>The push event code the apps route on ("SG:{requestId}").</summary>
		public static string EventCode(int signatureRequestId) => "SG:" + signatureRequestId.ToString(CultureInfo.InvariantCulture);

		public static string StepReadyMessage(SignatureRequestRecord request, bool reminder) =>
			(reminder ? "Reminder: your signature is still needed on " : "Your signature is needed on ") + request.Title +
			" (content code " + SignaturePolicy.ShortCode(request.ArtifactSha256) + "). " + StepLink(request.SignatureRequestId);

		public static string ClosedMessage(SignatureRequestRecord request)
		{
			var outcome = (SignatureRequestStatuses)request.Status switch
			{
				SignatureRequestStatuses.Completed => "was signed by everyone",
				SignatureRequestStatuses.Declined => "was declined",
				SignatureRequestStatuses.Expired => "expired before everyone signed",
				SignatureRequestStatuses.Voided => "was withdrawn",
				_ => "was closed"
			};
			return "The signature request for " + request.Title + " " + outcome + ". " + StepLink(request.SignatureRequestId);
		}

		public Task<int> NotifyStepReadyAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, bool reminder, CancellationToken cancellationToken = default) =>
			request == null ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, userIds, reminder ? "Signature reminder" : "Signature needed", StepReadyMessage(request, reminder), EventCode(request.SignatureRequestId), cancellationToken);

		public Task<int> NotifyClosedAsync(SignatureRequestRecord request, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
			request == null ? Task.FromResult(0)
				: SendAsync(request.DepartmentId, userIds, "Signature request closed", ClosedMessage(request), EventCode(request.SignatureRequestId), cancellationToken);

		private async Task<int> SendAsync(int departmentId, IEnumerable<string> userIds, string title, string message, string eventCode, CancellationToken cancellationToken)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return 0;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;

			// Inactive-member policy: deleted, disabled and hidden members are never addressed by automation.
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
			var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
			var sent = 0;
			foreach (var userId in wanted.Where(active.Contains))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, null, false, eventCode))
						sent++;
				}
				catch (Exception ex)
				{
					// One member's channel failing never stops the others.
					Logging.LogException(ex, $"Signature notification to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}
	}
}
