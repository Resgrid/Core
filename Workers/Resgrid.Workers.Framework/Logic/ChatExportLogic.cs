using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Processes queued chat transcript exports (records requests / FOIA). Each export produces a ZIP
	/// containing per-channel JSON and CSV transcripts (including sender identities, tombstones, edit
	/// history and the moderation log) stored back onto the ChatExports row for authenticated download.
	/// </summary>
	public sealed class ChatExportLogic
	{
		private const int MaxMessagesPerExport = 250000;

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var exportRepository = Bootstrapper.GetKernel().Resolve<IChatExportRepository>();

				var queued = (await exportRepository.GetQueuedAsync())?.ToList() ?? new List<ChatExport>();
				if (queued.Count == 0)
					return new Tuple<bool, string>(true, "No chat exports queued.");

				var processed = 0;

				foreach (var export in queued)
				{
					cancellationToken.ThrowIfCancellationRequested();

					export.Status = (int)ChatExportStatus.Running;
					await exportRepository.UpdateAsync(export, cancellationToken);

					try
					{
						export.Data = await BuildExportAsync(export, cancellationToken);
						export.Status = (int)ChatExportStatus.Complete;
						export.CompletedOn = DateTime.UtcNow;
						export.Error = null;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
						export.Status = (int)ChatExportStatus.Failed;
						export.CompletedOn = DateTime.UtcNow;
						export.Error = ex.Message;
					}

					await exportRepository.UpdateAsync(export, cancellationToken);
					processed++;
				}

				var summary = $"Chat export processed {processed} job(s).";
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

		private static async Task<byte[]> BuildExportAsync(ChatExport export, CancellationToken cancellationToken)
		{
			var channelRepository = Bootstrapper.GetKernel().Resolve<IChatChannelRepository>();
			var messageRepository = Bootstrapper.GetKernel().Resolve<IChatMessageRepository>();
			var editRepository = Bootstrapper.GetKernel().Resolve<IChatMessageEditRepository>();
			var moderationRepository = Bootstrapper.GetKernel().Resolve<IChatModerationActionRepository>();

			var messages = (await messageRepository.GetForExportAsync(export.DepartmentId, export.ChatChannelId, export.StartDate, export.EndDate, MaxMessagesPerExport))?.ToList()
				?? new List<ChatMessage>();

			var channelIds = messages.Select(m => m.ChatChannelId).Distinct().ToList();
			var channels = channelIds.Count > 0
				? (await channelRepository.GetByIdsAsync(channelIds))?.ToList() ?? new List<ChatChannel>()
				: new List<ChatChannel>();
			var channelsById = channels.ToDictionary(c => c.ChatChannelId, StringComparer.OrdinalIgnoreCase);

			var moderationActions = (await moderationRepository.GetByDepartmentAsync(export.DepartmentId, export.ChatChannelId, 0, 10000))?.ToList()
				?? new List<ChatModerationAction>();

			using var zipStream = new MemoryStream();
			using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
			{
				foreach (var channelGroup in messages.GroupBy(m => m.ChatChannelId))
				{
					cancellationToken.ThrowIfCancellationRequested();

					channelsById.TryGetValue(channelGroup.Key, out var channel);
					var baseName = SanitizeFileName(channel?.Name ?? channelGroup.Key);

					var channelMessages = channelGroup.OrderBy(m => m.MessageSeq).ToList();

					// Edit/delete history rows for the channel's exported messages (audit completeness).
					var edits = new List<ChatMessageEdit>();
					foreach (var message in channelMessages.Where(m => m.EditedOn.HasValue || m.DeletedOn.HasValue))
					{
						var messageEdits = await editRepository.GetByMessageIdAsync(message.ChatMessageId);
						if (messageEdits != null)
							edits.AddRange(messageEdits);
					}

					await WriteEntryAsync(archive, $"{baseName}.json", JsonConvert.SerializeObject(new
					{
						Channel = channel,
						Messages = channelMessages,
						EditHistory = edits
					}, Formatting.Indented));

					await WriteEntryAsync(archive, $"{baseName}.csv", BuildCsv(channelMessages));
				}

				await WriteEntryAsync(archive, "moderation-log.json", JsonConvert.SerializeObject(moderationActions, Formatting.Indented));

				await WriteEntryAsync(archive, "export-manifest.json", JsonConvert.SerializeObject(new
				{
					export.ChatExportId,
					export.DepartmentId,
					export.ChatChannelId,
					export.StartDate,
					export.EndDate,
					export.RequestedByUserId,
					export.RequestedOn,
					GeneratedOn = DateTime.UtcNow,
					MessageCount = messages.Count,
					Truncated = messages.Count >= MaxMessagesPerExport
				}, Formatting.Indented));
			}

			return zipStream.ToArray();
		}

		private static string BuildCsv(List<ChatMessage> messages)
		{
			var sb = new StringBuilder();
			sb.AppendLine("MessageSeq,SentOnUtc,SenderDisplayName,SenderUserId,SenderUnitId,MessageType,Priority,Body,EditedOn,DeletedOn,DeletedByUserId");

			foreach (var m in messages)
			{
				sb.AppendLine(string.Join(",",
					m.MessageSeq.ToString(CultureInfo.InvariantCulture),
					m.SentOn.ToString("O", CultureInfo.InvariantCulture),
					CsvEscape(m.SenderDisplayName),
					CsvEscape(m.SenderUserId),
					m.SenderUnitId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
					m.MessageType.ToString(CultureInfo.InvariantCulture),
					m.Priority.ToString(CultureInfo.InvariantCulture),
					CsvEscape(m.Body),
					m.EditedOn?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
					m.DeletedOn?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
					CsvEscape(m.DeletedByUserId)));
			}

			return sb.ToString();
		}

		private static string CsvEscape(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			return "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
		}

		private static string SanitizeFileName(string name)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

			return string.IsNullOrWhiteSpace(cleaned) ? "channel" : cleaned;
		}

		private static async Task WriteEntryAsync(ZipArchive archive, string entryName, string content)
		{
			var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
			using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
			await writer.WriteAsync(content);
		}
	}
}
