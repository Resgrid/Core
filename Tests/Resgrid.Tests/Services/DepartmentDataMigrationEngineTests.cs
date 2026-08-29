using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Engine tests over an in-memory bulk store — including the plan section 15 double-run proof:
	/// encrypting twice is a no-op, decrypting twice is a no-op, plaintext on the decrypt path passes
	/// through counted as an anomaly, and a foreign-AAD envelope halts the run and is never
	/// re-encrypted.
	/// </summary>
	[TestFixture]
	public class DepartmentDataMigrationEngineTests
	{
		private const int DeptId = 42;

		private InMemoryBulkRepository _bulk;
		private InMemoryMigrationRepository _migrations;
		private Mock<IDepartmentKeyService> _keyService;
		private LocalDevKeyWrappingProvider _keyProvider;
		private ProtectedFieldCryptoService _crypto;
		private DepartmentDataMigrationEngine _engine;
		private byte[] _dek;

		[SetUp]
		public async Task SetUp()
		{
			_bulk = new InMemoryBulkRepository();
			_migrations = new InMemoryMigrationRepository();
			_bulk.MigrationRows = _migrations.Rows;
			_keyProvider = new LocalDevKeyWrappingProvider();
			_crypto = new ProtectedFieldCryptoService();

			var wrapped = await _keyProvider.GenerateWrappedDataKeyAsync(DeptId);
			var keyRow = new DepartmentDataProtectionKey
			{
				DepartmentId = DeptId,
				Version = 1,
				WrappedKey = wrapped.WrappedKeyBase64,
				Status = (int)DepartmentDataProtectionKeyStatus.Active
			};
			_dek = await _keyProvider.UnwrapDataKeyAsync(DeptId, wrapped.WrappedKeyBase64);

			_keyService = new Mock<IDepartmentKeyService>();
			_keyService.Setup(x => x.GetKeyByVersionAsync(DeptId, 1)).ReturnsAsync(keyRow);
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId)).ReturnsAsync(keyRow);

			_engine = new DepartmentDataMigrationEngine(_bulk, _migrations, _keyService.Object, _keyProvider, _crypto, new ProtectedFieldCatalog());

			// Three call rows: one rich, one sparse, one with empty strings only.
			_bulk.Seed("Calls", "CallId",
				Row(("CallId", 1), ("Name", "Structure Fire"), ("NatureOfCall", "Smoke showing"), ("Notes", "Occupant on O2")),
				Row(("CallId", 2), ("Name", "MVA"), ("NatureOfCall", null), ("Notes", null)),
				Row(("CallId", 3), ("Name", ""), ("NatureOfCall", null), ("Notes", null)));
		}

		[TearDown]
		public void TearDown()
		{
			System.Security.Cryptography.CryptographicOperations.ZeroMemory(_dek);
		}

		private static Dictionary<string, object> Row(params (string Key, object Value)[] values) =>
			values.ToDictionary(v => v.Key, v => v.Value);

		private static AdpMigrationNightContext Context(DepartmentDataProtectionMigrationKind kind,
			DateTime? windowEndUtc = null) => new AdpMigrationNightContext
		{
			DepartmentId = DeptId,
			Kind = kind,
			CatalogVersion = 1,
			TargetKeyVersion = 1,
			WindowEndUtc = windowEndUtc ?? DateTime.UtcNow.AddHours(1),
			CorrelationId = "test-run"
		};

		[Test]
		public async Task Enrollment_envelopes_every_populated_text_value_and_persists_the_cursor()
		{
			var result = await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			result.Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);

			var calls = _bulk.Table("Calls");
			((string)calls[0]["Name"]).Should().StartWith("rgdp:1:1:");
			((string)calls[0]["NatureOfCall"]).Should().StartWith("rgdp:1:1:");
			((string)calls[0]["Notes"]).Should().StartWith("rgdp:1:1:");
			((string)calls[1]["Name"]).Should().StartWith("rgdp:1:1:");
			calls[1]["NatureOfCall"].Should().BeNull();
			((string)calls[2]["Name"]).Should().Be("", "empty strings are skipped, not encrypted");

			var migrationRow = _migrations.Rows.Values.Single(r => r.TargetTable == "Calls");
			migrationRow.Cursor.Should().Be("3");
			migrationRow.RowsProcessed.Should().Be(2);

			_crypto.DecryptText(_dek, (string)calls[0]["NatureOfCall"], DeptId, "calls.natureofcall", "1")
				.Should().Be("Smoke showing");
		}

		[Test]
		public async Task Double_run_proof_resuming_an_open_run_skips_the_completed_range()
		{
			await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);
			var snapshot = _bulk.Snapshot("Calls");

			// Same open run re-executed (worker crash after the last batch): the durable cursor makes
			// the resume a pure no-op — nothing is re-read, nothing is re-encrypted.
			var second = await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			second.Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);
			_bulk.Snapshot("Calls").Should().BeEquivalentTo(snapshot, "re-running a completed range must be byte-identical");

			var migrationRow = _migrations.Rows.Values.Single(r => r.TargetTable == "Calls");
			migrationRow.RowsProcessed.Should().Be(2, "the resume must not re-encrypt anything");
		}

		[Test]
		public async Task Double_run_proof_a_fresh_run_over_encrypted_data_counts_already_protected_and_changes_nothing()
		{
			await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);
			await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);
			var snapshot = _bulk.Snapshot("Calls");

			// A brand-new run (fresh migration rows, null cursor) rescans every row: the
			// double-encryption guard validates each envelope against this department's AAD, counts it
			// already-protected, and escalates nothing.
			var second = await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			second.Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);
			_bulk.Snapshot("Calls").Should().BeEquivalentTo(snapshot, "zero re-encryptions; envelopes stay byte-identical");

			var freshRow = _migrations.Rows.Values.Single(r => r.TargetTable == "Calls" && r.CompletedOn == null);
			freshRow.RowsProcessed.Should().Be(0, "no row may be re-encrypted");
			freshRow.RowsAlreadyProtected.Should().Be(2, "already-protected rows are counted, not errored");
		}

		[Test]
		public async Task Fresh_run_after_a_reprovision_validates_old_version_envelopes_with_their_own_key()
		{
			// Enrollment encrypted everything under key v1; a failed-run recovery then minted key v2.
			// The new run's double-encryption guard must validate v1 envelopes with the v1 DEK — with
			// the v2 DEK every such row would read as a foreign envelope and no retry could clear it.
			await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);
			await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);
			var snapshot = _bulk.Snapshot("Calls");

			var wrappedV2 = await _keyProvider.GenerateWrappedDataKeyAsync(DeptId);
			var keyRowV2 = new DepartmentDataProtectionKey
			{
				DepartmentId = DeptId,
				Version = 2,
				WrappedKey = wrappedV2.WrappedKeyBase64,
				Status = (int)DepartmentDataProtectionKeyStatus.Active
			};
			_keyService.Setup(x => x.GetKeyByVersionAsync(DeptId, 2)).ReturnsAsync(keyRowV2);
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId)).ReturnsAsync(keyRowV2);

			var context = Context(DepartmentDataProtectionMigrationKind.Enrollment);
			context.TargetKeyVersion = 2;
			var result = await _engine.RunEncryptionNightAsync(context, CancellationToken.None);

			result.Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables,
				"v1 envelopes validate with the v1 DEK instead of halting as foreign");
			_bulk.Snapshot("Calls").Should().BeEquivalentTo(snapshot,
				"old-version envelopes are counted already-protected, never re-encrypted");
		}

		[Test]
		public async Task Envelope_referencing_an_unknown_key_version_halts_the_run()
		{
			_bulk.Table("Calls")[0]["Name"] = "rgdp:1:9:AAAA";

			var result = await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			result.Outcome.Should().Be(AdpMigrationNightOutcome.Failed);
			result.ErrorCode.Should().Be("foreign_envelope");
			_bulk.Table("Calls")[0]["Name"].Should().Be("rgdp:1:9:AAAA", "an unresolvable envelope is halted on, never re-encrypted");
		}

		[Test]
		public async Task Offboarding_restores_plaintext_and_second_decrypt_pass_counts_anomalies_only()
		{
			await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			var decrypt = await _engine.RunDecryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Offboarding), CancellationToken.None);
			decrypt.Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);

			var calls = _bulk.Table("Calls");
			calls[0]["Name"].Should().Be("Structure Fire");
			calls[0]["NatureOfCall"].Should().Be("Smoke showing");
			calls[0]["Notes"].Should().Be("Occupant on O2");

			// Complete the first offboarding run, then start a FRESH one over now-plaintext data: the
			// decrypt path passes plaintext through untouched and only increments the anomaly counter.
			await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Offboarding), CancellationToken.None);
			var snapshot = _bulk.Snapshot("Calls");
			var secondDecrypt = await _engine.RunDecryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Offboarding), CancellationToken.None);

			secondDecrypt.Outcome.Should().Be(AdpMigrationNightOutcome.CompletedAllTables);
			_bulk.Snapshot("Calls").Should().BeEquivalentTo(snapshot, "plaintext passes through the decrypt path untouched");

			var freshOffboardingRow = _migrations.Rows.Values.Single(r =>
				r.TargetTable == "Calls" && r.Kind == (int)DepartmentDataProtectionMigrationKind.Offboarding && r.CompletedOn == null);
			freshOffboardingRow.RowsProcessed.Should().Be(0, "nothing may be 'decrypted' into garbage");
			freshOffboardingRow.RowsAnomalous.Should().BeGreaterThan(0, "plaintext on the decrypt path increments the anomaly counter");
		}

		[Test]
		public async Task Foreign_envelope_halts_the_run_and_is_never_re_encrypted()
		{
			// An envelope bound to another department's AAD, planted in this department's data.
			var foreign = _crypto.EncryptText(_dek, 1, "someone else's data", 43, "calls.name", "1");
			_bulk.Table("Calls")[0]["Name"] = foreign;

			var result = await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			result.Outcome.Should().Be(AdpMigrationNightOutcome.Failed);
			result.ErrorCode.Should().Be("foreign_envelope");
			_bulk.Table("Calls")[0]["Name"].Should().Be(foreign, "a foreign envelope is halted on, never re-encrypted");

			_migrations.Rows.Values.Single(r => r.TargetTable == "Calls").LastErrorCode.Should().Be("foreign_envelope");
		}

		[Test]
		public async Task Closed_window_checkpoints_before_touching_any_row()
		{
			var snapshot = _bulk.Snapshot("Calls");

			var result = await _engine.RunEncryptionNightAsync(
				Context(DepartmentDataProtectionMigrationKind.Enrollment, windowEndUtc: DateTime.UtcNow.AddMinutes(-1)),
				CancellationToken.None);

			result.Outcome.Should().Be(AdpMigrationNightOutcome.WindowClosed);
			_bulk.Snapshot("Calls").Should().BeEquivalentTo(snapshot);
		}

		[Test]
		public async Task Missing_kms_fails_the_run_closed()
		{
			var engine = new DepartmentDataMigrationEngine(_bulk, _migrations, _keyService.Object,
				new NotConfiguredKeyWrappingProvider(), _crypto, new ProtectedFieldCatalog());

			var result = await engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);

			result.Outcome.Should().Be(AdpMigrationNightOutcome.Failed);
			result.ErrorCode.Should().Be("kms_unavailable");
		}

		[Test]
		public async Task Verification_gates_on_residue_in_both_directions()
		{
			// Plaintext residue before enrollment ran -> verification must fail.
			(await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None))
				.Should().BeFalse("plaintext residue must block Enabled");

			await _engine.RunEncryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None);
			(await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Enrollment), CancellationToken.None))
				.Should().BeTrue("a clean plaintext-residue scan passes enrollment verification");

			_migrations.Rows.Values.Where(r => r.Kind == (int)DepartmentDataProtectionMigrationKind.Enrollment)
				.Should().OnlyContain(r => r.CompletedOn != null && r.VerificationState == (int)DepartmentDataProtectionVerificationState.Passed);

			// Envelope residue before offboarding ran -> verification must fail; clean after.
			(await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Offboarding), CancellationToken.None))
				.Should().BeFalse("envelope residue must block Disabled");

			await _engine.RunDecryptionNightAsync(Context(DepartmentDataProtectionMigrationKind.Offboarding), CancellationToken.None);
			(await _engine.VerifyAsync(Context(DepartmentDataProtectionMigrationKind.Offboarding), CancellationToken.None))
				.Should().BeTrue();
		}

		#region In-memory fakes

		private sealed class InMemoryBulkRepository : IDepartmentDataProtectionBulkRepository
		{
			private readonly Dictionary<string, (string PkColumn, List<Dictionary<string, object>> Rows)> _tables =
				new(StringComparer.OrdinalIgnoreCase);

			public Dictionary<int, DepartmentDataProtectionMigration> MigrationRows { get; set; }

			public void Seed(string table, string pkColumn, params Dictionary<string, object>[] rows) =>
				_tables[table] = (pkColumn, rows.ToList());

			public List<Dictionary<string, object>> Table(string table) => _tables[table].Rows;

			public List<Dictionary<string, object>> Snapshot(string table) =>
				_tables[table].Rows.Select(r => new Dictionary<string, object>(r)).ToList();

			public Task<long> CountRowsAsync(AdpTableBinding binding, int departmentId, CancellationToken cancellationToken = default) =>
				Task.FromResult<long>(_tables.TryGetValue(binding.TableName, out var t) ? t.Rows.Count : 0);

			public Task<IReadOnlyList<AdpBulkFieldRow>> GetBatchAsync(AdpTableBinding binding, int departmentId,
				string afterCursor, int batchSize, CancellationToken cancellationToken = default)
			{
				if (!_tables.TryGetValue(binding.TableName, out var table))
					return Task.FromResult<IReadOnlyList<AdpBulkFieldRow>>(Array.Empty<AdpBulkFieldRow>());

				var ordered = table.Rows.OrderBy(r => Convert.ToInt64(r[table.PkColumn], CultureInfo.InvariantCulture)).ToList();
				var filtered = string.IsNullOrEmpty(afterCursor)
					? ordered
					: ordered.Where(r => Convert.ToInt64(r[table.PkColumn], CultureInfo.InvariantCulture) >
										 long.Parse(afterCursor, CultureInfo.InvariantCulture)).ToList();

				var batch = filtered.Take(batchSize).Select(r => new AdpBulkFieldRow
				{
					RowKey = Convert.ToString(r[table.PkColumn], CultureInfo.InvariantCulture),
					Values = binding.Columns
						.SelectMany(c => c.StorageKind == ProtectedFieldStorageKind.CompanionColumn
							? new[] { c.ColumnName, c.CompanionColumn }
							: new[] { c.ColumnName })
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToDictionary(c => c, c => r.TryGetValue(c, out var v) ? v : null)
				}).ToList();

				return Task.FromResult<IReadOnlyList<AdpBulkFieldRow>>(batch);
			}

			public Task ApplyBatchAsync(AdpTableBinding binding, IReadOnlyList<AdpBulkRowUpdate> updates,
				int departmentDataProtectionMigrationId, string newCursor, long rowsProcessedDelta,
				long rowsAlreadyProtectedDelta, long rowsAnomalousDelta, CancellationToken cancellationToken)
			{
				var table = _tables[binding.TableName];
				foreach (var update in updates ?? (IReadOnlyList<AdpBulkRowUpdate>)Array.Empty<AdpBulkRowUpdate>())
				{
					var row = table.Rows.Single(r =>
						Convert.ToString(r[table.PkColumn], CultureInfo.InvariantCulture) == update.RowKey);
					foreach (var kv in update.SetValues)
						row[kv.Key] = kv.Value;
				}

				var migrationRow = MigrationRows[departmentDataProtectionMigrationId];
				migrationRow.Cursor = newCursor;
				migrationRow.RowsProcessed += rowsProcessedDelta;
				migrationRow.RowsAlreadyProtected += rowsAlreadyProtectedDelta;
				migrationRow.RowsAnomalous += rowsAnomalousDelta;
				migrationRow.CheckpointedOn = DateTime.UtcNow;
				return Task.CompletedTask;
			}

			public Task<long> CountTextResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped, CancellationToken cancellationToken = default)
			{
				if (!_tables.TryGetValue(binding.TableName, out var table))
					return Task.FromResult(0L);

				var textColumns = binding.Columns.Where(c => c.StorageKind == ProtectedFieldStorageKind.Text).ToList();
				var count = table.Rows.Count(r => textColumns.Any(c =>
				{
					var value = r.TryGetValue(c.ColumnName, out var v) ? v as string : null;
					if (string.IsNullOrEmpty(value))
						return false;
					var isEnvelope = value.StartsWith("rgdp:", StringComparison.Ordinal);
					return enveloped ? isEnvelope : !isEnvelope;
				}));
				return Task.FromResult((long)count);
			}

			public Task<long> CountBinaryResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped, CancellationToken cancellationToken = default)
			{
				if (!_tables.TryGetValue(binding.TableName, out var table))
					return Task.FromResult(0L);

				var binaryColumns = binding.Columns.Where(c => c.StorageKind == ProtectedFieldStorageKind.Binary).ToList();
				var prefix = Encoding.ASCII.GetBytes("rgdpb:");
				var count = table.Rows.Count(r => binaryColumns.Any(c =>
				{
					var value = r.TryGetValue(c.ColumnName, out var v) ? v as byte[] : null;
					if (value == null || value.Length == 0)
						return false;
					var isEnvelope = value.Length >= prefix.Length && prefix.SequenceEqual(value.Take(prefix.Length));
					return enveloped ? isEnvelope : !isEnvelope;
				}));
				return Task.FromResult((long)count);
			}

			public Task<long> CountCompanionResidueAsync(AdpTableBinding binding, int departmentId, bool enveloped, CancellationToken cancellationToken = default)
			{
				if (!_tables.TryGetValue(binding.TableName, out var table))
					return Task.FromResult(0L);

				var companionColumns = binding.Columns.Where(c => c.StorageKind == ProtectedFieldStorageKind.CompanionColumn).ToList();
				var count = table.Rows.Count(r => companionColumns.Any(c =>
				{
					var column = enveloped ? c.CompanionColumn : c.ColumnName;
					return r.TryGetValue(column, out var v) && v != null;
				}));
				return Task.FromResult((long)count);
			}
		}

		private sealed class InMemoryMigrationRepository : IDepartmentDataProtectionMigrationRepository
		{
			private int _nextId = 1;
			public Dictionary<int, DepartmentDataProtectionMigration> Rows { get; } = new();

			public Task<IReadOnlyList<DepartmentDataProtectionMigration>> GetActiveByDepartmentIdAsync(int departmentId,
				DepartmentDataProtectionMigrationKind kind) =>
				Task.FromResult<IReadOnlyList<DepartmentDataProtectionMigration>>(Rows.Values
					.Where(r => r.DepartmentId == departmentId && r.Kind == (int)kind && r.CompletedOn == null).ToList());

			public Task<DepartmentDataProtectionMigration> GetActiveByDepartmentAndTableAsync(int departmentId,
				DepartmentDataProtectionMigrationKind kind, string targetTable) =>
				Task.FromResult(Rows.Values.FirstOrDefault(r => r.DepartmentId == departmentId &&
					r.Kind == (int)kind && r.TargetTable == targetTable && r.CompletedOn == null));

			public Task<DepartmentDataProtectionMigration> InsertAsync(DepartmentDataProtectionMigration entity,
				CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				entity.DepartmentDataProtectionMigrationId = _nextId++;
				Rows[entity.DepartmentDataProtectionMigrationId] = entity;
				return Task.FromResult(entity);
			}

			public Task<DepartmentDataProtectionMigration> SaveOrUpdateAsync(DepartmentDataProtectionMigration entity,
				CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				Rows[entity.DepartmentDataProtectionMigrationId] = entity;
				return Task.FromResult(entity);
			}

			public Task<IEnumerable<DepartmentDataProtectionMigration>> GetAllAsync() =>
				Task.FromResult<IEnumerable<DepartmentDataProtectionMigration>>(Rows.Values.ToList());

			public Task<DepartmentDataProtectionMigration> GetByIdAsync(object id) =>
				Task.FromResult(Rows.TryGetValue((int)id, out var row) ? row : null);

			public Task<IEnumerable<DepartmentDataProtectionMigration>> GetAllByDepartmentIdAsync(int departmentId) =>
				Task.FromResult<IEnumerable<DepartmentDataProtectionMigration>>(
					Rows.Values.Where(r => r.DepartmentId == departmentId).ToList());

			public Task<DepartmentDataProtectionMigration> UpdateAsync(DepartmentDataProtectionMigration entity,
				CancellationToken cancellationToken, bool firstLevelOnly = false) =>
				SaveOrUpdateAsync(entity, cancellationToken, firstLevelOnly);

			public Task<bool> DeleteAsync(DepartmentDataProtectionMigration entity, CancellationToken cancellationToken) =>
				Task.FromResult(Rows.Remove(entity.DepartmentDataProtectionMigrationId));

			public Task<IEnumerable<DepartmentDataProtectionMigration>> GetAllByUserIdAsync(string userId) =>
				throw new NotSupportedException();

			public Task<bool> DeleteMultipleAsync(DepartmentDataProtectionMigration entity, string parentKeyName,
				object parentKeyId, List<object> ids, CancellationToken cancellationToken) =>
				throw new NotSupportedException();
		}

		#endregion
	}
}
