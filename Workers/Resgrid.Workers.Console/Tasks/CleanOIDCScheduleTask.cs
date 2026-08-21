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

				var identityRepository = Bootstrapper.GetKernel().Resolve<IIdentityRepository>();
				var tokensCleaned = await identityRepository.CleanUpOIDCTokensAsync(DateTime.UtcNow);
				var sessionsRepository = Bootstrapper.GetKernel().Resolve<IUserSessionsRepository>();
				var retentionDays = Math.Max(1, SessionSecurityConfig.RevokedSessionRetentionDays);
				var purgeBefore = DateTime.UtcNow.AddDays(-retentionDays);
				var sessionsPurged = await sessionsRepository.PurgeInactiveBeforeAsync(purgeBefore, cancellationToken);

				// UserSessions rows are the access record for every sign-in, so deleting them on retention
				// is itself an accountable event. Record what ran, the window it covered and how much it
				// removed; without this the history simply shrinks with nothing explaining why.
				var systemAuditsService = Bootstrapper.GetKernel().Resolve<ISystemAuditsService>();
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
						$"OIDC token cleanup succeeded: {tokensCleaned}.",
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
