using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class ConfigurationRuleTests
	{
		private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		private static readonly ConfigurationCatalog Catalog = new();

		[TestCase("shift-auto-without-dispatch", "DispatchShiftInsteadOfGroup", false, true)]
		[TestCase("text-sources", "textSourcePresent", false, true)]
		[TestCase("command-sources", "textSourcePresent", false, true)]
		[TestCase("map-token", "mapTokenPresent", false, true)]
		[TestCase("map-style", "mapStylePresent", false, true)]
		[TestCase("password-recovery", "RequirePasswordResetViaEmail", false, true)]
		[TestCase("import-heartbeat", "importHeartbeatMissing", true, false)]
		public void Boolean_rules_distinguish_the_documented_failure_from_correct_configuration(string ruleId, string fact, bool failing, bool passing)
		{
			var rule = Rule(ruleId);
			var evidence = Applicable(rule);
			evidence[fact] = Value(fact, boolean: failing);
			Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Fail));
			evidence[fact] = Value(fact, boolean: passing);
			Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Pass));
		}

		[TestCase("email-import-failures", "emailImportFailureCount", 1, 0)]
		[TestCase("policy-references", "unavailablePolicyReferences", 1, 0)]
		[TestCase("policy-expiry", "policyReferencesExpiring30Days", 1, 0)]
		[TestCase("site-references", "unavailableSiteReferences", 1, 0)]
		[TestCase("continuity-reference", "declaredContinuityReferences", 0, 1)]
		[TestCase("shift-coverage", "groupsWithoutShiftCoverage", 1, 0)]
		[TestCase("admin-mfa", "Require2FAForAdmins", 0, 1)]
		[TestCase("admin-enrollment", "adminsWithoutMfa", 1, 0)]
		[TestCase("admin-succession", "activeAdminCount", 1, 2)]
		[TestCase("empty-groups", "emptyGroupCount", 1, 0)]
		[TestCase("unit-type", "unitsWithoutType", 1, 0)]
		[TestCase("unit-group", "unitsWithoutGroup", 1, 0)]
		[TestCase("station-address", "stationsWithoutLocation", 1, 0)]
		[TestCase("person-location-age", "MappingPersonnelLocationTTL", 0, 30)]
		[TestCase("unit-location-age", "MappingUnitLocationTTL", 0, 30)]
		[TestCase("personnel-limit", "personnelUtilization", 0.9, 0.8)]
		[TestCase("unit-limit", "unitUtilization", 0.9, 0.8)]
		[TestCase("run-cards", "runCardCount", 0, 1)]
		[TestCase("checkin-timers", "checkInTimerCount", 0, 1)]
		[TestCase("weather-zones", "weatherZoneCount", 0, 1)]
		[TestCase("communication-tests", "communicationTestAgeDays", 31, 30)]
		[TestCase("qualified-coverage", "uncoveredQualificationCount", 1, 0)]
		[TestCase("credential-expiry", "qualificationsExpiring30Days", 1, 0)]
		[TestCase("checklist-overdue", "overdueChecklistCount", 1, 0)]
		[TestCase("equipment-holds", "activeSafetyHoldCount", 1, 0)]
		[TestCase("stock-expiry", "stockExpiring30Days", 1, 0)]
		[TestCase("workflow-failures", "failedWorkflowCount", 1, 0)]
		[TestCase("record-review", "overdueRecordReviewCount", 1, 0)]
		[TestCase("shift-open-slots", "upcomingOpenShiftSlots", 1, 0)]
		[TestCase("shift-overlaps", "overlappingShiftPersonnel", 1, 0)]
		[TestCase("shift-trades", "unfilledShiftTrades", 1, 0)]
		public void Numeric_rules_preserve_thresholds(string ruleId, string fact, double failing, double passing)
		{
			var rule = Rule(ruleId);
			var evidence = Applicable(rule);
			evidence[fact] = Value(fact, number: (decimal)failing);
			Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Fail));
			evidence[fact] = Value(fact, number: (decimal)passing);
			Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Pass));
		}

		[Test]
		public void Every_rule_has_an_explicit_passing_and_failing_example()
		{
			var examples = GetType().GetMethods().SelectMany(method => method.GetCustomAttributes(typeof(TestCaseAttribute), false).Cast<TestCaseAttribute>())
				.Where(test => test.Arguments.Length == 4).Select(test => test.Arguments[0].ToString());
			Assert.That(examples, Is.EquivalentTo(Catalog.Rules.Select(rule => rule.Id)));
		}
		[Test]
		public void Member_text_commands_do_not_require_dispatch_source_patterns_when_text_calls_are_off()
		{
			var evidence = Applicable(Rule("command-sources"));
			evidence["EnableTextToCall"] = Value("EnableTextToCall", boolean: false);
			evidence["textSourcePresent"] = Value("textSourcePresent", boolean: false);
			Assert.That(Rule("command-sources").Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.NotApplicable));
		}

		[Test]
		public void Partial_compound_evidence_cannot_produce_a_pass()
		{
			var definition = Catalog.Rules.First() with { AppliesWhen = Array.Empty<EvidenceCondition>(), FailsWhen = new[] {
				new EvidenceCondition("first", EvidenceComparison.IsTrue), new EvidenceCondition("second", EvidenceComparison.IsTrue) } };
			var rule = new ConfigurationRule(definition);
			Assert.That(rule.Evaluate(Snapshot(new Dictionary<string, ConfigurationEvidence> { ["first"] = Value("first", false) }), Now, TimeSpan.FromMinutes(1)).Result,
				Is.EqualTo(RuleResult.Unknown));
		}
		[Test]
		public void Every_rule_keeps_missing_redacted_unavailable_stale_and_future_evidence_unknown()
		{
			foreach (var definition in Catalog.Rules)
			{
				var rule = new ConfigurationRule(definition);
				var evidence = Applicable(rule);
				Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Unknown), definition.Id);
				foreach (var state in new[] { EvidenceState.Unknown, EvidenceState.Redacted, EvidenceState.Unavailable })
				{
					foreach (var condition in definition.FailsWhen)
						evidence[condition.EvidenceId] = Value(condition.EvidenceId, false, 0) with { State = state };
					Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Unknown), definition.Id);
				}
				foreach (var offset in new[] { -61, 1 })
				{
					foreach (var condition in definition.FailsWhen)
						evidence[condition.EvidenceId] = Value(condition.EvidenceId, false, 0) with { AsOfUtc = Now.AddSeconds(offset) };
					Assert.That(rule.Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Unknown), definition.Id);
				}
			}
		}

		[Test]
		public void Disabled_text_intake_is_not_a_missing_source_failure()
		{
			var evidence = new Dictionary<string, ConfigurationEvidence> { ["EnableTextToCall"] = Value("EnableTextToCall", false) };
			Assert.That(Rule("text-sources").Evaluate(Snapshot(evidence), Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.NotApplicable));
		}

		[Test]
		public void Concurrent_mutation_prevents_a_passing_snapshot()
		{
			var snapshot = Snapshot(new Dictionary<string, ConfigurationEvidence> { ["Require2FAForAdmins"] = Value("Require2FAForAdmins", number: 1) }) with { Consistent = false };
			Assert.That(Rule("admin-mfa").Evaluate(snapshot, Now, TimeSpan.FromMinutes(1)).Result, Is.EqualTo(RuleResult.Unknown));
		}

		[Test]
		public void Optional_areas_do_not_reduce_selected_completion_and_security_cannot_be_hidden()
		{
			var findings = new[]
			{
				Finding("admin-mfa", "security", RuleResult.Unknown, FindingSeverity.Critical),
				Finding("home", "home", RuleResult.Pass), Finding("optional", "business", RuleResult.Fail)
			};
			var report = new ConfigurationReport(Snapshot(new Dictionary<string, ConfigurationEvidence>()), findings, new[] { "home" });
			Assert.Multiple(() =>
			{
				Assert.That(report.Required, Is.EqualTo(2)); Assert.That(report.Verified, Is.EqualTo(1));
				Assert.That(report.Failed, Is.Zero); Assert.That(report.HasCriticalUncertainty, Is.True);
			});
		}

		private static ConfigurationFinding Finding(string id, string area, RuleResult result, FindingSeverity severity = FindingSeverity.Warning) =>
			new(id, area, severity, result, id, id, id, "/User/Department/Settings", Array.Empty<string>(), "1", Now);
		private static ConfigurationRule Rule(string id) => new(Catalog.Rules.Single(r => r.Id == id));
		private static ConfigurationEvidence Value(string id, bool? boolean = null, decimal? number = null) => new(id, EvidenceState.Known, "fixture", "1", Now, boolean, number);
		private static ConfigurationSnapshot Snapshot(Dictionary<string, ConfigurationEvidence> evidence) => new(1, "admin", "1", Now, true, evidence);
		private static Dictionary<string, ConfigurationEvidence> Applicable(ConfigurationRule rule)
		{
			var evidence = new Dictionary<string, ConfigurationEvidence>();
			foreach (var condition in rule.Definition.AppliesWhen)
				evidence[condition.EvidenceId] = Value(condition.EvidenceId, true, 1);
			return evidence;
		}
	}
}
