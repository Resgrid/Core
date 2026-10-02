using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Protected Data Grant issuance/validation (ADP plan section 3). ES256 compact JWS with the
	/// section 3.2 claims; the algorithm is pinned — a token presenting any other algorithm is
	/// invalid regardless of its signature. Issuance requires the signing PFX (identity tier only);
	/// validation requires only the public certificate (broker and API hosts). Everything fails
	/// closed: missing key material, parse faults, wrong department, stale policy epoch, or a
	/// missing scope all deny. The service never logs tokens or claims values beyond identifiers.
	/// </summary>
	public class ProtectedDataGrantService : IProtectedDataGrantService
	{
		private const string DepartmentClaim = "dept";
		private const string ClientAppClaim = "client_app";
		private const string PolicyEpochClaim = "policy_epoch";
		private const string MfaAtClaim = "mfa_at";
		private const string ScopeClaim = "scope";
		private const string AmrClaim = "amr";

		// Version 2 claims (passkey plan section 8.2, workbook section 7.5).
		private const string VersionClaim = "grant_ver";
		private const string MfaMethodClaim = "mfa_method";
		private const string MfaCredentialClaim = "mfa_credential_id";
		private const string MfaStateVersionClaim = "mfa_state_ver";
		private const string StepUpExemptClaim = "step_up_exempt";
		private const string SessionLockVersionClaim = "session_lock_version";
		private const string AuthenticationGenerationClaim = "auth_gen";

		// amr for version 2: the first factor (pwd or fed), otp for TOTP, and mfa whenever a second factor was verified.
		// Nothing else: Resgrid cannot establish hwk, face or fpt, so a grant claiming them is malformed.
		private static readonly HashSet<string> VersionTwoAmr = new(StringComparer.Ordinal) { "pwd", "fed", "otp", "mfa" };

		private static readonly JwtSecurityTokenHandler TokenHandler = new JwtSecurityTokenHandler();

		// Lazy<T> with ExecutionAndPublication provides the safe publication a hand-rolled
		// flag+lock does not: a thread that observes the initialized state is guaranteed to observe
		// the certificate write too (the flag/field pattern could transiently read null on weakly
		// ordered CPUs and mis-report NotConfigured). Load failures log once and cache null — the
		// factories never throw, so no exception is cached either.
		private readonly Lazy<X509Certificate2> _signingCertificate;
		private readonly Lazy<X509Certificate2> _validationCertificate;

		public ProtectedDataGrantService()
			: this(LoadSigningCertificateFromConfig, LoadValidationCertificateFromConfig)
		{
		}

		/// <summary>Test seam: supply certificates directly instead of loading from configured paths.</summary>
		public ProtectedDataGrantService(Func<X509Certificate2> signingCertificateLoader,
			Func<X509Certificate2> validationCertificateLoader)
		{
			if (signingCertificateLoader == null)
				throw new ArgumentNullException(nameof(signingCertificateLoader));
			if (validationCertificateLoader == null)
				throw new ArgumentNullException(nameof(validationCertificateLoader));

			_signingCertificate = new Lazy<X509Certificate2>(
				() => LoadSigningCertificateSafe(signingCertificateLoader),
				System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
			_validationCertificate = new Lazy<X509Certificate2>(
				() => LoadValidationCertificateSafe(validationCertificateLoader),
				System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public bool CanIssueGrants => GetSigningCertificate() != null;

		public bool CanValidateGrants => GetValidationCertificate() != null;

		public ProtectedDataGrantIssueResult IssueGrant(ProtectedDataGrantIssueRequest request)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			if (string.IsNullOrWhiteSpace(request.UserId))
				throw new ArgumentException("A grant requires a user id.", nameof(request));
			if (request.DepartmentId <= 0)
				throw new ArgumentException("A grant requires exactly one department.", nameof(request));
			if (request.Scopes == null || request.Scopes.Count == 0 || request.Scopes.Any(string.IsNullOrWhiteSpace))
				throw new ArgumentException("A grant requires at least one non-empty scope.", nameof(request));
			if (request.PolicyEpoch < 0)
				throw new ArgumentException("Policy epoch cannot be negative.", nameof(request));
			if (request.Version != 1 && request.Version != 2)
				throw new ArgumentException("Only grant versions 1 and 2 can be issued.", nameof(request));
			if (request.Version == 2)
				ValidateVersionTwoRequest(request);

			var signingCertificate = GetSigningCertificate();
			if (signingCertificate == null)
				throw new InvalidOperationException(
					"Protected Data Grant signing is not configured on this host. Grants are issued only by the identity tier (check CanIssueGrants before calling).");

			// Absolute lifetime: floor 1 minute, ceiling the operator maximum (plan section 3.3).
			var ceiling = Math.Max(1, Config.DataProtectionConfig.StepUpMaximumMinutes);
			var windowMinutes = Math.Min(Math.Max(1, request.WindowMinutes), ceiling);

			var now = DateTime.UtcNow;
			var expires = now.AddMinutes(windowMinutes);
			var grantId = Guid.NewGuid().ToString("N");

			if (request.Version == 2)
				return IssueVersionTwo(request, signingCertificate, now, expires, grantId);

			var mfaAt = request.MfaAtUtc == default ? now : request.MfaAtUtc;

			var claims = new List<Claim>
			{
				new Claim(JwtRegisteredClaimNames.Sub, request.UserId),
				new Claim(JwtRegisteredClaimNames.Jti, grantId),
				new Claim(DepartmentClaim, request.DepartmentId.ToString(), ClaimValueTypes.Integer32),
				new Claim(ClientAppClaim, request.ClientApp.ToString(), ClaimValueTypes.Integer32),
				new Claim(PolicyEpochClaim, request.PolicyEpoch.ToString(), ClaimValueTypes.Integer64),
				new Claim(MfaAtClaim, ToUnixSeconds(mfaAt).ToString(), ClaimValueTypes.Integer64),

				// amr states honestly how this grant was authenticated. An exempted client produced no
				// second factor, so claiming "otp" would put a lie in the audit trail of exactly the
				// grants an auditor is most likely to be asking about.
				new Claim(AmrClaim, request.StepUpExempt ? "pwd" : "otp"),
				new Claim(ScopeClaim, string.Join(" ", request.Scopes))
			};

			if (!string.IsNullOrWhiteSpace(request.SessionId))
				claims.Add(new Claim(Model.Security.SessionClaimTypes.SessionId, request.SessionId));

			var ecdsa = signingCertificate.GetECDsaPrivateKey();
			if (ecdsa == null)
				throw new InvalidOperationException("The grant signing certificate does not carry an ECDSA private key (ES256 is required).");

			var credentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa), SecurityAlgorithms.EcdsaSha256);
			var token = new JwtSecurityToken(
				issuer: Config.DataProtectionConfig.GrantIssuer,
				audience: Config.DataProtectionConfig.GrantAudience,
				claims: claims,
				notBefore: now,
				expires: expires,
				signingCredentials: credentials);
			token.Payload[JwtRegisteredClaimNames.Iat] = ToUnixSeconds(now);

			return new ProtectedDataGrantIssueResult
			{
				GrantId = grantId,
				Token = TokenHandler.WriteToken(token),
				ExpiresOnUtc = expires
			};
		}

		public ProtectedDataGrantValidationOutcome ValidateGrant(string token, int expectedDepartmentId,
			long currentPolicyEpoch, string requiredScope, out ProtectedDataGrant grant, DateTime? utcNow = null)
		{
			grant = null;

			var validationCertificate = GetValidationCertificate();
			if (validationCertificate == null)
				return ProtectedDataGrantValidationOutcome.NotConfigured;

			if (string.IsNullOrWhiteSpace(token) || expectedDepartmentId <= 0)
				return ProtectedDataGrantValidationOutcome.Invalid;

			ClaimsPrincipal principal;
			JwtSecurityToken parsedToken;
			try
			{
				var ecdsa = validationCertificate.GetECDsaPublicKey();
				if (ecdsa == null)
					return ProtectedDataGrantValidationOutcome.NotConfigured;

				// Lifetime is checked manually below against the caller-supplied clock (bounded
				// skew, deterministic tests); everything cryptographic is checked here with the
				// algorithm pinned to ES256 — "alg" in the token buys an attacker nothing.
				var parameters = new TokenValidationParameters
				{
					ValidIssuer = Config.DataProtectionConfig.GrantIssuer,
					ValidAudience = Config.DataProtectionConfig.GrantAudience,
					IssuerSigningKey = new ECDsaSecurityKey(ecdsa),
					ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateIssuerSigningKey = true,
					ValidateLifetime = false,
					RequireExpirationTime = true,
					RequireSignedTokens = true
				};

				principal = TokenHandler.ValidateToken(token, parameters, out var validated);
				parsedToken = (JwtSecurityToken)validated;
			}
			catch (Exception)
			{
				// Malformed, wrong algorithm, wrong issuer/audience, or bad signature — all one
				// value-free outcome; the distinction never reaches a caller.
				return ProtectedDataGrantValidationOutcome.Invalid;
			}

			var now = utcNow ?? DateTime.UtcNow;
			var skew = TimeSpan.FromSeconds(Math.Max(0, Config.DataProtectionConfig.GrantClockSkewSeconds));

			if (parsedToken.ValidTo == DateTime.MinValue || now > parsedToken.ValidTo.Add(skew))
				return ProtectedDataGrantValidationOutcome.Expired;
			if (parsedToken.ValidFrom != DateTime.MinValue && now < parsedToken.ValidFrom.Subtract(skew))
				return ProtectedDataGrantValidationOutcome.Invalid;

			// A grant without grant_ver is version 1. Any version this build does not read is refused outright, so a
			// future contract can never be half-understood by an older reader.
			var version = 1;
			if (parsedToken.Payload.ContainsKey(VersionClaim))
			{
				if (!TryGetInt64(parsedToken.Payload, VersionClaim, out var declaredVersion))
					return ProtectedDataGrantValidationOutcome.Invalid;
				if (declaredVersion != 2)
					return ProtectedDataGrantValidationOutcome.VersionUnsupported;
				version = 2;
			}

			if (!int.TryParse(principal.FindFirst(DepartmentClaim)?.Value, out var departmentId))
				return ProtectedDataGrantValidationOutcome.Invalid;
			if (departmentId != expectedDepartmentId)
				return ProtectedDataGrantValidationOutcome.WrongDepartment;

			if (!long.TryParse(principal.FindFirst(PolicyEpochClaim)?.Value, out var policyEpoch))
				return ProtectedDataGrantValidationOutcome.Invalid;
			// Exact match required: a bump revokes older grants, and a grant claiming a FUTURE epoch
			// is equally untrustworthy — fail closed on any mismatch.
			if (policyEpoch != currentPolicyEpoch)
				return ProtectedDataGrantValidationOutcome.EpochRevoked;

			var scopes = (principal.FindFirst(ScopeClaim)?.Value ?? string.Empty)
				.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (!string.IsNullOrWhiteSpace(requiredScope) && !scopes.Contains(requiredScope, StringComparer.Ordinal))
				return ProtectedDataGrantValidationOutcome.MissingScope;

			var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
				?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
			if (string.IsNullOrWhiteSpace(userId))
				return ProtectedDataGrantValidationOutcome.Invalid;

			if (version == 2)
			{
				if (!TryReadVersionTwo(parsedToken, now, skew, out var versionTwo))
					return ProtectedDataGrantValidationOutcome.Invalid;

				versionTwo.GrantId = parsedToken.Id;
				versionTwo.UserId = userId;
				versionTwo.DepartmentId = departmentId;
				versionTwo.PolicyEpoch = policyEpoch;
				versionTwo.Scopes = scopes;
				versionTwo.IssuedAtUtc = parsedToken.IssuedAt;
				versionTwo.ExpiresOnUtc = parsedToken.ValidTo;
				grant = versionTwo;
				return ProtectedDataGrantValidationOutcome.Valid;
			}

			int.TryParse(principal.FindFirst(ClientAppClaim)?.Value, out var clientApp);
			long.TryParse(principal.FindFirst(MfaAtClaim)?.Value, out var mfaAtSeconds);

			// amr is read from the token itself: the handler's inbound claim mapping renames "amr" on the principal,
			// so a principal lookup never found it and every version 1 grant read as step-up exempt.
			var amr = RawStrings(parsedToken, AmrClaim);

			grant = new ProtectedDataGrant
			{
				GrantId = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value,
				UserId = userId,
				DepartmentId = departmentId,
				SessionId = principal.FindFirst(Model.Security.SessionClaimTypes.SessionId)?.Value,
				ClientApp = clientApp,
				PolicyEpoch = policyEpoch,
				Scopes = scopes,
				MfaAtUtc = DateTimeOffset.FromUnixTimeSeconds(mfaAtSeconds).UtcDateTime,
				StepUpExempt = !amr.Contains("otp", StringComparer.Ordinal),
				IssuedAtUtc = parsedToken.IssuedAt,
				ExpiresOnUtc = parsedToken.ValidTo,
				Version = 1,
				Amr = amr
			};
			return ProtectedDataGrantValidationOutcome.Valid;
		}

		private static void ValidateVersionTwoRequest(ProtectedDataGrantIssueRequest request)
		{
			if (string.IsNullOrWhiteSpace(request.SessionId))
				throw new ArgumentException("A version 2 grant is bound to a session.", nameof(request));
			if (!IsGrantClient(request.ClientApp))
				throw new ArgumentException("A version 2 grant is bound to a known client application.", nameof(request));
			if (request.AuthenticationGeneration is not >= 0)
				throw new ArgumentException("A version 2 grant carries the account authentication generation.", nameof(request));
			if (!ProtectedDataGrantMfaMethods.IsKnown(request.MfaMethod))
				throw new ArgumentException("A version 2 grant names a known MFA method.", nameof(request));
			if (request.StepUpExempt != (request.MfaMethod == ProtectedDataGrantMfaMethods.None))
				throw new ArgumentException("Only an exempt grant has no MFA method, and an exempt grant has none.", nameof(request));
			if (!request.StepUpExempt && request.MfaAtUtc == default)
				throw new ArgumentException("A verified grant carries the real verification time; it never defaults to now.", nameof(request));
			if (RequiresEvidenceReference(request.MfaMethod) &&
				(string.IsNullOrWhiteSpace(request.MfaCredentialId) || request.MfaStateVersion is not >= 0))
				throw new ArgumentException("This MFA method needs its credential or evidence reference and state version.", nameof(request));
			if (request.StepUpExempt && (request.MfaCredentialId != null || request.MfaStateVersion != null))
				throw new ArgumentException("An exempt grant carries no MFA evidence reference.", nameof(request));
			if (request.SessionLockVersion is < 0)
				throw new ArgumentException("A lock version cannot be negative.", nameof(request));
		}

		private static ProtectedDataGrantIssueResult IssueVersionTwo(ProtectedDataGrantIssueRequest request,
			X509Certificate2 signingCertificate, DateTime now, DateTime expires, string grantId)
		{
			var skew = TimeSpan.FromSeconds(Math.Max(0, Config.DataProtectionConfig.GrantClockSkewSeconds));
			if (!request.StepUpExempt)
			{
				if (request.MfaAtUtc > now.Add(skew))
					throw new ArgumentException("The MFA verification time cannot be in the future.", nameof(request));

				// The window runs from the verification, not from issuance: reusing older evidence (plan section 9.1)
				// never extends it, and evidence older than the whole window cannot mint a grant.
				var evidenceExpiry = request.MfaAtUtc.Add(expires - now);
				if (evidenceExpiry <= now)
					throw new ArgumentException("The MFA verification is older than the grant window.", nameof(request));
				if (evidenceExpiry < expires)
					expires = evidenceExpiry;
			}

			// No grant outlives the session or shift it is bound to (plan section 9.2), and none is issued at or past it.
			if (request.NotAfterUtc is DateTime notAfter)
			{
				notAfter = DateTime.SpecifyKind(notAfter, DateTimeKind.Utc);
				if (notAfter <= now)
					throw new ArgumentException("The session the grant would be bound to has already ended.", nameof(request));
				if (notAfter < expires)
					expires = notAfter;
			}

			var ecdsa = signingCertificate.GetECDsaPrivateKey();
			if (ecdsa == null)
				throw new InvalidOperationException("The grant signing certificate does not carry an ECDSA private key (ES256 is required).");

			var credentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa), SecurityAlgorithms.EcdsaSha256);
			var token = new JwtSecurityToken(
				issuer: Config.DataProtectionConfig.GrantIssuer,
				audience: Config.DataProtectionConfig.GrantAudience,
				claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, request.UserId) },
				notBefore: now,
				expires: expires,
				signingCredentials: credentials);

			// Typed payload values: numbers stay JSON numbers and step_up_exempt stays a JSON boolean, so a reader never
			// has to guess at a string's meaning.
			var payload = token.Payload;
			payload[JwtRegisteredClaimNames.Iat] = ToUnixSeconds(now);
			payload[JwtRegisteredClaimNames.Jti] = grantId;
			payload[VersionClaim] = 2;
			payload[DepartmentClaim] = request.DepartmentId;
			payload[Model.Security.SessionClaimTypes.SessionId] = request.SessionId;
			payload[ClientAppClaim] = request.ClientApp;
			payload[PolicyEpochClaim] = request.PolicyEpoch;
			payload[AuthenticationGenerationClaim] = request.AuthenticationGeneration.Value;
			payload[ScopeClaim] = string.Join(" ", request.Scopes);
			payload[MfaMethodClaim] = request.MfaMethod;
			payload[StepUpExemptClaim] = request.StepUpExempt;

			var amr = new List<string> { request.FederatedFirstFactor ? "fed" : "pwd" };
			if (!request.StepUpExempt)
			{
				payload[MfaAtClaim] = ToUnixSeconds(request.MfaAtUtc);
				if (request.MfaMethod == ProtectedDataGrantMfaMethods.Totp)
					amr.Add("otp");
				amr.Add("mfa");
			}
			payload[AmrClaim] = amr;

			if (request.MfaCredentialId != null)
				payload[MfaCredentialClaim] = request.MfaCredentialId;
			if (request.MfaStateVersion != null)
				payload[MfaStateVersionClaim] = request.MfaStateVersion.Value;
			if (request.SessionLockVersion != null)
				payload[SessionLockVersionClaim] = request.SessionLockVersion.Value;

			return new ProtectedDataGrantIssueResult
			{
				GrantId = grantId,
				Token = TokenHandler.WriteToken(token),
				ExpiresOnUtc = expires
			};
		}

		/// <summary>
		/// Structural rules of a version 2 grant (workbook section 7.5). Everything required must be present with the right
		/// type, and the MFA facts must be consistent with each other; the caller binding (session, client, generation, lock
		/// version) is checked separately against the validated session by <c>ProtectedGrantBinding</c>.
		/// </summary>
		private static bool TryReadVersionTwo(JwtSecurityToken token, DateTime now, TimeSpan skew, out ProtectedDataGrant grant)
		{
			grant = null;
			var payload = token.Payload;

			if (string.IsNullOrWhiteSpace(token.Id) || token.IssuedAt == DateTime.MinValue || token.ValidFrom == DateTime.MinValue ||
				token.IssuedAt > now.Add(skew))
				return false;

			if (!TryGetString(payload, Model.Security.SessionClaimTypes.SessionId, out var sessionId) ||
				!TryGetInt64(payload, ClientAppClaim, out var clientApp) || !IsGrantClient(clientApp) ||
				!TryGetInt64(payload, AuthenticationGenerationClaim, out var generation) || generation < 0 ||
				!TryGetString(payload, MfaMethodClaim, out var method) || !ProtectedDataGrantMfaMethods.IsKnown(method) ||
				!TryGetBoolean(payload, StepUpExemptClaim, out var exempt))
				return false;

			if (exempt != (method == ProtectedDataGrantMfaMethods.None))
				return false;

			var mfaAt = default(DateTime);
			if (exempt)
			{
				if (payload.ContainsKey(MfaAtClaim) || payload.ContainsKey(MfaCredentialClaim) || payload.ContainsKey(MfaStateVersionClaim))
					return false;
			}
			else
			{
				// The verification happened no later than issuance, and never in the future.
				if (!TryGetInt64(payload, MfaAtClaim, out var mfaAtSeconds) || mfaAtSeconds <= 0)
					return false;
				mfaAt = DateTimeOffset.FromUnixTimeSeconds(mfaAtSeconds).UtcDateTime;
				if (mfaAt > token.IssuedAt.Add(skew) || mfaAt > now.Add(skew))
					return false;
			}

			string credentialId = null;
			long? stateVersion = null;
			var hasCredential = payload.ContainsKey(MfaCredentialClaim);
			var hasStateVersion = payload.ContainsKey(MfaStateVersionClaim);
			if (hasCredential != hasStateVersion || RequiresEvidenceReference(method) && !hasCredential)
				return false;
			if (hasCredential)
			{
				if (!TryGetString(payload, MfaCredentialClaim, out credentialId) ||
					!TryGetInt64(payload, MfaStateVersionClaim, out var parsedStateVersion) || parsedStateVersion < 0)
					return false;
				stateVersion = parsedStateVersion;
			}

			long? lockVersion = null;
			if (payload.ContainsKey(SessionLockVersionClaim))
			{
				if (!TryGetInt64(payload, SessionLockVersionClaim, out var parsedLockVersion) || parsedLockVersion < 0)
					return false;
				lockVersion = parsedLockVersion;
			}

			var amr = RawStrings(token, AmrClaim);
			if (amr.Count == 0 || amr.Any(value => !VersionTwoAmr.Contains(value)) || amr.Distinct(StringComparer.Ordinal).Count() != amr.Count ||
				amr.Count(value => value == "pwd" || value == "fed") != 1 ||
				amr.Contains("mfa") == exempt ||
				amr.Contains("otp") != (method == ProtectedDataGrantMfaMethods.Totp))
				return false;

			grant = new ProtectedDataGrant
			{
				Version = 2,
				SessionId = sessionId,
				ClientApp = (int)clientApp,
				AuthenticationGeneration = generation,
				MfaMethod = method,
				StepUpExempt = exempt,
				MfaAtUtc = mfaAt,
				MfaCredentialId = credentialId,
				MfaStateVersion = stateVersion,
				SessionLockVersion = lockVersion,
				Amr = amr
			};
			return true;
		}

		private static bool RequiresEvidenceReference(string method) =>
			method == ProtectedDataGrantMfaMethods.Passkey || method == ProtectedDataGrantMfaMethods.PasskeyApproval ||
			method == ProtectedDataGrantMfaMethods.Federated;

		private static bool IsGrantClient(long clientApp) =>
			clientApp > (int)UserSessionClientApplication.UnknownLegacy && Enum.IsDefined(typeof(UserSessionClientApplication), (int)clientApp);

		private static bool TryGetInt64(JwtPayload payload, string name, out long value)
		{
			value = 0;
			if (!payload.TryGetValue(name, out var raw))
				return false;

			switch (raw)
			{
				case int i: value = i; return true;
				case long l: value = l; return true;
				case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } element: return element.TryGetInt64(out value);
				default: return false;
			}
		}

		private static bool TryGetBoolean(JwtPayload payload, string name, out bool value)
		{
			value = false;
			if (!payload.TryGetValue(name, out var raw))
				return false;

			switch (raw)
			{
				case bool b: value = b; return true;
				case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.True }: value = true; return true;
				case System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.False }: value = false; return true;
				default: return false;
			}
		}

		private static bool TryGetString(JwtPayload payload, string name, out string value)
		{
			value = payload.TryGetValue(name, out var raw) ? raw as string : null;
			return !string.IsNullOrWhiteSpace(value);
		}

		// The token's own claim list is unmapped (no inbound claim-type translation), and a JSON array becomes one claim
		// per element.
		private static List<string> RawStrings(JwtSecurityToken token, string type) =>
			token.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToList();

		private X509Certificate2 GetSigningCertificate() => _signingCertificate.Value;

		private X509Certificate2 GetValidationCertificate() => _validationCertificate.Value;

		private static X509Certificate2 LoadSigningCertificateSafe(Func<X509Certificate2> loader)
		{
			try
			{
				var certificate = loader();
				if (certificate != null && certificate.GetECDsaPrivateKey() == null)
				{
					Logging.LogError("Protected Data Grant signing certificate has no ECDSA private key; grant issuance is disabled on this host.");
					return null;
				}

				return certificate;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protected Data Grant signing certificate failed to load; grant issuance is disabled on this host.");
				return null;
			}
		}

		private static X509Certificate2 LoadValidationCertificateSafe(Func<X509Certificate2> loader)
		{
			try
			{
				return loader();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protected Data Grant validation certificate failed to load; grant validation is disabled on this host.");
				return null;
			}
		}

		private static X509Certificate2 LoadSigningCertificateFromConfig()
		{
			var path = Config.DataProtectionConfig.GrantSigningCertificatePath;
			if (string.IsNullOrWhiteSpace(path))
				return null;

			return X509CertificateLoader.LoadPkcs12FromFile(path,
				Config.DataProtectionConfig.GrantSigningCertificatePassword);
		}

		private static X509Certificate2 LoadValidationCertificateFromConfig()
		{
			var path = Config.DataProtectionConfig.GrantValidationCertificatePath;
			if (string.IsNullOrWhiteSpace(path))
			{
				// Single-host development fallback: validate with the signing certificate's public part.
				return LoadSigningCertificateFromConfig();
			}

			try
			{
				return X509CertificateLoader.LoadCertificateFromFile(path);
			}
			catch (CryptographicException)
			{
				// Not a DER/PEM certificate — allow a PFX that carries only the public chain too.
				return X509CertificateLoader.LoadPkcs12FromFile(path, string.Empty);
			}
		}

		private static long ToUnixSeconds(DateTime utc) =>
			new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
	}
}
