using System;
using System.Security.Cryptography;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Framework.Testing;

namespace Resgrid.Tests.Services
{
	namespace EncryptionServiceTests
	{
		public class with_the_encryption_service : TestBase
		{
			protected IEncryptionService Sut;

			protected with_the_encryption_service()
			{
				// Set known test key/salt so tests are deterministic.
				// Use a low iteration count in tests to keep the suite fast;
				// the production value (600,000) is set via SecurityConfig.Pbkdf2Iterations.
				SecurityConfig.EncryptionKey       = "TestMasterKey1234567890_32Chars!";
				SecurityConfig.EncryptionSaltValue = "TestSaltValue_1234567890";
				SecurityConfig.Pbkdf2Iterations    = 1000;
				Sut = Resolve<IEncryptionService>();
			}
		}

		[TestFixture]
		public class WhenEncryptingAndDecryptingWithGlobalKey : with_the_encryption_service
		{
			[Test]
			public void ShouldRoundtripPlaintext()
			{
				const string original = "Hello, Resgrid Workflow Engine!";
				var decrypted = Sut.Decrypt(Sut.Encrypt(original));
				decrypted.Should().Be(original);
			}

			[Test]
			public void ShouldProduceDifferentCiphertextEachCallDueToRandomIv()
			{
				const string original = "same plaintext";
				var cipher1 = Sut.Encrypt(original);
				var cipher2 = Sut.Encrypt(original);
				cipher1.Should().NotBe(cipher2, "each call generates a fresh IV");
			}

			[Test]
			public void ShouldProduceVersionedBase64Output()
			{
				var cipher = Sut.Encrypt("test");
				cipher.Should().StartWith("enc2:", "new ciphertexts carry the GCM format prefix");

				byte[] bytes = null;
				Action act = () => { bytes = Convert.FromBase64String(cipher.Substring("enc2:".Length)); };
				act.Should().NotThrow();
				bytes.Should().NotBeNull();
			}

			[Test]
			public void ShouldThrowOnTamperedCiphertext()
			{
				var cipher = Sut.Encrypt("integrity matters");

				// Flip one character in the Base64 body (past the prefix and nonce region).
				var chars = cipher.ToCharArray();
				var index = chars.Length - 2;
				chars[index] = chars[index] == 'A' ? 'B' : 'A';

				Action act = () => Sut.Decrypt(new string(chars));
				act.Should().Throw<CryptographicException>("GCM authenticates the payload, so any tampering must fail the tag check");
			}

			[Test]
			public void ShouldDecryptLegacyCbcCiphertext()
			{
				// Fixed pre-GCM (AES-256-CBC/PKCS7, IV-prefixed, unversioned Base64) ciphertext of
				// "legacy global secret" under the fixture's test key/salt/iterations. Guards the
				// legacy fallback path that existing data at rest depends on.
				const string legacyCipher = "AQIDBAUGBwgJCgsMDQ4PEPTtkp0LPqxjlBC8ofdOfEjmKsxM3zYWcppjkR3460HA";
				Sut.Decrypt(legacyCipher).Should().Be("legacy global secret");
			}

			[Test]
			public void ShouldThrowOnNullPlaintext()
			{
				Action act = () => Sut.Encrypt(null);
				act.Should().Throw<ArgumentNullException>();
			}

			[Test]
			public void ShouldThrowOnNullCiphertextForDecrypt()
			{
				Action act = () => Sut.Decrypt(null);
				act.Should().Throw<ArgumentNullException>();
			}

			[Test]
			public void ShouldThrowOnInvalidBase64()
			{
				Action act = () => Sut.Decrypt("not-valid-base64!!!");
				act.Should().Throw<Exception>("invalid base64 should throw");
			}

			[Test]
			public void ShouldEncryptEmptyStringSuccessfully()
			{
				var decrypted = Sut.Decrypt(Sut.Encrypt(string.Empty));
				decrypted.Should().Be(string.Empty);
			}

