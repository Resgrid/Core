using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ProtectedFieldCryptoServiceTests
	{
		private ProtectedFieldCryptoService _crypto;
		private byte[] _dek;

		[SetUp]
		public void SetUp()
		{
			_crypto = new ProtectedFieldCryptoService();
			_dek = new byte[32];
			RandomNumberGenerator.Fill(_dek);
		}

		[Test]
		public void Text_round_trip_preserves_value_and_envelope_shape()
		{
			var envelope = _crypto.EncryptText(_dek, 3, "Chest pain, 62yo male", 42, "calls.natureofcall", "1001");

			envelope.Should().StartWith("rgdp:1:3:");
			ProtectedDataEnvelope.IsEnveloped(envelope).Should().BeTrue();

			_crypto.DecryptText(_dek, envelope, 42, "calls.natureofcall", "1001")
				.Should().Be("Chest pain, 62yo male");
		}

		[TestCase(43, "calls.natureofcall", "1001", Description = "different department")]
		[TestCase(42, "calls.notes", "1001", Description = "different field")]
		[TestCase(42, "calls.natureofcall", "1002", Description = "different row")]
		public void Any_aad_component_mismatch_fails_authentication(int departmentId, string fieldId, string rowKey)
		{
			var envelope = _crypto.EncryptText(_dek, 1, "secret", 42, "calls.natureofcall", "1001");

			var act = () => _crypto.DecryptText(_dek, envelope, departmentId, fieldId, rowKey);
			act.Should().Throw<CryptographicException>(
				"moving ciphertext between tenants, rows, or fields must fail AEAD authentication");
		}

		/// <summary>
		/// The catalog version is deliberately NOT an AAD component. If it were, advancing a
		/// department's pinned catalog version would make its entire stored corpus undecryptable,
		/// so every catalog addition would force a full decrypt/re-encrypt of every protected row.
		/// Field ids are stable forever, so the field id already provides the binding.
		/// </summary>
		[Test]
		public void Catalog_version_is_not_bound_so_a_catalog_upgrade_never_breaks_stored_envelopes()
		{
			var envelope = _crypto.EncryptText(_dek, 1, "Chest pain, 62yo male", 42, "calls.natureofcall", "1001");

			// Same department, field and row — the department has since moved to a later catalog.
			_crypto.DecryptText(_dek, envelope, 42, "calls.natureofcall", "1001")
				.Should().Be("Chest pain, 62yo male");
		}

		[Test]
		public void Tampered_ciphertext_fails_authentication()
		{
			var envelope = _crypto.EncryptText(_dek, 1, "secret", 42, "calls.notes", "7");
			var payload = Convert.FromBase64String(envelope.Split(':', 4)[3]);
			payload[payload.Length - 1] ^= 0x01;
			var tampered = "rgdp:1:1:" + Convert.ToBase64String(payload);

			var act = () => _crypto.DecryptText(_dek, tampered, 42, "calls.notes", "7");
			act.Should().Throw<CryptographicException>();
		}

		[Test]
		public void Encrypting_an_enveloped_value_is_refused()
		{
			var envelope = _crypto.EncryptText(_dek, 1, "secret", 42, "calls.notes", "7");

			var act = () => _crypto.EncryptText(_dek, 1, envelope, 42, "calls.notes", "7");
			act.Should().Throw<InvalidOperationException>(
				"the double-encryption guard must make re-encrypting an envelope impossible");
		}

		[Test]
		public void Binary_round_trip_with_header_and_key_version()
		{
			var blob = new byte[2048];
			RandomNumberGenerator.Fill(blob);

			var envelope = _crypto.EncryptBinary(_dek, 5, blob, 42, "callattachments.data", "88");

			_crypto.IsBinaryEnveloped(envelope).Should().BeTrue();
			Encoding.ASCII.GetString(envelope, 0, 10).Should().Be("rgdpb:1:5:");
			_crypto.TryGetBinaryEnvelopeKeyVersion(envelope, out var keyVersion).Should().BeTrue();
			keyVersion.Should().Be(5);

			_crypto.DecryptBinary(_dek, envelope, 42, "callattachments.data", "88").Should().Equal(blob);
		}

		[Test]
		public void Binary_double_encrypt_is_refused_and_plain_blobs_are_not_enveloped()
		{
			var blob = new byte[64];
			RandomNumberGenerator.Fill(blob);
			// Guarantee the random blob cannot accidentally start with the header.
			blob[0] = 0x00;

			_crypto.IsBinaryEnveloped(blob).Should().BeFalse();
			_crypto.TryGetBinaryEnvelopeKeyVersion(blob, out _).Should().BeFalse();

			var envelope = _crypto.EncryptBinary(_dek, 1, blob, 42, "contacts.image", "c-1");
			var act = () => _crypto.EncryptBinary(_dek, 1, envelope, 42, "contacts.image", "c-1");
			act.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Binary_encrypt_refuses_a_non_positive_key_version()
		{
			// A zero/negative version would produce a blob TryParseBinaryHeader always rejects —
			// an undecryptable write must fail at write time (same invariant as the text path).
			var blob = new byte[16];
			RandomNumberGenerator.Fill(blob);
			blob[0] = 0x00;

			var zero = () => _crypto.EncryptBinary(_dek, 0, blob, 42, "contacts.image", "c-1");
			zero.Should().Throw<ArgumentOutOfRangeException>();

			var negative = () => _crypto.EncryptBinary(_dek, -3, blob, 42, "contacts.image", "c-1");
			negative.Should().Throw<ArgumentOutOfRangeException>();
		}

		[Test]
		public void Binary_aad_mismatch_fails_authentication()
		{
			var blob = new byte[16];
			RandomNumberGenerator.Fill(blob);
			blob[0] = 0x00;

			var envelope = _crypto.EncryptBinary(_dek, 1, blob, 42, "contacts.image", "c-1");

			var act = () => _crypto.DecryptBinary(_dek, envelope, 43, "contacts.image", "c-1");
			act.Should().Throw<CryptographicException>();
		}

		[Test]
		public void Wrong_dek_size_is_rejected()
		{
			var shortKey = new byte[16];
			var act = () => _crypto.EncryptText(shortKey, 1, "x", 42, "calls.notes", "7");
			act.Should().Throw<ArgumentException>();
		}
	}
}
