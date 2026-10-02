using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Broker.Models;

namespace Resgrid.Web.Broker.Services
{
	/// <summary>
	/// The broker's field-crypto pipeline (ADP plan section 3.1 steps 8-9): validate the grant
	/// against the department's CURRENT policy epoch, refuse replayed request ids, unwrap the
	/// referenced DEK versions once per request, run AEAD field crypto with full AAD binding, zero
	/// key material, and emit a value-free audit line. Every failure is closed — a request-level
	/// fault processes NO items, and item-level faults return error codes, never partial values.
	/// Plaintext and ciphertext values are never logged.
	///
	/// Attended requests are also bound to the end user's live session (passkey workbook section 6.2):
	/// the calling host's session assertion is verified, must agree with the grant, is single use, and
	/// the session it names must still validate. Replay records live in a shared store, so every
	/// broker replica refuses a replay.
	/// </summary>
	public class BrokerOperationService
	{
		private readonly ILifetimeScope _rootScope;
		private readonly IProtectedDataGrantService _grantService;
		private readonly IProtectedFieldCryptoService _cryptoService;
		private readonly IKeyWrappingProvider _keyWrappingProvider;
		private readonly IAdpAuditRepository _audit;
		private readonly IBrokerSessionAssertionService _assertions;

		public BrokerOperationService(ILifetimeScope rootScope, IProtectedDataGrantService grantService,
			IProtectedFieldCryptoService cryptoService, IKeyWrappingProvider keyWrappingProvider,
			IAdpAuditRepository audit, IBrokerSessionAssertionService assertions)
		{
			_rootScope = rootScope;
			_grantService = grantService;
			_cryptoService = cryptoService;
			_keyWrappingProvider = keyWrappingProvider;
			_audit = audit;
			_assertions = assertions;
		}

		// Replayed ids are refused for at least this long; grants outlive it, so a replayed id can never slip
		// back in while its grant is still valid.
		private static TimeSpan ReplayWindow => TimeSpan.FromMinutes(Math.Max(15, Config.DataProtectionConfig.BrokerReplayWindowMinutes));

		public Task<ProtectedDataBrokerResult> DecryptAsync(BrokerFieldOperationRequest request, CancellationToken cancellationToken) =>
			ProcessAsync(request, decrypt: true, cancellationToken);

		public Task<ProtectedDataBrokerResult> EncryptAsync(BrokerFieldOperationRequest request, CancellationToken cancellationToken) =>
			ProcessAsync(request, decrypt: false, cancellationToken);

		/// <summary>
		/// The purpose-bound workload decrypt lane (RMS plan section 5.9.4, ADP plan 3.4): no grant, the workload key
		/// plus a purpose on the broker's allow-list (DataProtectionConfig.BrokerWorkloadPurposes) for a department
		/// that is actively protected. The application enforces the department's per-purpose acknowledgement before it
		/// calls; the broker records the purpose on every use and refuses anything it was not configured for.
		/// </summary>
		public Task<ProtectedDataBrokerResult> DecryptForWorkloadAsync(BrokerFieldOperationRequest request, string purpose,
			CancellationToken cancellationToken) =>
			ProcessAsync(request, decrypt: true, cancellationToken, workloadPurpose: NormalizePurpose(purpose));

		private static string NormalizePurpose(string purpose) =>
			string.IsNullOrWhiteSpace(purpose) ? string.Empty : purpose.Trim().ToLowerInvariant();

		/// <summary>True when the purpose is on the configured allow-list; an empty list or purpose allows nothing.</summary>
		public static bool IsAllowedWorkloadPurpose(string purpose)
		{
			if (string.IsNullOrEmpty(purpose))
				return false;
			return (Config.DataProtectionConfig.BrokerWorkloadPurposes ?? string.Empty)
				.Split(',')
				.Select(p => p.Trim().ToLowerInvariant())
				.Any(p => p.Length > 0 && p == purpose);
		}

