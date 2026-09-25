using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class ConfigurationImpactTests
	{
		private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		private static readonly ConfigurationCatalog Catalog = new();
		private static ConfigurationSnapshot Snapshot(params ConfigurationEvidence[] evidence) => new(7, "admin", "3", Now, true, evidence.ToDictionary(e => e.Id));
		private static ConfigurationEvidence Boolean(string id, bool value) => new(id, EvidenceState.Known, "test", "1", Now, Boolean: value);
		private static ConfigurationEvidence Number(string id, decimal value) => new(id, EvidenceState.Known, "test", "1", Now, Number: value);
		private ConfigurationImpactReport Evaluate(ConfigurationSnapshot snapshot, string id, bool? boolean = null, decimal? number = null) =>
			new ConfigurationImpactEvaluator(Catalog).Evaluate(snapshot, new("setting." + id, "3", boolean, number), Now, TimeSpan.FromMinutes(1));

		[Test]
		public void Preview_has_no_mutation_and_shows_rule_dependencies()
		{
			var snapshot = Snapshot(Boolean("AutoSetStatusForShiftDispatchPersonnel", true), Boolean("DispatchShiftInsteadOfGroup", false));
			var result = Evaluate(snapshot, "DispatchShiftInsteadOfGroup", true);
			Assert.That(snapshot.Find("DispatchShiftInsteadOfGroup").Boolean, Is.False);
			var change = result.RuleChanges.Single(r => r.RuleId == "shift-auto-without-dispatch");
			Assert.That(change.Before, Is.EqualTo(RuleResult.Fail)); Assert.That(change.After, Is.EqualTo(RuleResult.Pass));
			Assert.That(result.RuleChanges.Single(r => r.RuleId == "shift-coverage").After, Is.EqualTo(RuleResult.Unknown));
		}
		[Test]
		public void Map_override_overlay_models_credential_deletion_without_returning_secrets()
		{
			var snapshot = Snapshot(Boolean("MappingUseMapboxOverride", true), Boolean("mapTokenPresent", true), Boolean("mapStylePresent", true));
			var result = Evaluate(snapshot, "MappingUseMapboxOverride", false);
			Assert.That(result.LimitKeys, Does.Contain("Impact.MapCredentialsRemoved"));
			Assert.That(snapshot.Find("mapTokenPresent").Boolean, Is.True);
			Assert.That(result.RuleChanges.Where(r => r.RuleId.StartsWith("map-")).All(r => r.After == RuleResult.NotApplicable), Is.True);
		}
		[Test]
		public void Missing_enrollment_evidence_is_not_zero_and_known_enrollment_has_bounded_scope()
		{
			Assert.That(Evaluate(Snapshot(Number("Require2FAForAdmins", 0)), "Require2FAForAdmins", number: 1).Metrics[1].After, Is.Null);
			var result = Evaluate(Snapshot(Number("Require2FAForAdmins", 0), Number("adminsWithoutMfa", 2)), "Require2FAForAdmins", number: 1);
			Assert.That(result.Metrics[1].Before, Is.Zero); Assert.That(result.Metrics[1].After, Is.EqualTo(2));
			Assert.That(result.LimitKeys, Does.Contain("Impact.MfaRecoveryUnknown"));
		}
		[Test]
		public void Group_admin_scope_uses_disjoint_enrollment_counts_and_missing_groups_remain_unknown()
		{
			var evidence = Snapshot(Number("Require2FAForAdmins", 1), Number("adminsWithoutMfa", 2), Number("groupOnlyAdminsWithoutMfa", 3));
			var result = Evaluate(evidence, "Require2FAForAdmins", number: 2);
			Assert.That(result.Metrics[1].Before, Is.EqualTo(2)); Assert.That(result.Metrics[1].After, Is.EqualTo(5));
			var unknown = Evaluate(Snapshot(Number("Require2FAForAdmins", 1), Number("adminsWithoutMfa", 2)), "Require2FAForAdmins", number: 2);
			Assert.That(unknown.Metrics[1].Before, Is.EqualTo(2)); Assert.That(unknown.Metrics[1].After, Is.Null);
		}
		[TestCase(-1)] [TestCase(3)] [TestCase(0.5)]
		public void Invalid_MFA_policy_is_rejected(decimal value) => Assert.Throws<ArgumentException>(() => Evaluate(Snapshot(), "Require2FAForAdmins", number: value));
		[Test]
		public void Unsupported_secret_and_stale_revision_are_rejected()
		{
			Assert.Throws<ArgumentException>(() => Evaluate(Snapshot(), "MappingMapboxAccessToken", true));
			Assert.Throws<AdminAssistConcurrencyException>(() => Evaluate(Snapshot() with { Revision = "4" }, "EnableTextToCall", true));
			Assert.Throws<AdminAssistConcurrencyException>(() => Evaluate(Snapshot() with { Consistent = false }, "EnableTextToCall", true));
		}
		[Test]
		public void Mid_preview_revocation_rejects_the_result()
		{
			var actor = new AdminAssistActor(7, "admin"); var access = new Mock<IAdminAssistAccessService>();
			access.SetupSequence(a => a.CanAccessAsync(actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			var snapshots = new Mock<IConfigurationSnapshotProvider>(); snapshots.Setup(s => s.ReadAsync(actor, It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
			var repository = new Mock<IAdminAssistRepository>(); repository.Setup(r => r.GetConfigurationRevisionAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(3);
			var service = new ConfigurationImpactService(access.Object, snapshots.Object, Catalog, repository.Object, TimeProvider.System);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(actor, new("setting.EnableTextToCall", "3", true)));
		}
	}
}
