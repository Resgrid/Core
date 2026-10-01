using System;
using System.Collections.Generic;
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
	/// <summary>
	/// Passkey plan Phase 1, slice 9: a brokered SAML response must answer our AuthnRequest in a part the IdP signed, and
	/// reauthentication looks up the linked account without linking or provisioning anything.
	/// </summary>
	[TestFixture]
	public class BrokeredSamlValidationTests
	{
		private const int DepartmentId = 42;
		private const string RequestId = "_request-7c1e";

		private Mock<IDepartmentSsoConfigRepository> _configs;
		private Mock<IExternalIdentityLinkService> _links;
		private Mock<IDepartmentMembersRepository> _members;
		private Mock<ICacheProvider> _cache;
		private DepartmentSsoService _service;
		private DepartmentSsoConfig _config;
		private RSA _idpKey;
		private X509Certificate2 _certificate;
		private System.Collections.Concurrent.ConcurrentDictionary<string, int> _replayCounts;

		[SetUp]
		public void SetUp()
		{
			_idpKey = RSA.Create(2048);
			_certificate = new CertificateRequest("CN=Test SAML IdP", _idpKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
			_config = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "saml-config", DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Saml2, IsEnabled = true,
				EntityId = "https://api.resgrid.test/saml/saml-config", AssertionConsumerServiceUrl = "https://api.resgrid.test/api/v4/connect/saml-mobile-callback",
				EncryptedIdpCertificate = "stored-certificate", CreatedByUserId = "admin", CreatedOn = DateTime.UtcNow
			};

			_configs = new Mock<IDepartmentSsoConfigRepository>();
			_configs.Setup(c => c.GetByDepartmentIdAndTypeAsync(DepartmentId, SsoProviderType.Saml2)).ReturnsAsync(_config);
			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(e => e.DecryptForDepartment("stored-certificate", DepartmentId, "DEPT")).Returns(_certificate.ExportCertificatePem());
			_cache = new Mock<ICacheProvider>();
			_replayCounts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
			_cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync((string key, TimeSpan _) => (long)_replayCounts.AddOrUpdate(key, 1, (_, count) => count + 1));
			_links = new Mock<IExternalIdentityLinkService>();
			_members = new Mock<IDepartmentMembersRepository>();

			_service = new DepartmentSsoService(_configs.Object, Mock.Of<IDepartmentSecurityPolicyRepository>(), _members.Object,
				Mock.Of<IDepartmentsService>(), Mock.Of<IUserProfileService>(), encryption.Object, _cache.Object, _links.Object,
				Mock.Of<ILimitsService>(), Mock.Of<Resgrid.Model.Repositories.Queries.IUnitOfWork>(),
				Mock.Of<IDepartmentDataProtectionPolicyRepository>(),
				new Lazy<IDepartmentDataProtectionService>(() => Mock.Of<IDepartmentDataProtectionService>()), Mock.Of<IUserSessionMfaEvidenceRepository>(),
				Mock.Of<IAuditLogsRepository>());
		}

		[TearDown]
		public void TearDown()
		{
			_certificate.Dispose();
			_idpKey.Dispose();
		}

		private Task<Resgrid.Model.Security.SsoIdentityAssertion> Validate(string response, string expected = RequestId) =>
			_service.ValidateBrokeredSamlResponseAsync(DepartmentId, response, "DEPT", expected);

		[Test]
		public async Task A_signed_answer_to_our_request_validates_with_its_authn_instant()
		{
			var authnInstant = DateTime.UtcNow.AddSeconds(-30);

			var result = await Validate(Response(confirmationInResponseTo: RequestId, responseInResponseTo: RequestId, authnInstant: authnInstant));

			result.Should().NotBeNull();
			result.Principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("external-user");
			result.AuthenticatedAtUtc.Should().BeCloseTo(authnInstant, TimeSpan.FromSeconds(1));
		}

		[Test]
		public async Task An_unsolicited_or_foreign_response_is_refused()
		{
			(await Validate(Response(confirmationInResponseTo: null, responseInResponseTo: null))).Should().BeNull("IdP-initiated responses stay on the legacy relay");
			(await Validate(Response(confirmationInResponseTo: "_another-request", responseInResponseTo: "_another-request"))).Should().BeNull();
			(await Validate(Response(confirmationInResponseTo: RequestId, responseInResponseTo: "_another-request"))).Should().BeNull();
			(await Validate(Response(confirmationInResponseTo: RequestId, responseInResponseTo: RequestId), expected: null)).Should().BeNull();
		}

		[Test]
		public async Task The_binding_must_be_in_the_signed_part()
		{
			(await Validate(Response(confirmationInResponseTo: null, responseInResponseTo: RequestId))).Should()
				.BeNull("only the assertion is signed, so an InResponseTo on the unsigned Response proves nothing");

			(await Validate(Response(confirmationInResponseTo: null, responseInResponseTo: RequestId, signResponse: true))).Should()
				.NotBeNull("a signed Response carries its own InResponseTo");
		}

		[Test]
		public async Task A_brokered_response_is_still_single_use()
		{
			var response = Response(confirmationInResponseTo: RequestId, responseInResponseTo: RequestId);
			(await Validate(response)).Should().NotBeNull();
			(await Validate(response)).Should().BeNull();
		}

		[Test]
		public async Task The_legacy_path_still_accepts_an_unsolicited_response()
		{
			var principal = await _service.ValidateExternalTokenAsync(DepartmentId, SsoProviderType.Saml2,
				Response(confirmationInResponseTo: null, responseInResponseTo: null), "DEPT");
			principal.Should().NotBeNull();
		}

		[Test]
		public async Task The_legacy_identity_says_when_the_idp_authenticated_the_member()
		{
			// Under an id_token's claim, for the legacy exchange's check on shared installations (plan section 12.5.2).
			var authnInstant = DateTime.UtcNow.AddMinutes(-42);
			var principal = await _service.ValidateExternalTokenAsync(DepartmentId, SsoProviderType.Saml2,
				Response(confirmationInResponseTo: null, responseInResponseTo: null, authnInstant: authnInstant), "DEPT");

			principal.FindFirst(Resgrid.Model.Security.ProviderSignInTime.ClaimType)!.ValueType.Should().Be(ClaimValueTypes.Integer64);
			Resgrid.Model.Security.ProviderSignInTime.Read(principal).Should().BeCloseTo(authnInstant, TimeSpan.FromSeconds(1));
		}

		[Test]
		public async Task Reauthentication_finds_the_linked_account_without_linking_anything()
		{
			var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "external-user") }, "SAML2"));
			_links.Setup(l => l.GetBySubjectAsync("saml-config", "external-user", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new UserExternalIdentityLink { UserId = "user-1", DepartmentId = DepartmentId });
			(await _service.FindLinkedUserIdAsync(DepartmentId, principal, _config)).Should().Be("user-1");

			_links.Setup(l => l.GetBySubjectAsync("saml-config", "external-user", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new UserExternalIdentityLink { UserId = "user-1", DepartmentId = 99 });
			(await _service.FindLinkedUserIdAsync(DepartmentId, principal, _config)).Should().BeNull("a link from another department never counts");

			_links.Setup(l => l.GetBySubjectAsync("saml-config", "external-user", It.IsAny<CancellationToken>())).ReturnsAsync((UserExternalIdentityLink)null);
			_members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(DepartmentId))
				.ReturnsAsync(new List<DepartmentMember> { new() { UserId = "user-2", ExternalSsoId = "external-user" } });
			(await _service.FindLinkedUserIdAsync(DepartmentId, principal, _config)).Should().Be("user-2", "links from before the binding table still count");

			var unlinked = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.NameIdentifier, "stranger"), new Claim(ClaimTypes.Email, "user@example.com"), new Claim("email_verified", "true")
			}, "SAML2"));
			(await _service.FindLinkedUserIdAsync(DepartmentId, unlinked, _config)).Should().BeNull("no email bootstrap and no provisioning");

			_members.Verify(m => m.SaveOrUpdateAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_links.Verify(l => l.SaveAsync(It.IsAny<UserExternalIdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private string Response(string confirmationInResponseTo, string responseInResponseTo, DateTime? authnInstant = null, bool signResponse = false)
		{
			var now = DateTime.UtcNow;
			var assertionId = $"_{Guid.NewGuid():N}";
			var responseId = $"_{Guid.NewGuid():N}";
			string Instant(DateTime value) => XmlConvert.ToString(value, XmlDateTimeSerializationMode.Utc);
			var responseBinding = responseInResponseTo == null ? "" : $" InResponseTo=\"{responseInResponseTo}\"";
			var confirmationBinding = confirmationInResponseTo == null ? "" : $" InResponseTo=\"{confirmationInResponseTo}\"";
			var xml = $"""
				<samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion" ID="{responseId}" Version="2.0" IssueInstant="{Instant(now)}" Destination="{_config.AssertionConsumerServiceUrl}"{responseBinding}>
				  <saml:Issuer>https://idp.example.test</saml:Issuer>
				  <samlp:Status><samlp:StatusCode Value="urn:oasis:names:tc:SAML:2.0:status:Success" /></samlp:Status>
				  <saml:Assertion ID="{assertionId}" Version="2.0" IssueInstant="{Instant(now)}">
				    <saml:Issuer>https://idp.example.test</saml:Issuer>
				    <saml:Subject>
				      <saml:NameID>external-user</saml:NameID>
				      <saml:SubjectConfirmation Method="urn:oasis:names:tc:SAML:2.0:cm:bearer">
				        <saml:SubjectConfirmationData Recipient="{_config.AssertionConsumerServiceUrl}" NotOnOrAfter="{Instant(now.AddMinutes(5))}"{confirmationBinding} />
				      </saml:SubjectConfirmation>
				    </saml:Subject>
				    <saml:Conditions NotBefore="{Instant(now.AddMinutes(-1))}" NotOnOrAfter="{Instant(now.AddMinutes(5))}">
				      <saml:AudienceRestriction><saml:Audience>{_config.EntityId}</saml:Audience></saml:AudienceRestriction>
				    </saml:Conditions>
				    <saml:AuthnStatement AuthnInstant="{Instant(authnInstant ?? now)}" />
				    <saml:AttributeStatement>
				      <saml:Attribute Name="email"><saml:AttributeValue>user@example.com</saml:AttributeValue></saml:Attribute>
				    </saml:AttributeStatement>
				  </saml:Assertion>
				</samlp:Response>
				""";

			var document = new XmlDocument { PreserveWhitespace = true };
			document.LoadXml(xml);
			var namespaces = new XmlNamespaceManager(document.NameTable);
			namespaces.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
			namespaces.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
			var target = signResponse ? document.DocumentElement! : (XmlElement)document.SelectSingleNode("/samlp:Response/saml:Assertion", namespaces)!;
			var signedXml = new SignedXml(target) { SigningKey = _idpKey };
			signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
			signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
			var reference = new Reference($"#{(signResponse ? responseId : assertionId)}") { DigestMethod = SignedXml.XmlDsigSHA256Url };
			reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
			reference.AddTransform(new XmlDsigExcC14NTransform());
			signedXml.AddReference(reference);
			signedXml.ComputeSignature();
			var signature = document.ImportNode(signedXml.GetXml(), deep: true);
			target.InsertAfter(signature, target.FirstChild);
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(document.OuterXml));
		}
	}
}
