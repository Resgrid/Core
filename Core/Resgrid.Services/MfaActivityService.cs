using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IMfaActivityService"/>
	public class MfaActivityService : IMfaActivityService
	{
		private const int ViewSize = 100;

		private readonly IMfaActivityRepository _rows;
		private readonly IUserSessionsRepository _sessions;
		private readonly IUserSessionService _userSessions;
		private readonly ISecurityNoticeService _notices;
		private readonly ISystemAuditsService _audits;
		private readonly TimeProvider _time;

		public MfaActivityService(IMfaActivityRepository rows, IUserSessionsRepository sessions, IUserSessionService userSessions,
			ISecurityNoticeService notices, ISystemAuditsService audits, TimeProvider time)
		{
			_rows = rows;
			_sessions = sessions;
			_userSessions = userSessions;
			_notices = notices;
			_audits = audits;
			_time = time;
		}

		public async Task RecordAsync(MfaActivityEntry entry, CancellationToken cancellationToken = default)
		{
			if (entry == null || string.IsNullOrWhiteSpace(entry.UserId))
				return;

			try
			{
				var now = _time.GetUtcNow().UtcDateTime;
				// Plan section 13: failed attempts are bounded, so guessing cannot fill the table. The lockout still counts them.
				if (!entry.Successful &&
					await _rows.CountDeniedSinceAsync(entry.UserId, now.AddHours(-1), cancellationToken) >= Math.Max(1, TwoFactorConfig.MfaActivityDeniedPerHour))
					return;

				var session = !string.IsNullOrWhiteSpace(entry.SessionId) && string.IsNullOrWhiteSpace(entry.InstallationLabel)
					? await _sessions.GetByIdAsync(entry.SessionId)
					: null;
				await _rows.InsertAsync(MfaActivityRecords.Create(entry, session, now), cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The verification it describes already happened (or was refused); a missing history row changes neither.
				Logging.LogException(ex, "MFA activity could not be recorded.");
			}
		}

		public Task<IReadOnlyList<MfaActivity>> GetRecentAsync(string userId, CancellationToken cancellationToken = default) =>
			_rows.GetRecentAsync(userId, RetentionCutoff(), ViewSize, cancellationToken);

		public async Task<MfaActivityReport> ReportAsync(string userId, string mfaActivityId, string reportingSessionId, SharedSessionRequestInfo request,
			CancellationToken cancellationToken = default)
		{
			var activity = string.IsNullOrWhiteSpace(mfaActivityId) ? null : await _rows.GetAsync(mfaActivityId, cancellationToken);
			if (activity == null || !string.Equals(activity.UserId, userId, StringComparison.OrdinalIgnoreCase) || activity.OccurredOnUtc < RetentionCutoff())
				return new MfaActivityReport { Outcome = MfaActivityReportOutcome.NotFound };

			var now = _time.GetUtcNow().UtcDateTime;
			if (!await _rows.TryMarkReportedAsync(activity.MfaActivityId, userId, now, cancellationToken))
				return new MfaActivityReport { Outcome = MfaActivityReportOutcome.AlreadyReported };

			// A verification that someone else made opened or served a session: that session ends now. The reporting session
			// stays, so the user can change the password from it.
			var ended = false;
			if (activity.Successful && !string.IsNullOrWhiteSpace(activity.SessionId) &&
				!string.Equals(activity.SessionId, reportingSessionId, StringComparison.Ordinal))
				ended = (await _userSessions.RevokeSessionAsync(userId, userId, activity.SessionId, UserSessionRevocationReason.AccountCompromised,
					cancellationToken)).RevokedSessionCount > 0;

			try
			{
				await _audits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Api,
					Type = (int)SystemAuditTypes.MfaActivityReported,
					UserId = userId,
					Username = request?.UserName,
					TargetUserId = userId,
					SessionId = SharedSessionAudit.SessionSuffix(activity.SessionId),
					Successful = true,
					IpAddress = request?.IpAddress,
					ServerName = Environment.MachineName,
					CorrelationId = request?.CorrelationId,
					Data = $"MFA activity {activity.MfaActivityId} reported as not the account holder's " +
						$"({(MfaEvidenceMethod)activity.Method}, {(activity.Successful ? "successful" : "denied")}); session ended: {ended}.",
					LoggedOn = now
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "MFA activity report audit failed.");
			}

			await _notices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = userId,
				Kind = SecurityNoticeKind.ActivityReported,
				ClientApplication = (UserSessionClientApplication)activity.ClientApplication,
				InstallationLabel = activity.InstallationLabel
			}, cancellationToken);

			return new MfaActivityReport { Outcome = MfaActivityReportOutcome.Reported, SessionEnded = ended };
		}

		private DateTime RetentionCutoff() => _time.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, TwoFactorConfig.MfaActivityRetentionDays));
	}
}
