using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class FindingLifecycleTests
	{
		private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		private static ConfigurationFinding Finding(RuleResult result) => new("admin-mfa", "security", FindingSeverity.Critical,
			result, "title", "help", "next", "/User/Department/Settings", Array.Empty<string>(), "4", Now);
		private static AdminAssistFindingRow Row() => new() { Result = (int)RuleResult.Unknown };
		[Test]
		public void Unknown_then_failure_opens_once_and_verified_pass_resolves()
		{
			var row = Row();
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Unknown), Now), Is.Null);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now), Is.EqualTo(WorkflowTriggerEventType.AdminAssistFindingOpened));
			Assert.That(row.Episode, Is.EqualTo(1));
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now), Is.Null);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Unknown), Now), Is.Null);
			Assert.That(row.ReviewStatus, Is.Not.EqualTo((int)FindingReviewStatus.Resolved));
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Pass), Now), Is.EqualTo(WorkflowTriggerEventType.AdminAssistFindingResolved));
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Pass), Now), Is.Null);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now), Is.EqualTo(WorkflowTriggerEventType.AdminAssistFindingReopened));
			Assert.That(row.Episode, Is.EqualTo(2));
		}
		[Test]
		public void Exception_is_not_resolution_and_expiry_reopens_once()
		{
			var row = Row();
			FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now);
			row.ReviewStatus = (int)FindingReviewStatus.AcceptedException;
			row.ExceptionUntil = Now.AddHours(1);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now), Is.Null);
			Assert.That(row.Result, Is.EqualTo((int)RuleResult.Fail));
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now.AddHours(1)), Is.EqualTo(WorkflowTriggerEventType.AdminAssistFindingReopened));
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now.AddHours(2)), Is.Null);
			Assert.That(row.ExceptionUntil, Is.Null);
		}
		[Test]
		public void Evidence_outage_after_resolution_does_not_emit_another_resolution()
		{
			var row = Row();
			FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now);
			FindingLifecycle.Observe(row, Finding(RuleResult.Pass), Now);
			FindingLifecycle.Observe(row, Finding(RuleResult.Unknown), Now);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Pass), Now), Is.Null);
		}
		[Test]
		public void Exception_expiring_during_an_outage_reopens_on_the_next_verified_failure()
		{
			var row = Row();
			FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now);
			row.ReviewStatus = (int)FindingReviewStatus.AcceptedException;
			row.ExceptionUntil = Now.AddHours(1);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Unknown), Now.AddHours(2)), Is.Null);
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now.AddHours(3)), Is.EqualTo(WorkflowTriggerEventType.AdminAssistFindingReopened));
			Assert.That(FindingLifecycle.Observe(row, Finding(RuleResult.Fail), Now.AddHours(4)), Is.Null);
		}
		[Test]
		public void Workflow_projection_strips_notes_owners_grants_unknown_rules_and_invalid_scalars()
		{
			var id = Guid.NewGuid().ToString("D");
			var input = JObject.FromObject(new { FindingId = id, RuleId = "admin-mfa", Episode = 1, Result = 1, Severity = 2,
				ReviewStatus = 0, Content = "protected note", OwnerId = "private user", GrantToken = "secret" });
			var output = JObject.Parse(AdminAssistWorkflowPayload.Routing(input));
			Assert.That(output.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "FindingId", "RuleId", "Episode", "Result", "Severity", "ReviewStatus" }));
			Assert.That(output["FindingId"].Value<string>(), Is.EqualTo(id));
			input["RuleId"] = "private arbitrary string"; input["Episode"] = -1; input["Result"] = 4; input["Severity"] = "2";
			output = JObject.Parse(AdminAssistWorkflowPayload.Routing(input));
			Assert.That(output.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "FindingId", "ReviewStatus" }));
			Assert.That(AdminAssistWorkflowPayload.RuleIds, Is.EquivalentTo(new ConfigurationCatalog().Rules.Select(r => r.Id)));
		}
	}
}
