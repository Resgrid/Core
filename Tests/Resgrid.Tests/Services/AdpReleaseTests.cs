using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    [TestFixture]
    public class AdpReleaseTests
    {
        private MemoryAccessStore _store;
        private Mock<IAdpAuditRepository> _audit;
        private Mock<IDepartmentDataProtectionService> _protection;
        private Mock<IAuthorizationService> _authorization;
        private Mock<IProtectedDataGrantService> _grants;
        private Mock<IProtectedDataBrokerClient> _broker;
        private Mock<IUserProfileService> _profiles;
        private Mock<ICallsService> _calls;
        private AdpReleaseService _service;
        private AdpReleaseReceiptService _receipts;
        private DepartmentDataProtectionPolicy _policy;
        private DepartmentProtectedDataEgressPolicy _egress;
        private const string Phone = "+12015550123";
        private const string Pin = "739152";

        [SetUp]
        public void Setup()
        {
            _store = new MemoryAccessStore();
            _audit = new Mock<IAdpAuditRepository>();
            _policy = new DepartmentDataProtectionPolicy { DepartmentId = 7, PolicyEpoch = 3, State = (int)DepartmentDataProtectionState.Enabled };
            _egress = new DepartmentProtectedDataEgressPolicy { DepartmentId = 7, SmsMode = 1, VoiceMode = 1,
                AcknowledgementVersion = "v1", AcknowledgedOn = DateTime.UtcNow, PinChallengeExpiryMinutes = 5, PinMaxAttempts = 3, PinLockoutMinutes = 15 };
            _protection = new Mock<IDepartmentDataProtectionService>();
            _protection.Setup(p => p.IsProtectionEnforcedAsync(7)).ReturnsAsync(true);
            _protection.Setup(p => p.GetPolicyByDepartmentIdAsync(7, true)).ReturnsAsync(_policy);
            _protection.Setup(p => p.GetEgressPolicyByDepartmentIdAsync(7, true)).ReturnsAsync(_egress);
            _grants = new Mock<IProtectedDataGrantService>();
            var grant = new ProtectedDataGrant { UserId = "member", MfaAtUtc = DateTime.UtcNow };
            _grants.Setup(g => g.ValidateGrant("grant", 7, 3, ProtectedDataGrantScopes.Read, out grant, null))
                .Returns(ProtectedDataGrantValidationOutcome.Valid);
            _authorization = new Mock<IAuthorizationService>();
            _authorization.Setup(a => a.CanUserViewCallAsync("member", 42)).ReturnsAsync(true);
            _profiles = new Mock<IUserProfileService>();
            _profiles.Setup(p => p.GetProfileByUserIdAsync("member", false)).ReturnsAsync(
                new UserProfile { UserId = "member", MobileNumber = Phone, MobileNumberVerified = true });
            _calls = new Mock<ICallsService>();
            _calls.Setup(c => c.GetCallByIdAsync(42, true)).ReturnsAsync(new Call { CallId = 42, DepartmentId = 7,
                Name = "Dispatch name", Address = "Dispatch address", NatureOfCall = "Dispatch nature" });
            var departments = new Mock<IDepartmentsService>();
            departments.Setup(d => d.GetDepartmentMemberAsync("member", 7, true)).ReturnsAsync(new DepartmentMember());
            var permissions = new Mock<IPermissionsService>();
            permissions.Setup(p => p.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>())).Returns(true);
            _broker = new Mock<IProtectedDataBrokerClient>();
            _receipts = new AdpReleaseReceiptService(_store, _audit.Object);
            _service = new AdpReleaseService(_store, _audit.Object, _protection.Object, _grants.Object,
                _receipts, _broker.Object, _calls.Object, _authorization.Object, _profiles.Object, departments.Object,
                permissions.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
                new Resgrid.Providers.NumberProvider.PhoneNumberProcesserProvider());
        }

        private async Task<string> Challenge()
        {
            (await _service.EnrollPinAsync(7, "member", "grant", Pin)).Should().BeTrue();
            var id = await _service.CreateChallengeAsync(7, 42, "member", Phone, ProtectedDataEgressChannel.Sms);
            id.Should().NotBeNull();
            return id;
        }

        [Test]
        public async Task Release_is_one_use_and_never_stores_the_pin_or_content()
        {
            var id = await Challenge();
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().Contain("Dispatch nature");
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
            string.Join("", _store.Snapshot()).Should().NotContain(Pin).And.NotContain("Dispatch nature");
        }

        [Test]
        public async Task Lockout_survives_new_challenges_and_correct_pin()
        {
            var id = await Challenge();
            for (var i = 0; i < 3; i++)
                (await _service.ReleaseAsync(id, Phone, "000000", ProtectedDataEgressChannel.Sms)).Should().BeNull();
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
            (await _service.CreateChallengeAsync(7, 42, "member", Phone, ProtectedDataEgressChannel.Sms)).Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Encrypted_release_uses_bound_receipt_and_rechecks_authorization_after_broker(bool revoked)
        {
            _calls.Setup(c => c.GetCallByIdAsync(42, true)).ReturnsAsync(new Call { CallId = 42, DepartmentId = 7,
                Name = "rgdp:1:1:encrypted", Address = "Address", NatureOfCall = "Nature" });
            var id = await Challenge();
            _broker.Setup(b => b.DecryptAsync(7, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()))
                .Returns(async (int dept, string token, string requestId, IReadOnlyList<ProtectedFieldOperationItem> fields, CancellationToken ct) =>
                {
                    fields.Should().ContainSingle(f => f.FieldId == "calls.name" && f.RowKey == "42" && f.Value == "rgdp:1:1:encrypted");
                    (await _receipts.ConsumeAsync(token, dept, 3, fields, ct)).Should().Be("member");
                    if (revoked) _authorization.Setup(a => a.CanUserViewCallAsync("member", 42)).ReturnsAsync(false);
                    return new ProtectedDataBrokerResult { Success = true, Items = new List<ProtectedFieldOperationResult>
                        { new() { FieldId = "calls.name", RowKey = "42", Value = "Protected dispatch" } } };
                });
            var text = await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms);
            if (revoked) text.Should().BeNull();
            else text.Should().Be("Protected dispatch. Address. Nature");
            _broker.Verify(b => b.DecryptAsync(7, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ProtectedFieldOperationItem>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Wrong_phone_channel_epoch_and_changed_authorization_refuse_release()
        {
            var id = await Challenge();
            (await _service.ReleaseAsync(id, "+15555550999", Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Voice)).Should().BeNull();
            _policy.PolicyEpoch++;
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
            _policy.PolicyEpoch--;
            _authorization.Setup(a => a.CanUserViewCallAsync("member", 42)).ReturnsAsync(false);
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
        }

        [Test]
        public async Task Reset_pin_revokes_pending_challenges()
        {
            var id = await Challenge();
            (await _service.EnrollPinAsync(7, "member", "grant", "825173")).Should().BeTrue();
            (await _service.ReleaseAsync(id, Phone, "825173", ProtectedDataEgressChannel.Sms)).Should().BeNull();
        }

        [Test]
        public async Task Unverified_phone_and_disabled_egress_refuse_release()
        {
            var id = await Challenge();
            _egress.SmsMode = 0;
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
            _egress.SmsMode = 1;
            _profiles.Setup(p => p.GetProfileByUserIdAsync("member", false)).ReturnsAsync(new UserProfile { MobileNumber = Phone, MobileNumberVerified = false });
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
        }

        [TestCase("(201) 555-0123", "+12015550123")]
        [TestCase("0044 7400 555012", "+447400555012")]
        public async Task Stored_phone_formats_match_the_canonical_provider_callback(string stored, string callback)
        {
            _profiles.Setup(p => p.GetProfileByUserIdAsync("member", false)).ReturnsAsync(new UserProfile
                { UserId = "member", MobileNumber = stored, MobileNumberVerified = true });
            (await _service.EnrollPinAsync(7, "member", "grant", Pin)).Should().BeTrue();
            var id = await _service.CreateChallengeAsync(7, 42, "member", stored, ProtectedDataEgressChannel.Sms);
            id.Should().NotBeNull();
            (await _service.ReleaseAsync(id, callback, Pin, ProtectedDataEgressChannel.Sms)).Should().Contain("Dispatch nature");
        }

        [Test]
        public async Task Concurrent_release_has_exactly_one_winner()
        {
            var id = await Challenge();
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms))));
            results.Count(r => r != null).Should().Be(1);
        }

        [Test]
        public async Task Receipt_is_bound_to_tenant_epoch_exact_fields_and_is_atomic()
        {
            var fields = new[] { new ProtectedFieldOperationItem { FieldId = "calls.name", RowKey = "42", Value = "envelope" } };
            var token = await _receipts.IssueAsync(7, 3, "member", "sms-pin", fields);
            (await _receipts.ConsumeAsync(token, 8, 3, fields)).Should().BeNull();
            (await _receipts.ConsumeAsync(token, 7, 4, fields)).Should().BeNull();
            fields[0].RowKey = "43";
            (await _receipts.ConsumeAsync(token, 7, 3, fields)).Should().BeNull();
            fields[0].RowKey = "42";
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _receipts.ConsumeAsync(token, 7, 3, fields))));
            results.Count(r => r == "member").Should().Be(1);
        }

        [Test]
        public async Task Staff_receipt_requires_consent_and_revoke_reenable_does_not_restore_it()
        {
            var fields = new[] { new ProtectedFieldOperationItem { FieldId = "calls.name", RowKey = "42", Value = "envelope" } };
            Func<Task> issue = () => _receipts.IssueAsync(7, 3, "staff", "staff-support", fields);
            await issue.Should().ThrowAsync<UnauthorizedAccessException>();
            await _store.SaveAsync(AdpSupportConsent.Key(7), JsonConvert.SerializeObject(new AdpSupportConsent { Enabled = true }), 0);
            var token = await _receipts.IssueAsync(7, 3, "staff", "staff-support", fields);
            await _store.SaveAsync(AdpSupportConsent.Key(7), JsonConvert.SerializeObject(new AdpSupportConsent { Enabled = false }), 1);
            await _store.SaveAsync(AdpSupportConsent.Key(7), JsonConvert.SerializeObject(new AdpSupportConsent { Enabled = true }), 2);
            (await _receipts.ConsumeAsync(token, 7, 3, fields)).Should().BeNull();
        }

        [Test]
        public async Task Audit_failure_prevents_issuance()
        {
            _audit.Setup(a => a.AppendAsync(It.IsAny<AdpAuditEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
            Func<Task> issue = () => _service.EnrollPinAsync(7, "member", "grant", Pin);
            await issue.Should().ThrowAsync<InvalidOperationException>();
            _store.Snapshot().Should().BeEmpty();
        }

        [Test]
        public async Task Kms_audit_failure_zeros_the_unwrapped_key_before_refusing_it()
        {
            var key = Enumerable.Repeat((byte)42, 32).ToArray();
            var inner = new Mock<IKeyWrappingProvider>();
            inner.Setup(p => p.UnwrapDataKeyAsync(7, "wrapped", It.IsAny<CancellationToken>())).ReturnsAsync(key);
            _audit.Setup(a => a.AppendAsync(It.Is<AdpAuditEvent>(e => e.Outcome == "completed"), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Audit storage unavailable"));
            var audited = new Resgrid.Providers.ProtectedData.AuditedKeyWrappingProvider(inner.Object, _audit.Object);
            Func<Task> unwrap = () => audited.UnwrapDataKeyAsync(7, "wrapped");
            await unwrap.Should().ThrowAsync<InvalidOperationException>();
            key.Should().OnlyContain(b => b == 0);
        }

        [Test]
        public async Task Expired_challenge_is_refused()
        {
            var id = await Challenge();
            var state = await _store.GetAsync("challenge:" + id);
            var json = Newtonsoft.Json.Linq.JObject.Parse(state.Json);
            json["ExpiresUtc"] = DateTime.UtcNow.AddMinutes(-1);
            await _store.SaveAsync(state.StateId, json.ToString(), state.Version);
            (await _service.ReleaseAsync(id, Phone, Pin, ProtectedDataEgressChannel.Sms)).Should().BeNull();
        }

        [TestCase(true, "member", 0)]
        [TestCase(false, "someone-else", 0)]
        [TestCase(false, "member", -10)]
        [TestCase(false, "member", 10)]
        public async Task Pin_enrollment_requires_same_user_and_recent_real_mfa(bool exempt, string userId, int offset)
        {
            var grant = new ProtectedDataGrant { UserId = userId, StepUpExempt = exempt, MfaAtUtc = DateTime.UtcNow.AddMinutes(offset) };
            _grants.Setup(g => g.ValidateGrant("grant", 7, 3, ProtectedDataGrantScopes.Read, out grant, null)).Returns(ProtectedDataGrantValidationOutcome.Valid);
            (await _service.EnrollPinAsync(7, "member", "grant", Pin)).Should().BeFalse();
            _store.Snapshot().Should().BeEmpty();
        }

        [Test]
        public void Chain_detects_mutation_reordering_cross_tenant_and_truncation_against_checkpoint()
        {
            var first = new AdpAuditEvent { DepartmentId = 7, Layer = "identity", Operation = "grant", Outcome = "verified" };
            var second = new AdpAuditEvent { DepartmentId = 7, Layer = "broker", Operation = "decrypt", Outcome = "completed" };
            AdpAuditChain.Link(first, null);
            AdpAuditChain.Link(second, first);
            var checkpoint = second.Hash;
            AdpAuditChain.Verify(new[] { first, second }, 2, checkpoint).Should().BeTrue();
            AdpAuditChain.Verify(new[] { first }, 2, checkpoint).Should().BeFalse();
            AdpAuditChain.Verify(new[] { second, first }, 2, checkpoint).Should().BeFalse();
            second.Outcome = "denied";
            AdpAuditChain.Verify(new[] { first, second }, 2, checkpoint).Should().BeFalse();
            second.DepartmentId = 8;
            AdpAuditChain.Link(second, first);
            AdpAuditChain.Verify(new[] { first, second }, 2, second.Hash).Should().BeFalse();
        }

        [Test]
        public void Chain_page_verifies_only_as_a_continuation_of_the_callers_anchor()
        {
            var first = new AdpAuditEvent { DepartmentId = 7, Layer = "identity", Operation = "grant", Outcome = "verified" };
            var second = new AdpAuditEvent { DepartmentId = 7, Layer = "broker", Operation = "decrypt", Outcome = "requested" };
            var third = new AdpAuditEvent { DepartmentId = 7, Layer = "broker", Operation = "decrypt", Outcome = "completed" };
            AdpAuditChain.Link(first, null);
            AdpAuditChain.Link(second, first);
            AdpAuditChain.Link(third, second);
            AdpAuditChain.VerifySegment(new[] { first }, 0, AdpAuditChain.Genesis).Should().BeTrue();
            AdpAuditChain.VerifySegment(new[] { second, third }, 1, first.Hash).Should().BeTrue();
            AdpAuditChain.VerifySegment(new AdpAuditEvent[0], 3, third.Hash).Should().BeTrue();
            // A gap, a stale anchor or a rewritten predecessor breaks continuity.
            AdpAuditChain.VerifySegment(new[] { third }, 1, first.Hash).Should().BeFalse();
            AdpAuditChain.VerifySegment(new[] { second, third }, 1, AdpAuditChain.Genesis).Should().BeFalse();
            AdpAuditChain.VerifySegment(new[] { second, third }, 1, null).Should().BeFalse();
        }

        private sealed class MemoryAccessStore : IAdpAccessStore
        {
            private readonly Dictionary<string, AdpAccessState> _rows = new();
            public Task<AdpAccessState> GetAsync(string id, CancellationToken cancellationToken = default)
            {
                lock (_rows) return Task.FromResult(_rows.TryGetValue(id, out var row)
                    ? new AdpAccessState { StateId = id, Version = row.Version, Json = row.Json } : null);
            }
            public Task<bool> SaveAsync(string id, string json, long expectedVersion, CancellationToken cancellationToken = default)
            {
                lock (_rows)
                {
                    if ((_rows.TryGetValue(id, out var row) ? row.Version : 0) != expectedVersion) return Task.FromResult(false);
                    _rows[id] = new AdpAccessState { StateId = id, Version = expectedVersion + 1, Json = json };
                    return Task.FromResult(true);
                }
            }
            public string[] Snapshot() { lock (_rows) return _rows.Values.Select(r => r.Json).ToArray(); }
        }
    }
}