		private async Task<ProtectedDataBrokerResult> ProcessAsync(BrokerFieldOperationRequest request, bool decrypt,
			CancellationToken cancellationToken, string workloadPurpose = null)
		{
			if (request == null || request.DepartmentId <= 0) return Fail("invalid_request");

			// Only an authenticated host reaches here (BrokerCredentialMiddleware); anything else is refused outright.
			var caller = request.Caller;
			if (caller == null) return Fail("lane_denied");

			var operation = workloadPurpose != null ? "workload-decrypt" : decrypt ? "decrypt" : "encrypt";
			var lane = Classify(request, decrypt, workloadPurpose);
			var layer = lane == null ? $"broker/{caller.Id}/refused" : caller.AuditLayer(lane.Value);
			try
			{
				await _audit.AppendAsync(new AdpAuditEvent { DepartmentId = request.DepartmentId, Layer = layer,
					Operation = operation, Outcome = "requested", CorrelationId = request.RequestId }, cancellationToken);

				// The lane gate runs before anything is consumed: no request id burned, no grant or receipt read,
				// no key touched. A refusal never falls through to another lane.
				var result = lane == null || !caller.Allows(lane.Value)
					? Fail("lane_denied")
					: await ProcessCoreAsync(request, decrypt, cancellationToken, workloadPurpose, layer);

				// A purpose the broker allows but this host was not granted is a cross-lane attempt too; a purpose off
				// the global list, or a department not actively protected, is an ordinary refusal.
				var laneRefused = result.ErrorCode == "lane_denied" ||
					result.ErrorCode == "workload_purpose_denied" && IsAllowedWorkloadPurpose(workloadPurpose) && !caller.AllowsPurpose(workloadPurpose);
				if (laneRefused)
					Logging.LogError($"ADP broker refused a cross-lane request: credential {caller.Id}, lane {(lane == null ? "none" : BrokerCredential.LaneName(lane.Value))}, " +
						$"operation {operation}, department {request.DepartmentId}{(workloadPurpose == null ? string.Empty : $", purpose {workloadPurpose}")}.");

				await _audit.AppendAsync(new AdpAuditEvent { DepartmentId = request.DepartmentId, Layer = layer,
					Operation = operation, Outcome = result.Success ? "completed" : laneRefused ? "lane-denied" : "denied",
					CorrelationId = request.RequestId }, cancellationToken);
				return result;
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception) { return Fail("audit_unavailable"); }
		}

		/// <summary>
		/// The one lane a request belongs to (passkey plan section 8.5): workload decrypt takes a purpose and never a
		/// token; decrypt with an adpr. receipt is the receipt lane; encrypt without a token is the workload lane; any other
		/// decrypt or encrypt is attended. Null means the request fits no lane.
		/// </summary>
		public static BrokerLane? Classify(BrokerFieldOperationRequest request, bool decrypt, string workloadPurpose)
		{
			var hasToken = !string.IsNullOrWhiteSpace(request.GrantToken);
			if (workloadPurpose != null)
				return hasToken ? null : BrokerLane.Workload;
			if (decrypt)
				return hasToken && request.GrantToken.StartsWith("adpr.", StringComparison.Ordinal) ? BrokerLane.Receipt : BrokerLane.Attended;
			return hasToken ? BrokerLane.Attended : BrokerLane.Workload;
		}

