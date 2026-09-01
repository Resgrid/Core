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

			int.TryParse(principal.FindFirst(ClientAppClaim)?.Value, out var clientApp);
			long.TryParse(principal.FindFirst(MfaAtClaim)?.Value, out var mfaAtSeconds);

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
				StepUpExempt = !string.Equals(principal.FindFirst(AmrClaim)?.Value, "otp", StringComparison.Ordinal),
				IssuedAtUtc = parsedToken.IssuedAt,
				ExpiresOnUtc = parsedToken.ValidTo
			};
			return ProtectedDataGrantValidationOutcome.Valid;
		}

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
