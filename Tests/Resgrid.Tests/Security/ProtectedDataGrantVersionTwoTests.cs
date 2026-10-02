using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The version 2 Protected Data Grant contract (plan section 8.2, workbook section 7.5): issue and read round trip,
	/// version 1 grants still read, and every structural rule a reader enforces on a genuinely signed token.
	/// </summary>
	[TestFixture]
	public class ProtectedDataGrantVersionTwoTests
	{
		private X509Certificate2 _certificate;
		private ProtectedDataGrantService _service;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			_certificate = new CertificateRequest("CN=grant-v2-tests", ecdsa, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown() => _certificate?.Dispose();

		[SetUp]
		public void SetUp() => _service = new ProtectedDataGrantService(() => _certificate, () => _certificate);

		private static ProtectedDataGrantIssueRequest VersionTwo(Action<ProtectedDataGrantIssueRequest> edit = null)
		{
			var request = new ProtectedDataGrantIssueRequest
			{
				Version = 2,
				UserId = "user-1",
				DepartmentId = 42,
				SessionId = "session-9",
				ClientApp = (int)UserSessionClientApplication.Responder,
				PolicyEpoch = 7,
				WindowMinutes = 15,
				Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaMethod = ProtectedDataGrantMfaMethods.Totp,
				MfaAtUtc = DateTime.UtcNow.AddSeconds(-20),
				AuthenticationGeneration = 4
			};
			edit?.Invoke(request);
			return request;
		}

		private ProtectedDataGrant Read(string token, ProtectedDataGrantValidationOutcome expected = ProtectedDataGrantValidationOutcome.Valid)
		{
			_service.ValidateGrant(token, 42, 7, ProtectedDataGrantScopes.Read, out var grant).Should().Be(expected);
			return grant;
		}

		[Test]
		public void A_totp_grant_round_trips_every_binding_fact()
		{
			var request = VersionTwo(r => r.SessionLockVersion = 3);
			var issued = _service.IssueGrant(request);

			var grant = Read(issued.Token);

			grant.Version.Should().Be(2);
			grant.UserId.Should().Be("user-1");
			grant.SessionId.Should().Be("session-9");
			grant.ClientApp.Should().Be((int)UserSessionClientApplication.Responder);
			grant.AuthenticationGeneration.Should().Be(4);
			grant.SessionLockVersion.Should().Be(3);
			grant.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.Totp);
			grant.StepUpExempt.Should().BeFalse();
			grant.MfaAtUtc.Should().BeCloseTo(request.MfaAtUtc, TimeSpan.FromSeconds(1));
			grant.Amr.Should().Equal("pwd", "otp", "mfa");
			grant.GrantId.Should().Be(issued.GrantId);
		}

		[Test]
		public void An_exempt_grant_carries_no_verification_and_says_so()
		{
			var issued = _service.IssueGrant(VersionTwo(r =>
			{
				r.StepUpExempt = true;
				r.MfaMethod = ProtectedDataGrantMfaMethods.None;
				r.MfaAtUtc = default;
			}));

			var grant = Read(issued.Token);

			grant.StepUpExempt.Should().BeTrue();
			grant.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.None);
			grant.MfaAtUtc.Should().Be(default, "an exempt grant never invents a verification time");
			grant.Amr.Should().Equal("pwd");
		}

		[Test]
		public void A_federated_first_factor_is_recorded_as_fed()
		{
			var grant = Read(_service.IssueGrant(VersionTwo(r => r.FederatedFirstFactor = true)).Token);

			grant.Amr.Should().Equal("fed", "otp", "mfa");
		}

		[Test]
		public void A_passkey_grant_carries_its_credential_reference()
		{
			var grant = Read(_service.IssueGrant(VersionTwo(r =>
			{
				r.MfaMethod = ProtectedDataGrantMfaMethods.Passkey;
				r.MfaCredentialId = "credential-1";
				r.MfaStateVersion = 2;
			})).Token);

			grant.MfaCredentialId.Should().Be("credential-1");
			grant.MfaStateVersion.Should().Be(2);
			grant.Amr.Should().Equal("pwd", "mfa");
		}

		[Test]
		public void The_grant_ends_one_window_after_the_verification_not_after_issuance()
		{
			var verifiedAt = DateTime.UtcNow.AddMinutes(-10);
			var issued = _service.IssueGrant(VersionTwo(r => r.MfaAtUtc = verifiedAt));

			issued.ExpiresOnUtc.Should().BeCloseTo(verifiedAt.AddMinutes(15), TimeSpan.FromSeconds(1));
			Read(issued.Token).ExpiresOnUtc.Should().BeCloseTo(verifiedAt.AddMinutes(15), TimeSpan.FromSeconds(1));
		}

		[Test]
		public void Version_one_grants_still_read_as_version_one()
		{
			var grant = Read(_service.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				DepartmentId = 42,
				PolicyEpoch = 7,
				WindowMinutes = 15,
				Scopes = new[] { ProtectedDataGrantScopes.Read },
				MfaAtUtc = DateTime.UtcNow
			}).Token);

			grant.Version.Should().Be(1);
			grant.MfaMethod.Should().BeNull();
			grant.StepUpExempt.Should().BeFalse("a TOTP step-up grant is not exempt (this read as exempt before: amr was looked up on the mapped principal)");
		}

		[Test]
		public void An_exempt_version_one_grant_still_reads_as_exempt()
		{
			var grant = Read(_service.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				DepartmentId = 42,
				PolicyEpoch = 7,
				WindowMinutes = 15,
				Scopes = new[] { ProtectedDataGrantScopes.Read },
				StepUpExempt = true
			}).Token);

			grant.StepUpExempt.Should().BeTrue();
			grant.Amr.Should().Equal("pwd");
		}

		private static readonly (string Name, Action<ProtectedDataGrantIssueRequest> Edit)[] BadRequests =
		{
			("no session", r => r.SessionId = null),
			("no generation", r => r.AuthenticationGeneration = null),
			("an unknown client", r => r.ClientApp = 0),
			("an unknown method", r => r.MfaMethod = "sms"),
			("a verified grant with no verification time", r => r.MfaAtUtc = default),
			("a verification in the future", r => r.MfaAtUtc = DateTime.UtcNow.AddMinutes(5)),
			("a verification older than the window", r => r.MfaAtUtc = DateTime.UtcNow.AddMinutes(-20)),
			("an exempt grant naming a method", r => r.StepUpExempt = true),
			("a verified grant with no method", r => r.MfaMethod = ProtectedDataGrantMfaMethods.None),
			("a passkey grant with no credential", r => r.MfaMethod = ProtectedDataGrantMfaMethods.Passkey),
			("version 3", r => r.Version = 3)
		};

		[TestCaseSource(nameof(BadRequests))]
		public void The_issuer_refuses_an_inconsistent_version_two_request((string Name, Action<ProtectedDataGrantIssueRequest> Edit) bad)
		{
			var issue = () => _service.IssueGrant(VersionTwo(bad.Edit));

			issue.Should().Throw<ArgumentException>(bad.Name);
		}

		// ---- Genuinely signed tokens that break one structural rule each -------------------------------------------

		private static long Unix(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();

		private string Sign(Action<JwtPayload> edit)
		{
			var now = DateTime.UtcNow;
			var payload = new JwtPayload
			{
				["iss"] = DataProtectionConfig.GrantIssuer,
				["aud"] = DataProtectionConfig.GrantAudience,
				["sub"] = "user-1",
				["jti"] = "grant-1",
				["iat"] = Unix(now),
				["nbf"] = Unix(now),
				["exp"] = Unix(now.AddMinutes(10)),
				["grant_ver"] = 2,
				["dept"] = 42,
				["sid"] = "session-9",
				["client_app"] = 2,
				["policy_epoch"] = 7L,
				["auth_gen"] = 4L,
				["scope"] = ProtectedDataGrantScopes.Read,
				["mfa_method"] = "totp",
				["step_up_exempt"] = false,
				["mfa_at"] = Unix(now.AddSeconds(-30)),
				["amr"] = new List<string> { "pwd", "otp", "mfa" }
			};
			edit(payload);

			var header = new JwtHeader(new SigningCredentials(new ECDsaSecurityKey(_certificate.GetECDsaPrivateKey()), SecurityAlgorithms.EcdsaSha256));
			return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
		}

		[Test]
		public void The_hand_built_baseline_is_valid()
		{
			// Guards the helper: every malformed case below differs from a token the reader accepts in one way only.
			Read(Sign(_ => { })).Version.Should().Be(2);
		}

		private static readonly (string Name, Action<JwtPayload> Edit)[] Malformed =
		{
			("no session", p => p.Remove("sid")),
			("an empty session", p => p["sid"] = ""),
			("no client", p => p.Remove("client_app")),
			("the legacy client", p => p["client_app"] = 0),
			("an undefined client", p => p["client_app"] = 99),
			("a client as a string", p => p["client_app"] = "2"),
			("no generation", p => p.Remove("auth_gen")),
			("a negative generation", p => p["auth_gen"] = -1L),
			("no method", p => p.Remove("mfa_method")),
			("an unknown method", p => p["mfa_method"] = "sms"),
			("no exemption flag", p => p.Remove("step_up_exempt")),
			("an exemption flag as a string", p => p["step_up_exempt"] = "false"),
			("an exempt grant naming a method", p => p["step_up_exempt"] = true),
			("a verified grant with no method", p => { p["mfa_method"] = "none"; p["amr"] = new List<string> { "pwd", "mfa" }; }),
			("a verified grant with no time", p => p.Remove("mfa_at")),
			("a verification after issuance", p => p["mfa_at"] = Unix(DateTime.UtcNow.AddMinutes(5))),
			("an exempt grant with a verification time", p => { p["step_up_exempt"] = true; p["mfa_method"] = "none"; p["amr"] = new List<string> { "pwd" }; }),
			("a passkey grant with no credential", p => { p["mfa_method"] = "passkey"; p["amr"] = new List<string> { "pwd", "mfa" }; }),
			("a credential without its state version", p => p["mfa_credential_id"] = "credential-1"),
			("a negative lock version", p => p["session_lock_version"] = -1L),
			("a hardware-key amr", p => p["amr"] = new List<string> { "pwd", "otp", "mfa", "hwk" }),
			("a biometric amr", p => p["amr"] = new List<string> { "pwd", "otp", "mfa", "fpt" }),
			("no first factor", p => p["amr"] = new List<string> { "otp", "mfa" }),
			("two first factors", p => p["amr"] = new List<string> { "pwd", "fed", "otp", "mfa" }),
			("a verified grant without mfa", p => p["amr"] = new List<string> { "pwd", "otp" }),
			("otp claimed for a non-TOTP method", p => { p["mfa_method"] = "passkey"; p["mfa_credential_id"] = "c"; p["mfa_state_ver"] = 1L; }),
			("a duplicated amr", p => p["amr"] = new List<string> { "pwd", "otp", "mfa", "mfa" }),
			("no grant id", p => p.Remove("jti")),
			("no not-before", p => p.Remove("nbf")),
			("no issue time", p => p.Remove("iat")),
			("a grant version as a string", p => p["grant_ver"] = "2")
		};

		[TestCaseSource(nameof(Malformed))]
		public void A_malformed_version_two_grant_is_invalid((string Name, Action<JwtPayload> Edit) malformed)
		{
			Read(Sign(malformed.Edit), ProtectedDataGrantValidationOutcome.Invalid).Should().BeNull(malformed.Name);
		}

		[Test]
		public void An_unknown_grant_version_is_unsupported_rather_than_half_read()
		{
			Read(Sign(p => p["grant_ver"] = 3), ProtectedDataGrantValidationOutcome.VersionUnsupported).Should().BeNull();
		}

		[Test]
		public void The_existing_checks_still_apply_to_version_two()
		{
			_service.ValidateGrant(Sign(_ => { }), 43, 7, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.WrongDepartment);
			_service.ValidateGrant(Sign(_ => { }), 42, 8, ProtectedDataGrantScopes.Read, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.EpochRevoked);
			_service.ValidateGrant(Sign(_ => { }), 42, 7, ProtectedDataGrantScopes.Write, out _)
				.Should().Be(ProtectedDataGrantValidationOutcome.MissingScope);
		}
	}
}
