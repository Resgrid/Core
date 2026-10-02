using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.IdentityModel.Tokens;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>Same observable rules as SsoLoginTransactionRepository: guarded state changes that succeed once.</summary>
	internal sealed class InMemorySsoLoginTransactionRepository : ISsoLoginTransactionRepository
	{
		private readonly object _gate = new();
		public readonly List<SsoLoginTransaction> Rows = new();

		public Task InsertAsync(SsoLoginTransaction transaction, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows.Add(Copy(transaction));
			return Task.CompletedTask;
		}

		public Task<SsoLoginTransaction> GetAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.FirstOrDefault(r => r.SsoLoginTransactionId == transactionId)));
		}

		public Task<SsoLoginTransaction> GetByStateHashAsync(byte[] stateHash, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Copy(Rows.FirstOrDefault(r => r.StateHash.AsSpan().SequenceEqual(stateHash))));
		}

		public Task<bool> TryAuthenticateAsync(string transactionId, string userId, DateTime authenticatedOnUtc, string federatedMfaValue, byte[] codeHash,
			DateTime codeExpiresOnUtc, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.SsoLoginTransactionId == transactionId && r.State == (int)SsoLoginTransactionState.Pending && r.ExpiresOnUtc > utcNow);
				if (row == null) return Task.FromResult(false);
				row.State = (int)SsoLoginTransactionState.Authenticated;
				row.UserId = userId;
				row.AuthenticatedOnUtc = authenticatedOnUtc;
				row.FederatedMfaValue = federatedMfaValue;
				row.CodeHash = codeHash;
				row.CodeExpiresOnUtc = codeExpiresOnUtc;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryFailAsync(string transactionId, string failureCode, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.SsoLoginTransactionId == transactionId && r.State == (int)SsoLoginTransactionState.Pending);
				if (row == null) return Task.FromResult(false);
				row.State = (int)SsoLoginTransactionState.Failed;
				row.FailureCode = failureCode;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryRedeemAsync(string transactionId, byte[] codeHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.SsoLoginTransactionId == transactionId && r.State == (int)SsoLoginTransactionState.Authenticated
					&& r.CodeHash != null && r.CodeHash.AsSpan().SequenceEqual(codeHash) && r.CodeExpiresOnUtc > utcNow);
				if (row == null) return Task.FromResult(false);
				row.State = (int)SsoLoginTransactionState.Redeemed;
				row.RedeemedOnUtc = utcNow;
				return Task.FromResult(true);
			}
		}

		public Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.ExpiresOnUtc < utcCutoff));
		}

		private static SsoLoginTransaction Copy(SsoLoginTransaction row) => row == null ? null : (SsoLoginTransaction)typeof(object)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
	}

	/// <summary>
	/// A fake OIDC identity provider: publishes an RSA signing key, and exchanges its authorization code for an id_token
	/// it signs itself, but only when the code verifier matches the challenge the authorize URL carried. Tests shape the
	/// token (nonce, audience, auth_time, signing key) to play a legitimate or a hostile IdP.
	/// </summary>
	internal sealed class FakeOidcProvider : IOidcProviderClient
	{
		public const string Authority = "https://idp.example.test";
		public const string Issuer = "https://idp.example.test/";
		public const string AuthorizationEndpoint = "https://idp.example.test/authorize";
		public const string TokenEndpoint = "https://idp.example.test/token";
		public const string Code = "idp-authorization-code";

		private readonly RSA _key = RSA.Create(2048);
		public readonly List<IReadOnlyDictionary<string, string>> Exchanges = new();

		/// <summary>The id_token the next exchange returns; built by <see cref="Token"/> from the authorize URL.</summary>
		public Func<string> NextIdToken { get; set; }

		public string ExpectedChallenge { get; set; }

		public Task<OidcProviderMetadata> GetMetadataAsync(string authority, bool forceRefresh = false, CancellationToken cancellationToken = default)
		{
			var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_key.ExportParameters(false)) { KeyId = "test-key" });
			var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = jwk.Kty, kid = jwk.Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } });
			return Task.FromResult(authority?.TrimEnd('/') == Authority
				? new OidcProviderMetadata { Issuer = Issuer, AuthorizationEndpoint = AuthorizationEndpoint, TokenEndpoint = TokenEndpoint, JwksJson = jwks }
				: null);
		}

		public Task<OidcCodeExchangeResult> ExchangeCodeAsync(string tokenEndpoint, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken = default)
		{
			Exchanges.Add(form);
			var verifierMatches = form.TryGetValue("code_verifier", out var verifier) &&
				Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) == ExpectedChallenge;
			return Task.FromResult(tokenEndpoint == TokenEndpoint && form.GetValueOrDefault("code") == Code && verifierMatches && NextIdToken != null
				? new OidcCodeExchangeResult { IdToken = NextIdToken() }
				: new OidcCodeExchangeResult { Error = "invalid_grant" });
		}

		/// <summary>Reads what the authorize URL asked for, as the IdP would.</summary>
		public static (string State, string Nonce, string Challenge, string Prompt, string MaxAge) Authorize(string authorizeUrl)
		{
			var query = HttpUtility.ParseQueryString(new Uri(authorizeUrl).Query);
			return (query["state"], query["nonce"], query["code_challenge"], query["prompt"], query["max_age"]);
		}

		/// <param name="amr">Authentication methods; more than one serializes as a JSON array, as providers send it.</param>
		public string Token(string nonce, string audience = "resgrid-client", string subject = "external-user", DateTime? authTime = null,
			RSA signingKey = null, DateTime? expires = null, IEnumerable<string> amr = null, string acr = null, IEnumerable<string> acrs = null)
		{
			var now = DateTime.UtcNow;
			var claims = new List<Claim> { new("sub", subject), new("email", "user@example.com"), new("email_verified", "true", ClaimValueTypes.Boolean) };
			if (nonce != null) claims.Add(new Claim("nonce", nonce));
			if (authTime != null) claims.Add(new Claim("auth_time", new DateTimeOffset(authTime.Value).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
			foreach (var value in amr ?? Array.Empty<string>()) claims.Add(new Claim("amr", value));
			if (acr != null) claims.Add(new Claim("acr", acr));
			foreach (var value in acrs ?? Array.Empty<string>()) claims.Add(new Claim("acrs", value));
			var credentials = new SigningCredentials(new RsaSecurityKey(signingKey ?? _key) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256);
			var expiresOn = expires ?? now.AddMinutes(10);
			return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, audience, claims, expiresOn.AddMinutes(-11), expiresOn, credentials));
		}

		public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
