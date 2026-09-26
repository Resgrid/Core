using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class SetupPlanTests
	{
		private readonly ConfigurationCatalog _catalog = new();
		private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		[Test]
		public void Learning_and_interest_do_not_verify_configuration_and_effort_is_per_area()
		{
			var workspace = new SetupWorkspace(7, 1, SetupMode.Fresh, new Dictionary<string, SetupAreaChoice> { ["people"] = SetupAreaChoice.UseNow },
				new[] { "personnel" }, new[] { "groups" }, _catalog.Version, null, RevisitOnUtc: Now.AddDays(-1));
			var plan = SetupPlanBuilder.Build(_catalog, workspace, Array.Empty<CapabilityAccess>(), Array.Empty<CapabilitySetupAssessment>(), "1", Now);
			Assert.That(plan.Tasks.Single(t => t.Id == "personnel.Learn").State, Is.EqualTo("Learned"));
			Assert.That(plan.Tasks.Where(t => t.Id.EndsWith(".Verify")).All(t => t.State == "Unknown"), Is.True);
			Assert.That(plan.Tasks.All(t => t.Destination == null), Is.True);
			Assert.That(plan.Effort.Select(e => e.AreaId), Is.EqualTo(new[] { "people" }));
			Assert.That(plan.RevisitDue, Is.True);
		}
		[Test]
		public void Stale_access_never_emits_a_configuration_link()
		{
			var workspace = new SetupWorkspace(7, 1, SetupMode.Fresh, new Dictionary<string, SetupAreaChoice>(), Array.Empty<string>(), new[] { "personnel" }, _catalog.Version, null);
			var plan = SetupPlanBuilder.Build(_catalog, workspace, new[] { new CapabilityAccess("personnel", EvidenceState.Known, Array.Empty<string>(), true, "/User/Personnel/Index", Now.AddMinutes(-2)) }, Array.Empty<CapabilitySetupAssessment>(), "1", Now);
			Assert.That(plan.Tasks.Count, Is.EqualTo(4));
			Assert.That(plan.Tasks.All(t => t.Destination == null), Is.True);
		}
		[Test]
		public void Calendar_escapes_injection_and_folds_UTF8_without_corrupting_characters()
		{
			var title = string.Concat(Enumerable.Repeat("مراجعة", 30));
			var calendar = SetupReviewCalendar.Create(7, Now.AddDays(1), Now, title, "Review\r\nATTENDEE:attacker@example.invalid");
			Assert.That(calendar, Does.Not.Contain("\r\nATTENDEE:"));
			Assert.That(calendar, Does.Contain("DESCRIPTION:Review\\nATTENDEE:"));
			Assert.That(calendar.Split("\r\n").All(line => Encoding.UTF8.GetByteCount(line) <= 75), Is.True);
			Assert.That(calendar.Replace("\r\n ", ""), Does.Contain("SUMMARY:" + title));
			Assert.That(calendar, Does.Contain("DTSTART:20260925T120000Z"));
		}
		[Test]
		public void Setup_checklist_lists_only_key_features_of_selected_modules_with_module_effort()
		{
			var workspace = new SetupWorkspace(7, 1, SetupMode.Fresh, new Dictionary<string, SetupAreaChoice> { ["calls"] = SetupAreaChoice.UseNow, ["records"] = SetupAreaChoice.LearnLater },
				Array.Empty<string>(), Array.Empty<string>(), _catalog.Version, null);
			var plan = SetupPlanBuilder.Build(_catalog, workspace, Array.Empty<CapabilityAccess>(), Array.Empty<CapabilitySetupAssessment>(), "1", Now);
			var planned = plan.Tasks.Select(t => t.CapabilityId).Distinct().ToArray();
			// Page actions such as New Call and Archived Calls are left to Ask and reference.
			Assert.That(planned, Is.EquivalentTo(_catalog.Capabilities.Where(c => c.AreaId == "calls" && c.IsKey).Select(c => c.Id)));
			Assert.That(planned, Does.Not.Contain("new-call").And.Not.Contain("archived-calls"));
			var calls = _catalog.Areas.Single(a => a.Id == "calls");
			Assert.That(plan.Effort.Single(), Is.EqualTo(new SetupEffort("calls", calls.MinimumMinutes, calls.MaximumMinutes, "Ui.EffortAssumptions")));
		}
	}
}
