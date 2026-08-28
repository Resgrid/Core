using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Protected Data Grant issue/validate (ADP plan section 3): claims binding, pinned ES256,
	/// tamper/tenant-swap/epoch/scope/lifetime rejection, and fail-closed behavior when key
	/// material is absent. Certificates are ephemeral in-memory ECDSA — no files, no config paths.
	/// </summary>
	[TestFixture]
	public class ProtectedDataGrantServiceTests
	{
		private X509Certificate2 _signingCertificate;
		private X509Certificate2 _publicOnlyCertificate;
		private ProtectedDataGrantService _service;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			var request = new CertificateRequest("CN=adp-grant-tests", ecdsa, HashAlgorithmName.SHA256);
			_signingCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
			_publicOnlyCertificate = X509CertificateLoader.LoadCertificate(_signingCertificate.Export(X509ContentType.Cert));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_signingCertificate?.Dispose();
			_publicOnlyCertificate?.Dispose();
		}

		[SetUp]
		public void SetUp()
		{
			// Identity-tier shape: private key signs; validation happens against the public part.
			_service = new ProtectedDataGrantService(() => _signingCertificate, () => _publicOnlyCertificate);
		}

		private static ProtectedDataGrantIssueRequest Request(int departmentId = 42, long policyEpoch = 7,
			int windowMinutes = 15) => new ProtectedDataGrantIssueRequest
		{
			UserId = "user-1",
			DepartmentId = departmentId,
			SessionId = "session-9",
			ClientApp = (int)UserSessionClientApplication.Responder,
			PolicyEpoch = policyEpoch,
			WindowMinutes = windowMinutes,
			Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
			MfaAtUtc = DateTime.UtcNow
		};

		[Test]
		public void Issue_and_validate_roundtrip_binds_every_claim()
		{
			var issued = _service.IssueGrant(Request());

			issued.GrantId.Should().NotBeNullOrWhiteSpace();
			issued.Token.Should().NotBeNullOrWhiteSpace();

			var outcome = _service.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Read, out var grant);

			outcome.Should().Be(ProtectedDataGrantValidationOutcome.Valid);
			grant.GrantId.Should().Be(issued.GrantId);
			grant.UserId.Should().Be("user-1");
			grant.DepartmentId.Should().Be(42);
			grant.SessionId.Should().Be("session-9");
			grant.ClientApp.Should().Be((int)UserSessionClientApplication.Responder);
			grant.PolicyEpoch.Should().Be(7);
			grant.Scopes.Should().BeEquivalentTo(ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write);
			grant.ExpiresOnUtc.Should().BeCloseTo(issued.ExpiresOnUtc, TimeSpan.FromSeconds(2));
			grant.MfaAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
		}

		[Test]
		public void Tampered_token_is_invalid()
		{
			var issued = _service.IssueGrant(Request());
			var parts = issued.Token.Split('.');
			// Flip a character inside the signed payload.
			var payload = parts[1];
			var flipped = (payload[3] == 'A' ? 'B' : 'A');
			parts[1] = payload.Substring(0, 3) + flipped + payload.Substring(4);
			var tampered = string.Join(".", parts);

			_service.ValidateGrant(tampered, 42, 7, ProtectedDataGrantScopes.Read, out var grant)
				.Should().Be(ProtectedDataGrantValidationOutcome.Invalid);
			grant.Should().BeNull();
		}

		[Test]
		public void Department_swap_is_rejected()
		{
			var issued = _service.IssueGrant(Request(departmentId: 42));

			_service.ValidateGrant(issued.Token, 43, 7, ProtectedDataGrantScopes.Read, out var grant)
				.Should().Be(ProtectedDataGrantValidationOutcome.WrongDepartment);
			grant.Should().BeNull();
		}

		[Test]
		public void Policy_epoch_bump_revokes_earlier_grants()
		{
			var issued = _service.IssueGrant(Request(policyEpoch: 7));

			_service.ValidateGrant(issued.Token, 42, 8, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.EpochRevoked);
		}

		[Test]
		public void Grant_claiming_a_future_epoch_is_equally_revoked()
		{
			var issued = _service.IssueGrant(Request(policyEpoch: 9));

			_service.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.EpochRevoked);
		}

		[Test]
		public void Missing_scope_is_rejected()
		{
			var request = Request();
			request.Scopes = new[] { ProtectedDataGrantScopes.Read };
			var issued = _service.IssueGrant(request);

			_service.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Write, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.MissingScope);
		}

		[Test]
		public void Expired_grant_is_rejected_beyond_the_bounded_skew()
		{
			var issued = _service.IssueGrant(Request(windowMinutes: 15));

			// Inside skew: still valid; past skew: expired. Lifetime is absolute.
			_service.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Read, out _,
				utcNow: DateTime.UtcNow.AddMinutes(15).AddSeconds(10))
				.Should().Be(ProtectedDataGrantValidationOutcome.Valid);
			_service.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Read, out _,
				utcNow: DateTime.UtcNow.AddMinutes(16))
				.Should().Be(ProtectedDataGrantValidationOutcome.Expired);
		}

		[Test]
		public void Window_is_clamped_to_the_operator_ceiling()
		{
			var issued = _service.IssueGrant(Request(windowMinutes: 100000));

			issued.ExpiresOnUtc.Should().BeOnOrBefore(
				DateTime.UtcNow.AddMinutes(Resgrid.Config.DataProtectionConfig.StepUpMaximumMinutes).AddSeconds(5));
		}

		[Test]
		public void Algorithm_confusion_with_a_symmetric_key_is_invalid()
		{
			// A forged HS256 token keyed on public material must never validate against the pinned
			// ES256 check.
			var handler = new JwtSecurityTokenHandler();
			var forged = handler.WriteToken(new JwtSecurityToken(
				issuer: Resgrid.Config.DataProtectionConfig.GrantIssuer,
				audience: Resgrid.Config.DataProtectionConfig.GrantAudience,
				claims: new[]
				{
					new System.Security.Claims.Claim("sub", "user-1"),
					new System.Security.Claims.Claim("dept", "42"),
					new System.Security.Claims.Claim("policy_epoch", "7"),
					new System.Security.Claims.Claim("scope", ProtectedDataGrantScopes.Read)
				},
				notBefore: DateTime.UtcNow,
				expires: DateTime.UtcNow.AddMinutes(15),
				signingCredentials: new SigningCredentials(
					new SymmetricSecurityKey(SHA256.HashData(_publicOnlyCertificate.RawData)),
					SecurityAlgorithms.HmacSha256)));

			_service.ValidateGrant(forged, 42, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.Invalid);
		}

		[Test]
		public void Broker_shape_validates_with_public_certificate_only_and_cannot_issue()
		{
			var issued = _service.IssueGrant(Request());
			var brokerShape = new ProtectedDataGrantService(() => null, () => _publicOnlyCertificate);

			brokerShape.CanIssueGrants.Should().BeFalse();
			brokerShape.CanValidateGrants.Should().BeTrue();
			brokerShape.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.Valid);

			Action issueOnBroker = () => brokerShape.IssueGrant(Request());
			issueOnBroker.Should().Throw<InvalidOperationException>();
		}

		[Test]
		public void Missing_validation_material_fails_closed_as_not_configured()
		{
			var unconfigured = new ProtectedDataGrantService(() => null, () => null);
			var issued = _service.IssueGrant(Request());

			unconfigured.CanValidateGrants.Should().BeFalse();
			unconfigured.ValidateGrant(issued.Token, 42, 7, ProtectedDataGrantScopes.Read, out var grant)
				.Should().Be(ProtectedDataGrantValidationOutcome.NotConfigured);
			grant.Should().BeNull();
		}

		[Test]
		public void Garbage_and_empty_tokens_are_invalid_never_throwing()
		{
			_service.ValidateGrant(null, 42, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.Invalid);
			_service.ValidateGrant("", 42, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.Invalid);
			_service.ValidateGrant("not.a.token", 42, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.Invalid);
		}

		[Test]
		public void Issue_refuses_unusable_requests()
		{
			Action noUser = () => _service.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				DepartmentId = 42,
				Scopes = new[] { ProtectedDataGrantScopes.Read },
				WindowMinutes = 15
			});
			noUser.Should().Throw<ArgumentException>();

			Action noDepartment = () => _service.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				Scopes = new[] { ProtectedDataGrantScopes.Read },
				WindowMinutes = 15
			});
			noDepartment.Should().Throw<ArgumentException>();

			Action noScopes = () => _service.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				DepartmentId = 42,
				WindowMinutes = 15
			});
			noScopes.Should().Throw<ArgumentException>();
		}
	}
}
