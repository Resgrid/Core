using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.ProtectedData;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The application-tier readers of version 2 grants (plan section 8.3): protected reads and writes decrypt only for the
	/// session the grant was issued to, and the broker client attaches a session assertion to attended calls only.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ProtectedGrantReaderTests
	{
		private const int DeptId = 42;
		private const long Epoch = 3;
		private const string UserId = "user-1";

		private X509Certificate2 _certificate;
		private ProtectedDataGrantService _grants;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IProtectedDataBrokerClient> _broker;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			_certificate = new CertificateRequest("CN=grant-reader-tests", ecdsa, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown() => _certificate?.Dispose();

		[SetUp]
		public void SetUp()
		{
			_grants = new ProtectedDataGrantService(() => _certificate, () => _certificate);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(x => x.IsProtectionEnforcedAsync(DeptId)).ReturnsAsync(true);
			_protection.Setup(x => x.GetPolicyByDepartmentIdAsync(DeptId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch, CatalogVersion = 1, StepUpWindowMinutes = 15 });
			_broker = new Mock<IProtectedDataBrokerClient>();
			_broker.Setup(x => x.DecryptAsync(DeptId, It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string g, string r, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken ct) =>
					new ProtectedDataBrokerResult
					{
						Success = true,
						Items = items.Select(i => new ProtectedFieldOperationResult { FieldId = i.FieldId, RowKey = i.RowKey, Value = "plain" }).ToList()
					});
		}

		private static ProtectedGrantSessionContext Session(string sessionId = "session-9",
			int client = (int)UserSessionClientApplication.Web, long generation = 4) => new()
		{
			SessionId = sessionId,
			ClientApplication = client,
			AuthenticationGeneration = generation
		};

		private ProtectedReadService Reader(ProtectedGrantSessionContext session, string contextUser = UserId) =>
			new(_protection.Object, _grants, _broker.Object, new ProtectedFieldCatalog(),
				new FixedProtectedGrantContext("unused", isWorkloadCaller: false, contextUser, session));

		private string VersionTwoGrant() => _grants.IssueGrant(new ProtectedDataGrantIssueRequest
		{
			Version = 2,
			UserId = UserId,
			DepartmentId = DeptId,
			SessionId = "session-9",
			ClientApp = (int)UserSessionClientApplication.Web,
			AuthenticationGeneration = 4,
			MfaMethod = ProtectedDataGrantMfaMethods.Totp,
			MfaAtUtc = DateTime.UtcNow.AddSeconds(-5),
			PolicyEpoch = Epoch,
			WindowMinutes = 15,
			Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write }
		}).Token;

		private static Call EnvelopedCall() => new()
		{
			CallId = 17,
			DepartmentId = DeptId,
			Number = "C-100",
			Name = "rgdp:1:1:name==",
			NatureOfCall = "rgdp:1:1:nature==",
			Address = "rgdp:1:1:address=="
		};

		[Test]
		public async Task A_version_two_grant_reads_for_the_session_it_was_issued_to()
		{
			var result = await Reader(Session()).ResolveForReadAsync(DeptId, EnvelopedCall(), VersionTwoGrant(), UserId);

			result.ProtectedReason.Should().BeNull();
			result.Call.Name.Should().Be("plain");
		}

		private static readonly (string Name, ProtectedGrantSessionContext Session, string Code)[] OtherCallers =
		{
			("no validated session", null, "grant_session_mismatch"),
			("another session", Session("session-10"), "grant_session_mismatch"),
			("another app", Session(client: (int)UserSessionClientApplication.Unit), "grant_client_mismatch"),
			("a password change since issuance", Session(generation: 5), "grant_revoked")
		};

		[TestCaseSource(nameof(OtherCallers))]
		public async Task A_version_two_grant_redacts_for_any_other_caller((string Name, ProtectedGrantSessionContext Session, string Code) caller)
		{
			var result = await Reader(caller.Session).ResolveForReadAsync(DeptId, EnvelopedCall(), VersionTwoGrant(), UserId);

			result.ProtectedReason.Should().Be(caller.Code, caller.Name);
			result.Call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			_broker.Verify(x => x.DecryptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_session_described_for_another_user_is_never_borrowed()
		{
			var result = await Reader(Session(), contextUser: "user-2").ResolveForReadAsync(DeptId, EnvelopedCall(), VersionTwoGrant(), UserId);

			result.ProtectedReason.Should().Be("grant_session_mismatch");
		}

		[Test]
		public async Task A_version_two_grant_is_refused_by_a_reader_without_a_grant_context()
		{
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(DeptId)).ReturnsAsync(true);
			var reader = new ProtectedReadService(_protection.Object, _grants, _broker.Object, new ProtectedFieldCatalog());

			(await reader.PreflightWriteAsync(DeptId, VersionTwoGrant(), UserId, workloadCaller: false))
				.Reason.Should().Be("grant_session_mismatch");
		}

		[Test]
		public async Task Writes_apply_the_same_binding()
		{
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(DeptId)).ReturnsAsync(true);

			(await Reader(Session()).PreflightWriteAsync(DeptId, VersionTwoGrant(), UserId, workloadCaller: false)).Success.Should().BeTrue();
			(await Reader(Session("session-10")).PreflightWriteAsync(DeptId, VersionTwoGrant(), UserId, workloadCaller: false))
				.Reason.Should().Be("grant_session_mismatch");
		}

		private string PasskeyGrant() => _grants.IssueGrant(new ProtectedDataGrantIssueRequest
		{
			Version = 2, UserId = UserId, DepartmentId = DeptId, SessionId = "session-9", ClientApp = (int)UserSessionClientApplication.Web,
			AuthenticationGeneration = 4, MfaMethod = ProtectedDataGrantMfaMethods.Passkey, MfaCredentialId = "passkey:pk-web", MfaStateVersion = 2,
			MfaAtUtc = DateTime.UtcNow.AddSeconds(-5), PolicyEpoch = Epoch, WindowMinutes = 15,
			Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write }
		}).Token;

		[Test]
		public async Task A_passkey_grant_reads_and_writes_only_while_its_passkey_is_current()
		{
			// Slice 18: every reader checks the credential behind a passkey, approval or provider step-up grant.
			_protection.Setup(x => x.ShouldEncryptNewWritesAsync(DeptId)).ReturnsAsync(true);
			var current = true;
			var states = new Mock<IMfaCredentialStateService>();
			states.Setup(s => s.IsCurrentAsync(It.Is<ProtectedDataGrant>(g => g.MfaCredentialId == "passkey:pk-web" && g.MfaStateVersion == 2),
				It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
			ProtectedReadService Reader(IMfaCredentialStateService credentialStates) => new(_protection.Object, _grants, _broker.Object, new ProtectedFieldCatalog(),
				new FixedProtectedGrantContext("unused", isWorkloadCaller: false, UserId, Session()), credentialStates);

			(await Reader(states.Object).ResolveForReadAsync(DeptId, EnvelopedCall(), PasskeyGrant(), UserId)).Call.Name.Should().Be("plain");
			(await Reader(states.Object).PreflightWriteAsync(DeptId, PasskeyGrant(), UserId, workloadCaller: false)).Success.Should().BeTrue();

			current = false;
			var revoked = await Reader(states.Object).ResolveForReadAsync(DeptId, EnvelopedCall(), PasskeyGrant(), UserId);
			revoked.ProtectedReason.Should().Be("grant_revoked");
			revoked.Call.Name.Should().Be(ProtectedDataEnvelope.RedactionValue);
			(await Reader(states.Object).PreflightWriteAsync(DeptId, PasskeyGrant(), UserId, workloadCaller: false)).Reason.Should().Be("grant_revoked");

			(await Reader(null).ResolveForReadAsync(DeptId, EnvelopedCall(), PasskeyGrant(), UserId)).ProtectedReason.Should().Be("grant_revoked",
				"a reader that cannot check the passkey refuses its grant");
		}

		[Test]
		public async Task An_unsupported_grant_version_redacts_with_its_own_code()
		{
			// A grant naming version 3, genuinely signed: an older reader must not half-read it.
			var header = new System.IdentityModel.Tokens.Jwt.JwtHeader(new Microsoft.IdentityModel.Tokens.SigningCredentials(
				new Microsoft.IdentityModel.Tokens.ECDsaSecurityKey(_certificate.GetECDsaPrivateKey()), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256));
			var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			var payload = new System.IdentityModel.Tokens.Jwt.JwtPayload
			{
				["iss"] = DataProtectionConfig.GrantIssuer, ["aud"] = DataProtectionConfig.GrantAudience, ["sub"] = UserId,
				["jti"] = "g", ["iat"] = now, ["nbf"] = now, ["exp"] = now + 600, ["grant_ver"] = 3, ["dept"] = DeptId,
				["policy_epoch"] = Epoch, ["scope"] = ProtectedDataGrantScopes.Read
			};
			var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
				new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(header, payload));

			(await Reader(Session()).ResolveForReadAsync(DeptId, EnvelopedCall(), token, UserId)).ProtectedReason.Should().Be("grant_version_unsupported");
		}

		// ---- Broker client: assertions for attended calls only ----------------------------------------------------

		private sealed class CapturingHandler : HttpMessageHandler
		{
			public HttpRequestMessage Last;

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Last = request;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("{\"Success\":true,\"Items\":[]}")
				});
			}
		}

		private X509Certificate2 _assertionCertificate;
		private string _baseUrl;

		[SetUp]
		public void SetUpBroker()
		{
			_baseUrl = DataProtectionConfig.BrokerBaseUrl;
			DataProtectionConfig.BrokerBaseUrl = "https://broker.test";
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			_assertionCertificate = new CertificateRequest("CN=assertion", ecdsa, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[TearDown]
		public void TearDownBroker()
		{
			DataProtectionConfig.BrokerBaseUrl = _baseUrl;
			_assertionCertificate?.Dispose();
		}

		private static readonly ProtectedFieldOperationItem[] Items =
		{
			new() { FieldId = "calls.name", RowKey = "17", Value = "rgdp:1:1:name==", CatalogVersion = 1 }
		};

		private (ProtectedDataBrokerClient Client, CapturingHandler Handler, BrokerSessionAssertionService Assertions) Client(
			IProtectedGrantContext context)
		{
			var handler = new CapturingHandler();
			var assertions = new BrokerSessionAssertionService(() => _assertionCertificate, () => _assertionCertificate);
			return (new ProtectedDataBrokerClient(handler, Mock.Of<IAdpAuditRepository>(), assertions, context), handler, assertions);
		}

		private static string AssertionOf(HttpRequestMessage request) =>
			request.Headers.TryGetValues(BrokerSessionAssertion.HeaderName, out var values) ? values.Single() : null;

		[Test]
		public async Task An_attended_decrypt_carries_an_assertion_for_exactly_that_request()
		{
			var (client, handler, assertions) = Client(new FixedProtectedGrantContext("grant", false, UserId, Session()));

			await client.DecryptAsync(DeptId, "grant", "req-1", Items);

			var token = AssertionOf(handler.Last);
			token.Should().NotBeNull();
			assertions.Validate(token, BrokerRequestDigest.Compute("decrypt", DeptId, "req-1", Items), out var assertion)
				.Should().Be(BrokerSessionAssertionOutcome.Valid);
			assertion.UserId.Should().Be(UserId);
			assertion.SessionId.Should().Be("session-9");
			assertion.DepartmentId.Should().Be(DeptId);
			assertion.ClientApplication.Should().Be((int)UserSessionClientApplication.Web);
		}

		[Test]
		public async Task Workload_calls_release_receipts_and_untracked_sessions_carry_none()
		{
			var (workload, workloadHandler, _) = Client(FixedProtectedGrantContext.Workload);
			await workload.EncryptAsync(DeptId, null, "req-2", Items);
			AssertionOf(workloadHandler.Last).Should().BeNull();

			var (attended, attendedHandler, _) = Client(new FixedProtectedGrantContext("grant", false, UserId, Session()));
			await attended.DecryptAsync(DeptId, "adpr.receipt", "req-3", Items);
			AssertionOf(attendedHandler.Last).Should().BeNull("a release receipt is its own lane");

			var (untracked, untrackedHandler, _) = Client(new FixedProtectedGrantContext("grant", false, UserId, session: null));
			await untracked.DecryptAsync(DeptId, "grant", "req-4", Items);
			AssertionOf(untrackedHandler.Last).Should().BeNull("nothing is asserted without a validated session");
		}
	}
}
