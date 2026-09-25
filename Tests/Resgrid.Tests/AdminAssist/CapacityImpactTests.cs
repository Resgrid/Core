using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class CapacityImpactTests
	{
		private readonly DateTime _now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		private ConfigurationSnapshot Snapshot(string kind, params (string, decimal)[] values)
		{
			var facts = values.ToDictionary(v => v.Item1, v => new ConfigurationEvidence(v.Item1, EvidenceState.Known, "test", "1", _now, Number: v.Item2));
			facts["capacityKind"] = new("capacityKind", EvidenceState.Known, "test", "1", _now, Code: kind);
			return new(7, "admin", "3", _now, true, facts);
		}
		[Test]
		public void Entity_plan_combines_personnel_and_units_and_shows_exceeded_capacity()
		{
			var snapshot = Snapshot("entities", ("capacityPersonnelCount", 8), ("capacityUnitCount", 2), ("capacityEntityLimit", 15));
			var report = CapacityImpactEvaluator.Evaluate(snapshot, new("3", 12, 6), _now, TimeSpan.FromMinutes(1));
			var headroom = report.Metrics.Single(m => m.LabelKey == "Impact.SharedHeadroom");
			Assert.That(headroom.Before, Is.EqualTo(5)); Assert.That(headroom.After, Is.EqualTo(-3));
			Assert.That(snapshot.Find("capacityPersonnelCount").Number, Is.EqualTo(8));
		}
		[Test]
		public void Separate_allowances_include_zero_capacity_without_interpreting_it_as_unlimited()
		{
			var snapshot = Snapshot("separate", ("capacityPersonnelCount", 8), ("capacityUnitCount", 0), ("capacityPersonnelLimit", 10), ("capacityUnitLimit", 0));
			var report = CapacityImpactEvaluator.Evaluate(snapshot, new("3", 9, 1), _now, TimeSpan.FromMinutes(1));
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.PersonnelHeadroom").After, Is.EqualTo(1));
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.UnitHeadroom").After, Is.EqualTo(-1));
		}
		[Test]
		public void Missing_and_stale_limits_are_unknown_and_proposed_counts_are_still_identified()
		{
			var snapshot = Snapshot("entities", ("capacityPersonnelCount", 8), ("capacityUnitCount", 2));
			var report = CapacityImpactEvaluator.Evaluate(snapshot, new("3", 10, 3), _now, TimeSpan.FromMinutes(1));
			Assert.That(report.Metrics.Last().State, Is.EqualTo(EvidenceState.Unknown)); Assert.That(report.Metrics.Last().After, Is.Null);
			var stale = CapacityImpactEvaluator.Evaluate(snapshot, new("3", 10, 3), _now.AddMinutes(2), TimeSpan.FromMinutes(1));
			Assert.That(stale.Metrics.Last().After, Is.Null); Assert.That(stale.Metrics[0].Before, Is.Null); Assert.That(stale.Metrics[0].After, Is.EqualTo(10));
		}
		[Test]
		public void Invalid_totals_and_stale_revision_are_rejected()
		{
			Assert.Throws<ArgumentException>(() => CapacityImpactEvaluator.Evaluate(Snapshot("entities"), new("3", -1, 0), _now, TimeSpan.FromMinutes(1)));
			Assert.Throws<ArgumentException>(() => CapacityImpactEvaluator.Evaluate(Snapshot("entities"), new("3", 0, 1000001), _now, TimeSpan.FromMinutes(1)));
			Assert.Throws<AdminAssistConcurrencyException>(() => CapacityImpactEvaluator.Evaluate(Snapshot("entities"), new("2", 1, 1), _now, TimeSpan.FromMinutes(1)));
		}
		[Test]
		public async Task Fresh_limit_read_also_bypasses_the_underlying_plan_cache()
		{
			var previous = Resgrid.Config.SystemBehaviorConfig.RedirectHomeToLogin;
			Resgrid.Config.SystemBehaviorConfig.RedirectHomeToLogin = false;
			try
			{
				var subscription = new Mock<ISubscriptionsService>();
				subscription.Setup(s => s.GetCurrentPlanForDepartmentAsync(7, true)).ReturnsAsync(new Plan { PlanId = 36, PlanLimits = new List<PlanLimit> { new() { LimitType = (int)PlanLimitTypes.Entities, LimitValue = 25 } } });
				subscription.Setup(s => s.GetPlanCountsForDepartmentAsync(7)).ReturnsAsync(new DepartmentPlanCount { UsersCount = 10, UnitsCount = 3 });
				var service = new LimitsService(subscription.Object, Mock.Of<ICacheProvider>());
				var limits = await service.GetLimitsForEntityPlanWithFallbackAsync(7, true);
				Assert.That(limits.EntityTotal, Is.EqualTo(25));
				subscription.Verify(s => s.GetCurrentPlanForDepartmentAsync(7, true), Times.Once);
				subscription.Verify(s => s.GetCurrentPlanForDepartmentAsync(7, false), Times.Never);
			}
			finally { Resgrid.Config.SystemBehaviorConfig.RedirectHomeToLogin = previous; }
		}
	}
}
