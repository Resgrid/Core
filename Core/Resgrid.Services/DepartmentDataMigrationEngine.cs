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

		/// <summary>
		/// The tables and columns THIS run touches. Enrollment, rotation and offboarding sweep the
		/// whole catalog; a CatalogUpgrade sweeps only the fields added since the department's pinned
		/// version, so it never re-reads a table it has nothing to do in and never re-encrypts an
		/// already-protected value.
		/// </summary>
		private IReadOnlyList<AdpTableBinding> BindingsFor(AdpMigrationNightContext context)
		{
			if (context.Kind != DepartmentDataProtectionMigrationKind.CatalogUpgrade)
				return Bindings;

			return AdpTableBindings.ForVersionRange(_fieldCatalog, context.FromCatalogVersion, context.CatalogVersion);
		}

		private readonly IDepartmentDataProtectionBulkRepository _bulkRepository;
		private readonly IDepartmentDataProtectionMigrationRepository _migrationRepository;
		private readonly IDepartmentKeyService _keyService;
		private readonly IKeyWrappingProvider _keyWrappingProvider;
		private readonly IProtectedFieldCryptoService _cryptoService;
		private readonly IProtectedFieldCatalog _fieldCatalog;

		public DepartmentDataMigrationEngine(IDepartmentDataProtectionBulkRepository bulkRepository,
			IDepartmentDataProtectionMigrationRepository migrationRepository, IDepartmentKeyService keyService,
			IKeyWrappingProvider keyWrappingProvider, IProtectedFieldCryptoService cryptoService,
			IProtectedFieldCatalog fieldCatalog)
		{
			_bulkRepository = bulkRepository;
			_migrationRepository = migrationRepository;
			_keyService = keyService;
			_keyWrappingProvider = keyWrappingProvider;
			_cryptoService = cryptoService;
			_fieldCatalog = fieldCatalog;
		}

		/// <summary>
		/// False where the key wrapping provider is the fail-closed placeholder (Web/API/worker
		/// hosts): the coordinator then SKIPS nights instead of opening a window that can only fail
		/// with kms_unavailable — queued departments wait for a host with a real KMS adapter (the
		/// Protected Data Broker) rather than being marked Failed by a host that can never succeed.
		/// </summary>
		public bool IsAvailable => !(_keyWrappingProvider is NotConfiguredKeyWrappingProvider);

		public async Task<AdpMigrationNightResult> RunEncryptionNightAsync(AdpMigrationNightContext context, CancellationToken cancellationToken)
		{
			var keyVersion = context.TargetKeyVersion ?? 0;
			var keyRow = keyVersion > 0
				? await _keyService.GetKeyByVersionAsync(context.DepartmentId, keyVersion)
				: await _keyService.GetActiveKeyAsync(context.DepartmentId);
			if (keyRow == null)
				return AdpMigrationNightResult.Failed("key_unavailable");

			// Rows already enveloped may reference an EARLIER key version (a run that failed after
			// encrypting part of the table, followed by a re-provision that minted a new version).
			// Validation of those envelopes must use the version that wrote them — validating with
			// the target DEK would read every such row as a foreign envelope and halt the run with
			// no retry able to clear it.
			var deks = new Dictionary<int, byte[]>();

			async Task<byte[]> ResolveDekAsync(int version)
			{
				if (deks.TryGetValue(version, out var cached))
					return cached;

				var versionRow = await _keyService.GetKeyByVersionAsync(context.DepartmentId, version);
				if (versionRow == null)
					return null;

				var unwrapped = await _keyWrappingProvider.UnwrapDataKeyAsync(context.DepartmentId, versionRow.WrappedKey, cancellationToken);
				deks[version] = unwrapped;
				return unwrapped;
			}

			try
			{
				deks[keyRow.Version] = await _keyWrappingProvider.UnwrapDataKeyAsync(context.DepartmentId, keyRow.WrappedKey, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ADP engine could not unwrap the DEK for department {context.DepartmentId}; failing closed.");
				return AdpMigrationNightResult.Failed("kms_unavailable");
			}

			try
			{
				var targetDek = deks[keyRow.Version];
				return await RunNightAsync(context, isEncrypting: true,
					(spec, row, updates) => EncryptRowColumnAsync(ResolveDekAsync, targetDek, keyRow.Version, context, spec, row, updates),
					cancellationToken);
			}
			finally
			{
				foreach (var dek in deks.Values)
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

			foreach (var binding in BindingsFor(context))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var textResidue = await _bulkRepository.CountTextResidueAsync(binding, context.DepartmentId, enveloped, cancellationToken);
				var binaryResidue = await _bulkRepository.CountBinaryResidueAsync(binding, context.DepartmentId, enveloped, cancellationToken);
				var companionResidue = await _bulkRepository.CountCompanionResidueAsync(binding, context.DepartmentId, enveloped, cancellationToken);

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

			foreach (var binding in BindingsFor(context))
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
						RowsTotal = await _bulkRepository.CountRowsAsync(binding, context.DepartmentId, cancellationToken),
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

					var batch = await _bulkRepository.GetBatchAsync(binding, context.DepartmentId, cursor, batchSize, cancellationToken);
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

		private async Task<ColumnOutcome> EncryptRowColumnAsync(Func<int, Task<byte[]>> resolveDekAsync,
			byte[] targetDek, int keyVersion, AdpMigrationNightContext context,
			AdpColumnSpec spec, AdpBulkFieldRow row, Dictionary<string, object> setValues)
		{
			// Resolves the DEK for the key version an EXISTING envelope references; an unparseable
			// header or an unknown version reads as corrupt/foreign and halts the run (fail closed).
			async Task<byte[]> ValidationDekForTextAsync(string envelope)
			{
				if (!ProtectedDataEnvelope.TryParse(envelope, out _, out var envelopeKeyVersion, out _))
					throw new CryptographicException("Prefixed value is not a parseable ADP envelope; treating as corrupt.");

				var validationDek = envelopeKeyVersion == keyVersion ? targetDek : await resolveDekAsync(envelopeKeyVersion);
				if (validationDek == null)
					throw new CryptographicException("Envelope references an unknown department key version.");

				return validationDek;
			}

			switch (spec.StorageKind)
			{
				case ProtectedFieldStorageKind.Text:
				{
					var value = row.Values.TryGetValue(spec.ColumnName, out var raw) ? raw as string : null;
					if (string.IsNullOrEmpty(value))
						return ColumnOutcome.Skipped;

					if (ProtectedDataEnvelope.HasEnvelopePrefix(value))
					{
						// Validate against THIS department's AAD with the key version that wrote the
						// envelope; a mismatch throws (foreign envelope).
						var validationDek = await ValidationDekForTextAsync(value);
						_cryptoService.DecryptText(validationDek, value, context.DepartmentId, spec.FieldId, row.RowKey);
						return ColumnOutcome.AlreadyInTargetState;
					}

					setValues[spec.ColumnName] = _cryptoService.EncryptText(targetDek, keyVersion, value,
						context.DepartmentId, spec.FieldId, row.RowKey);
					return ColumnOutcome.Changed;
				}

				case ProtectedFieldStorageKind.Binary:
				{
					var value = row.Values.TryGetValue(spec.ColumnName, out var raw) ? raw as byte[] : null;
					if (value == null || value.Length == 0)
						return ColumnOutcome.Skipped;

					if (_cryptoService.IsBinaryEnveloped(value))
					{
						if (!_cryptoService.TryGetBinaryEnvelopeKeyVersion(value, out var envelopeKeyVersion))
							throw new CryptographicException("Prefixed blob is not a parseable rgdpb envelope; treating as corrupt.");

						var validationDek = envelopeKeyVersion == keyVersion ? targetDek : await resolveDekAsync(envelopeKeyVersion);
						if (validationDek == null)
							throw new CryptographicException("Envelope references an unknown department key version.");

						_cryptoService.DecryptBinary(validationDek, value, context.DepartmentId, spec.FieldId, row.RowKey);
						return ColumnOutcome.AlreadyInTargetState;
					}

					setValues[spec.ColumnName] = _cryptoService.EncryptBinary(targetDek, keyVersion, value,
						context.DepartmentId, spec.FieldId, row.RowKey);
					return ColumnOutcome.Changed;
				}

				case ProtectedFieldStorageKind.CompanionColumn:
				{
					var typed = row.Values.TryGetValue(spec.ColumnName, out var rawTyped) ? rawTyped : null;
					var companion = row.Values.TryGetValue(spec.CompanionColumn, out var rawCompanion) ? rawCompanion as string : null;

					if (typed != null)
					{
						var invariant = Convert.ToString(typed, CultureInfo.InvariantCulture);
						setValues[spec.CompanionColumn] = _cryptoService.EncryptText(targetDek, keyVersion, invariant,
							context.DepartmentId, spec.FieldId, row.RowKey);
						setValues[spec.ColumnName] = null;
						return ColumnOutcome.Changed;
					}

					if (!string.IsNullOrEmpty(companion))
					{
						var validationDek = await ValidationDekForTextAsync(companion);
						_cryptoService.DecryptText(validationDek, companion, context.DepartmentId, spec.FieldId, row.RowKey);
						return ColumnOutcome.AlreadyInTargetState;
					}

					return ColumnOutcome.Skipped;
				}

				default:
					return ColumnOutcome.Skipped;
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
						context.DepartmentId, spec.FieldId, row.RowKey);
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
						context.DepartmentId, spec.FieldId, row.RowKey);
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
						context.DepartmentId, spec.FieldId, row.RowKey);
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
			catch (Exception ex)
			{
				// Progress is advisory; the night result stands either way — but leave a trace.
				Logging.LogException(ex, $"ADP engine could not compute percent complete for department {context.DepartmentId}.");
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
