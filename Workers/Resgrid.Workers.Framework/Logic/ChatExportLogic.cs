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
	/// Exports are claimed atomically (multi-worker safe) and stale Running rows are requeued so a
	/// crashed worker can never strand an export.
	/// </summary>
	public sealed class ChatExportLogic
	{
		private const int MaxMessagesPerExport = 250000;
		private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(30);

		public async Task<Tuple<bool, string>> Process(CancellationToken cancellationToken)
		{
			try
			{
				var exportRepository = Bootstrapper.GetKernel().Resolve<IChatExportRepository>();

				// Recovery first: exports stranded in Running by a crashed worker go back to the queue
				// so they are picked up below (possibly by this run).
				await exportRepository.RequeueStaleRunningChatExportsAsync(StaleRunningThreshold);

				var queued = (await exportRepository.GetQueuedAsync())?.ToList() ?? new List<ChatExport>();
				if (queued.Count == 0)
					return new Tuple<bool, string>(true, "No chat exports queued.");

				var processed = 0;

				foreach (var export in queued)
				{
					cancellationToken.ThrowIfCancellationRequested();

					// Atomic claim: only the worker that flips Queued -> Running processes the row.
					if (!await exportRepository.ClaimChatExportAsync(export.ChatExportId))
						continue;

					export.Status = (int)ChatExportStatus.Running;

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

					// Suffix the entry name with the (unique) channel id so two channels sharing a name —
					// e.g. two groups both called "Engine 1" — produce distinct archive entries instead of
					// one silently overwriting the other on extraction (a records/FOIA completeness bug).
					var safeKey = channelGroup.Key.Length >= 8 ? channelGroup.Key.Substring(0, 8) : channelGroup.Key;
					var baseName = $"{SanitizeFileName(channel?.Name ?? channelGroup.Key)}-{SanitizeFileName(safeKey)}";

					var channelMessages = channelGroup.OrderBy(m => m.MessageSeq).ToList();

					// Edit/delete history rows for the channel's exported messages (audit completeness),
					// fetched in one batched query instead of per message.
					var editedMessageIds = channelMessages
						.Where(m => m.EditedOn.HasValue || m.DeletedOn.HasValue)
						.Select(m => m.ChatMessageId)
						.ToList();
					var edits = editedMessageIds.Count > 0
						? (await editRepository.GetChatExportEditsByMessageIdsAsync(editedMessageIds))?.ToList() ?? new List<ChatMessageEdit>()
						: new List<ChatMessageEdit>();

					WriteJsonEntry(archive, $"{baseName}.json", new
					{
						Channel = channel,
						Messages = channelMessages,
						EditHistory = edits
					});

					WriteCsvEntry(archive, $"{baseName}.csv", channelMessages);
				}

				WriteJsonEntry(archive, "moderation-log.json", moderationActions);

				WriteJsonEntry(archive, "export-manifest.json", new
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
				});
			}

			return zipStream.ToArray();
		}

		private static void WriteCsvEntry(ZipArchive archive, string entryName, List<ChatMessage> messages)
		{
			var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
			using var stream = entry.Open();
			using var writer = new StreamWriter(stream, Encoding.UTF8);

			writer.WriteLine("MessageSeq,SentOnUtc,SenderDisplayName,SenderUserId,SenderUnitId,MessageType,Priority,Body,EditedOn,DeletedOn,DeletedByUserId");

			foreach (var m in messages)
			{
				writer.WriteLine(string.Join(",",
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
		}

		private static string CsvEscape(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			// CSV formula injection guard: values a spreadsheet app would evaluate as a formula are
			// neutralized with a leading single quote inside the quoted field.
			var first = value[0];
			var prefix = first == '=' || first == '+' || first == '-' || first == '@' || first == '\t' || first == '\r'
				? "'"
				: string.Empty;

			return "\"" + prefix + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
		}

		private static string SanitizeFileName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return "channel";

			// Path.GetInvalidFileNameChars() is OS-specific — on Linux it excludes '\' and ':', so a
			// malicious channel name could survive on a Linux server and become a traversal path when the
			// ZIP is extracted on Windows (Zip Slip). Strip every directory separator, drive-colon, and
			// '..' fragment explicitly, regardless of the server OS.
			var invalid = Path.GetInvalidFileNameChars();
			var extraBad = new[] { '/', '\\', ':' };
			var cleaned = new string(name.Select(c => (invalid.Contains(c) || extraBad.Contains(c)) ? '_' : c).ToArray());
			cleaned = cleaned.Replace("..", "_");

			return string.IsNullOrWhiteSpace(cleaned) ? "channel" : cleaned;
		}

		private static void WriteJsonEntry<T>(ZipArchive archive, string entryName, T payload)
		{
			var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
			using var stream = entry.Open();
			using var writer = new StreamWriter(stream, Encoding.UTF8);
			using var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.Indented };

			JsonSerializer.CreateDefault().Serialize(jsonWriter, payload);
			jsonWriter.Flush();
		}
	}
}
