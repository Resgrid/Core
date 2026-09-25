using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class SecurityImpactTests
	{
		private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		private static readonly AdminAssistActor Actor = new(7, "admin");
		private string _gate;
		[SetUp] public void SetUp() { _gate = Resgrid.Config.SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc; Resgrid.Config.SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = "2026-09-01T00:00:00Z"; }
		[TearDown] public void TearDown() => Resgrid.Config.SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = _gate;
		private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
		private sealed class Fixture
		{
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Repository = new();
			public readonly Mock<ISecurityImpactStore> Store = new();
			public SecurityImpactService Service => new(Access.Object, Repository.Object, new ConfigurationCatalog(), Store.Object, new Clock());
			public Fixture(int enabledProviders = 0)
			{
				Access.Setup(a => a.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Store.Setup(s => s.ReadSecurityImpactAsync(7, Now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SecurityImpactEvidence(new[] {
					new SecurityMemberEvidence { DepartmentId = 7, MemberId = 1, UserId = "one", TwoFactorEnabled = false, PasswordLastSetOn = Now.AddDays(-31) },
					new SecurityMemberEvidence { DepartmentId = 7, MemberId = 2, UserId = "two", TwoFactorEnabled = true, PasswordLastSetOn = Now.AddDays(-30) },
					new SecurityMemberEvidence { DepartmentId = 7, MemberId = 3, UserId = "three", TwoFactorEnabled = null } },
					new[] { Session("a", Now.AddHours(-2), Now.AddMinutes(-31)), Session("b", Now.AddHours(-2), Now.AddMinutes(-30)),
						Session("old", Now.AddMonths(-1), Now.AddMinutes(-40)) }, enabledProviders));
			}
			private static SecuritySessionEvidence Session(string id, DateTime created, DateTime active) => new() { DepartmentId = 7, Id = id, UserId = "one",
				CreatedOn = created, LastActiveOn = active, ExpiresOn = Now.AddHours(1), AuthenticationGeneration = 1, CurrentGeneration = 1 };
			public Task<ConfigurationImpactReport> Preview(string field, bool? boolean = null, decimal? number = null) => Service.PreviewAsync(Actor, new("table.DepartmentSecurityPolicy." + field, "0", boolean, number));
		}
		private static ConfigurationImpactMetric Metric(ConfigurationImpactReport report, string suffix) => report.Metrics.Single(m => m.LabelKey == "Impact.Security" + suffix);
		[Test]
		public async Task Mfa_proposal_reports_enrollment_range_and_does_not_claim_factor_or_recovery_readiness()
		{
			var f = new Fixture(); var report = await f.Preview("RequireMfa", true);
			Assert.That(Metric(report, "MfaEnrollmentMinimum").Before, Is.Zero); Assert.That(Metric(report, "MfaEnrollmentMinimum").After, Is.EqualTo(1));
			Assert.That(Metric(report, "MfaEnrollmentMaximum").After, Is.EqualTo(2)); Assert.That(Metric(report, "MfaCompletionRequired").After, Is.EqualTo(3));
			Assert.That(Metric(report, "RecoveryReadiness").State, Is.EqualTo(EvidenceState.Unknown));
			f.Store.Verify(s => s.ReadSecurityPolicyAsync(7, It.IsAny<CancellationToken>()), Times.Exactly(2));
			f.Store.Verify(s => s.ReadSecurityImpactAsync(7, Now, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2)); f.Store.VerifyNoOtherCalls();
		}
		[TestCase(0, 0, 1)][TestCase(1, 3, 0)]
		public async Task Sso_matches_the_active_provider_safety_valve_without_claiming_connectivity(int providers, int blocked, int valve)
		{
			var report = await new Fixture(providers).Preview("RequireSso", true);
			Assert.That(Metric(report, "PasswordPathBlocked").After, Is.EqualTo(blocked)); Assert.That(Metric(report, "SsoSafetyValve").After, Is.EqualTo(valve));
			Assert.That(Metric(report, "ProviderAndRecovery").After, Is.Null);
		}
		[Test]
		public async Task Password_age_uses_strict_day_boundary_and_grandfathers_untracked_dates()
		{
			var report = await new Fixture().Preview("PasswordExpirationDays", number: 30);
			Assert.That(Metric(report, "ExpiredPasswordAge").Before, Is.Zero); Assert.That(Metric(report, "ExpiredPasswordAge").After, Is.EqualTo(1));
			Assert.That(Metric(report, "UntrackedPasswordAge").After, Is.EqualTo(1));
		}
		[Test]
		public async Task Length_preview_never_infers_plaintext_length_from_stored_passwords()
		{
			var report = await new Fixture().Preview("MinPasswordLength", number: 12);
			Assert.That(Metric(report, "MinimumLength").Before, Is.EqualTo(8)); Assert.That(Metric(report, "MinimumLength").After, Is.EqualTo(12));
			Assert.That(Metric(report, "ExistingPasswordCompliance").State, Is.EqualTo(EvidenceState.Unknown));
		}
		[Test]
		public async Task Idle_counts_include_equal_boundary_but_exclude_pre_rollout_sessions()
		{
			var report = await new Fixture().Preview("SessionTimeoutMinutes", number: 30);
			Assert.That(Metric(report, "ManagedSessions").After, Is.EqualTo(2)); Assert.That(Metric(report, "IdleExpiryCandidates").After, Is.EqualTo(2));
			Assert.That(Metric(report, "ActualReauthentication").State, Is.EqualTo(EvidenceState.Unknown));
		}
		[TestCase("")][TestCase("invalid")][TestCase("2027-01-01T00:00:00Z")]
		public async Task Inactive_host_policy_gate_does_not_claim_session_enforcement(string gate)
		{
			Resgrid.Config.SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = gate;
			var report = await new Fixture().Preview("MaxConcurrentSessions", number: 1);
			Assert.That(Metric(report, "AtSessionLimit").After, Is.Zero); Assert.That(Metric(report, "SessionGateActive").After, Is.Zero);
		}
		[Test]
		public async Task Concurrency_limit_estimates_next_insert_gate_without_revoking_existing_sessions()
		{
			var report = await new Fixture().Preview("MaxConcurrentSessions", number: 2);
			Assert.That(Metric(report, "AtSessionLimit").Before, Is.Zero); Assert.That(Metric(report, "AtSessionLimit").After, Is.EqualTo(1));
			Assert.That(report.LimitKeys, Does.Contain("Impact.SecurityMaxConcurrentSessionsScope"));
		}
		[Test]
		public async Task Source_failure_or_foreign_policy_is_unknown_not_an_empty_department()
		{
			var f = new Fixture(); f.Store.Setup(s => s.ReadSecurityPolicyAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = 8 });
			Assert.That((await f.Preview("RequireSso", true)).Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown));
			f = new Fixture(); f.Store.Setup(s => s.ReadSecurityImpactAsync(7, Now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
			Assert.That((await f.Preview("RequireSso", true)).Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown));
		}
		[Test]
		public void Revocation_and_policy_drift_reject_completed_calculations()
		{
			var f = new Fixture(); f.Access.SetupSequence(a => a.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Preview("RequireMfa", true));
			f = new Fixture(); f.Store.SetupSequence(s => s.ReadSecurityPolicyAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = 7 }).ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = 7, RequireMfa = true });
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Preview("RequireMfa", true));
		}
		[TestCase("MinPasswordLength", 7)][TestCase("MaxConcurrentSessions", -1)][TestCase("SessionTimeoutMinutes", 0.5)][TestCase("PasswordExpirationDays", 36501)][TestCase("AllowedIpRanges", 1)]
		public void Unreviewed_fields_and_invalid_values_are_rejected_before_metadata_reads(string field, decimal number)
		{
			var f = new Fixture(); Assert.ThrowsAsync<ArgumentException>(async () => await f.Preview(field, number: number)); f.Store.VerifyNoOtherCalls();
		}
	}
}
