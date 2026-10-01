using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IBrokerSessionAssertionService"/>
	/// <remarks>
	/// ES256 compact JWS with the algorithm pinned, issued by <c>DataProtectionConfig.SessionAssertionIssuer</c> for
	/// <c>DataProtectionConfig.BrokerAudience</c>. The certificate is dedicated to assertions. Load failures log once and
	/// leave the service unable to mint or validate; nothing here ever logs a token or claim value.
	/// </remarks>
	public sealed class BrokerSessionAssertionService : IBrokerSessionAssertionService
	{
		private const string GenerationClaim = "auth_ver";
		private const string DepartmentClaim = "dept";
		private const string ClientClaim = "client_app";
		private const string LockVersionClaim = "session_lock_version";
		private const string RequestClaim = "req";
		private const string CredentialIssuedClaim = "cred_iat";

		private static readonly JwtSecurityTokenHandler TokenHandler = new();

		private readonly Lazy<X509Certificate2> _signingCertificate;
		private readonly Lazy<X509Certificate2> _validationCertificate;

		public BrokerSessionAssertionService()
			: this(LoadSigningCertificateFromConfig, LoadValidationCertificateFromConfig)
		{
		}

		/// <summary>Test seam: supply certificates directly instead of loading from configured paths.</summary>
		public BrokerSessionAssertionService(Func<X509Certificate2> signingCertificateLoader,
			Func<X509Certificate2> validationCertificateLoader)
		{
			ArgumentNullException.ThrowIfNull(signingCertificateLoader);
			ArgumentNullException.ThrowIfNull(validationCertificateLoader);

			_signingCertificate = new Lazy<X509Certificate2>(() => LoadSafe(signingCertificateLoader, requirePrivateKey: true),
				System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
			_validationCertificate = new Lazy<X509Certificate2>(() => LoadSafe(validationCertificateLoader, requirePrivateKey: false),
				System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public bool CanMint => _signingCertificate.Value != null;

		public bool CanValidate => _validationCertificate.Value != null;

		public string Mint(BrokerSessionAssertion facts)
		{
			ArgumentNullException.ThrowIfNull(facts);
			if (string.IsNullOrWhiteSpace(facts.UserId) || string.IsNullOrWhiteSpace(facts.SessionId) || facts.DepartmentId <= 0 ||
				facts.ClientApplication <= 0 || facts.AuthenticationGeneration < 0 || string.IsNullOrWhiteSpace(facts.RequestDigest))
				throw new ArgumentException("A session assertion needs the user, session, generation, department, client and request.", nameof(facts));

			var certificate = _signingCertificate.Value
				?? throw new InvalidOperationException("Session-assertion signing is not configured on this host (check CanMint).");
			var ecdsa = certificate.GetECDsaPrivateKey()
				?? throw new InvalidOperationException("The session-assertion certificate does not carry an ECDSA private key.");

			var now = DateTime.UtcNow;
			var lifetime = TimeSpan.FromSeconds(Math.Clamp(Config.DataProtectionConfig.SessionAssertionLifetimeSeconds, 5, 300));
			var token = new JwtSecurityToken(
				issuer: Config.DataProtectionConfig.SessionAssertionIssuer,
				audience: Config.DataProtectionConfig.BrokerAudience,
				claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, facts.UserId) },
				notBefore: now,
				expires: now.Add(lifetime),
				signingCredentials: new SigningCredentials(new ECDsaSecurityKey(ecdsa), SecurityAlgorithms.EcdsaSha256));

			var payload = token.Payload;
			payload[JwtRegisteredClaimNames.Iat] = new DateTimeOffset(now).ToUnixTimeSeconds();
			payload[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N");
			payload[SessionClaimTypes.SessionId] = facts.SessionId;
			payload[GenerationClaim] = facts.AuthenticationGeneration;
			payload[DepartmentClaim] = facts.DepartmentId;
			payload[ClientClaim] = facts.ClientApplication;
			payload[RequestClaim] = facts.RequestDigest;
			if (facts.SessionLockVersion != null)
				payload[LockVersionClaim] = facts.SessionLockVersion.Value;
			if (facts.CredentialIssuedOnUtc != null)
				payload[CredentialIssuedClaim] = new DateTimeOffset(DateTime.SpecifyKind(facts.CredentialIssuedOnUtc.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();

			return TokenHandler.WriteToken(token);
		}

		public BrokerSessionAssertionOutcome Validate(string token, string expectedRequestDigest, out BrokerSessionAssertion assertion,
			DateTime? utcNow = null)
		{
			assertion = null;
			var certificate = _validationCertificate.Value;
			if (certificate == null)
				return BrokerSessionAssertionOutcome.NotConfigured;
			if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expectedRequestDigest))
				return BrokerSessionAssertionOutcome.Invalid;

			JwtSecurityToken parsed;
			try
			{
				var ecdsa = certificate.GetECDsaPublicKey();
				if (ecdsa == null)
					return BrokerSessionAssertionOutcome.NotConfigured;

				TokenHandler.ValidateToken(token, new TokenValidationParameters
				{
					ValidIssuer = Config.DataProtectionConfig.SessionAssertionIssuer,
					ValidAudience = Config.DataProtectionConfig.BrokerAudience,
					IssuerSigningKey = new ECDsaSecurityKey(ecdsa),
					ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateIssuerSigningKey = true,
					ValidateLifetime = false,
					RequireExpirationTime = true,
					RequireSignedTokens = true
				}, out var validated);
				parsed = (JwtSecurityToken)validated;
			}
			catch (Exception)
			{
				return BrokerSessionAssertionOutcome.Invalid;
			}

			var now = utcNow ?? DateTime.UtcNow;
			var skew = TimeSpan.FromSeconds(Math.Max(0, Config.DataProtectionConfig.GrantClockSkewSeconds));
			if (parsed.ValidTo == DateTime.MinValue || now > parsed.ValidTo.Add(skew))
				return BrokerSessionAssertionOutcome.Expired;
			if (parsed.ValidFrom == DateTime.MinValue || now < parsed.ValidFrom.Subtract(skew))
				return BrokerSessionAssertionOutcome.Invalid;

			// A long-lived assertion is not an assertion: refuse anything that claims more than the maximum lifetime.
			if (parsed.ValidTo - parsed.ValidFrom > TimeSpan.FromSeconds(300))
				return BrokerSessionAssertionOutcome.Invalid;

			var payload = parsed.Payload;
			var subject = payload.TryGetValue(JwtRegisteredClaimNames.Sub, out var rawSubject) ? rawSubject as string : null;
			var sessionId = payload.TryGetValue(SessionClaimTypes.SessionId, out var rawSession) ? rawSession as string : null;
			var request = payload.TryGetValue(RequestClaim, out var rawRequest) ? rawRequest as string : null;
			if (string.IsNullOrWhiteSpace(parsed.Id) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(sessionId) ||
				string.IsNullOrWhiteSpace(request) ||
				!TryGetInt64(payload, GenerationClaim, out var generation) || generation < 0 ||
				!TryGetInt64(payload, DepartmentClaim, out var department) || department <= 0 || department > int.MaxValue ||
				!TryGetInt64(payload, ClientClaim, out var client) || client <= 0 || client > int.MaxValue)
				return BrokerSessionAssertionOutcome.Invalid;

			long? lockVersion = null;
			if (payload.ContainsKey(LockVersionClaim))
			{
				if (!TryGetInt64(payload, LockVersionClaim, out var parsedLock) || parsedLock < 0)
					return BrokerSessionAssertionOutcome.Invalid;
				lockVersion = parsedLock;
			}

			DateTime? credentialIssuedOn = null;
			if (payload.ContainsKey(CredentialIssuedClaim))
			{
				if (!TryGetInt64(payload, CredentialIssuedClaim, out var issuedSeconds) || issuedSeconds <= 0 ||
					issuedSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
					return BrokerSessionAssertionOutcome.Invalid;
				credentialIssuedOn = DateTimeOffset.FromUnixTimeSeconds(issuedSeconds).UtcDateTime;
			}

			if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(request.ToUpperInvariant()),
					System.Text.Encoding.ASCII.GetBytes(expectedRequestDigest.ToUpperInvariant())))
				return BrokerSessionAssertionOutcome.RequestMismatch;

			assertion = new BrokerSessionAssertion
			{
				UserId = subject,
				SessionId = sessionId,
				AuthenticationGeneration = generation,
				DepartmentId = (int)department,
				ClientApplication = (int)client,
				SessionLockVersion = lockVersion,
				CredentialIssuedOnUtc = credentialIssuedOn,
				RequestDigest = request,
				AssertionId = parsed.Id,
				IssuedAtUtc = parsed.IssuedAt,
				ExpiresOnUtc = parsed.ValidTo
			};
			return BrokerSessionAssertionOutcome.Valid;
		}

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

		private static X509Certificate2 LoadSafe(Func<X509Certificate2> loader, bool requirePrivateKey)
		{
			try
			{
				var certificate = loader();
				if (certificate == null)
					return null;
				if (requirePrivateKey ? certificate.GetECDsaPrivateKey() == null : certificate.GetECDsaPublicKey() == null)
				{
					Logging.LogError("The broker session-assertion certificate has no suitable ECDSA key; session assertions are disabled on this host.");
					return null;
				}

				return certificate;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The broker session-assertion certificate failed to load; session assertions are disabled on this host.");
				return null;
			}
		}

		private static X509Certificate2 LoadSigningCertificateFromConfig()
		{
			var path = Config.DataProtectionConfig.SessionAssertionSigningCertificatePath;
			return string.IsNullOrWhiteSpace(path)
				? null
				: X509CertificateLoader.LoadPkcs12FromFile(path, Config.DataProtectionConfig.SessionAssertionSigningCertificatePassword);
		}

		private static X509Certificate2 LoadValidationCertificateFromConfig()
		{
			var path = Config.DataProtectionConfig.SessionAssertionValidationCertificatePath;
			if (string.IsNullOrWhiteSpace(path))
				return LoadSigningCertificateFromConfig();

			try
			{
				return X509CertificateLoader.LoadCertificateFromFile(path);
			}
			catch (CryptographicException)
			{
				return X509CertificateLoader.LoadPkcs12FromFile(path, string.Empty);
			}
		}
	}
}