			[Test]
			public void ShouldHandleUnicodeCharacters()
			{
				const string unicode = "Ré-sgrìd Wörk∫løw";
				Sut.Decrypt(Sut.Encrypt(unicode)).Should().Be(unicode);
			}
		}

		[TestFixture]
		public class WhenEncryptingAndDecryptingWithDepartmentKey : with_the_encryption_service
		{
			[Test]
			public void ShouldRoundtripDepartmentPlaintext()
			{
				const string original = "{\"AccountSid\":\"ACxxx\",\"AuthToken\":\"secret\"}";
				const int deptId      = 42;
				const string deptCode = "FDBC";

				var decrypted = Sut.DecryptForDepartment(Sut.EncryptForDepartment(original, deptId, deptCode), deptId, deptCode);
				decrypted.Should().Be(original);
			}

			[Test]
			public void DepartmentKeyShouldDifferFromGlobalKey()
			{
				const string plainText  = "secret credential";
				var globalCipher        = Sut.Encrypt(plainText);
				var deptCipher          = Sut.EncryptForDepartment(plainText, 1, "TST1");

				globalCipher.Should().NotBe(deptCipher);
				Sut.Decrypt(globalCipher).Should().Be(plainText);
				Sut.DecryptForDepartment(deptCipher, 1, "TST1").Should().Be(plainText);
			}

			[Test]
			public void DifferentDepartmentsShouldProduceDifferentCiphertexts()
			{
				// Deterministic with GCM: a wrong key always fails the authentication tag check.
				// (Under the old CBC format this was a flaky padding-check assertion.)
				const string plainText = "shared secret";
				Sut.EncryptForDepartment(plainText, 1, "DEPT1"); // ensures keys differ
				var cipher2 = Sut.EncryptForDepartment(plainText, 2, "DEPT2");

				Action act = () => Sut.DecryptForDepartment(cipher2, 1, "DEPT1");
				act.Should().Throw<CryptographicException>("wrong department key must fail the GCM tag check");
			}

			[Test]
			public void SameDepartmentDifferentCodeShouldFailDecrypt()
			{
				const string plainText = "credential";
				var cipher = Sut.EncryptForDepartment(plainText, 5, "ORIG");

				Action act = () => Sut.DecryptForDepartment(cipher, 5, "DIFF");
				act.Should().Throw<CryptographicException>("changed department code produces a different key, which must fail the GCM tag check");
			}

			[Test]
			public void ShouldDecryptLegacyCbcDepartmentCiphertext()
			{
				// Fixed pre-GCM (AES-256-CBC/PKCS7, IV-prefixed, unversioned Base64) ciphertext of
				// "legacy department secret" for department 5 / code "ORIG" under the fixture's test
				// key/salt/iterations. Guards the legacy fallback for stored department credentials.
				const string legacyCipher = "BwgJCgsMDQ4PEBESExQVFpXSN+nRnbOBO6F8HQ/JWEeucx3tCtsB9fO+TMX4ia4W";
				Sut.DecryptForDepartment(legacyCipher, 5, "ORIG").Should().Be("legacy department secret");
			}

			[Test]
			public void ShouldHandleNullDepartmentCodeGracefully()
			{
				const string plainText = "null code test";
				var decrypted = Sut.DecryptForDepartment(Sut.EncryptForDepartment(plainText, 10, null), 10, null);
				decrypted.Should().Be(plainText);
			}

			[Test]
			public void ShouldThrowOnNullPlaintext()
			{
				Action act = () => Sut.EncryptForDepartment(null, 1, "TST1");
				act.Should().Throw<ArgumentNullException>();
			}
		}

		[TestFixture]
		public class WhenEncryptingLargePayloads : with_the_encryption_service
		{
			[Test]
			public void ShouldHandleLargeJsonPayload()
			{
				var largeJson = "{\"key\":\"" + new string('x', 10000) + "\"}";
				Sut.Decrypt(Sut.Encrypt(largeJson)).Should().Be(largeJson);
			}
		}
	}
}
