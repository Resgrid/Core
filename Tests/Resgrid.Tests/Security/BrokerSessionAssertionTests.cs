using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The identity tier's session assertion to the broker (workbook section 6.2): a dedicated key, a short life, and one
	/// request only. Nothing else signed by Resgrid (a grant in particular) passes as an assertion.
	/// </summary>
	[TestFixture]
	public class BrokerSessionAssertionTests
	{
		private X509Certificate2 _assertionCertificate;
		private X509Certificate2 _otherCertificate;
		private BrokerSessionAssertionService _service;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_assertionCertificate = Create("CN=assertion-tests");
			_otherCertificate = Create("CN=other-key");
		}

		private static X509Certificate2 Create(string name)
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			return new CertificateRequest(name, ecdsa, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_assertionCertificate?.Dispose();
			_otherCertificate?.Dispose();
		}

		[SetUp]
		public void SetUp() => _service = new BrokerSessionAssertionService(() => _assertionCertificate, () => _assertionCertificate);

		private static ProtectedFieldOperationItem[] Items() => new[]
		{
			new ProtectedFieldOperationItem { FieldId = "calls.natureofcall", RowKey = "17", Value = "rgdp:1:1:abc", CatalogVersion = 3 }
		};

		private static string Digest() => BrokerRequestDigest.Compute("decrypt", 42, "req-1", Items());

		private static BrokerSessionAssertion Facts(DateTime? credentialIssuedOn = null) => new()
		{
			UserId = "user-1",
			SessionId = "session-9",
			AuthenticationGeneration = 4,
			DepartmentId = 42,
			ClientApplication = (int)UserSessionClientApplication.Web,
			SessionLockVersion = 2,
			CredentialIssuedOnUtc = credentialIssuedOn,
			RequestDigest = Digest()
		};

		[Test]
		public void An_assertion_round_trips_the_session_facts()
		{
			var credentialIssued = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);
			var token = _service.Mint(Facts(credentialIssued));

			_service.Validate(token, Digest(), out var assertion).Should().Be(BrokerSessionAssertionOutcome.Valid);

			assertion.UserId.Should().Be("user-1");
			assertion.SessionId.Should().Be("session-9");
			assertion.AuthenticationGeneration.Should().Be(4);
			assertion.DepartmentId.Should().Be(42);
			assertion.ClientApplication.Should().Be((int)UserSessionClientApplication.Web);
			assertion.SessionLockVersion.Should().Be(2);
			assertion.CredentialIssuedOnUtc.Should().Be(credentialIssued);
			assertion.AssertionId.Should().NotBeNullOrWhiteSpace();
			(assertion.ExpiresOnUtc - assertion.IssuedAtUtc).Should().BeCloseTo(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1));
		}

		[Test]
		public void Each_assertion_has_its_own_id()
		{
			_service.Validate(_service.Mint(Facts()), Digest(), out var first);
			_service.Validate(_service.Mint(Facts()), Digest(), out var second);

			first.AssertionId.Should().NotBe(second.AssertionId);
		}

		[Test]
		public void An_assertion_for_another_request_is_refused()
		{
			var token = _service.Mint(Facts());
			var otherItems = Items();
			otherItems[0].RowKey = "18";

			_service.Validate(token, BrokerRequestDigest.Compute("decrypt", 42, "req-1", otherItems), out var assertion)
				.Should().Be(BrokerSessionAssertionOutcome.RequestMismatch);
			assertion.Should().BeNull();
		}

		[Test]
		public void An_expired_assertion_is_refused()
		{
			_service.Validate(_service.Mint(Facts()), Digest(), out _, DateTime.UtcNow.AddMinutes(3))
				.Should().Be(BrokerSessionAssertionOutcome.Expired);
		}

		[Test]
		public void An_assertion_signed_by_any_other_key_is_invalid()
		{
			var forger = new BrokerSessionAssertionService(() => _otherCertificate, () => _otherCertificate);

			_service.Validate(forger.Mint(Facts()), Digest(), out _).Should().Be(BrokerSessionAssertionOutcome.Invalid);
		}

		[Test]
		public void A_protected_data_grant_is_not_an_assertion_even_with_the_same_key()
		{
			var grants = new ProtectedDataGrantService(() => _assertionCertificate, () => _assertionCertificate);
			var grant = grants.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				DepartmentId = 42,
				SessionId = "session-9",
				Scopes = new[] { ProtectedDataGrantScopes.Read },
				WindowMinutes = 15,
				MfaAtUtc = DateTime.UtcNow
			}).Token;

			_service.Validate(grant, Digest(), out _).Should().Be(BrokerSessionAssertionOutcome.Invalid, "issuer and audience differ");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not.a.token")]
		public void Garbage_is_invalid(string token)
		{
			_service.Validate(token, Digest(), out _).Should().Be(BrokerSessionAssertionOutcome.Invalid);
		}

		[Test]
		public void Without_a_certificate_nothing_is_minted_or_accepted()
		{
			var unconfigured = new BrokerSessionAssertionService(() => null, () => null);

			unconfigured.CanMint.Should().BeFalse();
			unconfigured.CanValidate.Should().BeFalse();
			unconfigured.Validate(_service.Mint(Facts()), Digest(), out _).Should().Be(BrokerSessionAssertionOutcome.NotConfigured);
			var mint = () => unconfigured.Mint(Facts());
			mint.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void A_validation_only_host_cannot_mint()
		{
			using var publicOnly = X509CertificateLoader.LoadCertificate(_assertionCertificate.Export(X509ContentType.Cert));
			var broker = new BrokerSessionAssertionService(() => publicOnly, () => publicOnly);

			broker.CanMint.Should().BeFalse();
			broker.CanValidate.Should().BeTrue();
			broker.Validate(_service.Mint(Facts()), Digest(), out _).Should().Be(BrokerSessionAssertionOutcome.Valid);
		}

		[Test]
		public void Minting_needs_every_session_fact()
		{
			var missingSession = () => _service.Mint(new BrokerSessionAssertion
			{
				UserId = "user-1", DepartmentId = 42, ClientApplication = 1, RequestDigest = Digest()
			});

			missingSession.Should().Throw<ArgumentException>();
		}

		[Test]
		public void The_request_digest_covers_every_part_of_the_request()
		{
			var baseline = Digest();

			BrokerRequestDigest.Compute("encrypt", 42, "req-1", Items()).Should().NotBe(baseline);
			BrokerRequestDigest.Compute("decrypt", 43, "req-1", Items()).Should().NotBe(baseline);
			BrokerRequestDigest.Compute("decrypt", 42, "req-2", Items()).Should().NotBe(baseline);

			foreach (var change in new Action<ProtectedFieldOperationItem>[]
			{
				i => i.FieldId = "calls.address",
				i => i.RowKey = "170",
				i => i.Value = "rgdp:1:1:abd",
				i => i.IsBinary = true,
				i => i.CatalogVersion = 4,
				i => i.Value = null
			})
			{
				var items = Items();
				change(items[0]);
				BrokerRequestDigest.Compute("decrypt", 42, "req-1", items).Should().NotBe(baseline);
			}

			BrokerRequestDigest.Compute("decrypt", 42, "req-1", Items()).Should().Be(baseline, "the digest is deterministic");
		}

		[Test]
		public void The_digest_cannot_be_shifted_between_fields()
		{
			var a = new[] { new ProtectedFieldOperationItem { FieldId = "ab", RowKey = "c" } };
			var b = new[] { new ProtectedFieldOperationItem { FieldId = "a", RowKey = "bc" } };

			BrokerRequestDigest.Compute("decrypt", 42, "r", a).Should().NotBe(BrokerRequestDigest.Compute("decrypt", 42, "r", b));
		}
	}
}
