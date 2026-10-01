using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.IdentityModel.Tokens;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="ISsoBrokerService"/>
	public sealed class SsoBrokerService : ISsoBrokerService
	{
		private const string SamlRelayPrefix = "rgsso.";
		private const int MaxSecretLength = 128;
		private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

		private readonly ISsoLoginTransactionRepository _transactions;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IDepartmentsService _departments;
		private readonly IIdentityUserRepository _identityUsers;
		private readonly IEncryptionService _encryption;
		private readonly IOidcProviderClient _oidc;
		private readonly ISsoReturnTargetRegistry _returnTargets;
		private readonly IBrokerReplayRepository _replay;
		private readonly IMfaPolicyService _policy;
		private readonly TimeProvider _time;

		public SsoBrokerService(ISsoLoginTransactionRepository transactions, IDepartmentSsoService departmentSso, IDepartmentsService departments,
			IIdentityUserRepository identityUsers, IEncryptionService encryption, IOidcProviderClient oidc, ISsoReturnTargetRegistry returnTargets,
			IBrokerReplayRepository replay, IMfaPolicyService policy, TimeProvider time)
		{
			_policy = policy;
			_transactions = transactions;
			_departmentSso = departmentSso;
			_departments = departments;
			_identityUsers = identityUsers;
			_encryption = encryption;
			_oidc = oidc;
			_returnTargets = returnTargets;
			_replay = replay;
			_time = time;
		}

		private static TimeSpan Lifetime => TimeSpan.FromSeconds(Math.Max(60, SsoConfig.BrokeredTransactionLifetimeSeconds));
		private static TimeSpan CodeLifetime => TimeSpan.FromSeconds(Math.Max(10, SsoConfig.BrokeredCodeLifetimeSeconds));
		private static TimeSpan ReauthenticationMaxAge => TimeSpan.FromSeconds(Math.Max(30, SsoConfig.ReauthenticationMaxAgeSeconds));

		public bool IsEnabled => SsoConfig.BrokeredSsoEnabled && _returnTargets.IsReady;

		public string OidcRedirectUri => $"{SystemBehaviorConfig.ResgridApiBaseUrl?.TrimEnd('/')}{SsoConfig.OidcCallbackPath}";

		public bool SupportsBrokered(DepartmentSsoConfig config)
		{
			if (config == null || !config.IsEnabled)
				return false;

			return (SsoProviderType)config.SsoProviderType switch
			{
				SsoProviderType.Oidc => IsHttps(config.Authority) && !string.IsNullOrWhiteSpace(config.ClientId),
				SsoProviderType.Saml2 => IsHttps(config.IdpSsoUrl) && !string.IsNullOrWhiteSpace(config.EntityId) &&
					!string.IsNullOrWhiteSpace(config.AssertionConsumerServiceUrl) && !string.IsNullOrWhiteSpace(config.EncryptedIdpCertificate),
				_ => false
			};
		}

		public string DepartmentTokenFor(Department department) =>
			department == null ? null : _encryption.Encrypt($"{department.DepartmentId}:{department.Code}");

		public async Task<Department> ResolveDepartmentAsync(string departmentToken, string departmentCode, string username,
			CancellationToken cancellationToken = default)
		{
			if (!string.IsNullOrWhiteSpace(departmentToken))
			{
				try
				{
					var parts = _encryption.Decrypt(departmentToken).Split(':');
					if (parts.Length >= 2 && int.TryParse(parts[0], out var departmentId))
					{
						var department = await _departments.GetDepartmentByIdAsync(departmentId);
						if (department != null && string.Equals(department.Code, string.Join(":", parts.Skip(1)), StringComparison.Ordinal))
							return department;
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// An unreadable token is simply not a department; the other identifiers may still resolve one.
				}
			}

			if (!string.IsNullOrWhiteSpace(departmentCode))
				return await _departments.GetDepartmentByNameAsync(departmentCode);

			if (!string.IsNullOrWhiteSpace(username))
			{
				var user = await _identityUsers.GetByUserNameAsync(username) ?? await _identityUsers.GetByEmailAsync(username);
				if (user != null)
					return await _departments.GetDepartmentByUserIdAsync(user.Id);
			}

			return null;
		}

		// ── Begin ─────────────────────────────────────────────────────────────────────

		public async Task<SsoBeginResult> BeginAsync(SsoBeginRequest request, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled)
				return SsoBeginResult.Of(SsoBrokerOutcome.Unavailable);
			if (request == null || request.DepartmentId <= 0 || !IsBindable(request))
				return SsoBeginResult.Of(SsoBrokerOutcome.InvalidRequest);

			if (!_returnTargets.IsAllowed(request.ClientApplication, request.ReturnTarget))
				return SsoBeginResult.Of(SsoBrokerOutcome.ReturnTargetNotAllowed);
			if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal) || !IsS256Challenge(request.CodeChallenge) ||
				request.ClientState?.Length > 512 || request.Platform?.Length > 32)
				return SsoBeginResult.Of(SsoBrokerOutcome.InvalidRequest);

			try
			{
				var config = (await _departmentSso.GetSsoConfigsForDepartmentAsync(request.DepartmentId, cancellationToken))?.FirstOrDefault(c => c.IsEnabled);
				if (!SupportsBrokered(config))
					return SsoBeginResult.Of(SsoBrokerOutcome.Unavailable);

				// Which provider step-up mapping, if any, this round trip asks for (plan section 7.8).
				var mapping = await MappingToRequestAsync(request, config, cancellationToken);
				if (mapping == null && request.Purpose is SsoTransactionPurpose.StepUp or SsoTransactionPurpose.AdpStepUp or SsoTransactionPurpose.MappingTest)
					return SsoBeginResult.Of(SsoBrokerOutcome.Unavailable);

				var now = _time.GetUtcNow().UtcDateTime;
				var bound = request.Purpose != SsoTransactionPurpose.Login;
				var transaction = new SsoLoginTransaction
				{
					SsoLoginTransactionId = Guid.NewGuid().ToString(),
					Purpose = (int)request.Purpose,
					DepartmentId = request.DepartmentId,
					DepartmentSsoConfigId = config.DepartmentSsoConfigId,
					ProviderType = config.SsoProviderType,
					ClientApplication = (int)request.ClientApplication,
					Platform = request.Platform,
					SharedInstallation = request.SharedInstallation,
					ReturnTarget = request.ReturnTarget,
					ClientState = request.ClientState,
					CodeChallenge = request.CodeChallenge,
					Operation = request.Purpose == SsoTransactionPurpose.StepUp ? request.Operation : null,
					LoginTransactionId = request.Purpose == SsoTransactionPurpose.StepUp ? request.LoginTransactionId : null,
					FederatedMappingVersion = mapping == null ? null : config.FederatedMfaMappingVersion,
					SessionId = bound ? request.SessionId : null,
					ExpectedUserId = bound ? request.UserId : null,
					AuthenticationGeneration = bound ? request.AuthenticationGeneration : null,
					CreatedOnUtc = now,
					ExpiresOnUtc = now.Add(Lifetime),
					State = (int)SsoLoginTransactionState.Pending
				};

				// Reauthentication must be recent; a provider step-up must happen now, with MFA (max_age=0, ForceAuthn).
				var forceAuthn = request.Purpose is SsoTransactionPurpose.Reauthentication or SsoTransactionPurpose.StepUp or SsoTransactionPurpose.AdpStepUp or
					SsoTransactionPurpose.MappingTest;
				var maxAge = request.Purpose == SsoTransactionPurpose.Reauthentication ? (int)ReauthenticationMaxAge.TotalSeconds : 0;

				string authorizeUrl;
				if ((SsoProviderType)config.SsoProviderType == SsoProviderType.Oidc)
				{
					var metadata = await _oidc.GetMetadataAsync(config.Authority, cancellationToken: cancellationToken);
					if (metadata == null || !IsHttps(metadata.AuthorizationEndpoint))
						return SsoBeginResult.Of(SsoBrokerOutcome.ServiceUnavailable);

					var state = NewSecret();
					var nonce = NewSecret();
					var verifier = NewSecret();
					transaction.StateHash = Hash(state);
					transaction.NonceHash = Hash(nonce);
					transaction.EncryptedIdpCodeVerifier = _encryption.Encrypt(verifier);

					var query = new List<KeyValuePair<string, string>>
					{
						new("response_type", "code"),
						new("client_id", config.ClientId),
						new("redirect_uri", OidcRedirectUri),
						new("scope", "openid email profile"),
						new("state", state),
						new("nonce", nonce),
						new("code_challenge", S256(verifier)),
						new("code_challenge_method", "S256")
					};

					// A shared installation's operator signs in to the provider afresh too: its browser may still hold the last
					// operator's provider session, which an account chooser would let the next one pick (plan section 12.5.2).
					// The callback checks that the sign-in is fresh.
					if (forceAuthn || request.SharedInstallation)
					{
						query.Add(new("prompt", forceAuthn && request.SharedInstallation ? "login select_account" : "login"));
						query.Add(new("max_age", maxAge.ToString(System.Globalization.CultureInfo.InvariantCulture)));
					}

					if (mapping?.RequestAcrValues?.Count > 0)
						query.Add(new("acr_values", string.Join(" ", mapping.RequestAcrValues)));
					if (!string.IsNullOrWhiteSpace(mapping?.RequestClaims))
						query.Add(new("claims", mapping.RequestClaims));

					authorizeUrl = Append(metadata.AuthorizationEndpoint, query);
				}
				else
				{
					var relayState = SamlRelayPrefix + NewSecret();
					var requestId = "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
					transaction.StateHash = Hash(relayState);
					transaction.SamlRequestId = requestId;
					// SAML has no account chooser; on a shared installation the provider authenticates again instead.
					authorizeUrl = BuildSamlRedirect(config, requestId, relayState, now, forceAuthn || request.SharedInstallation, request.DepartmentCode,
						mapping?.RequestAuthnContextClassRefs);
					if (authorizeUrl == null)
						return SsoBeginResult.Of(SsoBrokerOutcome.ServiceUnavailable);
				}

				await _transactions.InsertAsync(transaction, cancellationToken);
				return new SsoBeginResult
				{
					Outcome = SsoBrokerOutcome.Succeeded,
					AuthorizeUrl = authorizeUrl,
					TransactionId = transaction.SsoLoginTransactionId,
					ExpiresInSeconds = (int)Lifetime.TotalSeconds
				};
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A brokered SSO sign-in could not be started.");
				return SsoBeginResult.Of(SsoBrokerOutcome.ServiceUnavailable);
			}
		}

		/// <summary>
		/// Each purpose is bound to what it serves: reauthentication, a mapping test and a Protected Data Grant step-up to the
		/// signed-in session; a step-up to the session and a named operation, or to a password sign-in's login transaction.
		/// </summary>
		private static bool IsBindable(SsoBeginRequest request)
		{
			var session = !string.IsNullOrWhiteSpace(request.SessionId) && !string.IsNullOrWhiteSpace(request.UserId) && request.AuthenticationGeneration != null;
			return request.Purpose switch
			{
				SsoTransactionPurpose.Login => true,
				SsoTransactionPurpose.Reauthentication or SsoTransactionPurpose.MappingTest or SsoTransactionPurpose.AdpStepUp => session,
				SsoTransactionPurpose.StepUp when !string.IsNullOrWhiteSpace(request.LoginTransactionId) =>
					!string.IsNullOrWhiteSpace(request.UserId) && request.Operation == SsoLoginTransaction.LoginOperation,
				// Account factors are never managed through provider step-up (plan section 7.6 row 14). A shared session's unlock
				// is bound to that session like any step-up.
				SsoTransactionPurpose.StepUp => session && ((MfaStepUpOperations.IsKnown(request.Operation) && request.Operation != MfaStepUpOperations.AccountSecurity) ||
					request.Operation == SsoLoginTransaction.SharedUnlockOperation),
				_ => false
			};
		}

		/// <summary>
		/// The mapping this round trip requests: always the tested one for a step-up the department accepts; the saved one
		/// (tested or not) for the managing member's test; for a sign-in, the tested one when the department accepts provider
		/// MFA for sign-in, so the provider's MFA can satisfy it with no Resgrid prompt. Null means no MFA is requested.
		/// </summary>
		private async Task<FederatedMfaMapping> MappingToRequestAsync(SsoBeginRequest request, DepartmentSsoConfig config, CancellationToken cancellationToken)
		{
			var mapping = FederatedMfaMapping.Parse(config.FederatedMfaMappingJson);
			if (mapping == null || FederatedMfaMapping.Validate(mapping, (SsoProviderType)config.SsoProviderType) != null)
				return null;

			switch (request.Purpose)
			{
				case SsoTransactionPurpose.MappingTest:
					return mapping;
				case SsoTransactionPurpose.StepUp:
				case SsoTransactionPurpose.Login:
					var scope = request.Purpose == SsoTransactionPurpose.Login || request.Operation is SsoLoginTransaction.LoginOperation or SsoLoginTransaction.SharedUnlockOperation
						? MfaMethodScope.Login
						: MfaStepUpOperations.ScopeFor(request.Operation);
					return FederatedMfaMapping.IsTested(config) &&
						await _policy.IsMethodAcceptedAsync(request.DepartmentId, scope, MfaEvidenceMethod.Federated, cancellationToken)
							? mapping
							: null;
				case SsoTransactionPurpose.AdpStepUp:
					// Protected data follows the department's ADP switch, AllowFederatedMfaForAdp (plan section 10.1).
					return FederatedMfaMapping.IsTested(config) &&
						await _policy.IsMethodAcceptedAsync(request.DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Federated, cancellationToken)
							? mapping
							: null;
				default:
					return null;
			}
		}

		// ── IdP callbacks ─────────────────────────────────────────────────────────────

		public async Task<SsoCallbackResult> CompleteOidcCallbackAsync(string state, string code, string error, string clientIpAddress,
			CancellationToken cancellationToken = default)
		{
			var (transaction, refusal) = await OpenForCallbackAsync(state, SsoProviderType.Oidc, cancellationToken);
			if (refusal != null)
				return refusal;

			try
			{
				// The IdP declined (the user cancelled or was refused); the app learns only that.
				if (!string.IsNullOrWhiteSpace(error))
					return await FailAsync(transaction, SsoBrokerOutcome.AccessDenied, "idp_error");
				if (string.IsNullOrWhiteSpace(code) || code.Length > 4096)
					return await FailAsync(transaction, SsoBrokerOutcome.InvalidRequest, "missing_code");

				var (config, department) = await ConfigForAsync(transaction, SsoProviderType.Oidc, cancellationToken);
				if (config == null)
					return await FailAsync(transaction, SsoBrokerOutcome.Unavailable, "config_changed");

				var metadata = await _oidc.GetMetadataAsync(config.Authority, cancellationToken: cancellationToken);
				if (metadata == null)
					return await FailAsync(transaction, SsoBrokerOutcome.ServiceUnavailable, "idp_metadata_unavailable");

				var form = new Dictionary<string, string>
				{
					["grant_type"] = "authorization_code",
					["code"] = code,
					["redirect_uri"] = OidcRedirectUri,
					["client_id"] = config.ClientId,
					["code_verifier"] = _encryption.Decrypt(transaction.EncryptedIdpCodeVerifier)
				};
				if (!string.IsNullOrWhiteSpace(config.EncryptedClientSecret))
					form["client_secret"] = _encryption.DecryptForDepartment(config.EncryptedClientSecret, department.DepartmentId, department.Code);

				var exchanged = await _oidc.ExchangeCodeAsync(metadata.TokenEndpoint, form, cancellationToken);
				if (string.IsNullOrWhiteSpace(exchanged?.IdToken))
					return await FailAsync(transaction, SsoBrokerOutcome.VerificationFailed, "code_exchange_failed");

				var (principal, token) = await ValidateIdTokenAsync(exchanged.IdToken, config, metadata, cancellationToken);
				if (principal == null || !NonceMatches(token, transaction.NonceHash) || !AuthorizedPartyMatches(token, config.ClientId))
					return await FailAsync(transaction, SsoBrokerOutcome.VerificationFailed, "id_token_invalid");

				var authTime = AuthTime(token);
				if (MustBeFresh(transaction) && !IsFresh(authTime))
					return await FailAsync(transaction, SsoBrokerOutcome.ReauthenticationNotFresh, "auth_time_not_fresh");

				// Every id_token is accepted once, until it expires (plan section 7.7.2 item 8).
				if (!await TryRecordIdTokenUseAsync(exchanged.IdToken, token.ValidTo, cancellationToken))
					return await FailAsync(transaction, SsoBrokerOutcome.VerificationFailed, "id_token_replayed");

				return await FinishAsync(transaction, principal, config, department, authTime, OidcSignals(token), clientIpAddress, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A brokered OIDC callback failed; the sign-in was refused.");
				return await FailAsync(transaction, SsoBrokerOutcome.ServiceUnavailable, "callback_error");
			}
		}

		public bool IsBrokeredRelayState(string relayState) =>
			!string.IsNullOrWhiteSpace(relayState) && relayState.StartsWith(SamlRelayPrefix, StringComparison.Ordinal) && relayState.Length <= MaxSecretLength;

		public async Task<SsoCallbackResult> CompleteSamlCallbackAsync(string relayState, string samlResponse, string clientIpAddress,
			CancellationToken cancellationToken = default)
		{
			if (!IsBrokeredRelayState(relayState))
				return new SsoCallbackResult { Outcome = SsoBrokerOutcome.TransactionInvalid };

			var (transaction, refusal) = await OpenForCallbackAsync(relayState, SsoProviderType.Saml2, cancellationToken);
			if (refusal != null)
				return refusal;

			try
			{
				var (config, department) = await ConfigForAsync(transaction, SsoProviderType.Saml2, cancellationToken);
				if (config == null)
					return await FailAsync(transaction, SsoBrokerOutcome.Unavailable, "config_changed");

				// Only a response to this transaction's AuthnRequest is accepted; unsolicited responses stay on the legacy relay.
				var assertion = await _departmentSso.ValidateBrokeredSamlResponseAsync(department.DepartmentId, samlResponse, department.Code,
					transaction.SamlRequestId, cancellationToken);
				if (assertion?.Principal == null)
					return await FailAsync(transaction, SsoBrokerOutcome.VerificationFailed, "saml_response_invalid");

				if (MustBeFresh(transaction) && !IsFresh(assertion.AuthenticatedAtUtc))
					return await FailAsync(transaction, SsoBrokerOutcome.ReauthenticationNotFresh, "authn_instant_not_fresh");

				return await FinishAsync(transaction, assertion.Principal, config, department, assertion.AuthenticatedAtUtc,
					new FederatedMfaSignals { AuthnContextClassRefs = assertion.AuthnContextClassRefs }, clientIpAddress, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A brokered SAML callback failed; the sign-in was refused.");
				return await FailAsync(transaction, SsoBrokerOutcome.ServiceUnavailable, "callback_error");
			}
		}

		// ── Redemption ────────────────────────────────────────────────────────────────

		public async Task<SsoRedemptionResult> RedeemAsync(string transactionId, string code, string codeVerifier, UserSessionClientApplication client,
			CancellationToken cancellationToken = default, params SsoTransactionPurpose[] purposes)
		{
			if (purposes == null || purposes.Length == 0)
				purposes = new[] { SsoTransactionPurpose.Login, SsoTransactionPurpose.Reauthentication };

			if (!IsEnabled)
				return SsoRedemptionResult.Of(SsoBrokerOutcome.Unavailable);
			if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 64 || string.IsNullOrWhiteSpace(code) || code.Length > MaxSecretLength ||
				!IsPkceVerifier(codeVerifier))
				return SsoRedemptionResult.Of(SsoBrokerOutcome.InvalidRequest);

			try
			{
				var transaction = await _transactions.GetAsync(transactionId, cancellationToken);
				if (transaction == null || transaction.ClientApplication != (int)client || !purposes.Contains(transaction.TransactionPurpose))
					return SsoRedemptionResult.Of(SsoBrokerOutcome.TransactionInvalid);

				var now = _time.GetUtcNow().UtcDateTime;
				switch (transaction.TransactionState)
				{
					case SsoLoginTransactionState.Redeemed:
						return SsoRedemptionResult.Of(SsoBrokerOutcome.AlreadyUsed);
					case SsoLoginTransactionState.Authenticated when transaction.CodeExpiresOnUtc <= now:
						return SsoRedemptionResult.Of(SsoBrokerOutcome.Expired);
					case SsoLoginTransactionState.Authenticated:
						break;
					default:
						return SsoRedemptionResult.Of(SsoBrokerOutcome.TransactionInvalid);
				}

				// PKCE: only the client that began the sign-in holds the verifier, so an intercepted code is useless.
				if (!FixedTimeEquals(S256(codeVerifier), transaction.CodeChallenge))
					return SsoRedemptionResult.Of(SsoBrokerOutcome.TransactionInvalid);

				if (!await _transactions.TryRedeemAsync(transaction.SsoLoginTransactionId, Hash(code), now, cancellationToken))
					return SsoRedemptionResult.Of(SsoBrokerOutcome.TransactionInvalid);

				transaction.State = (int)SsoLoginTransactionState.Redeemed;
				transaction.RedeemedOnUtc = now;
				return SsoRedemptionResult.Of(SsoBrokerOutcome.Succeeded, transaction);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A brokered SSO code could not be redeemed; the sign-in was refused.");
				return SsoRedemptionResult.Of(SsoBrokerOutcome.ServiceUnavailable);
			}
		}

		public async Task<bool> TryRecordIdTokenUseAsync(string idToken, DateTime expiresOnUtc, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(idToken))
				return false;

			var now = _time.GetUtcNow().UtcDateTime;
			var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("sso-id-token:" + idToken)));
			var expires = DateTime.SpecifyKind(expiresOnUtc, DateTimeKind.Utc) + ClockSkew;
			return await _replay.TryClaimAsync(key, BrokerReplayKind.IdToken, expires > now ? expires : now.Add(ClockSkew), now, cancellationToken);
		}

		// ── Steps ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// The pending transaction a callback's state belongs to. An unknown state has no trusted return target, so the
		/// callback shows an error instead of redirecting anywhere.
		/// </summary>
		private async Task<(SsoLoginTransaction Transaction, SsoCallbackResult Refusal)> OpenForCallbackAsync(string state, SsoProviderType provider,
			CancellationToken cancellationToken)
		{
			if (!IsEnabled)
				return (null, new SsoCallbackResult { Outcome = SsoBrokerOutcome.Unavailable });
			if (string.IsNullOrWhiteSpace(state) || state.Length > MaxSecretLength)
				return (null, new SsoCallbackResult { Outcome = SsoBrokerOutcome.TransactionInvalid });

			SsoLoginTransaction transaction;
			try
			{
				transaction = await _transactions.GetByStateHashAsync(Hash(state), cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Brokered SSO transaction lookup failed; the sign-in was refused.");
				return (null, new SsoCallbackResult { Outcome = SsoBrokerOutcome.ServiceUnavailable });
			}

			if (transaction == null || transaction.ProviderType != (int)provider)
				return (null, new SsoCallbackResult { Outcome = SsoBrokerOutcome.TransactionInvalid });

			// The state belongs to us, so its registered return target is trusted for reporting the refusal.
			if (transaction.TransactionState != SsoLoginTransactionState.Pending)
				return (null, new SsoCallbackResult { Outcome = SsoBrokerOutcome.AlreadyUsed, RedirectUrl = ErrorRedirect(transaction, SsoBrokerOutcome.AlreadyUsed) });
			if (transaction.ExpiresOnUtc <= _time.GetUtcNow().UtcDateTime)
				return (null, await FailAsync(transaction, SsoBrokerOutcome.Expired, "expired"));

			return (transaction, null);
		}

		private async Task<(DepartmentSsoConfig Config, Department Department)> ConfigForAsync(SsoLoginTransaction transaction, SsoProviderType provider,
			CancellationToken cancellationToken)
		{
			var config = await _departmentSso.GetSsoConfigForDepartmentAsync(transaction.DepartmentId, provider, cancellationToken);
			if (config == null || !config.IsEnabled || !string.Equals(config.DepartmentSsoConfigId, transaction.DepartmentSsoConfigId, StringComparison.Ordinal))
				return (null, null);

			var department = await _departments.GetDepartmentByIdAsync(transaction.DepartmentId);
			return department == null ? (null, null) : (config, department);
		}

		/// <summary>
		/// Resolves the Resgrid account and issues the one-time code.
		/// <list type="bullet">
		/// <item>A login links or provisions the account exactly as the legacy exchange does, under the department's SSO and
		/// IP rules; if the round trip asked for provider MFA and got it, the matched value rides along to redemption.</item>
		/// <item>Reauthentication and a step-up only look the link up, and must find the account that began them.</item>
		/// <item>A step-up and a mapping test need a provider sign-in after the transaction began, carrying a value the
		/// unchanged mapping counts as MFA (plan section 7.8 acceptance rules).</item>
		/// </list>
		/// </summary>
		private async Task<SsoCallbackResult> FinishAsync(SsoLoginTransaction transaction, ClaimsPrincipal principal, DepartmentSsoConfig config,
			Department department, DateTime? authenticatedAtUtc, FederatedMfaSignals signals, string clientIpAddress, CancellationToken cancellationToken)
		{
			var purpose = transaction.TransactionPurpose;
			string matched = null;
			if (transaction.FederatedMappingVersion != null && config.FederatedMfaMappingVersion == transaction.FederatedMappingVersion)
				matched = FederatedMfaMapping.Parse(config.FederatedMfaMappingJson)?.Match(signals);

			if (purpose is SsoTransactionPurpose.StepUp or SsoTransactionPurpose.AdpStepUp or SsoTransactionPurpose.MappingTest)
			{
				if (!AuthenticatedSince(authenticatedAtUtc, transaction.CreatedOnUtc))
					return await FailAsync(transaction, SsoBrokerOutcome.ReauthenticationNotFresh, "authentication_not_fresh");
				if (matched == null)
					return await FailAsync(transaction, SsoBrokerOutcome.FederatedNotSatisfied,
						config.FederatedMfaMappingVersion == transaction.FederatedMappingVersion ? "mfa_not_asserted" : "mapping_changed");
			}

			string userId;
			if (purpose is SsoTransactionPurpose.Reauthentication or SsoTransactionPurpose.StepUp or SsoTransactionPurpose.AdpStepUp)
			{
				userId = await _departmentSso.FindLinkedUserIdAsync(department.DepartmentId, principal, config, cancellationToken);
				if (userId == null || !string.Equals(userId, transaction.ExpectedUserId, StringComparison.OrdinalIgnoreCase))
					return await FailAsync(transaction, SsoBrokerOutcome.IdentityMismatch, "identity_mismatch");
			}
			else if (purpose == SsoTransactionPurpose.MappingTest)
			{
				// A test proves the mapping works at the provider, with whichever account the managing member signs in with;
				// it authenticates nobody to Resgrid.
				userId = transaction.ExpectedUserId;
			}
			else
			{
				var user = await _departmentSso.ProvisionOrLinkUserAsync(department.DepartmentId, principal, config, department.Code, cancellationToken);
				if (user == null)
					return await FailAsync(transaction, SsoBrokerOutcome.AccessDenied, "no_linked_user");

				// MFA is completed through the login transaction when the code is redeemed; RequireSso and IP rules apply now.
				var violation = await _departmentSso.EnforceSecurityPolicyAsync(department.DepartmentId, user.Id, clientIpAddress,
					mfaCompleted: true, loginViaSso: true, cancellationToken);
				if (!string.IsNullOrWhiteSpace(violation))
					return await FailAsync(transaction, SsoBrokerOutcome.AccessDenied, "policy_denied");

				userId = user.Id;
			}

			var now = _time.GetUtcNow().UtcDateTime;
			var code = NewSecret();
			// Evidence is never dated after it was received: a provider clock slightly ahead reads as now.
			var authenticatedOn = authenticatedAtUtc is { } at && at <= now ? at : now;
			if (!await _transactions.TryAuthenticateAsync(transaction.SsoLoginTransactionId, userId, authenticatedOn, matched, Hash(code), now.Add(CodeLifetime),
					now, cancellationToken))
				return new SsoCallbackResult { Outcome = SsoBrokerOutcome.AlreadyUsed, RedirectUrl = ErrorRedirect(transaction, SsoBrokerOutcome.AlreadyUsed) };

			return new SsoCallbackResult
			{
				Outcome = SsoBrokerOutcome.Succeeded,
				RedirectUrl = Redirect(transaction, "sso_code", code)
			};
		}

		/// <summary>The provider authenticated the user after the transaction began (bounded skew), never in the future.</summary>
		private bool AuthenticatedSince(DateTime? authenticatedAtUtc, DateTime createdOnUtc) =>
			authenticatedAtUtc is { } at && at >= createdOnUtc - ClockSkew && at <= _time.GetUtcNow().UtcDateTime + ClockSkew;

		/// <summary>OIDC MFA signals: <c>amr</c> and <c>acrs</c> may be a string or an array; <c>acr</c> is a string.</summary>
		private static FederatedMfaSignals OidcSignals(JwtSecurityToken token)
		{
			IReadOnlyCollection<string> Read(string name)
			{
				if (token == null || !token.Payload.TryGetValue(name, out var value) || value == null)
					return Array.Empty<string>();

				return value switch
				{
					string single => new[] { single },
					IEnumerable<object> many => many.Select(item => item?.ToString()).Where(item => !string.IsNullOrEmpty(item)).ToList(),
					System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } array =>
						array.EnumerateArray().Select(item => item.ToString()).Where(item => !string.IsNullOrEmpty(item)).ToList(),
					_ => new[] { value.ToString() }
				};
			}

			return new FederatedMfaSignals { Amr = Read("amr"), Acr = Read("acr"), Acrs = Read("acrs") };
		}

		private async Task<SsoCallbackResult> FailAsync(SsoLoginTransaction transaction, SsoBrokerOutcome outcome, string failureCode)
		{
			try
			{
				await _transactions.TryFailAsync(transaction.SsoLoginTransactionId, failureCode);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A failed brokered SSO transaction could not be closed.");
			}

			Logging.LogInfo($"Brokered SSO {SsoBrokerOutcomes.PurposeName(transaction.TransactionPurpose)} for department {transaction.DepartmentId} refused: {failureCode}.");
			return new SsoCallbackResult { Outcome = outcome, RedirectUrl = ErrorRedirect(transaction, outcome) };
		}

		private async Task<(ClaimsPrincipal Principal, JwtSecurityToken Token)> ValidateIdTokenAsync(string idToken, DepartmentSsoConfig config,
			OidcProviderMetadata metadata, CancellationToken cancellationToken)
		{
			var handler = new JwtSecurityTokenHandler();
			try
			{
				return Validate(handler, idToken, config, metadata);
			}
			catch (SecurityTokenSignatureKeyNotFoundException)
			{
				// The IdP may have rotated its keys since they were cached: refetch once and try again below.
			}
			catch (Exception ex) when (ex is SecurityTokenException || ex is ArgumentException)
			{
				Logging.LogDebug($"Brokered id_token rejected: {ex.GetType().Name}.");
				return default;
			}

			var refreshed = await _oidc.GetMetadataAsync(config.Authority, forceRefresh: true, cancellationToken);
			if (refreshed == null)
				return default;

			try
			{
				return Validate(handler, idToken, config, refreshed);
			}
			catch (Exception ex) when (ex is SecurityTokenException || ex is ArgumentException)
			{
				Logging.LogDebug($"Brokered id_token rejected after a key refresh: {ex.GetType().Name}.");
				return default;
			}
		}

		private static (ClaimsPrincipal, JwtSecurityToken) Validate(JwtSecurityTokenHandler handler, string idToken, DepartmentSsoConfig config,
			OidcProviderMetadata metadata)
		{
			var keys = new JsonWebKeySet(metadata.JwksJson).GetSigningKeys();
			var principal = handler.ValidateToken(idToken,
				DepartmentSsoService.BuildOidcValidationParameters(config.ClientId, metadata.Issuer, keys), out var validated);
			return validated is JwtSecurityToken jwt ? (principal, jwt) : default;
		}

		private static bool NonceMatches(JwtSecurityToken token, byte[] expectedHash) =>
			token != null && expectedHash != null && !string.IsNullOrWhiteSpace(token.Payload.Nonce) &&
			CryptographicOperations.FixedTimeEquals(Hash(token.Payload.Nonce), expectedHash);

		/// <summary>An id_token for several audiences must name this client as its authorized party (OIDC Core 3.1.3.7).</summary>
		private static bool AuthorizedPartyMatches(JwtSecurityToken token, string clientId)
		{
			var audiences = token.Audiences.ToList();
			var azp = token.Payload.Azp;
			return audiences.Count <= 1
				? azp == null || string.Equals(azp, clientId, StringComparison.Ordinal)
				: string.Equals(azp, clientId, StringComparison.Ordinal);
		}

		private static DateTime? AuthTime(JwtSecurityToken token)
		{
			if (token == null || !token.Payload.TryGetValue("auth_time", out var value))
				return null;

			try
			{
				return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)).UtcDateTime;
			}
			catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentOutOfRangeException)
			{
				return null;
			}
		}

		/// <summary>
		/// Reauthentication must be recent, and so must any round trip from a shared installation: an older provider sign-in
		/// there may be the last operator's session, still in the installation's browser (plan section 12.5.2).
		/// </summary>
		private static bool MustBeFresh(SsoLoginTransaction transaction) =>
			transaction.TransactionPurpose == SsoTransactionPurpose.Reauthentication || transaction.SharedInstallation;

		/// <summary>The IdP authenticated the user within the reauthentication window (and not in the future).</summary>
		private bool IsFresh(DateTime? authenticatedAtUtc)
		{
			if (authenticatedAtUtc == null)
				return false;

			var now = _time.GetUtcNow().UtcDateTime;
			return authenticatedAtUtc.Value >= now - ReauthenticationMaxAge - ClockSkew && authenticatedAtUtc.Value <= now + ClockSkew;
		}

		public string LegacySamlSignInUrl(DepartmentSsoConfig config, string departmentCode, string relayState, bool forceAuthn)
		{
			if (config == null || (SsoProviderType)config.SsoProviderType != SsoProviderType.Saml2 || !SupportsBrokered(config) ||
				string.IsNullOrWhiteSpace(relayState) || IsBrokeredRelayState(relayState))
				return null;

			var requestId = "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
			return BuildSamlRedirect(config, requestId, relayState, _time.GetUtcNow().UtcDateTime, forceAuthn, departmentCode);
		}

		// ── SAML AuthnRequest (HTTP-Redirect binding) ─────────────────────────────────

		private string BuildSamlRedirect(DepartmentSsoConfig config, string requestId, string relayState, DateTime now, bool forceAuthn, string departmentCode,
			IReadOnlyCollection<string> requestedAuthnContexts = null)
		{
			var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) };
			using var xml = new StringWriter();
			using (var writer = XmlWriter.Create(xml, settings))
			{
				const string protocol = "urn:oasis:names:tc:SAML:2.0:protocol";
				const string assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
				writer.WriteStartElement("samlp", "AuthnRequest", protocol);
				writer.WriteAttributeString("xmlns", "saml", null, assertion);
				writer.WriteAttributeString("ID", requestId);
				writer.WriteAttributeString("Version", "2.0");
				writer.WriteAttributeString("IssueInstant", XmlConvert.ToString(now, XmlDateTimeSerializationMode.Utc));
				writer.WriteAttributeString("Destination", config.IdpSsoUrl);
				writer.WriteAttributeString("AssertionConsumerServiceURL", config.AssertionConsumerServiceUrl);
				writer.WriteAttributeString("ProtocolBinding", "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST");
				if (forceAuthn)
					writer.WriteAttributeString("ForceAuthn", "true");
				writer.WriteElementString("saml", "Issuer", assertion, config.EntityId);

				// Provider step-up: ask for the authentication context the department's mapping names (plan section 7.8).
				if (requestedAuthnContexts?.Count > 0)
				{
					writer.WriteStartElement("samlp", "RequestedAuthnContext", protocol);
					writer.WriteAttributeString("Comparison", "exact");
					foreach (var context in requestedAuthnContexts)
						writer.WriteElementString("saml", "AuthnContextClassRef", assertion, context);
					writer.WriteEndElement();
				}

				writer.WriteEndElement();
			}

			using var deflated = new MemoryStream();
			using (var deflate = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
			{
				var bytes = Encoding.UTF8.GetBytes(xml.ToString());
				deflate.Write(bytes, 0, bytes.Length);
			}

			var query = $"SAMLRequest={Uri.EscapeDataString(Convert.ToBase64String(deflated.ToArray()))}&RelayState={Uri.EscapeDataString(relayState)}";

			// Signed when the department configured an SP signing key: the redirect binding signs the exact query string.
			if (!string.IsNullOrWhiteSpace(config.EncryptedSigningCertificate))
			{
				try
				{
					using var rsa = RSA.Create();
					rsa.ImportFromPem(_encryption.DecryptForDepartment(config.EncryptedSigningCertificate, config.DepartmentId, departmentCode));
					query += "&SigAlg=" + Uri.EscapeDataString(SignedXmlRsaSha256);
					var signature = rsa.SignData(Encoding.UTF8.GetBytes(query), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
					query += "&Signature=" + Uri.EscapeDataString(Convert.ToBase64String(signature));
				}
				catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException || ex is FormatException)
				{
					Logging.LogException(ex, "The SAML SP signing key could not be used; the AuthnRequest was not sent.");
					return null;
				}
			}

			return config.IdpSsoUrl + (config.IdpSsoUrl.Contains('?') ? "&" : "?") + query;
		}

		private const string SignedXmlRsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

		// ── Helpers ───────────────────────────────────────────────────────────────────

		private static string Redirect(SsoLoginTransaction transaction, string name, string value)
		{
			var url = new StringBuilder(transaction.ReturnTarget).Append('?').Append(name).Append('=').Append(Uri.EscapeDataString(value));
			if (!string.IsNullOrEmpty(transaction.ClientState))
				url.Append("&state=").Append(Uri.EscapeDataString(transaction.ClientState));
			return url.ToString();
		}

		private static string ErrorRedirect(SsoLoginTransaction transaction, SsoBrokerOutcome outcome) =>
			Redirect(transaction, "error", SsoBrokerOutcomes.ErrorCode(outcome) ?? "sso_failed");

		private static string Append(string endpoint, IEnumerable<KeyValuePair<string, string>> query) =>
			endpoint + (endpoint.Contains('?') ? "&" : "?") +
			string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

		private static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

		private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

		private static string S256(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

		private static bool FixedTimeEquals(string a, string b) =>
			a != null && b != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

		/// <summary>An S256 challenge is 32 bytes in base64url: 43 characters.</summary>
		private static bool IsS256Challenge(string value) => value is { Length: 43 } && value.All(IsBase64UrlCharacter);

		/// <summary>RFC 7636: 43 to 128 characters from the unreserved set.</summary>
		private static bool IsPkceVerifier(string value) =>
			value is { Length: >= 43 and <= 128 } && value.All(c => IsBase64UrlCharacter(c) || c == '.' || c == '~');

		private static bool IsBase64UrlCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';

		private static bool IsHttps(string value) =>
			Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
	}
}
