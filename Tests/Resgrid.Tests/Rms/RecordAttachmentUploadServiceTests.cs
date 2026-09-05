using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>In-memory state store for the v4 Records contract tests.</summary>
	public sealed class MemoryRecordsApiStateStore : IRecordsApiStateStore
	{
		public ConcurrentDictionary<string, (string Value, DateTime ExpiresOn)> Entries { get; } = new ConcurrentDictionary<string, (string, DateTime)>(StringComparer.Ordinal);

		public Task<string> GetAsync(string key)
		{
			return Task.FromResult(Entries.TryGetValue(key, out var entry) && entry.ExpiresOn > DateTime.UtcNow ? entry.Value : null);
		}

		public Task SetAsync(string key, string value, TimeSpan timeToLive)
		{
			Entries[key] = (value, DateTime.UtcNow.Add(timeToLive));
			return Task.CompletedTask;
		}

		public Task RemoveAsync(string key)
		{
			Entries.TryRemove(key, out _);
			return Task.CompletedTask;
		}
	}

	/// <summary>Resumable attachment sessions (RMS plan 5.3): ordered chunks, resume from ReceivedBytes, checksum gate, hygiene path on completion.</summary>
	[TestFixture]
	public class RecordAttachmentUploadServiceTests
	{
		private const int Dept = 42;
		private const string RecordId = "rec-1";

		private MemoryRecordsApiStateStore _store;
		private Mock<IRecordsService> _records;
		private RecordAttachmentUploadService _service;
		private List<(string FileName, byte[] Data)> _stored;

		[SetUp]
		public void SetUp()
		{
			_store = new MemoryRecordsApiStateStore();
			_stored = new List<(string, byte[])>();
			_records = new Mock<IRecordsService>();
			_records.Setup(r => r.GetAsync(Dept, RecordId, It.IsAny<bool>()))
				.ReturnsAsync(new RecordAggregate { Record = new RmsOperationalRecord { RmsOperationalRecordId = RecordId, DepartmentId = Dept, State = (int)RmsRecordState.Draft } });
			_records.Setup(r => r.GetAsync(Dept, "final", It.IsAny<bool>()))
				.ReturnsAsync(new RecordAggregate { Record = new RmsOperationalRecord { RmsOperationalRecordId = "final", DepartmentId = Dept, State = (int)RmsRecordState.Finalized } });
			_records.Setup(r => r.AddAttachmentAsync(Dept, "author", RecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
				.ReturnsAsync((int d, string u, string rec, string name, string type, byte[] data, string desc, CancellationToken c, int classification) =>
				{
					_stored.Add((name, data));
					return new RmsRecordAttachment { RmsRecordAttachmentId = "att-1", RecordId = rec, FileName = name, ContentType = type, ByteSize = data.Length, Checksum = RecordSnapshotSerializer.Checksum(data), ScanState = (int)RmsAttachmentScanState.Skipped };
				});
			_service = new RecordAttachmentUploadService(_store, _records.Object);
		}

		private static byte[] Bytes(int length, int seed = 7)
		{
			var data = new byte[length];
			var rng = new Random(seed);
			rng.NextBytes(data);
			return data;
		}

		[Test]
		public async Task Chunks_in_order_then_complete_stores_through_the_hygiene_path()
		{
			var file = Bytes(_service.ChunkSize * 2 + 100);
			var session = await _service.BeginAsync(Dept, "author", RecordId, "scene.jpg", "image/jpeg", file.Length, RecordAttachmentUploadService.Sha256Hex(file));
			session.ChunkCount.Should().Be(3);
			session.State.Should().Be(RecordUploadSessionState.Open);

			session = await _service.AppendAsync(Dept, "author", session.UploadId, 0, file.Take(_service.ChunkSize).ToArray());
			session.ReceivedBytes.Should().Be(_service.ChunkSize);
			session = await _service.AppendAsync(Dept, "author", session.UploadId, _service.ChunkSize, file.Skip(_service.ChunkSize).Take(_service.ChunkSize).ToArray());
			session = await _service.AppendAsync(Dept, "author", session.UploadId, 2 * _service.ChunkSize, file.Skip(2 * _service.ChunkSize).ToArray());
			session.IsComplete.Should().BeTrue();

			var attachment = await _service.CompleteAsync(Dept, "author", session.UploadId, "Scene photo");

			attachment.RmsRecordAttachmentId.Should().Be("att-1");
			_stored.Should().ContainSingle();
			_stored[0].Data.Should().Equal(file, "the assembled bytes reach AddAttachmentAsync unchanged");
			_stored[0].FileName.Should().Be("scene.jpg");
			(await _service.GetAsync(Dept, "author", session.UploadId)).State.Should().Be(RecordUploadSessionState.Completed);
			_store.Entries.Keys.Should().NotContain(k => k.StartsWith("RecordsApiUploadChunk_"), "chunks are released after completion");
		}

		[Test]
		public async Task Out_of_order_chunk_is_refused_and_the_session_reports_where_to_resume()
		{
			var file = Bytes(_service.ChunkSize + 10);
			var session = await _service.BeginAsync(Dept, "author", RecordId, "a.png", "image/png", file.Length, RecordAttachmentUploadService.Sha256Hex(file));
			await _service.AppendAsync(Dept, "author", session.UploadId, 0, file.Take(_service.ChunkSize).ToArray());

			Func<Task> replay = () => _service.AppendAsync(Dept, "author", session.UploadId, 0, file.Take(_service.ChunkSize).ToArray());
			(await replay.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("bad_offset");

			var resumed = await _service.GetAsync(Dept, "author", session.UploadId);
			resumed.ReceivedBytes.Should().Be(_service.ChunkSize, "the client resumes from ReceivedBytes");
			await _service.AppendAsync(Dept, "author", session.UploadId, resumed.ReceivedBytes, file.Skip(_service.ChunkSize).ToArray());
			(await _service.CompleteAsync(Dept, "author", session.UploadId, null)).Should().NotBeNull();
		}

		[Test]
		public async Task Checksum_mismatch_refuses_completion_and_never_stores()
		{
			var file = Bytes(1000);
			var session = await _service.BeginAsync(Dept, "author", RecordId, "a.png", "image/png", file.Length, RecordAttachmentUploadService.Sha256Hex(Bytes(1000, 99)));
			await _service.AppendAsync(Dept, "author", session.UploadId, 0, file);

			Func<Task> act = () => _service.CompleteAsync(Dept, "author", session.UploadId, null);

			(await act.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("checksum_mismatch");
			_stored.Should().BeEmpty();
		}

		[Test]
		public async Task Incomplete_session_cannot_complete()
		{
			var file = Bytes(_service.ChunkSize + 1);
			var session = await _service.BeginAsync(Dept, "author", RecordId, "a.png", "image/png", file.Length, RecordAttachmentUploadService.Sha256Hex(file));
			await _service.AppendAsync(Dept, "author", session.UploadId, 0, file.Take(_service.ChunkSize).ToArray());

			Func<Task> act = () => _service.CompleteAsync(Dept, "author", session.UploadId, null);

			(await act.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("incomplete");
		}

		[Test]
		public async Task Declared_size_over_the_cap_or_a_bad_checksum_is_refused_at_begin()
		{
			Func<Task> big = () => _service.BeginAsync(Dept, "author", RecordId, "a.png", "image/png", RecordAttachmentHygiene.MaxBytes + 1, new string('a', 64));
			(await big.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("too_large");

			Func<Task> hash = () => _service.BeginAsync(Dept, "author", RecordId, "a.png", "image/png", 10, "not-a-hash");
			(await hash.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("checksum_mismatch");
		}

		[Test]
		public async Task Finalized_record_refuses_new_upload_sessions()
		{
			Func<Task> act = () => _service.BeginAsync(Dept, "author", "final", "a.png", "image/png", 10, new string('a', 64));
			await act.Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Another_user_cannot_see_or_append_to_the_session()
		{
			var file = Bytes(10);
			var session = await _service.BeginAsync(Dept, "author", RecordId, "a.png", "image/png", file.Length, RecordAttachmentUploadService.Sha256Hex(file));

			(await _service.GetAsync(Dept, "intruder", session.UploadId)).Should().BeNull();
			Func<Task> act = () => _service.AppendAsync(Dept, "intruder", session.UploadId, 0, file);
			(await act.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("not_found");
		}

		[Test]
		public async Task Hygiene_rejection_surfaces_as_rejected_and_the_session_stays_open_for_a_retry()
		{
			var file = Bytes(10);
			_records.Setup(r => r.AddAttachmentAsync(Dept, "author", RecordId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>()))
				.ThrowsAsync(new RecordAttachmentRejectedException("Attachment 'a.svg' is not an allowed type."));
			var session = await _service.BeginAsync(Dept, "author", RecordId, "a.svg", "image/svg+xml", file.Length, RecordAttachmentUploadService.Sha256Hex(file));
			await _service.AppendAsync(Dept, "author", session.UploadId, 0, file);

			Func<Task> act = () => _service.CompleteAsync(Dept, "author", session.UploadId, null);

			(await act.Should().ThrowAsync<RecordUploadSessionException>()).Which.Code.Should().Be("rejected");
			(await _service.GetAsync(Dept, "author", session.UploadId)).State.Should().Be(RecordUploadSessionState.Open);
		}

		[Test]
		public async Task Idempotency_service_replays_by_department_user_command_and_key()
		{
			var idempotency = new RecordsApiIdempotencyService(_store, Mock.Of<IRmsCommandReceiptsRepository>());

			(await idempotency.TryGetRecordIdAsync(Dept, "u1", "k1", "Finalize")).Should().BeNull();
			await idempotency.RememberAsync(Dept, "u1", "k1", "Finalize", "rec-9");

			(await idempotency.TryGetRecordIdAsync(Dept, "u1", "k1", "Finalize")).Should().Be("rec-9");
			(await idempotency.TryGetRecordIdAsync(Dept, "u2", "k1", "Finalize")).Should().BeNull("keys are scoped to the user");
			(await idempotency.TryGetRecordIdAsync(43, "u1", "k1", "Finalize")).Should().BeNull("keys are scoped to the department");
			(await idempotency.TryGetRecordIdAsync(Dept, "u1", null, "Finalize")).Should().BeNull();

			// A client that reuses one key for a second command must not be told that command already succeeded.
			(await idempotency.TryGetRecordIdAsync(Dept, "u1", "k1", "Void")).Should().BeNull("keys are scoped to the command");
		}

		[Test]
		public async Task Legacy_command_receipts_keep_request_identity_and_unbound_entries_cannot_be_treated_as_unused_keys()
		{
			var repository = new Mock<IRmsCommandReceiptsRepository>(MockBehavior.Strict);
			repository.Setup(r => r.GetAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((RecordCommandReceipt)null);
			var idempotency = new RecordsApiIdempotencyService(_store, repository.Object);
			await _store.SetAsync(RecordsApiIdempotencyService.Key(Dept, "officer", "key", "Finalize"), Newtonsoft.Json.JsonConvert.SerializeObject(new RecordCommandReceipt { RecordId = "record", RequestChecksum = "checksum" }), TimeSpan.FromHours(1));
			var receipt = await idempotency.TryGetCommandAsync(Dept, "officer", "key", "Finalize");
			receipt.RecordId.Should().Be("record"); receipt.RequestChecksum.Should().Be("checksum");
			(await idempotency.TryGetCommandAsync(Dept, "another-officer", "key", "Finalize")).Should().BeNull();
			(await idempotency.TryGetCommandAsync(Dept + 1, "officer", "key", "Finalize")).Should().BeNull();
			(await idempotency.TryGetCommandAsync(Dept, "officer", "key", "Cancel")).Should().BeNull();
			await idempotency.RememberAsync(Dept, "officer", "legacy", "Finalize", "old-record");
			var legacy = await idempotency.TryGetCommandAsync(Dept, "officer", "legacy", "Finalize");
			legacy.Should().NotBeNull(); legacy.RecordId.Should().Be("old-record"); legacy.RequestChecksum.Should().BeNull();
			(await idempotency.TryReserveCommandAsync(Dept, "officer", "key", "Finalize", "record", "checksum")).Should().BeFalse();
			(await idempotency.TryReserveCommandAsync(Dept, "officer", "legacy", "Finalize", "old-record", "checksum")).Should().BeFalse();
		}

		[Test]
		public void Conflict_resolver_names_only_the_paths_the_stale_copy_would_change()
		{
			var current = new RecordAggregate
			{
				Record = new RmsOperationalRecord { RmsOperationalRecordId = RecordId, State = (int)RmsRecordState.Draft, RowVersion = 5, StartedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), StationGroupId = 3 },
				Details = new RmsOperationalRecordDetail { Narrative = "Server narrative", Course = "CPR" },
				Units = new List<RmsRecordUnitResponse> { new RmsRecordUnitResponse { UnitId = 5 } }
			};
			var attempted = new RecordDraftInput
			{
				StartedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
				StationGroupId = 3,
				Details = new RmsOperationalRecordDetail { Narrative = "My narrative", Course = "CPR" },
				Units = new List<RecordUnitResponseInput> { new RecordUnitResponseInput { UnitId = 5 }, new RecordUnitResponseInput { UnitId = 6 } }
			};

			var conflict = RecordDraftConflictResolver.Describe(attempted, current, 4);

			conflict.ExpectedRowVersion.Should().Be(4);
			conflict.CurrentRowVersion.Should().Be(5);
			conflict.CurrentState.Should().Be(RmsRecordState.Draft);
			conflict.ChangedFieldPaths.Should().BeEquivalentTo(new[] { "Details.Narrative", "Units" });
		}

		[Test]
		public void Definition_catalog_describes_every_locked_definition_with_its_fields()
		{
			var catalog = RecordDefinitionCatalog.Describe();

			catalog.Select(d => d.Key).Should().Contain(RmsDefinitionKeys.LockedTypes.Keys).And.Contain(RmsDefinitionKeys.NerisIncidentReport);
			catalog.Should().OnlyContain(d => d.MinimumClientCapability == RecordsApiContract.LockedDefinitionCapability && d.Locked);
			var coroner = catalog.Single(d => d.Key == RmsDefinitionKeys.Coroner);
			coroner.Restricted.Should().BeTrue();
			coroner.Fields.Where(f => f.Restricted).Select(f => f.Key).Should().BeEquivalentTo(RecordSnapshotSerializer.RestrictedDetailFields);
			catalog.Single(d => d.Key == RmsDefinitionKeys.UnitActivity).Fields.Should().Contain(f => f.Key == "UnitId" && f.Required);
			catalog.Should().OnlyContain(d => d.Fields.Any(f => f.Key == "Narrative"));
			catalog.Single(d => d.Key == RmsDefinitionKeys.NerisIncidentReport).RequiresCall.Should().BeTrue();
		}

		[Test]
		public void ETag_round_trips_through_the_contract_helpers()
		{
			RecordsApiContract.ToETag(7).Should().Be("W/\"7\"");
			RecordsApiContract.ParseETag("W/\"7\"").Should().Be(7);
			RecordsApiContract.ParseETag("\"12\"").Should().Be(12);
			RecordsApiContract.ParseETag("3").Should().Be(3);
			RecordsApiContract.ParseETag("W/\"abc\"").Should().BeNull();
			RecordsApiContract.ParseETag(null).Should().BeNull();
		}
	}
}
