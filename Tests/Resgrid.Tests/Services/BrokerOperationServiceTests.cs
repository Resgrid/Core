using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Broker.Models;
using Resgrid.Web.Broker.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Broker field-crypto pipeline (ADP plan section 3.1 steps 8-9) against the REAL grant, crypto
	/// and LocalDev key-wrapping implementations — only the repositories are mocked. Proves the
	/// grant gates (invalid/revoked/scope), replay refusal, encrypt/decrypt roundtrip with AAD
	/// binding, the double-encryption guard, and fail-closed item errors.
	/// </summary>
	[TestFixture]
	public class BrokerOperationServiceTests
	{
		private const int DeptId = 42;
		private const long Epoch = 3;

		private X509Certificate2 _certificate;
		private ProtectedDataGrantService _grantService;
		private LocalDevKeyWrappingProvider _keyWrappingProvider;
		private ProtectedFieldCryptoService _cryptoService;
		private Mock<IDepartmentDataProtectionPolicyRepository> _policyRepo;
		private Mock<IDepartmentKeyService> _keyService;
		private IContainer _container;
		private BrokerOperationService _service;
		private DepartmentDataProtectionKey _activeKey;
		private Resgrid.Tests.Security.InMemoryBrokerReplayRepository _replay;
		private Mock<IUserSessionService> _sessions;
		private X509Certificate2 _assertionCertificate;
		private BrokerSessionAssertionService _assertionService;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			var request = new CertificateRequest("CN=adp-broker-tests", ecdsa, HashAlgorithmName.SHA256);
			_certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));

			// The session-assertion key is a different key from the grant key (workbook section 6.2).
			using var assertionKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			_assertionCertificate = new CertificateRequest("CN=adp-assertion-tests", assertionKey, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_certificate?.Dispose();
			_assertionCertificate?.Dispose();
			_container?.Dispose();
		}

		[SetUp]
		public async Task SetUp()
		{
			_container?.Dispose();

			_grantService = new ProtectedDataGrantService(() => _certificate, () => _certificate);
			_keyWrappingProvider = new LocalDevKeyWrappingProvider();
			_cryptoService = new ProtectedFieldCryptoService();

			var wrapped = await _keyWrappingProvider.GenerateWrappedDataKeyAsync(DeptId);
			_activeKey = new DepartmentDataProtectionKey
			{
				DepartmentId = DeptId,
				Version = 1,
				Status = (int)DepartmentDataProtectionKeyStatus.Active,
				WrappedKey = wrapped.WrappedKeyBase64
			};

			_policyRepo = new Mock<IDepartmentDataProtectionPolicyRepository>();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch });

			_keyService = new Mock<IDepartmentKeyService>();
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId)).ReturnsAsync(_activeKey);
			_keyService.Setup(x => x.GetKeyByVersionAsync(DeptId, 1)).ReturnsAsync(_activeKey);

			_replay = new Resgrid.Tests.Security.InMemoryBrokerReplayRepository();
			_sessions = new Mock<IUserSessionService>();
			_assertionService = new BrokerSessionAssertionService(() => _assertionCertificate, () => _assertionCertificate);

			var builder = new ContainerBuilder();
			builder.RegisterInstance(_policyRepo.Object).As<IDepartmentDataProtectionPolicyRepository>();
			builder.RegisterInstance(_keyService.Object).As<IDepartmentKeyService>();
			builder.RegisterInstance(_replay).As<IBrokerReplayRepository>();
			builder.RegisterInstance(_sessions.Object).As<IUserSessionService>();
			_container = builder.Build();

			_service = new BrokerOperationService(_container, _grantService, _cryptoService,
				_keyWrappingProvider, Mock.Of<IAdpAuditRepository>(), _assertionService);
		}

		private string IssueGrantToken(params string[] scopes)
		{
			var issued = _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				UserId = "user-1",
				DepartmentId = DeptId,
				PolicyEpoch = Epoch,
				WindowMinutes = 15,
				Scopes = scopes.Length > 0 ? scopes : new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write },
				MfaAtUtc = DateTime.UtcNow
			});
			return issued.Token;
		}

		// The legacy shared key's full authority; lane-specific tests set their own credential.
		private static BrokerFieldOperationRequest Request(string grantToken, string requestId,
			params ProtectedFieldOperationItem[] items) => new BrokerFieldOperationRequest
		{
			DepartmentId = DeptId,
			GrantToken = grantToken,
			RequestId = requestId,
			Caller = BrokerCredential.Legacy(),
			Items = items.ToList()
		};

		private static ProtectedFieldOperationItem Item(string value, string fieldId = "calls.natureofcall",
			string rowKey = "17", int catalogVersion = 1) => new ProtectedFieldOperationItem
		{
			FieldId = fieldId,
			RowKey = rowKey,
			Value = value,
			CatalogVersion = catalogVersion
		};

		[Test]
		public async Task Encrypt_then_decrypt_roundtrips_with_full_aad_binding()
		{
			var token = IssueGrantToken();

			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("Structure fire, 3 Main St")), CancellationToken.None);
			encrypted.Success.Should().BeTrue();
			encrypted.Items.Should().HaveCount(1);
			encrypted.Items[0].ErrorCode.Should().BeNull();
			ProtectedDataEnvelope.IsEnveloped(encrypted.Items[0].Value).Should().BeTrue();

			var decrypted = await _service.DecryptAsync(Request(token, "req-2", Item(encrypted.Items[0].Value)), CancellationToken.None);
			decrypted.Success.Should().BeTrue();
			decrypted.Items[0].ErrorCode.Should().BeNull();
			decrypted.Items[0].Value.Should().Be("Structure fire, 3 Main St");
		}

		[Test]
		public async Task Workload_lane_encrypts_without_a_grant_but_decrypt_still_requires_one()
		{
			// Encrypt-only workload lane (plan 3.4): no grant, past the workload-key middleware —
			// allowed, because encryption discloses nothing. Decrypt without a grant stays refused.
			var encrypted = await _service.EncryptAsync(Request(null, "req-w1", Item("dispatch note")), CancellationToken.None);
			encrypted.Success.Should().BeTrue();
			encrypted.Items[0].ErrorCode.Should().BeNull();
			ProtectedDataEnvelope.IsEnveloped(encrypted.Items[0].Value).Should().BeTrue();

			var decrypted = await _service.DecryptAsync(Request(null, "req-w2", Item(encrypted.Items[0].Value)), CancellationToken.None);
			decrypted.Success.Should().BeFalse();
			decrypted.ErrorCode.Should().Be("grant_invalid");
			decrypted.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Workload_lane_still_validates_a_presented_grant()
		{
			// A stale grant cannot be laundered through the encrypt path just because the lane
			// would have allowed no grant at all.
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch + 1 });

			var result = await _service.EncryptAsync(Request(IssueGrantToken(), "req-w3", Item("value")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_revoked");
		}

		[Test]
		public async Task Binary_encrypt_then_decrypt_roundtrips_over_base64()
		{
			var token = IssueGrantToken();
			var plaintext = new byte[] { 1, 2, 3, 4, 5 };

			var encrypted = await _service.EncryptAsync(Request(token, "req-b1", new ProtectedFieldOperationItem
			{
				FieldId = "callattachments.data",
				RowKey = "9",
				Value = Convert.ToBase64String(plaintext),
				IsBinary = true,
				CatalogVersion = 1
			}), CancellationToken.None);

			encrypted.Success.Should().BeTrue();
			encrypted.Items[0].ErrorCode.Should().BeNull();
			var envelopeBytes = Convert.FromBase64String(encrypted.Items[0].Value);
			System.Text.Encoding.ASCII.GetString(envelopeBytes, 0, 6).Should().Be("rgdpb:");

			var decrypted = await _service.DecryptAsync(Request(token, "req-b2", new ProtectedFieldOperationItem
			{
				FieldId = "callattachments.data",
				RowKey = "9",
				Value = encrypted.Items[0].Value,
				IsBinary = true,
				CatalogVersion = 1
			}), CancellationToken.None);

			decrypted.Success.Should().BeTrue();
			decrypted.Items[0].ErrorCode.Should().BeNull();
			Convert.FromBase64String(decrypted.Items[0].Value).Should().BeEquivalentTo(plaintext);
		}

		[Test]
		public async Task Binary_decrypt_of_a_non_enveloped_blob_reports_not_enveloped()
		{
			var token = IssueGrantToken();

			var result = await _service.DecryptAsync(Request(token, "req-b3", new ProtectedFieldOperationItem
			{
				FieldId = "callattachments.data",
				RowKey = "9",
				Value = Convert.ToBase64String(new byte[] { 7, 7, 7 }),
				IsBinary = true,
				CatalogVersion = 1
			}), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.Items[0].ErrorCode.Should().Be("not_enveloped");
			result.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Moved_ciphertext_fails_decrypt_per_item_without_failing_the_request()
		{
			var token = IssueGrantToken();
			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("secret", rowKey: "17")), CancellationToken.None);

			// Same envelope presented for a different row: AAD mismatch.
			var moved = await _service.DecryptAsync(Request(token, "req-2",
				Item(encrypted.Items[0].Value, rowKey: "99")), CancellationToken.None);

			moved.Success.Should().BeTrue();
			moved.Items[0].ErrorCode.Should().Be("decrypt_failed");
			moved.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Replayed_request_id_is_refused()
		{
			var token = IssueGrantToken();
			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);
			encrypted.Success.Should().BeTrue();

			var replay = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);
			replay.Success.Should().BeFalse();
			replay.ErrorCode.Should().Be("replayed_request");
			replay.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Revoked_grant_after_epoch_bump_is_refused()
		{
			var token = IssueGrantToken();
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch + 1 });

			var result = await _service.DecryptAsync(Request(token, "req-1", Item("rgdp:1:1:AAAA")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_revoked");
			result.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Grant_without_the_write_scope_cannot_encrypt()
		{
			var readOnlyToken = IssueGrantToken(ProtectedDataGrantScopes.Read);

			var result = await _service.EncryptAsync(Request(readOnlyToken, "req-1", Item("value")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_invalid");
		}

		[Test]
		public async Task Garbage_grant_is_refused()
		{
			var result = await _service.DecryptAsync(Request("not-a-grant", "req-1", Item("rgdp:1:1:AAAA")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("grant_invalid");
		}

		[Test]
		public async Task Decrypting_plaintext_reports_not_enveloped_and_never_echoes_the_value()
		{
			var token = IssueGrantToken();

			var result = await _service.DecryptAsync(Request(token, "req-1", Item("just plain text")), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.Items[0].ErrorCode.Should().Be("not_enveloped");
			result.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Encrypting_an_envelope_trips_the_double_encryption_guard()
		{
			var token = IssueGrantToken();
			var encrypted = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);

			var again = await _service.EncryptAsync(Request(token, "req-2", Item(encrypted.Items[0].Value)), CancellationToken.None);

			again.Success.Should().BeTrue();
			again.Items[0].ErrorCode.Should().Be("already_enveloped");
			again.Items[0].Value.Should().BeNull();
		}

		[Test]
		public async Task Unknown_key_version_reports_key_unknown()
		{
			var token = IssueGrantToken();

			var result = await _service.DecryptAsync(Request(token, "req-1", Item("rgdp:1:9:AAAA")), CancellationToken.None);

			result.Success.Should().BeTrue();
			result.Items[0].ErrorCode.Should().Be("key_unknown");
		}

		[Test]
		public async Task Oversized_requests_are_refused()
		{
			var token = IssueGrantToken();
			var items = Enumerable.Range(0, Resgrid.Config.DataProtectionConfig.BrokerMaxItemsPerRequest + 1)
				.Select(i => Item("value", rowKey: i.ToString()))
				.ToArray();

			var result = await _service.EncryptAsync(Request(token, "req-1", items), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("too_many_items");
		}

		[Test]
		public async Task Missing_active_key_fails_encrypt_closed()
		{
			var token = IssueGrantToken();
			_keyService.Setup(x => x.GetActiveKeyAsync(DeptId)).ReturnsAsync((DepartmentDataProtectionKey)null);

			var result = await _service.EncryptAsync(Request(token, "req-1", Item("value")), CancellationToken.None);

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("no_active_key");
			result.Items.Should().BeEmpty();
		}

		[Test]
		public async Task Empty_and_null_requests_are_invalid()
		{
			var missingItems = await _service.DecryptAsync(new BrokerFieldOperationRequest
			{
				DepartmentId = DeptId,
				GrantToken = "x",
				RequestId = "req-1",
				Caller = BrokerCredential.Legacy(),
				Items = new List<ProtectedFieldOperationItem>()
			}, CancellationToken.None);
			missingItems.Success.Should().BeFalse();
			missingItems.ErrorCode.Should().Be("invalid_request");

			var nullRequest = await _service.DecryptAsync(null, CancellationToken.None);
			nullRequest.Success.Should().BeFalse();
			nullRequest.ErrorCode.Should().Be("invalid_request");
		}
		// ---- workload decrypt lane (RMS plan section 5.9.4) --------------------------------------------

		private void EnrollDepartment(DepartmentDataProtectionState state) =>
			_policyRepo.Setup(x => x.GetByDepartmentIdAsync(DeptId))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DeptId, PolicyEpoch = Epoch, State = (int)state });

		[Test]
		public async Task Workload_decrypt_lane_opens_an_allow_listed_purpose_without_a_grant()
		{
			EnrollDepartment(DepartmentDataProtectionState.Enabled);
			var sealedResult = await _service.EncryptAsync(Request(null, "wl-enc", Item("Smoke showing from the rear", "rmsoperationalrecorddetails.narrative", "rec-1")), CancellationToken.None);
			sealedResult.Success.Should().BeTrue();
			var envelope = sealedResult.Items[0].Value;

			var opened = await _service.DecryptForWorkloadAsync(Request(null, "wl-dec", Item(envelope, "rmsoperationalrecorddetails.narrative", "rec-1")), "NERIS-Submission", CancellationToken.None);
			opened.Success.Should().BeTrue();
			opened.Items.Should().ContainSingle().Which.Value.Should().Be("Smoke showing from the rear");

			var export = await _service.DecryptForWorkloadAsync(Request(null, "wl-dec-2", Item(envelope, "rmsoperationalrecorddetails.narrative", "rec-1")), "records-export", CancellationToken.None);
			export.Success.Should().BeTrue("both shipped lanes are on the default allow-list");
		}

		[Test]
		public async Task Workload_decrypt_lane_refuses_unlisted_purposes_unprotected_departments_and_a_disabled_lane()
		{
			EnrollDepartment(DepartmentDataProtectionState.Enabled);
			var sealedResult = await _service.EncryptAsync(Request(null, "wl-enc", Item("Sensitive")), CancellationToken.None);
			var envelope = sealedResult.Items[0].Value;

			(await _service.DecryptForWorkloadAsync(Request(null, "wl-1", Item(envelope)), "bulk-dump", CancellationToken.None)).ErrorCode.Should().Be("workload_purpose_denied");
			(await _service.DecryptForWorkloadAsync(Request(null, "wl-2", Item(envelope)), "", CancellationToken.None)).ErrorCode.Should().Be("workload_purpose_denied");
			(await _service.DecryptForWorkloadAsync(Request(null, "wl-3", Item(envelope)), null, CancellationToken.None)).ErrorCode.Should().Be("workload_purpose_denied");

			// A denied purpose burns nothing: the same request id is still usable once the purpose is right.
			(await _service.DecryptForWorkloadAsync(Request(null, "wl-1", Item(envelope)), "records-export", CancellationToken.None)).Success.Should().BeTrue();

			EnrollDepartment(DepartmentDataProtectionState.Encrypting);
			(await _service.DecryptForWorkloadAsync(Request(null, "wl-4", Item(envelope)), "records-export", CancellationToken.None)).ErrorCode.Should().Be("workload_purpose_denied", "an enrolling department has acknowledged no egress yet");
			EnrollDepartment(DepartmentDataProtectionState.Disabled);
			(await _service.DecryptForWorkloadAsync(Request(null, "wl-5", Item(envelope)), "records-export", CancellationToken.None)).ErrorCode.Should().Be("workload_purpose_denied");

			var configured = Resgrid.Config.DataProtectionConfig.BrokerWorkloadPurposes;
			try
			{
				Resgrid.Config.DataProtectionConfig.BrokerWorkloadPurposes = "";
				EnrollDepartment(DepartmentDataProtectionState.Enabled);
				(await _service.DecryptForWorkloadAsync(Request(null, "wl-6", Item(envelope)), "records-export", CancellationToken.None)).ErrorCode.Should().Be("workload_purpose_denied", "an empty allow-list disables the lane");
			}
			finally { Resgrid.Config.DataProtectionConfig.BrokerWorkloadPurposes = configured; }

			// The attended decrypt path is untouched: no grant is still no decrypt.
			(await _service.DecryptAsync(Request(null, "wl-7", Item(envelope)), CancellationToken.None)).Success.Should().BeFalse();
		}

		[Test]
		public async Task Workload_decrypt_lane_never_carries_a_grant_or_receipt()
		{
			// Plan section 8.5: the workload lane is purpose-bound and never takes a grant or a receipt, so a token on it
			// is refused before anything is read, rather than validated.
			EnrollDepartment(DepartmentDataProtectionState.Enabled);
			var sealedResult = await _service.EncryptAsync(Request(null, "wl-enc", Item("Sensitive")), CancellationToken.None);
			var envelope = sealedResult.Items[0].Value;
			var refused = await _service.DecryptForWorkloadAsync(Request("not-a-grant", "wl-8", Item(envelope)), "records-export", CancellationToken.None);
			refused.Success.Should().BeFalse();
			refused.ErrorCode.Should().Be("lane_denied");
			(await _service.DecryptForWorkloadAsync(Request("adpr.receipt", "wl-9", Item(envelope)), "records-export", CancellationToken.None))
				.ErrorCode.Should().Be("lane_denied");
			BrokerOperationService.IsAllowedWorkloadPurpose("records-export").Should().BeTrue();
			BrokerOperationService.IsAllowedWorkloadPurpose("Records-Export").Should().BeFalse("purposes are normalized by the caller, not the allow-list");
		}

		// ---- Attended requests bound to the live session (passkey workbook section 6.2) ----------------------------

		private const string SessionId = "session-9";

		private string IssueVersionTwoGrant(string sessionId = SessionId, long? lockVersion = null)
		{
			return _grantService.IssueGrant(new ProtectedDataGrantIssueRequest
			{
				Version = 2,
				UserId = "user-1",
				DepartmentId = DeptId,
				SessionId = sessionId,
				ClientApp = (int)UserSessionClientApplication.Web,
				AuthenticationGeneration = 4,
				SessionLockVersion = lockVersion,
				MfaMethod = ProtectedDataGrantMfaMethods.Totp,
				MfaAtUtc = DateTime.UtcNow.AddSeconds(-10),
				PolicyEpoch = Epoch,
				WindowMinutes = 15,
				Scopes = new[] { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write }
			}).Token;
		}

		private BrokerFieldOperationRequest Asserted(BrokerFieldOperationRequest request, bool decrypt = false, string userId = "user-1",
			string sessionId = SessionId, int client = (int)UserSessionClientApplication.Web, long? lockVersion = null)
		{
			request.SessionAssertion = _assertionService.Mint(new Resgrid.Model.Security.BrokerSessionAssertion
			{
				UserId = userId,
				SessionId = sessionId,
				AuthenticationGeneration = 4,
				SessionLockVersion = lockVersion,
				DepartmentId = DeptId,
				ClientApplication = client,
				CredentialIssuedOnUtc = new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc),
				RequestDigest = Resgrid.Model.Security.BrokerRequestDigest.Compute(decrypt ? "decrypt" : "encrypt", request.DepartmentId,
					request.RequestId, request.Items)
			});
			return request;
		}

		private void LiveSession(bool valid = true, int client = (int)UserSessionClientApplication.Web, long? sharedLockVersion = null, bool locked = false)
		{
			var session = new UserSession
			{
				UserSessionId = SessionId, UserId = "user-1", ClientApplication = client, AuthenticationGeneration = 4,
				SharedMode = sharedLockVersion != null, LockVersion = sharedLockVersion ?? 0, IsLocked = locked
			};
			_sessions.Setup(s => s.ValidateAsync(It.IsAny<Resgrid.Model.Security.SessionPrincipalContext>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(!valid ? Resgrid.Model.Security.SessionValidationResult.Invalid("session_revoked")
					: locked ? Resgrid.Model.Security.SessionValidationResult.Locked(session)
					: Resgrid.Model.Security.SessionValidationResult.Valid(session));
		}

		[Test]
		public async Task A_shared_session_opens_only_with_a_grant_and_assertion_at_its_live_lock_version()
		{
			// Plan section 12.5.3: the grant and assertion agree with each other at version 0, but the session locked and
			// unlocked since, so both are from before the lock.
			LiveSession(sharedLockVersion: 1);
			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(lockVersion: 0), "shared-1", Item("value")), lockVersion: 0),
				CancellationToken.None)).ErrorCode.Should().Be("grant_session_locked");

			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(lockVersion: 1), "shared-2", Item("value")), lockVersion: 1),
				CancellationToken.None)).Success.Should().BeTrue();

			(await _service.EncryptAsync(Asserted(Request(IssueGrantToken(), "shared-3", Item("value")), lockVersion: 1),
				CancellationToken.None)).ErrorCode.Should().Be("grant_session_locked", "a version 1 grant cannot prove it postdates the lock");

			LiveSession(sharedLockVersion: 1, locked: true);
			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(lockVersion: 1), "shared-4", Item("value")), lockVersion: 1),
				CancellationToken.None)).ErrorCode.Should().Be("shared_session_locked", "a locked session opens nothing");
		}

		[Test]
		public async Task A_personal_session_assertion_cannot_carry_a_lock_version()
		{
			LiveSession();
			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(lockVersion: 0), "personal-1", Item("value")), lockVersion: 0),
				CancellationToken.None)).ErrorCode.Should().Be("grant_session_locked", "the live session is personal, so no lock version is current");
		}

		[Test]
		public async Task A_version_two_grant_needs_a_session_assertion()
		{
			var refused = await _service.EncryptAsync(Request(IssueVersionTwoGrant(), "v2-1", Item("value")), CancellationToken.None);

			refused.Success.Should().BeFalse();
			refused.ErrorCode.Should().Be("session_assertion_required");
		}

		[Test]
		public async Task A_version_two_grant_with_a_matching_live_session_round_trips()
		{
			LiveSession();
			var token = IssueVersionTwoGrant();

			var sealedResult = await _service.EncryptAsync(Asserted(Request(token, "v2-enc", Item("Sensitive"))), CancellationToken.None);
			sealedResult.Success.Should().BeTrue();
			var opened = await _service.DecryptAsync(Asserted(Request(token, "v2-dec", Item(sealedResult.Items[0].Value)), decrypt: true),
				CancellationToken.None);

			opened.Success.Should().BeTrue();
			opened.Items[0].Value.Should().Be("Sensitive");
			_sessions.Verify(s => s.ValidateAsync(It.Is<Resgrid.Model.Security.SessionPrincipalContext>(c =>
				c.UserId == "user-1" && c.SessionId == SessionId && c.AuthenticationGeneration == 4 && c.DepartmentId == DeptId &&
				c.CredentialIssuedOn == new DateTime(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc)), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task An_assertion_for_different_items_is_refused()
		{
			LiveSession();
			var request = Asserted(Request(IssueVersionTwoGrant(), "v2-2", Item("value")));
			request.Items[0].Value = "changed after the assertion was minted";

			(await _service.EncryptAsync(request, CancellationToken.None)).ErrorCode.Should().Be("session_assertion_invalid");
		}

		[Test]
		public async Task An_assertion_for_decrypt_cannot_authorize_encrypt()
		{
			LiveSession();

			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "v2-3", Item("value")), decrypt: true), CancellationToken.None))
				.ErrorCode.Should().Be("session_assertion_invalid");
		}

		[Test]
		public async Task The_assertion_and_grant_must_name_the_same_session_and_user()
		{
			LiveSession();

			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "v2-4", Item("value")), sessionId: "session-other"),
				CancellationToken.None)).ErrorCode.Should().Be("grant_session_mismatch");
			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "v2-5", Item("value")), userId: "user-2"),
				CancellationToken.None)).ErrorCode.Should().Be("grant_session_mismatch");
			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "v2-6", Item("value")), client: (int)UserSessionClientApplication.Unit),
				CancellationToken.None)).ErrorCode.Should().Be("grant_client_mismatch");
		}

		[Test]
		public async Task A_revoked_session_is_refused_even_with_a_valid_grant_and_assertion()
		{
			LiveSession(valid: false);

			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "v2-7", Item("value"))), CancellationToken.None))
				.ErrorCode.Should().Be("session_revoked");
		}

		[Test]
		public async Task The_live_session_must_belong_to_the_asserted_client()
		{
			LiveSession(client: (int)UserSessionClientApplication.Responder);

			(await _service.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "v2-8", Item("value"))), CancellationToken.None))
				.ErrorCode.Should().Be("grant_client_mismatch");
		}

		[Test]
		public async Task A_version_one_grant_without_an_assertion_keeps_working_until_assertions_are_required()
		{
			var required = Resgrid.Config.DataProtectionConfig.BrokerRequireSessionAssertion;
			try
			{
				(await _service.EncryptAsync(Request(IssueGrantToken(), "v1-1", Item("value")), CancellationToken.None)).Success.Should().BeTrue();

				Resgrid.Config.DataProtectionConfig.BrokerRequireSessionAssertion = true;
				(await _service.EncryptAsync(Request(IssueGrantToken(), "v1-2", Item("value")), CancellationToken.None))
					.ErrorCode.Should().Be("session_assertion_required");

				LiveSession();
				(await _service.EncryptAsync(Asserted(Request(IssueGrantToken(), "v1-3", Item("value"))), CancellationToken.None))
					.Success.Should().BeTrue();
			}
			finally { Resgrid.Config.DataProtectionConfig.BrokerRequireSessionAssertion = required; }
		}

		[Test]
		public async Task A_presented_assertion_must_be_valid_even_for_a_version_one_grant()
		{
			var garbage = Request(IssueGrantToken(), "v1-4", Item("value"));
			garbage.SessionAssertion = "not.an.assertion";
			(await _service.EncryptAsync(garbage, CancellationToken.None)).ErrorCode.Should().Be("session_assertion_invalid");

			LiveSession(valid: false);
			(await _service.EncryptAsync(Asserted(Request(IssueGrantToken(), "v1-5", Item("value"))), CancellationToken.None))
				.ErrorCode.Should().Be("session_revoked");
		}

		[Test]
		public async Task A_broker_without_the_assertion_certificate_refuses_version_two_and_ignores_it_for_version_one()
		{
			var unconfigured = new BrokerOperationService(_container, _grantService, _cryptoService, _keyWrappingProvider,
				Mock.Of<IAdpAuditRepository>(), new BrokerSessionAssertionService(() => null, () => null));

			(await unconfigured.EncryptAsync(Asserted(Request(IssueVersionTwoGrant(), "nc-1", Item("value"))), CancellationToken.None))
				.ErrorCode.Should().Be("session_assertion_unavailable");
			(await unconfigured.EncryptAsync(Asserted(Request(IssueGrantToken(), "nc-2", Item("value"))), CancellationToken.None))
				.Success.Should().BeTrue();
		}

		[Test]
		public async Task Grant_less_workload_encrypt_needs_no_assertion()
		{
			var required = Resgrid.Config.DataProtectionConfig.BrokerRequireSessionAssertion;
			try
			{
				Resgrid.Config.DataProtectionConfig.BrokerRequireSessionAssertion = true;
				(await _service.EncryptAsync(Request(null, "wl-a", Item("value")), CancellationToken.None)).Success.Should().BeTrue();
			}
			finally { Resgrid.Config.DataProtectionConfig.BrokerRequireSessionAssertion = required; }
		}

		[Test]
		public async Task Replay_records_are_shared_so_a_second_broker_refuses_the_same_request()
		{
			var token = IssueGrantToken();
			(await _service.EncryptAsync(Request(token, "shared-1", Item("value")), CancellationToken.None)).Success.Should().BeTrue();

			var otherReplica = new BrokerOperationService(_container, _grantService, _cryptoService, _keyWrappingProvider,
				Mock.Of<IAdpAuditRepository>(), _assertionService);
			(await otherReplica.EncryptAsync(Request(token, "shared-1", Item("value")), CancellationToken.None))
				.ErrorCode.Should().Be("replayed_request");
		}

		// ---- Lane split (passkey plan section 8.5) ---------------------------------------------------------------------

		private static BrokerCredential Credential(string id, string[] purposes, params BrokerLane[] lanes) => new()
		{
			Id = id,
			Lanes = lanes.ToHashSet(),
			WorkloadPurposes = purposes.ToHashSet(StringComparer.Ordinal)
		};

		private static BrokerFieldOperationRequest As(BrokerCredential caller, BrokerFieldOperationRequest request)
		{
			request.Caller = caller;
			return request;
		}

		private static readonly BrokerCredential Workers = Credential("workers", new[] { "neris-submission" }, BrokerLane.Workload);
		private static readonly BrokerCredential Api = Credential("api", Array.Empty<string>(), BrokerLane.Attended);
		// BackOffice support redeems receipts and saves messages (a grant-less encrypt); it never decrypts for workload.
		private static readonly BrokerCredential Backoffice = Credential("backoffice", Array.Empty<string>(), BrokerLane.Receipt, BrokerLane.Workload);

		[TestCase(true, null, null, BrokerLane.Attended)]
		[TestCase(true, "grant", null, BrokerLane.Attended)]
		[TestCase(true, "adpr.receipt", null, BrokerLane.Receipt)]
		[TestCase(false, null, null, BrokerLane.Workload)]
		[TestCase(false, "grant", null, BrokerLane.Attended)]
		[TestCase(false, "adpr.receipt", null, BrokerLane.Attended)]
		[TestCase(true, null, "records-export", BrokerLane.Workload)]
		public void Every_request_belongs_to_exactly_one_lane(bool decrypt, string token, string purpose, BrokerLane expected)
		{
			BrokerOperationService.Classify(new BrokerFieldOperationRequest { GrantToken = token }, decrypt, purpose).Should().Be(expected);
		}

		[TestCase("grant")]
		[TestCase("adpr.receipt")]
		public void A_token_on_the_workload_lane_fits_no_lane(string token)
		{
			BrokerOperationService.Classify(new BrokerFieldOperationRequest { GrantToken = token }, true, "records-export").Should().BeNull();
		}

		[Test]
		public async Task Workers_encrypt_and_use_their_own_purposes_but_never_act_for_a_user()
		{
			EnrollDepartment(DepartmentDataProtectionState.Enabled);

			var sealedResult = await _service.EncryptAsync(As(Workers, Request(null, "lane-1", Item("Sensitive"))), CancellationToken.None);
			sealedResult.Success.Should().BeTrue();
			var envelope = sealedResult.Items[0].Value;

			(await _service.DecryptForWorkloadAsync(As(Workers, Request(null, "lane-2", Item(envelope))), "neris-submission", CancellationToken.None))
				.Success.Should().BeTrue();
			(await _service.DecryptForWorkloadAsync(As(Workers, Request(null, "lane-3", Item(envelope))), "records-export", CancellationToken.None))
				.ErrorCode.Should().Be("workload_purpose_denied", "records-export is allowed by the broker but was not granted to this host");
			(await _service.DecryptAsync(As(Workers, Request(IssueGrantToken(), "lane-4", Item(envelope))), CancellationToken.None))
				.ErrorCode.Should().Be("lane_denied");
		}

		[Test]
		public async Task An_attended_only_host_cannot_encrypt_without_a_grant()
		{
			(await _service.EncryptAsync(As(Api, Request(null, "lane-5", Item("value"))), CancellationToken.None)).ErrorCode.Should().Be("lane_denied");
			(await _service.EncryptAsync(As(Api, Request(IssueGrantToken(), "lane-6", Item("value"))), CancellationToken.None)).Success.Should().BeTrue();
		}

		[Test]
		public async Task BackOffice_support_redeems_receipts_and_encrypts_but_never_decrypts_for_workload_or_a_user()
		{
			(await _service.EncryptAsync(As(Backoffice, Request(null, "lane-7", Item("value"))), CancellationToken.None)).Success.Should().BeTrue();
			(await _service.EncryptAsync(As(Backoffice, Request(IssueGrantToken(), "lane-8", Item("value"))), CancellationToken.None)).ErrorCode.Should().Be("lane_denied");
			(await _service.DecryptForWorkloadAsync(As(Backoffice, Request(null, "lane-9", Item("value"))), "records-export", CancellationToken.None))
				.ErrorCode.Should().Be("workload_purpose_denied", "an encrypt-only workload credential has no purposes");
		}

		[Test]
		public async Task A_host_without_the_receipt_lane_cannot_present_a_receipt()
		{
			(await _service.DecryptAsync(As(Api, Request("adpr.receipt", "lane-10", Item("rgdp:1:1:x"))), CancellationToken.None))
				.ErrorCode.Should().Be("lane_denied");
		}

		[Test]
		public async Task A_lane_refusal_consumes_nothing_and_is_audited_with_credential_and_lane()
		{
			var events = new List<AdpAuditEvent>();
			var audit = new Mock<IAdpAuditRepository>();
			audit.Setup(a => a.AppendAsync(It.IsAny<AdpAuditEvent>(), It.IsAny<CancellationToken>()))
				.Callback<AdpAuditEvent, CancellationToken>((e, _) => events.Add(e))
				.Returns(Task.CompletedTask);
			var service = new BrokerOperationService(_container, _grantService, _cryptoService, _keyWrappingProvider, audit.Object, _assertionService);

			var refused = await service.DecryptAsync(As(Workers, Request(IssueGrantToken(), "lane-11", Item("rgdp:1:1:x"))), CancellationToken.None);

			refused.ErrorCode.Should().Be("lane_denied");
			_replay.Count.Should().Be(0, "the refused request id was not burned");
			events.Select(e => e.Layer).Should().AllBe("broker/workers/attended");
			events.Select(e => e.Outcome).Should().Equal("requested", "lane-denied");

			events.Clear();
			(await service.EncryptAsync(As(Workers, Request(null, "lane-12", Item("value"))), CancellationToken.None)).Success.Should().BeTrue();
			events.Select(e => e.Layer).Should().AllBe("broker/workers/workload");
			events.Last().Outcome.Should().Be("completed");
		}

		[Test]
		public async Task A_request_without_an_authenticated_host_is_refused()
		{
			var request = Request(IssueGrantToken(), "lane-13", Item("value"));
			request.Caller = null;

			(await _service.EncryptAsync(request, CancellationToken.None)).ErrorCode.Should().Be("lane_denied");
		}

		[Test]
		public async Task A_replay_store_fault_refuses_the_request()
		{
			_replay.Faulted = true;

			var refused = await _service.EncryptAsync(Request(IssueGrantToken(), "fault-1", Item("value")), CancellationToken.None);

			refused.Success.Should().BeFalse();
			refused.ErrorCode.Should().Be("replay_store_unavailable");
		}

	}
}
