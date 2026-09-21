using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Resgrid.Providers.Chatbot.Services
{
	public static class ChatbotWebhookSignature
	{
		public static bool EqualsSecret(string actual, string expected) => !string.IsNullOrWhiteSpace(expected)
			&& !string.IsNullOrWhiteSpace(actual) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
		public static bool Hmac(string body, string secret, string signature, bool base64 = false, string prefix = "")
		{
			if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature)) return false;
			var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
			return EqualsSecret(signature, prefix + (base64 ? Convert.ToBase64String(hash) : Convert.ToHexString(hash).ToLowerInvariant()));
		}
		public static bool Recent(string timestamp) => long.TryParse(timestamp, out var seconds)
			&& Math.Abs((decimal)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) <= 300;
		public static bool Slack(string body, string secret, string timestamp, string signature)
			=> Recent(timestamp) && Hmac("v0:" + timestamp + ":" + body, secret, signature, prefix: "v0=");
		public static bool Discord(string body, string publicKey, string timestamp, string signature)
		{
			if (!Recent(timestamp) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature)) return false;
			try
			{
				var key = Convert.FromHexString(publicKey);
				var sig = Convert.FromHexString(signature);
				if (key.Length != 32 || sig.Length != 64) return false;
				var verifier = new Ed25519Signer();
				verifier.Init(false, new Ed25519PublicKeyParameters(key, 0));
				var bytes = Encoding.UTF8.GetBytes(timestamp + body);
				verifier.BlockUpdate(bytes, 0, bytes.Length);
				return verifier.VerifySignature(sig);
			}
			catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { return false; }
		}
	}
}
