using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class CapabilitySetupTests
	{
		private readonly ConfigurationCatalog _catalog = new();
		private readonly DateTime _now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		private CapabilitySetupAssessment Evaluate(decimal? count, RuleResult result = RuleResult.Pass, EvidenceState factState = EvidenceState.Known,
			bool consistent = true, bool subscribed = false, EvidenceState accessState = EvidenceState.Known, EvidenceState? commercial = null, bool stale = false)
		{
			var capability = _catalog.Capabilities.Single(c => c.Id == "units");
			if (subscribed) capability = capability with { Requirements = new[] { new CapabilityRequirement("addon", "ReadinessPro") } };
			var fact = new ConfigurationEvidence("unitCount", factState, "test", "1", stale ? _now.AddMinutes(-5) : _now, Number: count);
			var snapshot = new ConfigurationSnapshot(7,"admin","19",_now,consistent,new Dictionary<string,ConfigurationEvidence>{{fact.Id,fact}});
			var findings = capability.Setup.RuleIds.Select(id => new ConfigurationFinding(id, "location", FindingSeverity.Warning, result, "title", "why", "next", "/User/Units/Index", Array.Empty<string>(), "19", _now)).ToArray();
			var access = new CapabilityAccess(capability.Id, accessState, Array.Empty<string>(), true, "/User/Units/Index", _now, CommercialState: commercial);
			return CapabilitySetupEvaluator.Evaluate(capability, access, new ConfigurationReport(snapshot,findings,new[]{"location"}),_now,TimeSpan.FromMinutes(1));
		}
		[Test]
		public void No_configuration_does_not_pass_even_if_absence_makes_the_health_rules_pass()
		{
			var result = Evaluate(0);
			Assert.That(result.State, Is.EqualTo(CapabilitySetupState.NotConfigured));
			Assert.That(result.OpportunityKey, Is.EqualTo("Ui.OpportunityIncludedNotConfigured"));
		}
		[TestCase(RuleResult.Pass, CapabilitySetupState.ChecksPassed)]
		[TestCase(RuleResult.Fail, CapabilitySetupState.NeedsAttention)]
		[TestCase(RuleResult.Unknown, CapabilitySetupState.ConfigurationPresent)]
		[TestCase(RuleResult.NotApplicable, CapabilitySetupState.ConfigurationPresent)]
		public void Only_all_current_applicable_checks_verify_recorded_configuration(RuleResult rule, CapabilitySetupState expected) =>
			Assert.That(Evaluate(2, rule).State, Is.EqualTo(expected));
		[TestCase(EvidenceState.Unknown)][TestCase(EvidenceState.Redacted)][TestCase(EvidenceState.Unavailable)]
		public void An_unavailable_source_is_neither_empty_nor_verified(EvidenceState state) =>
			Assert.That(Evaluate(null, factState: state).State, Is.EqualTo(CapabilitySetupState.NotAssessed));
		[Test]
		public void Stale_and_inconsistent_observations_cannot_verify_setup()
		{
			Assert.That(Evaluate(2, stale: true).State, Is.EqualTo(CapabilitySetupState.NotAssessed));
			Assert.That(Evaluate(2, consistent: false).State, Is.EqualTo(CapabilitySetupState.NotAssessed));
			Assert.That(Evaluate(2, accessState: EvidenceState.Unavailable).State, Is.EqualTo(CapabilitySetupState.NotAssessed));
		}
		[Test]
		public void Subscription_and_setup_are_independent_dimensions()
		{
			Assert.That(Evaluate(0, subscribed:true, commercial:EvidenceState.Known).OpportunityKey, Is.EqualTo("Ui.OpportunitySubscribedNotConfigured"));
			Assert.That(Evaluate(null, subscribed:true, factState:EvidenceState.Unknown, commercial:EvidenceState.Known).OpportunityKey, Is.EqualTo("Ui.OpportunitySubscribed"));
			Assert.That(Evaluate(null, subscribed:true, accessState:EvidenceState.Unavailable, commercial:EvidenceState.Unavailable).OpportunityKey, Is.EqualTo("Ui.OpportunityNotOwned"));
			Assert.That(Evaluate(null, subscribed:true, commercial:EvidenceState.Unknown).OpportunityKey, Is.EqualTo("Ui.OpportunityUnknown"));
		}
	}
}
