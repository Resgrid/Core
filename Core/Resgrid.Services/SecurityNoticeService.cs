using System;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.SystemMessages;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="ISecurityNoticeService"/>
	public sealed class SecurityNoticeService : ISecurityNoticeService
	{
		private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
		private static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(6);

		private readonly ISecurityNoticeRepository _notices;
		private readonly IIdentityUserRepository _identityUsers;
		private readonly IUserProfileService _profiles;
		private readonly IEmailSender _email;
		private readonly ISystemAuditsService _audits;
		private readonly TimeProvider _time;
		private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

		public SecurityNoticeService(ISecurityNoticeRepository notices, IIdentityUserRepository identityUsers, IUserProfileService profiles,
			IEmailSender email, ISystemAuditsService audits, TimeProvider time)
		{
			_notices = notices;
			_identityUsers = identityUsers;
			_profiles = profiles;
			_email = email;
			_audits = audits;
			_time = time;
		}

		private static int MaxAttempts => Math.Max(1, TwoFactorConfig.SecurityNoticeMaxAttempts);

		public async Task QueueAsync(SecurityNoticeRequest request, CancellationToken cancellationToken = default)
		{
			if (!TwoFactorConfig.SecurityNoticesEnabled || request == null || string.IsNullOrWhiteSpace(request.UserId))
				return;

			SecurityNotice notice;
			try
			{
				var now = _time.GetUtcNow().UtcDateTime;
				notice = new SecurityNotice
				{
					SecurityNoticeId = Guid.NewGuid().ToString(),
					UserId = request.UserId,
					Kind = (int)request.Kind,
					OccurredOnUtc = request.OccurredOnUtc ?? now,
					ClientApplication = request.ClientApplication == null ? null : (int)request.ClientApplication.Value,
					InstallationLabel = Limit(request.InstallationLabel, 256),
					Region = Limit(request.Region, 256),
					State = (int)SecurityNoticeState.Pending,
					NextAttemptOnUtc = now,
					CreatedOnUtc = now
				};
				await _notices.InsertAsync(notice, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The change already committed; a notice that could not even be queued is an alert, not a rollback.
				Logging.LogException(ex, $"A security notice ({request.Kind}) could not be queued.");
				return;
			}

			// At once where possible; worker 13 retries whatever this does not send.
			try
			{
				var now = _time.GetUtcNow().UtcDateTime;
				if (await _notices.TryClaimAsync(notice.SecurityNoticeId, _owner, now, now.Add(Lease), cancellationToken))
					await DeliverAsync(notice, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A security notice could not be sent now; it will be retried.");
			}
		}

		public async Task QueueOnceAsync(SecurityNoticeRequest request, TimeSpan within, CancellationToken cancellationToken = default)
		{
			if (!TwoFactorConfig.SecurityNoticesEnabled)
				return;

			try
			{
				if (request != null && await _notices.ExistsSinceAsync(request.UserId, request.Kind, _time.GetUtcNow().UtcDateTime - within, cancellationToken))
					return;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Better a duplicate notice than a missing one.
				Logging.LogException(ex, "Earlier security notices could not be read; the notice is queued anyway.");
			}

			await QueueAsync(request, cancellationToken);
		}

		public async Task<int> DeliverDueAsync(int batchSize, CancellationToken cancellationToken = default)
		{
			// Turning the gate off stops sending too; anything already queued waits for it.
			if (!TwoFactorConfig.SecurityNoticesEnabled)
				return 0;

			var now = _time.GetUtcNow().UtcDateTime;
			var due = await _notices.ClaimDueAsync(_owner, now, now.Add(Lease), batchSize, cancellationToken);
			var sent = 0;
			foreach (var notice in due)
			{
				try
				{
					if (await DeliverAsync(notice, cancellationToken))
						sent++;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Logging.LogException(ex, "A security notice retry failed; it stays queued.");
				}
			}

			return sent;
		}

		/// <summary>Sends one claimed notice and records the result; true when it was sent.</summary>
		private async Task<bool> DeliverAsync(SecurityNotice notice, CancellationToken cancellationToken)
		{
			var user = await _identityUsers.GetByIdAsync(notice.UserId);
			if (user == null || string.IsNullOrWhiteSpace(user.Email) || !user.Email.Contains('@'))
			{
				await FailAsync(notice, "no_destination", cancellationToken);
				return false;
			}

			bool delivered;
			try
			{
				var profile = await _profiles.GetProfileByUserIdAsync(notice.UserId);
				using var mail = Compose(notice, user.Email, profile?.FirstName, profile?.Language);
				delivered = await _email.SendEmail(mail);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A security notice could not be sent.");
				delivered = false;
			}

			if (delivered)
				return await _notices.MarkSentAsync(notice.SecurityNoticeId, _owner, _time.GetUtcNow().UtcDateTime, cancellationToken);

			if (notice.Attempts + 1 >= MaxAttempts)
			{
				await FailAsync(notice, "delivery_failed", cancellationToken);
				return false;
			}

			// 1, 2, 4, 8... minutes, capped at six hours.
			var backoff = TimeSpan.FromMinutes(Math.Pow(2, Math.Min(notice.Attempts, 12)));
			await _notices.MarkRetryAsync(notice.SecurityNoticeId, _owner, _time.GetUtcNow().UtcDateTime.Add(backoff < MaximumBackoff ? backoff : MaximumBackoff),
				"delivery_failed", cancellationToken);
			return false;
		}

		/// <summary>A notice that can never be sent is logged as an error and audited; the change it reports stands.</summary>
		private async Task FailAsync(SecurityNotice notice, string reason, CancellationToken cancellationToken)
		{
			if (!await _notices.MarkFailedAsync(notice.SecurityNoticeId, _owner, reason, cancellationToken))
				return;

			Logging.LogError($"Security notice {notice.SecurityNoticeId} ({notice.NoticeKind}) for user {notice.UserId} was not delivered: {reason}.");
			try
			{
				await _audits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Worker,
					Type = (int)SystemAuditTypes.SecurityNoticeFailed,
					UserId = notice.UserId,
					Successful = false,
					ServerName = Environment.MachineName,
					Data = $"Security notice {notice.SecurityNoticeId} ({notice.NoticeKind}) was not delivered: {reason}."
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A failed security notice could not be audited.");
			}
		}

		/// <summary>
		/// The notice in the recipient's language: what happened, when and where, and what to do if it was not them. It
		/// carries no link that acts on the account, no code, and nothing from the account beyond the event itself.
		/// </summary>
		internal static MailMessage Compose(SecurityNotice notice, string emailAddress, string firstName, string culture)
		{
			var where = notice.ClientApplication is int client
				? string.Join(", ", new[] { PasskeyService.ClientLabel((UserSessionClientApplication)client), notice.InstallationLabel, notice.Region }
					.Where(part => !string.IsNullOrWhiteSpace(part)))
				: notice.Region;
			var lines = new System.Collections.Generic.List<string>
			{
				string.IsNullOrWhiteSpace(firstName)
					? SystemMessagesResources.Get("SecurityNoticeGreetingNoName", culture)
					: SystemMessagesResources.Get("SecurityNoticeGreeting", culture, firstName.Trim()),
				"",
				SystemMessagesResources.Get("SecurityNotice" + notice.NoticeKind, culture),
				"",
				SystemMessagesResources.Get("SecurityNoticeWhen", culture, notice.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture))
			};
			if (!string.IsNullOrWhiteSpace(where))
				lines.Add(SystemMessagesResources.Get("SecurityNoticeWhere", culture, where));
			lines.Add("");
			lines.Add(SystemMessagesResources.Get("SecurityNoticeNotYou", culture, $"{SystemBehaviorConfig.ResgridBaseUrl?.TrimEnd('/')}/Account/ForgotPassword"));
			lines.Add("");
			lines.Add(SystemMessagesResources.Get("SecurityNoticeFooter", culture));

			var mail = new MailMessage();
			mail.To.Add(emailAddress);
			mail.From = new MailAddress(OutboundEmailServerConfig.FromMail, "Resgrid");
			mail.Subject = SystemMessagesResources.Get("SecurityNoticeSubject", culture);
			mail.Body = string.Join(Environment.NewLine, lines);
			mail.IsBodyHtml = false;
			return mail;
		}

		private static string Limit(string value, int length) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];
	}
}
