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
			public void ShouldProduceBase64Output()
			{
				var cipher = Sut.Encrypt("test");
				byte[] bytes = null;
				Action act = () => { bytes = Convert.FromBase64String(cipher); };
				act.Should().NotThrow();
				bytes.Should().NotBeNull();
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
				const string plainText = "shared secret";
				Sut.EncryptForDepartment(plainText, 1, "DEPT1"); // ensures keys differ
				var cipher2 = Sut.EncryptForDepartment(plainText, 2, "DEPT2");

				Action act = () => Sut.DecryptForDepartment(cipher2, 1, "DEPT1");
				act.Should().Throw<Exception>("wrong department key should fail to decrypt");
			}

			[Test]
			public void SameDepartmentDifferentCodeShouldFailDecrypt()
			{
				const string plainText = "credential";
				var cipher = Sut.EncryptForDepartment(plainText, 5, "ORIG");

				Action act = () => Sut.DecryptForDepartment(cipher, 5, "DIFF");
				act.Should().Throw<Exception>("changed department code produces a different key");
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
