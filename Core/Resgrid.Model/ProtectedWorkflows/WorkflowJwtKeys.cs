using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>One private_key_jwt signing key as stored INSIDE a credential's encrypted data. Never shown or exported.</summary>
	public sealed class WorkflowSigningKey
	{
		[JsonProperty("kid")] public string Kid { get; set; }
		[JsonProperty("alg")] public string Alg { get; set; }

		/// <summary>PKCS#8, base64.</summary>
		[JsonProperty("privateKey")] public string PrivateKey { get; set; }

		[JsonProperty("createdOn")] public DateTime CreatedOn { get; set; }

		/// <summary>When a newer key replaced it; it stays published (never used to sign) for the overlap window.</summary>
		[JsonProperty("retiredOn")] public DateTime? RetiredOn { get; set; }
	}

	/// <summary>One PUBLIC key in <see cref="WorkflowCredential.PublicJwks"/>, with its lifecycle dates.</summary>
	public sealed class WorkflowPublicKey
	{
		[JsonProperty("kid")] public string Kid { get; set; }
		[JsonProperty("alg")] public string Alg { get; set; }
		[JsonProperty("jwk")] public JObject Jwk { get; set; }
		[JsonProperty("createdOn")] public DateTime CreatedOn { get; set; }
		[JsonProperty("retiredOn")] public DateTime? RetiredOn { get; set; }
	}

	/// <summary>
	/// SMART Backend Services (OAuth2 private_key_jwt) for workflow credentials: key generation (RS384 by default,
	/// ES384 optional), the published JWKS (current key plus rotated keys for the overlap window), and the signed client
	/// assertion (iss = sub = client id, aud = token URL, exp at most five minutes out, a random jti, the kid in the header).
	/// </summary>
	public static class WorkflowJwtKeys
	{
		public const string ClientSecret = "client_secret";
		public const string PrivateKeyJwt = "private_key_jwt";
		public const string Rs384 = "RS384";
		public const string Es384 = "ES384";
		public const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

		public static readonly TimeSpan MaxAssertionLifetime = TimeSpan.FromMinutes(5);

		public static string NormalizeAuthMethod(string authMethod) =>
			string.Equals(authMethod?.Trim(), PrivateKeyJwt, StringComparison.OrdinalIgnoreCase) ? PrivateKeyJwt : ClientSecret;

		public static string NormalizeAlgorithm(string alg) =>
			string.Equals(alg?.Trim(), Es384, StringComparison.OrdinalIgnoreCase) ? Es384 : Rs384;

		/// <summary>A new key pair. The kid is the RFC 7638 thumbprint of the public key.</summary>
		public static (WorkflowSigningKey Signing, WorkflowPublicKey Public) Generate(string alg, DateTime utcNow)
		{
			alg = NormalizeAlgorithm(alg);
			byte[] pkcs8;
			JObject jwk;
			if (alg == Es384)
			{
				using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP384);
				pkcs8 = ec.ExportPkcs8PrivateKey();
				var p = ec.ExportParameters(false);
				jwk = new JObject { ["crv"] = "P-384", ["kty"] = "EC", ["x"] = Base64Url(p.Q.X), ["y"] = Base64Url(p.Q.Y) };
			}
			else
			{
				using var rsa = RSA.Create(3072);
				pkcs8 = rsa.ExportPkcs8PrivateKey();
				var p = rsa.ExportParameters(false);
				jwk = new JObject { ["e"] = Base64Url(p.Exponent), ["kty"] = "RSA", ["n"] = Base64Url(p.Modulus) };
			}

			var kid = Thumbprint(jwk);
			var published = new JObject(jwk.Properties()) { ["kid"] = kid, ["alg"] = alg, ["use"] = "sig" };
			var signing = new WorkflowSigningKey { Kid = kid, Alg = alg, PrivateKey = Convert.ToBase64String(pkcs8), CreatedOn = utcNow };
			CryptographicOperations.ZeroMemory(pkcs8);
			return (signing, new WorkflowPublicKey { Kid = kid, Alg = alg, Jwk = published, CreatedOn = utcNow });
		}

		/// <summary>The key that signs now: the newest one not retired.</summary>
		public static WorkflowSigningKey Current(IEnumerable<WorkflowSigningKey> keys) =>
			(keys ?? Enumerable.Empty<WorkflowSigningKey>())
				.Where(k => k != null && !k.RetiredOn.HasValue && !string.IsNullOrWhiteSpace(k.PrivateKey))
				.OrderByDescending(k => k.CreatedOn)
				.FirstOrDefault();

		/// <summary>Keys still worth keeping: not retired, or retired less than <paramref name="overlapDays"/> ago.</summary>
		public static bool IsPublished(DateTime? retiredOn, DateTime utcNow, int overlapDays) =>
			!retiredOn.HasValue || retiredOn.Value.AddDays(Math.Max(0, overlapDays)) > utcNow;

		/// <summary>The JWKS document served for a credential: { "keys": [ public JWKs ] }.</summary>
		public static string BuildJwks(string publicJwksColumn, DateTime utcNow, int overlapDays)
		{
			var keys = new JArray();
			foreach (var key in ReadPublicKeys(publicJwksColumn).Where(k => k.Jwk != null && IsPublished(k.RetiredOn, utcNow, overlapDays)))
				keys.Add(key.Jwk.DeepClone());
			return new JObject { ["keys"] = keys }.ToString(Formatting.None);
		}

		public static List<WorkflowPublicKey> ReadPublicKeys(string publicJwksColumn)
		{
			if (string.IsNullOrWhiteSpace(publicJwksColumn))
				return new List<WorkflowPublicKey>();
			try
			{
				return JsonConvert.DeserializeObject<List<WorkflowPublicKey>>(publicJwksColumn) ?? new List<WorkflowPublicKey>();
			}
			catch (JsonException)
			{
				return new List<WorkflowPublicKey>();
			}
		}

		public static string WritePublicKeys(IEnumerable<WorkflowPublicKey> keys) =>
			JsonConvert.SerializeObject((keys ?? Enumerable.Empty<WorkflowPublicKey>()).ToList());

		/// <summary>The public half of a stored signing key (used to rebuild the column after an edit).</summary>
		public static WorkflowPublicKey PublicFor(WorkflowSigningKey key)
		{
			var pkcs8 = Convert.FromBase64String(key.PrivateKey);
			try
			{
				JObject jwk;
				if (NormalizeAlgorithm(key.Alg) == Es384)
				{
					using var ec = ECDsa.Create();
					ec.ImportPkcs8PrivateKey(pkcs8, out _);
					var p = ec.ExportParameters(false);
					jwk = new JObject { ["crv"] = "P-384", ["kty"] = "EC", ["x"] = Base64Url(p.Q.X), ["y"] = Base64Url(p.Q.Y) };
				}
				else
				{
					using var rsa = RSA.Create();
					rsa.ImportPkcs8PrivateKey(pkcs8, out _);
					var p = rsa.ExportParameters(false);
					jwk = new JObject { ["e"] = Base64Url(p.Exponent), ["kty"] = "RSA", ["n"] = Base64Url(p.Modulus) };
				}

				var published = new JObject(jwk.Properties()) { ["kid"] = key.Kid, ["alg"] = NormalizeAlgorithm(key.Alg), ["use"] = "sig" };
				return new WorkflowPublicKey { Kid = key.Kid, Alg = NormalizeAlgorithm(key.Alg), Jwk = published, CreatedOn = key.CreatedOn, RetiredOn = key.RetiredOn };
			}
			finally
			{
				CryptographicOperations.ZeroMemory(pkcs8);
			}
		}

		/// <summary>A signed client assertion for the token request.</summary>
		public static string CreateAssertion(WorkflowSigningKey key, string clientId, string tokenUrl, DateTime utcNow, TimeSpan? lifetime = null)
		{
			if (key == null || string.IsNullOrWhiteSpace(key.PrivateKey))
				throw new InvalidOperationException("No signing key.");

			var life = lifetime ?? MaxAssertionLifetime;
			if (life > MaxAssertionLifetime || life <= TimeSpan.Zero)
				life = MaxAssertionLifetime;

			var alg = NormalizeAlgorithm(key.Alg);
			var issuedAt = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
			var header = new JObject { ["alg"] = alg, ["kid"] = key.Kid, ["typ"] = "JWT" };
			var claims = new JObject
			{
				["iss"] = clientId,
				["sub"] = clientId,
				["aud"] = tokenUrl,
				["exp"] = issuedAt + (long)life.TotalSeconds,
				["iat"] = issuedAt,
				["jti"] = Base64Url(RandomNumberGenerator.GetBytes(24))
			};

			var signingInput = Base64Url(Encoding.UTF8.GetBytes(header.ToString(Formatting.None))) + "." +
				Base64Url(Encoding.UTF8.GetBytes(claims.ToString(Formatting.None)));
			var data = Encoding.ASCII.GetBytes(signingInput);

			var pkcs8 = Convert.FromBase64String(key.PrivateKey);
			try
			{
				byte[] signature;
				if (alg == Es384)
				{
					using var ec = ECDsa.Create();
					ec.ImportPkcs8PrivateKey(pkcs8, out _);
					signature = ec.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
				}
				else
				{
					using var rsa = RSA.Create();
					rsa.ImportPkcs8PrivateKey(pkcs8, out _);
					signature = rsa.SignData(data, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);
				}

				return signingInput + "." + Base64Url(signature);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(pkcs8);
			}
		}

		/// <summary>Verifies an assertion against a public JWK (tests and diagnostics).</summary>
		public static bool Verify(string assertion, JObject jwk)
		{
			var parts = assertion?.Split('.');
			if (parts == null || parts.Length != 3 || jwk == null)
				return false;

			var data = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
			var signature = FromBase64Url(parts[2]);
			if ((string)jwk["kty"] == "EC")
			{
				using var ec = ECDsa.Create(new ECParameters
				{
					Curve = ECCurve.NamedCurves.nistP384,
					Q = new ECPoint { X = FromBase64Url((string)jwk["x"]), Y = FromBase64Url((string)jwk["y"]) }
				});
				return ec.VerifyData(data, signature, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
			}

			using var rsa = RSA.Create(new RSAParameters { Modulus = FromBase64Url((string)jwk["n"]), Exponent = FromBase64Url((string)jwk["e"]) });
			return rsa.VerifyData(data, signature, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);
		}

		/// <summary>RFC 7638 thumbprint: SHA-256 over the required members in lexicographic order.</summary>
		private static string Thumbprint(JObject requiredMembers)
		{
			var ordered = new JObject(requiredMembers.Properties().OrderBy(p => p.Name, StringComparer.Ordinal));
			return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(ordered.ToString(Formatting.None))));
		}

		public static string Base64Url(byte[] data) =>
			Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		public static byte[] FromBase64Url(string text)
		{
			var s = (text ?? string.Empty).Replace('-', '+').Replace('_', '/');
			switch (s.Length % 4)
			{
				case 2: s += "=="; break;
				case 3: s += "="; break;
			}
			return Convert.FromBase64String(s);
		}
	}
}
