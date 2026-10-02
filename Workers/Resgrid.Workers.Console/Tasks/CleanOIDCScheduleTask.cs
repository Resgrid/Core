using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class CleanOIDCScheduleTask : IQuidjiboHandler<Commands.CleanOIDCCommand>
	{
		public string Name => "Clean OIDC Tokens";
		public int Priority => 1;
		public ILogger _logger;

		public CleanOIDCScheduleTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(Commands.CleanOIDCCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var identityRepository = scope.Resolve<IIdentityRepository>();
				var tokensCleaned = await identityRepository.CleanUpOIDCTokensAsync(DateTime.UtcNow);
				var sessionsRepository = scope.Resolve<IUserSessionsRepository>();
				var retentionDays = Math.Max(1, SessionSecurityConfig.RevokedSessionRetentionDays);
				var purgeBefore = DateTime.UtcNow.AddDays(-retentionDays);
				var sessionsPurged = await sessionsRepository.PurgeInactiveBeforeAsync(purgeBefore, cancellationToken);

				// Spent and expired WebAuthn challenges, and MFA evidence past its retention (passkey plan section 5.2).
				var now = DateTime.UtcNow;
				var challengesPurged = await scope.Resolve<IAuthenticationChallengeRepository>()
					.PurgeExpiredBeforeAsync(now.AddHours(-1), cancellationToken);
				var evidencePurged = await scope.Resolve<IUserSessionMfaEvidenceRepository>()
					.PurgeExpiredBeforeAsync(now, cancellationToken);
				var loginTransactionsPurged = await scope.Resolve<IMfaLoginTransactionRepository>()
					.PurgeExpiredBeforeAsync(now.AddHours(-1), cancellationToken);
				var ssoTransactionsPurged = await scope.Resolve<ISsoLoginTransactionRepository>()
					.PurgeExpiredBeforeAsync(now.AddHours(-1), cancellationToken);
				// Kept a day: the approval rate limit and suspension read the last 15 minutes of a user's requests.
				var approvalRequestsPurged = await scope.Resolve<IMfaApprovalRequestRepository>()
					.PurgeCreatedBeforeAsync(now.AddDays(-1), cancellationToken);
				var recoveriesPurged = await scope.Resolve<IFactorRecoveryTransactionRepository>()
					.PurgeExpiredBeforeAsync(now.AddHours(-1), cancellationToken);

				// Security notices (passkey plan section 6.4): retry what was not sent at once, and keep the record for its retention.
				var noticesSent = await scope.Resolve<ISecurityNoticeService>().DeliverDueAsync(500, cancellationToken);
				var noticesPurged = await scope.Resolve<ISecurityNoticeRepository>()
					.PurgeFinishedBeforeAsync(now.AddDays(-Math.Max(1, Resgrid.Config.TwoFactorConfig.SecurityNoticeRetentionDays)), cancellationToken);

				// Protected Data Broker replay records past their window (passkey workbook section 6.2).
				var replayKeysPurged = await scope.Resolve<IBrokerReplayRepository>()
					.PurgeExpiredBeforeAsync(now, cancellationToken);
				var activityPurged = await scope.Resolve<IMfaActivityRepository>()
					.PurgeBeforeAsync(now.AddDays(-Math.Max(1, Resgrid.Config.TwoFactorConfig.MfaActivityRetentionDays)), cancellationToken);

				// UserSessions rows are the access record for every sign-in, so deleting them on retention
				// is itself an accountable event. Record what ran, the window it covered and how much it
				// removed; without this the history simply shrinks with nothing explaining why.
				var systemAuditsService = scope.Resolve<ISystemAuditsService>();
				await systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Worker,
					Type = (int)SystemAuditTypes.SessionHistoryPurged,
					Username = Name,
					Successful = true,
					ServerName = Environment.MachineName,
					CorrelationId = command?.CorrelationId?.ToString("N"),
					Data = $"Retention purge removed {sessionsPurged} session row(s) revoked or expired before " +
						$"{purgeBefore:u} (RevokedSessionRetentionDays={retentionDays}). " +
						$"OIDC token cleanup succeeded: {tokensCleaned}. " +
						$"Removed {challengesPurged} expired authentication challenge(s), {loginTransactionsPurged} expired login MFA " +
						$"transaction(s), {ssoTransactionsPurged} expired SSO transaction(s), {approvalRequestsPurged} approval request(s) older than a day, " +
						$"{recoveriesPurged} expired factor recovery transaction(s), {noticesSent} security notice(s) sent on retry, " +
						$"{noticesPurged} finished security notice(s) past retention, {activityPurged} MFA activity row(s) past retention, " +
						$"{evidencePurged} expired MFA evidence row(s) and {replayKeysPurged} expired broker replay record(s).",
					LoggedOn = DateTime.UtcNow
				}, cancellationToken);

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
