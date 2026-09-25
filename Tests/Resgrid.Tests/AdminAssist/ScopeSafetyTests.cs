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
