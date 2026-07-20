using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DepartmentSsoServiceTests
	{
		private Mock<IDepartmentSsoConfigRepository> _ssoConfigRepository;
		private Mock<IEncryptionService> _encryptionService;
		private Mock<ICacheProvider> _cacheProvider;
		private int _samlReplayUseCount;
		private DepartmentSsoService _service;

		[SetUp]
		public void SetUp()
		{
			_ssoConfigRepository = new Mock<IDepartmentSsoConfigRepository>();
			_encryptionService = new Mock<IEncryptionService>();
			_cacheProvider = new Mock<ICacheProvider>();
			_samlReplayUseCount = 0;
			_encryptionService
				.Setup(x => x.EncryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((string value, int _, string _) => $"encrypted:{value}");
			_cacheProvider
				.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(() => Interlocked.Increment(ref _samlReplayUseCount));

			_service = new DepartmentSsoService(
				_ssoConfigRepository.Object,
				new Mock<IDepartmentSecurityPolicyRepository>().Object,
				new Mock<IDepartmentMembersRepository>().Object,
				new Mock<IDepartmentsService>().Object,
				new Mock<IUserProfileService>().Object,
				_encryptionService.Object,
				_cacheProvider.Object);
		}

		[Test]
		public async Task SaveSsoConfigAsync_NewConfigWithPreallocatedId_UsesInsertInsteadOfUpdate()
		{
			// Arrange
			var config = CreateSamlConfig();
			_ssoConfigRepository
				.Setup(x => x.GetByDepartmentIdAndTypeAsync(config.DepartmentId, SsoProviderType.Saml2))
				.ReturnsAsync((DepartmentSsoConfig)null);
			_ssoConfigRepository
				.Setup(x => x.InsertAsync(config, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(config);

			// Act
			var saved = await _service.SaveSsoConfigAsync(config, "DEPT");

			// Assert
			saved.DepartmentSsoConfigId.Should().Be(config.DepartmentSsoConfigId);
			saved.EncryptedIdpCertificate.Should().Be("encrypted:certificate");
			_ssoConfigRepository.Verify(x => x.InsertAsync(config, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_ssoConfigRepository.Verify(x => x.UpdateAsync(It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_ssoConfigRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SaveSsoConfigAsync_BlankSecretsOnUpdate_PreservesStoredCiphertexts()
		{
			// Arrange
			var stored = CreateSamlConfig();
			stored.EncryptedClientSecret = "stored-client-secret";
			stored.EncryptedIdpCertificate = "stored-idp-certificate";
			stored.EncryptedSigningCertificate = "stored-signing-certificate";
			stored.EncryptedScimBearerToken = "stored-scim-token";

			var update = CreateSamlConfig();
			update.EncryptedClientSecret = null;
			update.EncryptedIdpCertificate = null;
			update.EncryptedSigningCertificate = null;
			update.EncryptedScimBearerToken = null;

			_ssoConfigRepository
				.Setup(x => x.GetByDepartmentIdAndTypeAsync(update.DepartmentId, SsoProviderType.Saml2))
				.ReturnsAsync(stored);
			_ssoConfigRepository
				.Setup(x => x.UpdateAsync(update, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(update);

			// Act
			var saved = await _service.SaveSsoConfigAsync(update, "DEPT");

			// Assert
			saved.EncryptedClientSecret.Should().Be("stored-client-secret");
			saved.EncryptedIdpCertificate.Should().Be("stored-idp-certificate");
			saved.EncryptedSigningCertificate.Should().Be("stored-signing-certificate");
			saved.EncryptedScimBearerToken.Should().Be("stored-scim-token");
			_ssoConfigRepository.Verify(x => x.UpdateAsync(update, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task ValidateExternalTokenAsync_UnsignedSamlAssertion_IsRejected()
		{
			// Arrange
			using var rsa = RSA.Create(2048);
			using var certificate = CreateCertificate(rsa);
			var config = CreateSamlConfig();
			config.IsEnabled = true;
			config.EncryptedIdpCertificate = "stored-certificate";
			_ssoConfigRepository
				.Setup(x => x.GetByDepartmentIdAndTypeAsync(config.DepartmentId, SsoProviderType.Saml2))
				.ReturnsAsync(config);
			_encryptionService
				.Setup(x => x.DecryptForDepartment("stored-certificate", config.DepartmentId, "DEPT"))
				.Returns(certificate.ExportCertificatePem());
			var samlResponse = BuildSamlResponse(config, rsa, signAssertion: false);

			// Act
			var principal = await _service.ValidateExternalTokenAsync(
				config.DepartmentId, SsoProviderType.Saml2, samlResponse, "DEPT");

			// Assert
			principal.Should().BeNull();
		}

		[Test]
		public async Task ValidateExternalTokenAsync_SignedSamlAssertion_ValidatesOnceAndRejectsReplay()
		{
			// Arrange
			using var rsa = RSA.Create(2048);
			using var certificate = CreateCertificate(rsa);
			var config = CreateSamlConfig();
			config.IsEnabled = true;
			config.EncryptedIdpCertificate = "stored-certificate";
			_ssoConfigRepository
				.Setup(x => x.GetByDepartmentIdAndTypeAsync(config.DepartmentId, SsoProviderType.Saml2))
				.ReturnsAsync(config);
			_encryptionService
				.Setup(x => x.DecryptForDepartment("stored-certificate", config.DepartmentId, "DEPT"))
				.Returns(certificate.ExportCertificatePem());
			var samlResponse = BuildSamlResponse(config, rsa, signAssertion: true);

			// Act
			var firstAttempt = await _service.ValidateExternalTokenAsync(
				config.DepartmentId, SsoProviderType.Saml2, samlResponse, "DEPT");
			var replayAttempt = await _service.ValidateExternalTokenAsync(
				config.DepartmentId, SsoProviderType.Saml2, samlResponse, "DEPT");

			// Assert
			firstAttempt.Should().NotBeNull();
			firstAttempt.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("external-user");
			firstAttempt.FindFirst(ClaimTypes.Email)?.Value.Should().Be("user@example.com");
			replayAttempt.Should().BeNull();
		}

		[Test]
		public async Task ValidateExternalTokenAsync_SignedSamlAssertionForWrongAudience_IsRejected()
		{
			// Arrange
			using var rsa = RSA.Create(2048);
			using var certificate = CreateCertificate(rsa);
			var config = CreateSamlConfig();
			config.IsEnabled = true;
			config.EncryptedIdpCertificate = "stored-certificate";
			_ssoConfigRepository
				.Setup(x => x.GetByDepartmentIdAndTypeAsync(config.DepartmentId, SsoProviderType.Saml2))
				.ReturnsAsync(config);
			_encryptionService
				.Setup(x => x.DecryptForDepartment("stored-certificate", config.DepartmentId, "DEPT"))
				.Returns(certificate.ExportCertificatePem());
			var samlResponse = BuildSamlResponse(config, rsa, signAssertion: true, audience: "https://another-service.example.test");

			// Act
			var principal = await _service.ValidateExternalTokenAsync(
				config.DepartmentId, SsoProviderType.Saml2, samlResponse, "DEPT");

			// Assert
			principal.Should().BeNull();
		}

		private static DepartmentSsoConfig CreateSamlConfig()
		{
			return new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = Guid.NewGuid().ToString(),
				DepartmentId = 42,
				SsoProviderType = (int)SsoProviderType.Saml2,
				EntityId = "https://api.resgrid.test/saml/test",
				AssertionConsumerServiceUrl = "https://api.resgrid.test/api/v4/connect/saml-mobile-callback",
				EncryptedIdpCertificate = "certificate",
				CreatedByUserId = "admin",
				CreatedOn = DateTime.UtcNow
			};
		}

		private static X509Certificate2 CreateCertificate(RSA rsa)
		{
			var request = new CertificateRequest("CN=Test SAML IdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
		}

		private static string BuildSamlResponse(DepartmentSsoConfig config, RSA signingKey, bool signAssertion, string audience = null)
		{
			var now = DateTime.UtcNow;
			var assertionId = $"_{Guid.NewGuid():N}";
			var responseId = $"_{Guid.NewGuid():N}";
			var notBefore = XmlConvert.ToString(now.AddMinutes(-1), XmlDateTimeSerializationMode.Utc);
			var notOnOrAfter = XmlConvert.ToString(now.AddMinutes(5), XmlDateTimeSerializationMode.Utc);
			var issueInstant = XmlConvert.ToString(now, XmlDateTimeSerializationMode.Utc);
			var xml = $"""
				<samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion" ID="{responseId}" Version="2.0" IssueInstant="{issueInstant}" Destination="{config.AssertionConsumerServiceUrl}">
				  <saml:Issuer>https://idp.example.test</saml:Issuer>
				  <samlp:Status><samlp:StatusCode Value="urn:oasis:names:tc:SAML:2.0:status:Success" /></samlp:Status>
				  <saml:Assertion ID="{assertionId}" Version="2.0" IssueInstant="{issueInstant}">
				    <saml:Issuer>https://idp.example.test</saml:Issuer>
				    <saml:Subject>
				      <saml:NameID>external-user</saml:NameID>
				      <saml:SubjectConfirmation Method="urn:oasis:names:tc:SAML:2.0:cm:bearer">
				        <saml:SubjectConfirmationData Recipient="{config.AssertionConsumerServiceUrl}" NotOnOrAfter="{notOnOrAfter}" />
				      </saml:SubjectConfirmation>
				    </saml:Subject>
				    <saml:Conditions NotBefore="{notBefore}" NotOnOrAfter="{notOnOrAfter}">
					  <saml:AudienceRestriction><saml:Audience>{audience ?? config.EntityId}</saml:Audience></saml:AudienceRestriction>
				    </saml:Conditions>
				    <saml:AttributeStatement>
				      <saml:Attribute Name="email"><saml:AttributeValue>user@example.com</saml:AttributeValue></saml:Attribute>
				    </saml:AttributeStatement>
				  </saml:Assertion>
				</samlp:Response>
				""";

			var document = new XmlDocument { PreserveWhitespace = true };
			document.LoadXml(xml);
			if (signAssertion)
			{
				var assertion = (XmlElement)document.SelectSingleNode("/samlp:Response/saml:Assertion", CreateSamlNamespaces(document));
				var signedXml = new SignedXml(assertion) { SigningKey = signingKey };
				signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
				signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
				var reference = new Reference($"#{assertionId}") { DigestMethod = SignedXml.XmlDsigSHA256Url };
				reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
				reference.AddTransform(new XmlDsigExcC14NTransform());
				signedXml.AddReference(reference);
				signedXml.ComputeSignature();
				var signature = document.ImportNode(signedXml.GetXml(), deep: true);
				assertion.InsertAfter(signature, assertion.FirstChild);
			}

			return Convert.ToBase64String(Encoding.UTF8.GetBytes(document.OuterXml));
		}

		private static XmlNamespaceManager CreateSamlNamespaces(XmlDocument document)
		{
			var namespaces = new XmlNamespaceManager(document.NameTable);
			namespaces.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
			namespaces.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
			return namespaces;
		}
	}
}
