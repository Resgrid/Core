using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The real ADP bulk migration engine (plan sections 19.3–19.4). Walks the code-reviewed table
	/// bindings in bounded transactional batches with durable cursors in
	/// DepartmentDataProtectionMigrations. Double-encryption guard: the encrypt path validates any
	/// value already carrying an envelope against this department's AAD — a matching envelope counts
	/// as already-protected and moves on; a foreign or corrupt one halts the run with a value-free
	/// error and is never re-encrypted. The decrypt path passes plaintext through untouched and
	/// counts the anomaly. DEKs are unwrapped through IKeyWrappingProvider (broker-backed in
	/// production; LocalDev for synthetic testing; the app tier's NotConfigured provider makes every
	/// run fail closed with kms_unavailable), held pinned, and zeroed before return. No plaintext is
	/// ever staged in temp tables, files, or queues.
	/// </summary>
	public class DepartmentDataMigrationEngine : IDepartmentDataMigrationEngine
	{
		private static readonly IReadOnlyList<AdpTableBinding> Bindings = AdpTableBindings.V1;

		private readonly IDepartmentDataProtectionBulkRepository _bulkRepository;
		private readonly IDepartmentDataProtectionMigrationRepository _migrationRepository;
		private readonly IDepartmentKeyService _keyService;
		private readonly IKeyWrappingProvider _keyWrappingProvider;
		private readonly IProtectedFieldCryptoService _cryptoService;

		public DepartmentDataMigrationEngine(IDepartmentDataProtectionBulkRepository bulkRepository,
			IDepartmentDataProtectionMigrationRepository migrationRepository, IDepartmentKeyService keyService,
			IKeyWrappingProvider keyWrappingProvider, IProtectedFieldCryptoService cryptoService)
		{
			_bulkRepository = bulkRepository;
			_migrationRepository = migrationRepository;
			_keyService = keyService;
			_keyWrappingProvider = keyWrappingProvider;
			_cryptoService = cryptoService;
		}

		public bool IsAvailable => true;

		public async Task<AdpMigrationNightResult> RunEncryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken)
		{
			var keyVersion = context.TargetKeyVersion ?? 0;
			var keyRow = keyVersion > 0
				? await _keyService.GetKeyByVersionAsync(context.DepartmentId, keyVersion)
				: await _keyService.GetActiveKeyAsync(context.DepartmentId);
			if (keyRow == null)
				return AdpMigrationNightResult.Failed("key_unavailable");

			byte[] dek;
			try
			{
				dek = await _keyWrappingProvider.UnwrapDataKeyAsync(context.DepartmentId, keyRow.WrappedKey, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP engine could not unwrap the DEK for department {context.DepartmentId}; failing closed.");
				return AdpMigrationNightResult.Failed("kms_unavailable");
			}

			try
			{
				return await RunNightAsync(context, isEncrypting: true,
					(spec, row, updates) => EncryptRowColumn(dek, keyRow.Version, context, spec, row, updates),
					cancellationToken);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(dek);
			}
		}

		public async Task<AdpMigrationNightResult> RunDecryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken)
		{
			// Offboarding resolves DEKs lazily per envelope key version (older Retiring versions may
			// still be referenced by envelopes written before a rotation completed).
			var deks = new Dictionary<int, byte[]>();

			async Task<byte[]> ResolveDekAsync(int version)
			{
				if (deks.TryGetValue(version, out var cached))
					return cached;

				var keyRow = await _keyService.GetKeyByVersionAsync(context.DepartmentId, version);
				if (keyRow == null)
					return null;

				var unwrapped = await _keyWrappingProvider.UnwrapDataKeyAsync(context.DepartmentId, keyRow.WrappedKey, cancellationToken);
				deks[version] = unwrapped;
				return unwrapped;
			}

			try
			{
				return await RunNightAsync(context, isEncrypting: false,
					(spec, row, updates) => DecryptRowColumnAsync(ResolveDekAsync, context, spec, row, updates),
					cancellationToken);
			}
			catch (Exception ex) when (ex is CryptographicException || ex is InvalidOperationException)
			{
				Logging.LogException(ex, $"ADP engine decryption failed for department {context.DepartmentId}; failing closed.");
				return AdpMigrationNightResult.Failed("kms_unavailable");
			}
			finally
			{
				foreach (var dek in deks.Values)
					CryptographicOperations.ZeroMemory(dek);
			}
		}

		public async Task<bool> VerifyAsync(AdpMigrationNightContext context, CancellationToken cancellationToken)
		{
			var enveloped = context.Kind == DepartmentDataProtectionMigrationKind.Offboarding;

			foreach (var binding in Bindings)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var textResidue = await _bulkRepository.CountTextResidueAsync(binding, context.DepartmentId, enveloped);
				var binaryResidue = await _bulkRepository.CountBinaryResidueAsync(binding, context.DepartmentId, enveloped);
				var companionResidue = await _bulkRepository.CountCompanionResidueAsync(binding, context.DepartmentId, enveloped);

				if (textResidue > 0 || binaryResidue > 0 || companionResidue > 0)
				{
					// Value-free: counts and table only, never content.
					Logging.LogError($"ADP verification failed for department {context.DepartmentId} table {binding.TableName}: residue text={textResidue} binary={binaryResidue} companion={companionResidue}.");
					await MarkVerificationAsync(context, DepartmentDataProtectionVerificationState.Failed, complete: false, cancellationToken);
					return false;
				}
			}

			await MarkVerificationAsync(context, DepartmentDataProtectionVerificationState.Passed, complete: true, cancellationToken);
			return true;
		}

		private async Task<AdpMigrationNightResult> RunNightAsync(AdpMigrationNightContext context, bool isEncrypting,
			Func<AdpColumnSpec, AdpBulkFieldRow, Dictionary<string, object>, Task<ColumnOutcome>> processColumnAsync,
			CancellationToken cancellationToken)
		{
			long nightProcessed = 0;
			var batchSize = Math.Max(50, Config.DataProtectionConfig.MigrationBatchSize);

			foreach (var binding in Bindings)
			{
				var migrationRow = await _migrationRepository.GetActiveByDepartmentAndTableAsync(context.DepartmentId,
					context.Kind, binding.TableName);
				if (migrationRow == null)
				{
					migrationRow = await _migrationRepository.InsertAsync(new DepartmentDataProtectionMigration
					{
						DepartmentId = context.DepartmentId,
						Kind = (int)context.Kind,
						CatalogVersion = context.CatalogVersion,
						TargetKeyVersion = context.TargetKeyVersion,
						TargetTable = binding.TableName,
						RowsTotal = await _bulkRepository.CountRowsAsync(binding, context.DepartmentId),
						CorrelationId = context.CorrelationId,
						CreatedOn = DateTime.UtcNow,
						StartedOn = DateTime.UtcNow
					}, cancellationToken);
				}

				var cursor = migrationRow.Cursor;

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (DateTime.UtcNow >= context.WindowEndUtc)
						return AdpMigrationNightResult.WindowClosed(nightProcessed, await ComputePercentCompleteAsync(context));

					var batch = await _bulkRepository.GetBatchAsync(binding, context.DepartmentId, cursor, batchSize);
					if (batch.Count == 0)
						break;

					var updates = new List<AdpBulkRowUpdate>();
					long processedDelta = 0, alreadyDelta = 0, anomalousDelta = 0;

					foreach (var row in batch)
					{
						var setValues = new Dictionary<string, object>();
						var rowAlready = false;
						var rowAnomalous = false;

						foreach (var spec in binding.Columns)
						{
							ColumnOutcome outcome;
							try
							{
								outcome = await processColumnAsync(spec, row, setValues);
							}
							catch (CryptographicException)
							{
								// Foreign or corrupt envelope: never re-encrypted, never silently
								// skipped — the run halts at this row with a value-free code.
								await RecordRunErrorAsync(migrationRow, "foreign_envelope", cancellationToken);
								return AdpMigrationNightResult.Failed("foreign_envelope");
							}

							rowAlready |= outcome == ColumnOutcome.AlreadyInTargetState;
							rowAnomalous |= outcome == ColumnOutcome.Anomalous;
						}

						if (setValues.Count > 0)
						{
							if (!string.IsNullOrEmpty(binding.ProtectedMarkerColumn))
								setValues[binding.ProtectedMarkerColumn] = isEncrypting;

							updates.Add(new AdpBulkRowUpdate { RowKey = row.RowKey, SetValues = setValues });
							processedDelta++;
						}
						else if (rowAlready)
						{
							alreadyDelta++;
						}

						if (rowAnomalous)
							anomalousDelta++;
					}

					cursor = batch[batch.Count - 1].RowKey;
					await _bulkRepository.ApplyBatchAsync(binding, updates, migrationRow.DepartmentDataProtectionMigrationId,
						cursor, processedDelta, alreadyDelta, anomalousDelta, cancellationToken);
					nightProcessed += processedDelta;

					if (context.HeartbeatAsync != null)
						await context.HeartbeatAsync();
				}
			}

			return AdpMigrationNightResult.Completed(nightProcessed);
		}

		private enum ColumnOutcome
		{
			Skipped = 0,
			Changed = 1,
			AlreadyInTargetState = 2,
			Anomalous = 3
		}

		private Task<ColumnOutcome> EncryptRowColumn(byte[] dek, int keyVersion, AdpMigrationNightContext context,
			AdpColumnSpec spec, AdpBulkFieldRow row, Dictionary<string, object> setValues)
		{
			switch (spec.StorageKind)
			{
				case ProtectedFieldStorageKind.Text:
				{
					var value = row.Values.TryGetValue(spec.ColumnName, out var raw) ? raw as string : null;
					if (string.IsNullOrEmpty(value))
						return Task.FromResult(ColumnOutcome.Skipped);

					if (ProtectedDataEnvelope.HasEnvelopePrefix(value))
					{
						// Validate against THIS department's AAD; a mismatch throws (foreign envelope).
						_cryptoService.DecryptText(dek, value, context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
						return Task.FromResult(ColumnOutcome.AlreadyInTargetState);
					}

					setValues[spec.ColumnName] = _cryptoService.EncryptText(dek, keyVersion, value,
						context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
					return Task.FromResult(ColumnOutcome.Changed);
				}

				case ProtectedFieldStorageKind.Binary:
				{
					var value = row.Values.TryGetValue(spec.ColumnName, out var raw) ? raw as byte[] : null;
					if (value == null || value.Length == 0)
						return Task.FromResult(ColumnOutcome.Skipped);

					if (_cryptoService.IsBinaryEnveloped(value))
					{
						_cryptoService.DecryptBinary(dek, value, context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
						return Task.FromResult(ColumnOutcome.AlreadyInTargetState);
					}

					setValues[spec.ColumnName] = _cryptoService.EncryptBinary(dek, keyVersion, value,
						context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
					return Task.FromResult(ColumnOutcome.Changed);
				}

				case ProtectedFieldStorageKind.CompanionColumn:
				{
					var typed = row.Values.TryGetValue(spec.ColumnName, out var rawTyped) ? rawTyped : null;
					var companion = row.Values.TryGetValue(spec.CompanionColumn, out var rawCompanion) ? rawCompanion as string : null;

					if (typed != null)
					{
						var invariant = Convert.ToString(typed, CultureInfo.InvariantCulture);
						setValues[spec.CompanionColumn] = _cryptoService.EncryptText(dek, keyVersion, invariant,
							context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
						setValues[spec.ColumnName] = null;
						return Task.FromResult(ColumnOutcome.Changed);
					}

					if (!string.IsNullOrEmpty(companion))
					{
						_cryptoService.DecryptText(dek, companion, context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
						return Task.FromResult(ColumnOutcome.AlreadyInTargetState);
					}

					return Task.FromResult(ColumnOutcome.Skipped);
				}

				default:
					return Task.FromResult(ColumnOutcome.Skipped);
			}
		}

		private async Task<ColumnOutcome> DecryptRowColumnAsync(Func<int, Task<byte[]>> resolveDekAsync,
			AdpMigrationNightContext context, AdpColumnSpec spec, AdpBulkFieldRow row, Dictionary<string, object> setValues)
		{
			switch (spec.StorageKind)
			{
				case ProtectedFieldStorageKind.Text:
				{
					var value = row.Values.TryGetValue(spec.ColumnName, out var raw) ? raw as string : null;
					if (string.IsNullOrEmpty(value))
						return ColumnOutcome.Skipped;

					if (!ProtectedDataEnvelope.TryParse(value, out _, out var envelopeKeyVersion, out _))
					{
						// Plaintext reaching the decrypt path passes through untouched; the anomaly is
						// counted, never "decrypted" into garbage (plan section 19.4).
						return ProtectedDataEnvelope.HasEnvelopePrefix(value)
							? throw new CryptographicException("Corrupt envelope on the decrypt path.")
							: ColumnOutcome.Anomalous;
					}

					var dek = await resolveDekAsync(envelopeKeyVersion);
					if (dek == null)
						throw new InvalidOperationException($"No key row for envelope version {envelopeKeyVersion}.");

					setValues[spec.ColumnName] = _cryptoService.DecryptText(dek, value,
						context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
					return ColumnOutcome.Changed;
				}

				case ProtectedFieldStorageKind.Binary:
				{
					var value = row.Values.TryGetValue(spec.ColumnName, out var raw) ? raw as byte[] : null;
					if (value == null || value.Length == 0)
						return ColumnOutcome.Skipped;

					if (!_cryptoService.TryGetBinaryEnvelopeKeyVersion(value, out var envelopeKeyVersion))
						return _cryptoService.IsBinaryEnveloped(value)
							? throw new CryptographicException("Corrupt binary envelope on the decrypt path.")
							: ColumnOutcome.Anomalous;

					var dek = await resolveDekAsync(envelopeKeyVersion);
					if (dek == null)
						throw new InvalidOperationException($"No key row for envelope version {envelopeKeyVersion}.");

					setValues[spec.ColumnName] = _cryptoService.DecryptBinary(dek, value,
						context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
					return ColumnOutcome.Changed;
				}

				case ProtectedFieldStorageKind.CompanionColumn:
				{
					var companion = row.Values.TryGetValue(spec.CompanionColumn, out var rawCompanion) ? rawCompanion as string : null;
					if (string.IsNullOrEmpty(companion))
						return ColumnOutcome.Skipped;

					if (!ProtectedDataEnvelope.TryParse(companion, out _, out var envelopeKeyVersion, out _))
						return ColumnOutcome.Anomalous;

					var dek = await resolveDekAsync(envelopeKeyVersion);
					if (dek == null)
						throw new InvalidOperationException($"No key row for envelope version {envelopeKeyVersion}.");

					var plaintext = _cryptoService.DecryptText(dek, companion,
						context.DepartmentId, spec.FieldId, row.RowKey, context.CatalogVersion);
					setValues[spec.ColumnName] = decimal.Parse(plaintext, CultureInfo.InvariantCulture);
					setValues[spec.CompanionColumn] = null;
					return ColumnOutcome.Changed;
				}

				default:
					return ColumnOutcome.Skipped;
			}
		}

		private async Task RecordRunErrorAsync(DepartmentDataProtectionMigration migrationRow, string errorCode,
			CancellationToken cancellationToken)
		{
			try
			{
				migrationRow.LastErrorCode = errorCode;
				migrationRow.Attempts += 1;
				await _migrationRepository.SaveOrUpdateAsync(migrationRow, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "ADP engine could not record the run error code.");
			}
		}

		private async Task<int?> ComputePercentCompleteAsync(AdpMigrationNightContext context)
		{
			try
			{
				var rows = await _migrationRepository.GetActiveByDepartmentIdAsync(context.DepartmentId, context.Kind);
				var total = rows.Sum(r => r.RowsTotal);
				if (total <= 0)
					return null;

				var done = rows.Sum(r => r.RowsProcessed + r.RowsAlreadyProtected);
				return (int)Math.Min(100, done * 100 / total);
			}
			catch
			{
				return null;
			}
		}

		private async Task MarkVerificationAsync(AdpMigrationNightContext context,
			DepartmentDataProtectionVerificationState state, bool complete, CancellationToken cancellationToken)
		{
			var rows = await _migrationRepository.GetActiveByDepartmentIdAsync(context.DepartmentId, context.Kind);
			foreach (var row in rows)
			{
				row.VerificationState = (int)state;
				if (complete)
					row.CompletedOn = DateTime.UtcNow;
				await _migrationRepository.SaveOrUpdateAsync(row, cancellationToken);
			}
		}

	}
}
