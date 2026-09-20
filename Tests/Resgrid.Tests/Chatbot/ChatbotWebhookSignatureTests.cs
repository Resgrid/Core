using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	public class ChatbotWebhookSignatureTests
	{
		private const string Secret = "test-signing-secret";
		private const string Body = "{\"text\":\"LINK ABC123 — 🚒\"}";

		[Test]
		public void Slack_accepts_a_valid_signature_and_rejects_body_or_timestamp_changes()
		{
			var timestamp = Timestamp();
			var signature = SlackSignature(timestamp, Body);
			Assert.That(ChatbotWebhookSignature.Slack(Body, Secret, timestamp, signature), Is.True);
			Assert.That(ChatbotWebhookSignature.Slack(Body + " ", Secret, timestamp, signature), Is.False);
			Assert.That(ChatbotWebhookSignature.Slack(Body, Secret, PreviousSecond(timestamp), signature), Is.False);
			Assert.That(ChatbotWebhookSignature.Slack(Body, "different-secret", timestamp, signature), Is.False);
		}

		[TestCase(-600)]
		[TestCase(600)]
		public void Slack_rejects_correctly_signed_stale_or_future_requests(int seconds)
		{
			var timestamp = Timestamp(seconds);
			Assert.That(ChatbotWebhookSignature.Slack(Body, Secret, timestamp, SlackSignature(timestamp, Body)), Is.False);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not-a-timestamp")]
		[TestCase("9223372036854775807")]
		[TestCase("-9223372036854775808")]
		public void Malformed_or_extreme_timestamps_fail_closed_without_overflow(string timestamp)
		{
			Assert.That(ChatbotWebhookSignature.Recent(timestamp), Is.False);
			Assert.That(ChatbotWebhookSignature.Slack(Body, Secret, timestamp, "v0=signature"), Is.False);
		}

		[Test]
		public void LINE_accepts_base64_HMAC_and_rejects_hex_or_modified_bodies()
		{
			var signature = Convert.ToBase64String(Hash(Body));
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, signature, base64: true), Is.True);
			Assert.That(ChatbotWebhookSignature.Hmac(Body + " ", Secret, signature, base64: true), Is.False);
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, Convert.ToHexString(Hash(Body)).ToLowerInvariant(), base64: true), Is.False);
		}

		[Test]
		public void Viber_accepts_hex_HMAC_and_rejects_a_modified_body()
		{
			var signature = Convert.ToHexString(Hash(Body)).ToLowerInvariant();
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, signature), Is.True);
			Assert.That(ChatbotWebhookSignature.Hmac(Body.Replace("ABC123", "ABC124"), Secret, signature), Is.False);
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, Convert.ToBase64String(Hash(Body))), Is.False);
		}

		[Test]
		public void Meta_requires_the_sha256_signature_prefix()
		{
			var signature = Convert.ToHexString(Hash(Body)).ToLowerInvariant();
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, "sha256=" + signature, prefix: "sha256="), Is.True);
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, signature, prefix: "sha256="), Is.False);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase(" ")]
		public void Missing_server_secrets_never_authenticate_a_webhook(string missing)
		{
			Assert.That(ChatbotWebhookSignature.EqualsSecret(missing, missing), Is.False);
			Assert.That(ChatbotWebhookSignature.EqualsSecret(Secret, missing), Is.False);
			Assert.That(ChatbotWebhookSignature.Hmac(Body, missing, "signature"), Is.False);
			Assert.That(ChatbotWebhookSignature.Slack(Body, missing, Timestamp(), "v0=signature"), Is.False);
			Assert.That(ChatbotWebhookSignature.Discord(Body, missing, Timestamp(), "signature"), Is.False);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase(" ")]
		public void Missing_client_signatures_fail_closed(string missing)
		{
			Assert.That(ChatbotWebhookSignature.EqualsSecret(missing, Secret), Is.False);
			Assert.That(ChatbotWebhookSignature.Hmac(Body, Secret, missing), Is.False);
			Assert.That(ChatbotWebhookSignature.Slack(Body, Secret, Timestamp(), missing), Is.False);
		}

		[Test]
		public void Discord_verifies_a_real_Ed25519_signature_over_timestamp_and_raw_body()
		{
			var timestamp = Timestamp();
			var signed = SignDiscord(timestamp, Body);
			Assert.That(ChatbotWebhookSignature.Discord(Body, signed.PublicKey, timestamp, signed.Signature), Is.True);
			Assert.That(ChatbotWebhookSignature.Discord(Body + " ", signed.PublicKey, timestamp, signed.Signature), Is.False);
			Assert.That(ChatbotWebhookSignature.Discord(Body, signed.PublicKey, PreviousSecond(timestamp), signed.Signature), Is.False);
			var modifiedSignature = Convert.FromHexString(signed.Signature);
			modifiedSignature[0] ^= 1;
			Assert.That(ChatbotWebhookSignature.Discord(Body, signed.PublicKey, timestamp, Convert.ToHexString(modifiedSignature)), Is.False);
		}

		[TestCase(-600)]
		[TestCase(600)]
		public void Discord_rejects_stale_and_future_requests_even_with_valid_signatures(int seconds)
		{
			var timestamp = Timestamp(seconds);
			var signed = SignDiscord(timestamp, Body);
			Assert.That(ChatbotWebhookSignature.Discord(Body, signed.PublicKey, timestamp, signed.Signature), Is.False);
		}

		[TestCase("not-hex", "not-hex")]
		[TestCase("00", "00")]
		[TestCase(null, null)]
		public void Discord_rejects_malformed_keys_and_signatures(string publicKey, string signature)
		{
			Assert.That(ChatbotWebhookSignature.Discord(Body, publicKey, Timestamp(), signature), Is.False);
		}

		private static string Timestamp(int seconds = 0)
			=> DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		private static string PreviousSecond(string timestamp)
			=> (long.Parse(timestamp, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture);
		private static byte[] Hash(string body)
		{
			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
			return hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
		}
		private static string SlackSignature(string timestamp, string body)
			=> "v0=" + Convert.ToHexString(Hash("v0:" + timestamp + ":" + body)).ToLowerInvariant();
		private static (string PublicKey, string Signature) SignDiscord(string timestamp, string body)
		{
			// The full test graph includes both legacy and modern BouncyCastle assemblies with
			// these type names. Bind explicitly to the modern assembly used by the provider.
			var keyType = Type.GetType("Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters, BouncyCastle.Cryptography", true);
			var signerType = Type.GetType("Org.BouncyCastle.Crypto.Signers.Ed25519Signer, BouncyCastle.Cryptography", true);
			dynamic key = Activator.CreateInstance(keyType, Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(), 0);
			dynamic signer = Activator.CreateInstance(signerType);
			signer.Init(true, key);
			var bytes = Encoding.UTF8.GetBytes(timestamp + body);
			signer.BlockUpdate(bytes, 0, bytes.Length);
			return (Convert.ToHexString((byte[])key.GeneratePublicKey().GetEncoded()), Convert.ToHexString((byte[])signer.GenerateSignature()));
		}
	}
}
