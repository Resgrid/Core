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
	public class ScopeSafetyTests
	{
		private static readonly ConfigurationCatalog Catalog = new();
		private readonly DateTime _now = DateTime.UtcNow;
		private ConfigurationSnapshot Snapshot(params ConfigurationEvidence[] facts) => new(7, "admin", "1", _now, true, facts.ToDictionary(f => f.Id));
		[Test]
		public void Deferred_area_cannot_hide_a_verified_critical_failure_or_unknown_check_for_enabled_routing()
		{
			var rule = new ConfigurationRule(Catalog.Rules.Single(r => r.Id == "text-sources"));
			var snapshot = Snapshot(new("EnableTextToCall", EvidenceState.Known, "test", "1", _now, Boolean: true), new("textSourcePresent", EvidenceState.Known, "test", "1", _now, Boolean: false));
			var failure = rule.Evaluate(snapshot, _now, TimeSpan.FromMinutes(1));
			var report = new ConfigurationReport(snapshot, new[] { failure }, new[] { "security" });
			Assert.That(report.Failed, Is.EqualTo(1)); Assert.That(failure.ScopeIndependent, Is.True);
			var unknown = rule.Evaluate(Snapshot(new ConfigurationEvidence("EnableTextToCall", EvidenceState.Known, "test", "1", _now, Boolean: true)), _now, TimeSpan.FromMinutes(1));
			Assert.That(new ConfigurationReport(snapshot, new[] { unknown }, new[] { "security" }).HasCriticalUncertainty, Is.True);
		}
		[Test]
		public void Known_unavailable_optional_maintenance_does_not_lower_selected_area_readiness()
		{
			var snapshot = Snapshot(new ConfigurationEvidence("maintenanceAvailable", EvidenceState.Known, "test", "1", _now, Boolean: false));
			var finding = new ConfigurationRule(Catalog.Rules.Single(r => r.Id == "equipment-holds")).Evaluate(snapshot, _now, TimeSpan.FromMinutes(1));
			Assert.That(finding.Result, Is.EqualTo(RuleResult.NotApplicable));
			var report = new ConfigurationReport(snapshot, new[] { finding }, new[] { "maintenance" });
			Assert.That(report.Required, Is.Zero); Assert.That(report.HasCriticalUncertainty, Is.False);
		}
		[Test]
		public void Fixing_a_deferred_critical_failure_raises_verified_instead_of_shrinking_required()
		{
			var rule = new ConfigurationRule(Catalog.Rules.Single(r => r.Id == "text-sources"));
			ConfigurationReport Report(bool sourcePresent)
			{
				var snapshot = Snapshot(new("EnableTextToCall", EvidenceState.Known, "test", "1", _now, Boolean: true), new("textSourcePresent", EvidenceState.Known, "test", "1", _now, Boolean: sourcePresent));
				return new ConfigurationReport(snapshot, new[] { rule.Evaluate(snapshot, _now, TimeSpan.FromMinutes(1)) }, new[] { "security" });
			}
			var failing = Report(false); var repaired = Report(true);
			Assert.That((failing.Required, failing.Failed, failing.Verified), Is.EqualTo((1, 1, 0)));
			Assert.That((repaired.Required, repaired.Failed, repaired.Verified), Is.EqualTo((1, 0, 1)));
		}
		[TestCase("person-location-age", "MappingPersonnelLocationTTL")]
		[TestCase("unit-location-age", "MappingUnitLocationTTL")]
		public void Product_default_location_lifetime_is_a_suggestion_not_a_failure(string ruleId, string setting)
		{
			var snapshot = Snapshot(new ConfigurationEvidence(setting, EvidenceState.Known, "test", "1", _now, Number: 0));
			var finding = new ConfigurationRule(Catalog.Rules.Single(r => r.Id == ruleId)).Evaluate(snapshot, _now, TimeSpan.FromMinutes(1));
			Assert.That(finding.Result, Is.EqualTo(RuleResult.Fail)); Assert.That(finding.Severity, Is.EqualTo(FindingSeverity.Information));
			var report = new ConfigurationReport(snapshot, new[] { finding }, new[] { "mapping" });
			Assert.That((report.Required, report.Failed, report.Suggestions), Is.EqualTo((0, 0, 1)));
		}
		[Test]
		public void Combined_text_intake_and_commands_count_one_failure_for_one_missing_source()
		{
			var snapshot = Snapshot(new("EnableTextToCall", EvidenceState.Known, "test", "1", _now, Boolean: true), new("EnableTextCommand", EvidenceState.Known, "test", "1", _now, Boolean: true),
				new("textSourcePresent", EvidenceState.Known, "test", "1", _now, Boolean: false));
			var findings = new[] { "text-sources", "command-sources" }.Select(id => new ConfigurationRule(Catalog.Rules.Single(r => r.Id == id)).Evaluate(snapshot, _now, TimeSpan.FromMinutes(1))).ToArray();
			var report = new ConfigurationReport(snapshot, findings, new[] { "calls", "communication" });
			Assert.That((report.Failed, report.Suggestions), Is.EqualTo((1, 1)));
		}
		[TestCase(EvidenceState.Unavailable, "AddonRequired.ReadinessPro", EvidenceState.Known)]
		[TestCase(EvidenceState.Unknown, "SubscriptionStatusUnavailable", EvidenceState.Unknown)]
		[TestCase(EvidenceState.Unavailable, "SourceAccessUnavailable", EvidenceState.Unknown)]
		public async Task Availability_failure_cannot_be_misreported_as_no_entitlement(EvidenceState source, string reason, EvidenceState expected)
		{
			var capability = Catalog.Capabilities.First(c => c.Location.Controller == "WorkOrders"); var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.GetCapabilityAsync(It.IsAny<AdminAssistActor>(), capability.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new CapabilityAccess(capability.Id, source, new[] { reason }, false, null, _now));
			var evidence = (await new CapabilityEvidenceSource(access.Object, Catalog).ReadAsync(new(7, "admin"), _now, CancellationToken.None)).Single();
			Assert.That(evidence.State, Is.EqualTo(expected)); Assert.That(evidence.Boolean, Is.EqualTo(expected == EvidenceState.Known ? false : (bool?)null));
		}
	}
}
