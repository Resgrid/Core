using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Nightly chat retention purge. Two passes per department: the department-default window
	/// (ChatDepartmentSettings.RetentionDays; 0 = keep forever) over channels without an override, then
	/// each channel carrying its own RetentionOverrideDays. Deletes are batched and capped per run so
	/// a large backlog can never monopolize the database.
	/// </summary>
	public sealed class ChatRetentionLogic
	{
		private const int BatchSize = 1000;
		private const int MaximumMessagesPerRun = 100000;
		private const int ExportRetentionDays = 7;

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var settingsRepository = Bootstrapper.GetKernel().Resolve<IChatDepartmentSettingRepository>();
				var channelRepository = Bootstrapper.GetKernel().Resolve<IChatChannelRepository>();
				var messageRepository = Bootstrapper.GetKernel().Resolve<IChatMessageRepository>();
				var exportRepository = Bootstrapper.GetKernel().Resolve<IChatExportRepository>();

				var runUtc = DateTime.UtcNow;
				var totalDeleted = 0;
				var departmentsProcessed = 0;

				// Export results carry a full transcript blob; they are download-once artifacts and are
				// purged unconditionally after a short window regardless of message retention settings.
				var exportsDeleted = await exportRepository.DeleteOldChatExportsAsync(runUtc.AddDays(-ExportRetentionDays));
				if (exportsDeleted > 0)
					Logging.LogInfo($"Chat retention purged {exportsDeleted} export(s) older than {ExportRetentionDays} days.");

				// Only departments that saved chat settings can have a non-default retention policy;
				// the config default is keep-forever, so everyone else is skipped outright.
				var allSettings = (await settingsRepository.GetAllAsync())?.ToList() ?? new List<ChatDepartmentSetting>();

				foreach (var settings in allSettings)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (totalDeleted >= MaximumMessagesPerRun)
						break;

					var departmentHasDefaultWindow = settings.RetentionDays > 0;
					var overrideChannels = (await channelRepository.GetWithRetentionOverrideAsync(settings.DepartmentId))?.ToList() ?? new List<ChatChannel>();

					if (!departmentHasDefaultWindow && overrideChannels.Count == 0)
						continue;

					departmentsProcessed++;

					if (departmentHasDefaultWindow)
					{
						var cutoff = runUtc.AddDays(-settings.RetentionDays);
						totalDeleted += await PurgeAsync(messageRepository, settings.DepartmentId, null, cutoff, totalDeleted, cancellationToken);
					}

					foreach (var channel in overrideChannels.Where(c => c.RetentionOverrideDays.GetValueOrDefault() > 0))
					{
						if (totalDeleted >= MaximumMessagesPerRun)
							break;

						var cutoff = runUtc.AddDays(-channel.RetentionOverrideDays.Value);
						totalDeleted += await PurgeAsync(messageRepository, settings.DepartmentId, channel.ChatChannelId, cutoff, totalDeleted, cancellationToken);
					}
				}

				var summary = $"Chat retention purged {totalDeleted} message(s) across {departmentsProcessed} department(s).";
				Logging.LogInfo(summary);

				return new Tuple<bool, string>(true, summary);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return new Tuple<bool, string>(false, ex.ToString());
			}
		}

		private static async Task<int> PurgeAsync(IChatMessageRepository messageRepository, int departmentId, string chatChannelId,
			DateTime cutoffUtc, int alreadyDeleted, CancellationToken cancellationToken)
		{
			var deleted = 0;

			while (alreadyDeleted + deleted < MaximumMessagesPerRun)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var requested = Math.Min(BatchSize, MaximumMessagesPerRun - alreadyDeleted - deleted);
				var ids = await messageRepository.GetRetentionBatchIdsAsync(departmentId, chatChannelId, cutoffUtc, requested);
				if (ids == null || ids.Count == 0)
					break;

				deleted += await messageRepository.DeleteMessagesByIdsAsync(ids, cancellationToken);

				if (ids.Count < requested)
					break;
			}

			return deleted;
		}
	}
}