		private async Task<ProtectedDataBrokerResult> ProcessCoreAsync(BrokerFieldOperationRequest request, bool decrypt,
			CancellationToken cancellationToken, string workloadPurpose, string layer)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.RequestId) ||
				request.Items == null || request.Items.Count == 0)
				return Fail("invalid_request");

			// Workload lane: the purpose must be on the broker's allow-list AND granted to this host's credential, and
			// the gate runs before anything is consumed (no request id burned, no key touched).
			if (workloadPurpose != null && (!IsAllowedWorkloadPurpose(workloadPurpose) || !request.Caller.AllowsPurpose(workloadPurpose)))
				return Fail("workload_purpose_denied");

			var maxItems = Math.Max(1, Config.DataProtectionConfig.BrokerMaxItemsPerRequest);
			if (request.Items.Count > maxItems)
				return Fail("too_many_items");

			using var scope = _rootScope.BeginLifetimeScope();

			// Replay: a request id is single-use per department (plan section 2.2), across every broker replica.
			var claim = await TryClaimAsync(scope, $"adp-broker-request:{request.DepartmentId}:{request.RequestId}",
				BrokerReplayKind.RequestId, cancellationToken);
			if (claim != null)
				return Fail(claim);

			var policyRepository = scope.Resolve<IDepartmentDataProtectionPolicyRepository>();
			var keyService = scope.Resolve<IDepartmentKeyService>();

			// Current policy epoch straight from the database — a bump anywhere revokes here now.
			var policy = await policyRepository.GetByDepartmentIdAsync(request.DepartmentId);
			var currentEpoch = policy?.PolicyEpoch ?? 0;

			// A workload purpose only ever opens data of a department that is actively protected; an unenrolled,
			// enrolling or offboarding department has no acknowledged egress to honor.
			if (workloadPurpose != null && (policy == null ||
				policy.State != (int)DepartmentDataProtectionState.Enabled && policy.State != (int)DepartmentDataProtectionState.Rotating))
				return Fail("workload_purpose_denied");

			// DECRYPT always requires a valid attended grant. ENCRYPT has a workload lane (plan 3.4
			// "required protected submissions"): a request WITHOUT a grant — already past the
			// workload-key middleware — may encrypt, because encryption discloses nothing; system
			// integrations, text-to-call and workers must never be blocked from writing safely. A
			// grant that IS presented is still fully validated, so a stolen/stale token cannot be
			// laundered through the encrypt path either.
			ProtectedDataGrant grant = null;
			if (decrypt && workloadPurpose == null && request.GrantToken?.StartsWith("adpr.", StringComparison.Ordinal) == true)
			{
				var receipts = scope.Resolve<IAdpReleaseReceiptService>();
				if (policy == null || policy.State != (int)DepartmentDataProtectionState.Enabled && policy.State != (int)DepartmentDataProtectionState.Rotating ||
					await receipts.ConsumeAsync(request.GrantToken, request.DepartmentId, currentEpoch, request.Items, cancellationToken) == null)
					return Fail("grant_invalid");
			}
			else if (decrypt && workloadPurpose == null || !string.IsNullOrWhiteSpace(request.GrantToken))
			{
				var requiredScope = decrypt ? ProtectedDataGrantScopes.Read : ProtectedDataGrantScopes.Write;
				var outcome = _grantService.ValidateGrant(request.GrantToken, request.DepartmentId, currentEpoch,
					requiredScope, out grant);
				if (outcome != ProtectedDataGrantValidationOutcome.Valid)
					return Fail(MapGrantOutcome(outcome));

				// A failed session check never falls through to the workload lane.
				var sessionRefusal = await CheckAttendedSessionAsync(scope, request, grant, policy, decrypt, cancellationToken);
				if (sessionRefusal != null)
					return Fail(sessionRefusal);
			}

			await _audit.AppendAsync(new AdpAuditEvent { DepartmentId = request.DepartmentId, Layer = layer,
				Operation = decrypt ? "decrypt-authorized" : "encrypt-authorized", Outcome = "authorized",
				ActorId = grant?.UserId, ResourceId = grant?.GrantId, CorrelationId = request.RequestId,
				PolicyEpoch = currentEpoch }, cancellationToken);
			var result = new ProtectedDataBrokerResult { Success = true };
			var unwrappedKeys = new Dictionary<int, byte[]>();
			try
			{
				if (decrypt)
					await DecryptItemsAsync(request, keyService, unwrappedKeys, result, cancellationToken);
				else
					await EncryptItemsAsync(request, keyService, unwrappedKeys, result, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// KMS unreachable or another crypto-path fault: the WHOLE request fails closed.
				Logging.LogError($"ADP broker {(decrypt ? "decrypt" : "encrypt")} failed closed for department {request.DepartmentId}: {ex.GetType().Name}.");
				return Fail("kms_unavailable");
			}
			finally
			{
				foreach (var dek in unwrappedKeys.Values)
					CryptographicOperations.ZeroMemory(dek);
			}

			Audit(workloadPurpose != null ? "workload-decrypt" : decrypt ? "decrypt" : "encrypt", request, grant, result, layer, workloadPurpose);
			return result;
		}

		private async Task DecryptItemsAsync(BrokerFieldOperationRequest request, IDepartmentKeyService keyService,
			Dictionary<int, byte[]> unwrappedKeys, ProtectedDataBrokerResult result, CancellationToken cancellationToken)
		{
			foreach (var item in request.Items)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var itemResult = new ProtectedFieldOperationResult { FieldId = item?.FieldId, RowKey = item?.RowKey };
				result.Items.Add(itemResult);

				if (item == null || string.IsNullOrWhiteSpace(item.FieldId) || string.IsNullOrWhiteSpace(item.RowKey))
				{
					itemResult.ErrorCode = "invalid_item";
					continue;
				}

				if (item.IsBinary)
				{
					// rgdpb binary field: Value is base64 of the enveloped blob; the plaintext bytes
					// go back as base64 too.
					byte[] blob;
					try
					{
						blob = Convert.FromBase64String(item.Value ?? string.Empty);
					}
					catch (FormatException)
					{
						itemResult.ErrorCode = "invalid_item";
						continue;
					}

					if (!_cryptoService.TryGetBinaryEnvelopeKeyVersion(blob, out var binaryKeyVersion))
					{
						itemResult.ErrorCode = _cryptoService.IsBinaryEnveloped(blob)
							? "envelope_malformed"
							: "not_enveloped";
						continue;
					}

					var binaryDek = await ResolveKeyAsync(request.DepartmentId, binaryKeyVersion, keyService, unwrappedKeys, cancellationToken);
					if (binaryDek == null)
					{
						itemResult.ErrorCode = "key_unknown";
						continue;
					}

					try
					{
						itemResult.Value = Convert.ToBase64String(_cryptoService.DecryptBinary(binaryDek, blob,
							request.DepartmentId, item.FieldId, item.RowKey));
					}
					catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException)
					{
						itemResult.ErrorCode = "decrypt_failed";
					}

					continue;
				}

				if (!ProtectedDataEnvelope.TryParse(item.Value, out var formatVersion, out var keyVersion, out _) ||
					formatVersion > ProtectedDataEnvelope.CurrentVersion)
				{
					// Plaintext or corrupt: the broker never echoes the input back — the caller
					// already holds it, and an unparseable prefixed value must read as corrupt.
					itemResult.ErrorCode = ProtectedDataEnvelope.HasEnvelopePrefix(item.Value)
						? "envelope_malformed"
						: "not_enveloped";
					continue;
				}

				var dek = await ResolveKeyAsync(request.DepartmentId, keyVersion, keyService, unwrappedKeys, cancellationToken);
				if (dek == null)
				{
					itemResult.ErrorCode = "key_unknown";
					continue;
				}

				try
				{
					itemResult.Value = _cryptoService.DecryptText(dek, item.Value, request.DepartmentId,
						item.FieldId, item.RowKey);
				}
				catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException)
				{
					// AAD mismatch (foreign/moved ciphertext) or malformed payload — value-free.
					itemResult.ErrorCode = "decrypt_failed";
				}
			}
		}

		private async Task EncryptItemsAsync(BrokerFieldOperationRequest request, IDepartmentKeyService keyService,
			Dictionary<int, byte[]> unwrappedKeys, ProtectedDataBrokerResult result, CancellationToken cancellationToken)
		{
			var activeKey = await keyService.GetActiveKeyAsync(request.DepartmentId);
			if (activeKey == null)
			{
				result.Success = false;
				result.ErrorCode = "no_active_key";
				result.Items.Clear();
				return;
			}

			foreach (var item in request.Items)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var itemResult = new ProtectedFieldOperationResult { FieldId = item?.FieldId, RowKey = item?.RowKey };
				result.Items.Add(itemResult);

				if (item == null || string.IsNullOrWhiteSpace(item.FieldId) || string.IsNullOrWhiteSpace(item.RowKey) ||
					item.Value == null)
				{
					itemResult.ErrorCode = "invalid_item";
					continue;
				}

				if (item.IsBinary)
				{
					byte[] plaintextBytes;
					try
					{
						plaintextBytes = Convert.FromBase64String(item.Value);
					}
					catch (FormatException)
					{
						itemResult.ErrorCode = "invalid_item";
						continue;
					}

					if (_cryptoService.IsBinaryEnveloped(plaintextBytes))
					{
						itemResult.ErrorCode = "already_enveloped";
						continue;
					}

					var binaryDek = await ResolveKeyAsync(request.DepartmentId, activeKey.Version, keyService, unwrappedKeys, cancellationToken);
					if (binaryDek == null)
					{
						itemResult.ErrorCode = "key_unknown";
						continue;
					}

					try
					{
						itemResult.Value = Convert.ToBase64String(_cryptoService.EncryptBinary(binaryDek, activeKey.Version,
							plaintextBytes, request.DepartmentId, item.FieldId, item.RowKey));
					}
					catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException || ex is InvalidOperationException)
					{
						itemResult.ErrorCode = "encrypt_failed";
					}

					continue;
				}

				if (ProtectedDataEnvelope.HasEnvelopePrefix(item.Value))
				{
					// Double-encryption guard: enveloped input reaching an encrypt call is a caller
					// bug, never something to encrypt again.
					itemResult.ErrorCode = "already_enveloped";
					continue;
				}

				var dek = await ResolveKeyAsync(request.DepartmentId, activeKey.Version, keyService, unwrappedKeys, cancellationToken);
				if (dek == null)
				{
					itemResult.ErrorCode = "key_unknown";
					continue;
				}

				try
				{
					itemResult.Value = _cryptoService.EncryptText(dek, activeKey.Version, item.Value,
						request.DepartmentId, item.FieldId, item.RowKey);
				}
				catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException || ex is InvalidOperationException)
				{
					itemResult.ErrorCode = "encrypt_failed";
				}
			}
		}

		/// <summary>Unwraps each referenced key version once per request; null when the version is unknown.</summary>
		private async Task<byte[]> ResolveKeyAsync(int departmentId, int keyVersion, IDepartmentKeyService keyService,
			Dictionary<int, byte[]> unwrappedKeys, CancellationToken cancellationToken)
		{
			if (unwrappedKeys.TryGetValue(keyVersion, out var cached))
				return cached;

			var keyRow = await keyService.GetKeyByVersionAsync(departmentId, keyVersion);
			if (keyRow == null || string.IsNullOrWhiteSpace(keyRow.WrappedKey))
				return null;

			// The provider returns the DEK in pinned memory; ProcessAsync zeroes it in its finally.
			var dek = await _keyWrappingProvider.UnwrapDataKeyAsync(departmentId, keyRow.WrappedKey, cancellationToken);
			unwrappedKeys[keyVersion] = dek;
			return dek;
		}

		/// <summary>
		/// Binds an attended request to the end user's live session (passkey workbook section 6.2); null when it may
		/// proceed, otherwise the value-free refusal. A version 2 grant always needs a valid assertion. A version 1
		/// grant needs one only once <c>BrokerRequireSessionAssertion</c> is on, but an assertion that is presented
		/// must be valid either way.
		/// </summary>
		private async Task<string> CheckAttendedSessionAsync(ILifetimeScope scope, BrokerFieldOperationRequest request,
			ProtectedDataGrant grant, DepartmentDataProtectionPolicy policy, bool decrypt, CancellationToken cancellationToken)
		{
			var required = grant.Version >= 2 || Config.DataProtectionConfig.BrokerRequireSessionAssertion;
			if (string.IsNullOrWhiteSpace(request.SessionAssertion))
				return required ? "session_assertion_required" : null;

			var digest = BrokerRequestDigest.Compute(decrypt ? "decrypt" : "encrypt", request.DepartmentId, request.RequestId, request.Items);
			var outcome = _assertions.Validate(request.SessionAssertion, digest, out var assertion);
			if (outcome == BrokerSessionAssertionOutcome.NotConfigured)
				return required ? "session_assertion_unavailable" : null;
			if (outcome != BrokerSessionAssertionOutcome.Valid)
				return "session_assertion_invalid";

			var claim = await TryClaimAsync(scope, "adp-broker-assertion:" + assertion.AssertionId,
				BrokerReplayKind.SessionAssertion, cancellationToken);
			if (claim != null)
				return claim;

			// The assertion and the grant must describe the same user and department; a version 2 grant must also
			// match the asserted session, client, generation and lock version exactly.
			if (!string.Equals(assertion.UserId, grant.UserId, StringComparison.OrdinalIgnoreCase) ||
				assertion.DepartmentId != request.DepartmentId)
				return "grant_session_mismatch";

			var binding = await ProtectedGrantBinding.CheckAsync(grant, assertion.UserId, new ProtectedGrantSessionContext
			{
				SessionId = assertion.SessionId,
				ClientApplication = assertion.ClientApplication,
				AuthenticationGeneration = assertion.AuthenticationGeneration,
				SessionLockVersion = assertion.SessionLockVersion
			}, policy?.StepUpWindowMinutes, scope.ResolveOptional<IMfaCredentialStateService>(), cancellationToken);
			if (binding != ProtectedGrantBindingOutcome.Bound)
				return ProtectedGrantBinding.ErrorCode(binding);

			// The live session: active, unexpired, same generation, department membership, idle timeout, and the
			// credential cutoff checked with the issue time the calling host validated.
			SessionValidationResult validation;
			try
			{
				validation = await scope.Resolve<IUserSessionService>().ValidateAsync(new SessionPrincipalContext
				{
					UserId = assertion.UserId,
					SessionId = assertion.SessionId,
					AuthenticationGeneration = assertion.AuthenticationGeneration,
					DepartmentId = assertion.DepartmentId,
					CredentialIssuedOn = assertion.CredentialIssuedOnUtc
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogError($"ADP broker session validation failed closed for department {request.DepartmentId}: {ex.GetType().Name}.");
				return "session_validation_unavailable";
			}

			if (validation.IsLocked)
				return SharedSessionRules.LockedFailureCode;
			if (!validation.IsValid || validation.Session == null)
				return "session_revoked";
			if (validation.Session.ClientApplication != assertion.ClientApplication)
				return "grant_client_mismatch";

			// The assertion's lock version must still be the session's (plan section 12.5.3): an assertion or grant minted
			// before a lock stays unusable after the unlock, even though both still agree with each other.
			if (SharedSessionRules.LockVersionOf(validation.Session) != assertion.SessionLockVersion)
				return "grant_session_locked";

			return null;
		}

		/// <summary>Claims a replay key in the shared store: null when claimed, otherwise the refusal. Faults refuse.</summary>
		private async Task<string> TryClaimAsync(ILifetimeScope scope, string key, BrokerReplayKind kind, CancellationToken cancellationToken)
		{
			var digest = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
			var now = DateTime.UtcNow;
			try
			{
				var claimed = await scope.Resolve<IBrokerReplayRepository>().TryClaimAsync(digest, kind, now.Add(ReplayWindow), now,
					cancellationToken);
				return claimed ? null : "replayed_request";
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogError($"ADP broker replay store failed closed: {ex.GetType().Name}.");
				return "replay_store_unavailable";
			}
		}

		private static string MapGrantOutcome(ProtectedDataGrantValidationOutcome outcome)
		{
			switch (outcome)
			{
				case ProtectedDataGrantValidationOutcome.NotConfigured:
					return "grant_validation_unavailable";
				case ProtectedDataGrantValidationOutcome.Expired:
					return "grant_expired";
				case ProtectedDataGrantValidationOutcome.EpochRevoked:
					return "grant_revoked";
				case ProtectedDataGrantValidationOutcome.VersionUnsupported:
					return "grant_version_unsupported";
				default:
					return "grant_invalid";
			}
		}

		private static ProtectedDataBrokerResult Fail(string errorCode) =>
			new ProtectedDataBrokerResult { Success = false, ErrorCode = errorCode };

		/// <summary>Value-free audit line: identifiers and counts only, never field values.</summary>
		private static void Audit(string operation, BrokerFieldOperationRequest request, ProtectedDataGrant grant,
			ProtectedDataBrokerResult result, string layer, string workloadPurpose = null)
		{
			var failed = result.Items.Count(i => i.ErrorCode != null);
			var fields = string.Join(",", request.Items.Where(i => i?.FieldId != null).Select(i => i.FieldId).Distinct());
			var identity = grant == null ? (workloadPurpose == null ? "workload" : $"workload purpose {workloadPurpose}") : $"user {grant.UserId}, grant {grant.GrantId}";
			Logging.LogInfo($"ADP broker {operation} ({layer}): department {request.DepartmentId}, {identity}, request {request.RequestId}, items {result.Items.Count}, failed {failed}, fields [{fields}]");
		}
	}
}
