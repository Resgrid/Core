using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Caching.Memory;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
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
	/// </summary>
	public class BrokerOperationService
	{
		// Replayed request ids are refused for this long; grants outlive it, so a replayed id can
		// never slip back in while its grant is still valid.
		private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(15);

		private readonly ILifetimeScope _rootScope;
		private readonly IProtectedDataGrantService _grantService;
		private readonly IProtectedFieldCryptoService _cryptoService;
		private readonly IKeyWrappingProvider _keyWrappingProvider;
		private readonly IMemoryCache _replayCache;

		public BrokerOperationService(ILifetimeScope rootScope, IProtectedDataGrantService grantService,
			IProtectedFieldCryptoService cryptoService, IKeyWrappingProvider keyWrappingProvider,
			IMemoryCache replayCache)
		{
			_rootScope = rootScope;
			_grantService = grantService;
			_cryptoService = cryptoService;
			_keyWrappingProvider = keyWrappingProvider;
			_replayCache = replayCache;
		}

		public Task<ProtectedDataBrokerResult> DecryptAsync(BrokerFieldOperationRequest request, CancellationToken cancellationToken) =>
			ProcessAsync(request, decrypt: true, cancellationToken);

		public Task<ProtectedDataBrokerResult> EncryptAsync(BrokerFieldOperationRequest request, CancellationToken cancellationToken) =>
			ProcessAsync(request, decrypt: false, cancellationToken);

		private async Task<ProtectedDataBrokerResult> ProcessAsync(BrokerFieldOperationRequest request, bool decrypt,
			CancellationToken cancellationToken)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.RequestId) ||
				request.Items == null || request.Items.Count == 0)
				return Fail("invalid_request");

			var maxItems = Math.Max(1, Config.DataProtectionConfig.BrokerMaxItemsPerRequest);
			if (request.Items.Count > maxItems)
				return Fail("too_many_items");

			// Replay: a request id is single-use per department (plan section 2.2).
			var replayKey = $"adp-broker-request:{request.DepartmentId}:{request.RequestId}";
			if (!TryClaimRequestId(replayKey))
				return Fail("replayed_request");

			using var scope = _rootScope.BeginLifetimeScope();
			var policyRepository = scope.Resolve<IDepartmentDataProtectionPolicyRepository>();
			var keyService = scope.Resolve<IDepartmentKeyService>();

			// Current policy epoch straight from the database — a bump anywhere revokes here now.
			var policy = await policyRepository.GetByDepartmentIdAsync(request.DepartmentId);
			var currentEpoch = policy?.PolicyEpoch ?? 0;

			var requiredScope = decrypt ? ProtectedDataGrantScopes.Read : ProtectedDataGrantScopes.Write;
			var outcome = _grantService.ValidateGrant(request.GrantToken, request.DepartmentId, currentEpoch,
				requiredScope, out var grant);
			if (outcome != ProtectedDataGrantValidationOutcome.Valid)
				return Fail(MapGrantOutcome(outcome));

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

			Audit(decrypt ? "decrypt" : "encrypt", request, grant, result);
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
						item.FieldId, item.RowKey, item.CatalogVersion);
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
						request.DepartmentId, item.FieldId, item.RowKey, item.CatalogVersion);
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

		private bool TryClaimRequestId(string replayKey)
		{
			lock (_replayCache)
			{
				if (_replayCache.TryGetValue(replayKey, out _))
					return false;

				_replayCache.Set(replayKey, true, ReplayWindow);
				return true;
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
				default:
					return "grant_invalid";
			}
		}

		private static ProtectedDataBrokerResult Fail(string errorCode) =>
			new ProtectedDataBrokerResult { Success = false, ErrorCode = errorCode };

		/// <summary>Value-free audit line: identifiers and counts only, never field values.</summary>
		private static void Audit(string operation, BrokerFieldOperationRequest request, ProtectedDataGrant grant,
			ProtectedDataBrokerResult result)
		{
			var failed = result.Items.Count(i => i.ErrorCode != null);
			var fields = string.Join(",", request.Items.Where(i => i?.FieldId != null).Select(i => i.FieldId).Distinct());
			Logging.LogInfo($"ADP broker {operation}: department {request.DepartmentId}, user {grant.UserId}, grant {grant.GrantId}, request {request.RequestId}, items {result.Items.Count}, failed {failed}, fields [{fields}]");
		}
	}
}
