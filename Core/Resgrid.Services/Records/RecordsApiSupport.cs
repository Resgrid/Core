using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Keyed short-lived state for the v4 Records contract. Redis (through ICacheProvider string entries) when caching
	/// is on and connected; a process-local dictionary otherwise, which is single-node only and says so in the log.
	/// </summary>
	public class RecordsApiStateStore : IRecordsApiStateStore
	{
		private static readonly ConcurrentDictionary<string, (string Value, DateTime ExpiresOn)> Local = new ConcurrentDictionary<string, (string, DateTime)>(StringComparer.Ordinal);
		private static int _localWarned;
		// Low-volume nodes still expire uploaded bytes; expiry must not depend on reaching 512 cached entries.
		private static readonly Timer ExpiryTimer = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
		private readonly ICacheProvider _cache;

		public RecordsApiStateStore(ICacheProvider cache)
		{
			_cache = cache;
		}

		private bool UseCache => Config.SystemBehaviorConfig.CacheEnabled && _cache != null && SafeConnected();

		private bool SafeConnected()
		{
			try
			{
				return _cache.IsConnected();
			}
			catch (Exception ex)
			{
				// Falling back to process-local state is correct, but the reason the cache is unreachable has to be
				// recorded: without it the only symptom is idempotency keys that stop working across instances.
				Logging.LogException(ex, "Records API state store could not reach the cache; using process-local state.");
				return false;
			}
		}

		public async Task<string> GetAsync(string key)
		{
			if (UseCache)
				return await _cache.GetStringAsync(key);

			Sweep();
			return Local.TryGetValue(key, out var entry) && entry.ExpiresOn > DateTime.UtcNow ? entry.Value : null;
		}

		public async Task SetAsync(string key, string value, TimeSpan timeToLive)
		{
			if (UseCache)
			{
				await _cache.SetStringAsync(key, value, timeToLive);
				return;
			}

			if (Interlocked.Exchange(ref _localWarned, 1) == 0)
				Logging.LogInfo("Records API state (upload sessions, idempotency) is process-local because caching is off; resumable uploads must reach the same node.");
			Sweep();
			Local[key] = (value, DateTime.UtcNow.Add(timeToLive));
		}

		public async Task RemoveAsync(string key)
		{
			if (UseCache)
			{
				await _cache.RemoveAsync(key);
				return;
			}
			Local.TryRemove(key, out _);
		}

		private static void Sweep()
		{
			var now = DateTime.UtcNow;
			foreach (var kv in Local.Where(kv => kv.Value.ExpiresOn <= now).ToList())
				Local.TryRemove(kv.Key, out _);
		}

		/// <summary>Test seam: clears the process-local store.</summary>
		public static void ResetLocal() => Local.Clear();
	}

	/// <summary>Durable command reservations scoped by department, actor, command and client key. Reads older cached receipts during rollout.</summary>
	public class RecordsApiIdempotencyService : IRecordsApiIdempotencyService
	{
		public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
		private readonly IRecordsApiStateStore _store;
		private readonly IRmsCommandReceiptsRepository _receipts;
		private readonly ConcurrentDictionary<string, string> _reservations = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

		public RecordsApiIdempotencyService(IRecordsApiStateStore store, IRmsCommandReceiptsRepository receipts)
		{
			_store = store;
			_receipts = receipts;
		}

		public static string DurableKey(int departmentId, string userId, string idempotencyKey, string command) =>
			RecordSnapshotSerializer.Checksum(JsonConvert.SerializeObject(new { DepartmentId = departmentId, UserId = userId, Command = command?.Trim(), Key = idempotencyKey?.Trim() }));

		public async Task<bool> TryReserveCommandAsync(int departmentId, string userId, string idempotencyKey, string command, string recordId, string requestChecksum)
		{
			ValidateIdentity(userId, idempotencyKey, command, recordId, requestChecksum);
			// Preserve an older receipt rather than treating deployment or cache migration as permission to repeat.
			if (await TryGetCommandAsync(departmentId, userId, idempotencyKey, command) != null) return false;
			var key = DurableKey(departmentId, userId, idempotencyKey, command);
			var reservation = Guid.NewGuid().ToString();
			if (!await _receipts.ReserveAsync(departmentId, key, recordId, requestChecksum, reservation)) return false;
			_reservations[key] = reservation;
			return true;
		}

		private static void ValidateIdentity(string userId, string idempotencyKey, string command, string recordId, string requestChecksum)
		{
			if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(recordId) || string.IsNullOrWhiteSpace(requestChecksum))
				throw new ArgumentException("A command receipt requires its actor, command, key, record and request checksum.");
		}

		/// <summary>
		/// The command is part of the key. A client that reuses one key for SubmitForReview and then Finalize would
		/// otherwise match on the record alone and be told the second command succeeded without it ever running.
		/// </summary>
		public static string Key(int departmentId, string userId, string idempotencyKey, string command)
			=> $"RecordsApiCmd_{departmentId}_{userId}_{(string.IsNullOrWhiteSpace(command) ? "any" : command.Trim())}_{idempotencyKey}";

		public Task<string> TryGetRecordIdAsync(int departmentId, string userId, string idempotencyKey, string command)
		{
			if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(userId))
				return Task.FromResult<string>(null);
			return _store.GetAsync(Key(departmentId, userId, idempotencyKey.Trim(), command));
		}

		public Task RememberAsync(int departmentId, string userId, string idempotencyKey, string command, string recordId)
		{
			if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(recordId))
				return Task.CompletedTask;
			return _store.SetAsync(Key(departmentId, userId, idempotencyKey.Trim(), command), recordId, Retention);
		}

		public async Task<RecordCommandReceipt> TryGetCommandAsync(int departmentId, string userId, string idempotencyKey, string command)
		{
			if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(userId)) return null;
			var durable = await _receipts.GetAsync(departmentId, DurableKey(departmentId, userId, idempotencyKey, command));
			if (durable != null) return durable;
			var value = await TryGetRecordIdAsync(departmentId, userId, idempotencyKey, command);
			if (value == null) return null;
			try { return JsonConvert.DeserializeObject<RecordCommandReceipt>(value) ?? new RecordCommandReceipt(); }
			catch (JsonException) { return new RecordCommandReceipt { RecordId = value }; }
			// A legacy/corrupt unbound receipt is deliberately present with no request checksum. Callers must
			// reject it, rather than treating it as a cache miss and repeating a possibly completed operation.
		}

		public async Task RememberCommandAsync(int departmentId, string userId, string idempotencyKey, string command, string recordId, string requestChecksum)
		{
			ValidateIdentity(userId, idempotencyKey, command, recordId, requestChecksum);
			var key = DurableKey(departmentId, userId, idempotencyKey, command);
			if (!_reservations.TryGetValue(key, out var reservation) || !await _receipts.CompleteAsync(departmentId, key, recordId, requestChecksum, reservation))
				throw new RecordIdempotencyException("The command outcome could not be acknowledged. Review the current record before issuing another command.");
			_reservations.TryRemove(key, out _);
		}
	}

	/// <summary>
	/// Resumable attachment upload sessions (plan section 5.3). The client declares size and SHA-256, sends ordered
	/// chunks (each resumable from the reported ReceivedBytes), and completes; completion verifies the checksum and
	/// then stores through RecordsService.AddAttachmentAsync, so hygiene, scanning and authorization apply exactly as
	/// they do to a direct upload. Sessions expire after a day and never outlive the record's editability.
	/// </summary>
	public class RecordAttachmentUploadService : IRecordAttachmentUploadService
	{
		public const int DefaultChunkSize = 512 * 1024;
		public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

		private readonly IRecordsApiStateStore _store;
		private readonly IRecordsService _records;

		public RecordAttachmentUploadService(IRecordsApiStateStore store, IRecordsService records)
		{
			_store = store;
			_records = records;
		}

		public int ChunkSize => DefaultChunkSize;

		public static string SessionKey(int departmentId, string uploadId) => $"RecordsApiUpload_{departmentId}_{uploadId}";
		public static string ChunkKey(int departmentId, string uploadId, int index) => $"RecordsApiUploadChunk_{departmentId}_{uploadId}_{index}";

		public async Task<RecordAttachmentUploadSession> BeginAsync(int departmentId, string userId, string recordId, string fileName, string contentType, long declaredSize, string sha256)
		{
			if (string.IsNullOrWhiteSpace(recordId)) throw new ArgumentException("A record is required.", nameof(recordId));
			if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A file name is required.", nameof(fileName));
			if (declaredSize <= 0) throw new RecordUploadSessionException("too_large", "The declared size must be positive.");
			if (declaredSize > RecordAttachmentHygiene.MaxBytes) throw new RecordUploadSessionException("too_large", $"Attachments are limited to {RecordAttachmentHygiene.MaxBytes / (1024 * 1024)} MB.");
			if (!IsSha256(sha256)) throw new RecordUploadSessionException("checksum_mismatch", "A lower-case hex SHA-256 of the file is required.");

			var aggregate = await _records.GetAsync(departmentId, recordId);
			if (aggregate == null)
				throw new ArgumentException($"Record {recordId} was not found.", nameof(recordId));
			var state = (RmsRecordState)aggregate.Record.State;
			if (!RmsLifecycle.IsEditable(state) && aggregate.Record.AmendsRevisionId == null)
				throw new RecordTransitionException(recordId, state, state, "attachments can only be added to an editable draft");

			var now = DateTime.UtcNow;
			var session = new RecordAttachmentUploadSession
			{
				UploadId = Guid.NewGuid().ToString("N"),
				DepartmentId = departmentId,
				RecordId = recordId,
				UserId = userId,
				FileName = FileHelper.GetSafeFileName(fileName?.Trim()),
				ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
				DeclaredSize = declaredSize,
				Sha256 = sha256.Trim().ToLowerInvariant(),
				ChunkSize = ChunkSize,
				ChunkCount = (int)((declaredSize + ChunkSize - 1) / ChunkSize),
				CreatedOn = now,
				ExpiresOn = now.Add(SessionLifetime)
			};
			await SaveAsync(session);
			return session;
		}

		public async Task<RecordAttachmentUploadSession> GetAsync(int departmentId, string userId, string uploadId)
		{
			var session = await LoadAsync(departmentId, uploadId);
			if (session == null)
				return null;
			if (!string.Equals(session.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return null;
			return session;
		}

		public async Task<RecordAttachmentUploadSession> AppendAsync(int departmentId, string userId, string uploadId, long offset, byte[] data)
		{
			var session = await RequireOpenAsync(departmentId, userId, uploadId);
			if (data == null || data.Length == 0)
				throw new RecordUploadSessionException("bad_offset", "A chunk must carry data.");
			if (offset != session.ReceivedBytes)
				throw new RecordUploadSessionException("bad_offset", $"Chunks must be sent in order; the next offset is {session.ReceivedBytes}.");
			if (offset % session.ChunkSize != 0)
				throw new RecordUploadSessionException("bad_offset", $"Offsets must be multiples of {session.ChunkSize}.");
			if (data.Length > session.ChunkSize)
				throw new RecordUploadSessionException("bad_offset", $"A chunk may carry at most {session.ChunkSize} bytes.");
			if (session.ReceivedBytes + data.Length > session.DeclaredSize)
				throw new RecordUploadSessionException("too_large", "The upload exceeds its declared size.");
			var index = (int)(offset / session.ChunkSize);
			if (data.Length < session.ChunkSize && index != session.ChunkCount - 1)
				throw new RecordUploadSessionException("bad_offset", "Only the last chunk may be shorter than the chunk size.");

			await _store.SetAsync(ChunkKey(departmentId, uploadId, index), Convert.ToBase64String(data), Remaining(session));
			session.ReceivedBytes += data.Length;
			await SaveAsync(session);
			return session;
		}

		public async Task<RmsRecordAttachment> CompleteAsync(int departmentId, string userId, string uploadId, string description, CancellationToken cancellationToken = default, int classification = 1)
		{
			var session = await RequireOpenAsync(departmentId, userId, uploadId);
			if (!session.IsComplete)
				throw new RecordUploadSessionException("incomplete", $"{session.ReceivedBytes} of {session.DeclaredSize} bytes received.");

			var buffer = new byte[session.DeclaredSize];
			long position = 0;
			for (var index = 0; index < session.ChunkCount; index++)
			{
				var chunk = await _store.GetAsync(ChunkKey(departmentId, uploadId, index));
				if (chunk == null)
					throw new RecordUploadSessionException("expired", $"Chunk {index} is no longer available; restart the upload.");
				var bytes = Convert.FromBase64String(chunk);
				Buffer.BlockCopy(bytes, 0, buffer, (int)position, bytes.Length);
				position += bytes.Length;
			}
			if (position != session.DeclaredSize)
				throw new RecordUploadSessionException("incomplete", "The assembled file does not match the declared size.");

			var actual = RecordSnapshotSerializer.Checksum(buffer);
			if (!string.Equals(actual, session.Sha256, StringComparison.Ordinal))
				throw new RecordUploadSessionException("checksum_mismatch", "The uploaded bytes do not match the declared SHA-256; restart the upload.");

			RmsRecordAttachment attachment;
			try
			{
				attachment = await _records.AddAttachmentAsync(departmentId, userId, session.RecordId, session.FileName, session.ContentType, buffer, description, cancellationToken, classification);
			}
			catch (RecordAttachmentRejectedException ex)
			{
				throw new RecordUploadSessionException("rejected", ex.Message);
			}

			session.State = RecordUploadSessionState.Completed;
			session.AttachmentId = attachment.RmsRecordAttachmentId;
			await SaveAsync(session);
			await RemoveChunksAsync(session);
			return attachment;
		}

		public async Task<bool> AbortAsync(int departmentId, string userId, string uploadId)
		{
			var session = await GetAsync(departmentId, userId, uploadId);
			if (session == null)
				return false;
			session.State = RecordUploadSessionState.Aborted;
			await SaveAsync(session);
			await RemoveChunksAsync(session);
			return true;
		}

		private async Task<RecordAttachmentUploadSession> RequireOpenAsync(int departmentId, string userId, string uploadId)
		{
			var session = await GetAsync(departmentId, userId, uploadId);
			if (session == null)
				throw new RecordUploadSessionException("not_found", "The upload session was not found.");
			if (session.ExpiresOn <= DateTime.UtcNow)
				throw new RecordUploadSessionException("expired", "The upload session has expired; restart the upload.");
			if (session.State != RecordUploadSessionState.Open)
				throw new RecordUploadSessionException("closed", $"The upload session is {session.State}.");
			var aggregate = await _records.GetAsync(departmentId, session.RecordId);
			if (aggregate == null || aggregate.Record.PurgedOn.HasValue || aggregate.Record.DeletedOn.HasValue
				|| !RmsLifecycle.IsEditable((RmsRecordState)aggregate.Record.State) && aggregate.Record.AmendsRevisionId == null)
			{
				await RemoveChunksAsync(session);
				await _store.RemoveAsync(SessionKey(departmentId, uploadId));
				throw new RecordUploadSessionException("closed", "The record is no longer editable; the unfinished upload was removed.");
			}
			return session;
		}

		private async Task<RecordAttachmentUploadSession> LoadAsync(int departmentId, string uploadId)
		{
			if (string.IsNullOrWhiteSpace(uploadId))
				return null;
			var json = await _store.GetAsync(SessionKey(departmentId, uploadId));
			return json == null ? null : JsonConvert.DeserializeObject<RecordAttachmentUploadSession>(json);
		}

		private Task SaveAsync(RecordAttachmentUploadSession session)
		{
			return _store.SetAsync(SessionKey(session.DepartmentId, session.UploadId), JsonConvert.SerializeObject(session), Remaining(session));
		}

		private static TimeSpan Remaining(RecordAttachmentUploadSession session) => TimeSpan.FromMilliseconds(Math.Max(1, (session.ExpiresOn - DateTime.UtcNow).TotalMilliseconds));

		private async Task RemoveChunksAsync(RecordAttachmentUploadSession session)
		{
			for (var index = 0; index < session.ChunkCount; index++)
				await _store.RemoveAsync(ChunkKey(session.DepartmentId, session.UploadId, index));
		}

		public static bool IsSha256(string value)
		{
			return !string.IsNullOrWhiteSpace(value) && value.Trim().Length == 64 && value.Trim().All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
		}

		public static string Sha256Hex(byte[] data)
		{
			using var sha = SHA256.Create();
			return string.Concat(sha.ComputeHash(data ?? Array.Empty<byte>()).Select(b => b.ToString("x2")));
		}
	}

	/// <summary>Names the field paths a stale draft save would have changed, so a client reconciles instead of overwriting (plan section 5.3).</summary>
	public static class RecordDraftConflictResolver
	{
		public static RecordDraftConflict Describe(RecordDraftInput input, RecordAggregate current, long expectedRowVersion)
		{
			var record = current.Record;
			var conflict = new RecordDraftConflict
			{
				RecordId = record.RmsOperationalRecordId,
				ExpectedRowVersion = expectedRowVersion,
				CurrentRowVersion = record.RowVersion,
				CurrentState = (RmsRecordState)record.State,
				CurrentRevisionId = record.CurrentRevisionId
			};
			if (input == null)
				return conflict;

			void Compare(string path, string mine, string theirs)
			{
				if (!string.Equals(mine ?? string.Empty, theirs ?? string.Empty, StringComparison.Ordinal))
					conflict.ChangedFieldPaths.Add(path);
			}

			Compare("StartedOn", Iso(input.StartedOn), Iso(record.StartedOn));
			Compare("EndedOn", Iso(input.EndedOn), Iso(record.EndedOn));
			Compare("StationGroupId", input.StationGroupId?.ToString(), record.StationGroupId?.ToString());
			Compare("CallId", input.CallId?.ToString(), record.CallId?.ToString());
			Compare("ExternalId", input.ExternalId, record.ExternalId);

			var mineDetails = input.Details ?? new RmsOperationalRecordDetail();
			var theirDetails = current.Details ?? new RmsOperationalRecordDetail();
			foreach (var field in RecordSnapshotSerializer.DetailFieldOrder)
			{
				var property = typeof(RmsOperationalRecordDetail).GetProperty(field);
				if (property == null)
					continue;
				Compare("Details." + field, Stringify(property.GetValue(mineDetails)), Stringify(property.GetValue(theirDetails)));
			}

			var mineParticipants = new HashSet<string>((input.Participants ?? new List<RecordParticipantInput>()).Select(p => p.UserId ?? string.Empty), StringComparer.OrdinalIgnoreCase);
			var theirParticipants = new HashSet<string>(current.Participants.Select(p => p.UserId ?? string.Empty), StringComparer.OrdinalIgnoreCase);
			if (!mineParticipants.SetEquals(theirParticipants))
				conflict.ChangedFieldPaths.Add("Participants");

			var mineUnits = new HashSet<string>((input.Units ?? new List<RecordUnitResponseInput>()).Select(u => $"{u.UnitId}|{Iso(u.Dispatched)}|{Iso(u.Enroute)}|{Iso(u.OnScene)}|{Iso(u.Released)}|{Iso(u.InQuarters)}"), StringComparer.Ordinal);
			var theirUnits = new HashSet<string>(current.Units.Select(u => $"{u.UnitId}|{Iso(u.Dispatched)}|{Iso(u.Enroute)}|{Iso(u.OnScene)}|{Iso(u.Released)}|{Iso(u.InQuarters)}"), StringComparer.Ordinal);
			if (!mineUnits.SetEquals(theirUnits))
				conflict.ChangedFieldPaths.Add("Units");

			return conflict;
		}

		private static string Iso(DateTime? value) => value?.ToUniversalTime().ToString("O");

		private static string Stringify(object value)
		{
			switch (value)
			{
				case null: return null;
				case DateTime dt: return dt.ToUniversalTime().ToString("O");
				case bool b: return b ? "true" : "false";
				default: return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
			}
		}
	}
}
