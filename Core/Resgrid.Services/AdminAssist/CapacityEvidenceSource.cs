using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class CapacityEvidenceSource(ISubscriptionsService subscriptions, ILimitsService limits) : IAdminAssistEvidenceSource
	{
		public string SourceId => "SubscriptionCapacity";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "personnelUtilization", "unitUtilization", "personnelHeadroom", "unitHeadroom", "capacityKind", "capacityPersonnelCount", "capacityUnitCount", "capacityPersonnelLimit", "capacityUnitLimit", "capacityEntityLimit" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) || string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
				throw new InvalidOperationException("Subscription metadata unavailable.");
			var plan = await subscriptions.GetCurrentPlanForDepartmentAsync(actor.DepartmentId, true).WaitAsync(ct);
			// Stop at the first missing billing answer; an unavailable Billing API should not cost two more timeouts.
			if (plan?.PlanLimits == null || plan.PlanLimits.Count == 0) throw new InvalidOperationException("Subscription metadata unavailable.");
			var counts = await subscriptions.GetPlanCountsForDepartmentAsync(actor.DepartmentId).WaitAsync(ct)
				?? throw new InvalidOperationException("Subscription metadata unavailable.");
			var current = await limits.GetLimitsForEntityPlanWithFallbackAsync(actor.DepartmentId, true).WaitAsync(ct)
				?? throw new InvalidOperationException("Subscription limits unavailable.");
			// Independent billing reads must agree; a moving count or stale plan is not a verified capacity snapshot.
			if (current.PersonnelCount != counts.UsersCount || current.UnitsCount != counts.UnitsCount || current.IsEntityPlan != (plan.PlanId >= 36))
				throw new InvalidOperationException("Subscription metadata changed during the read.");
			if (current.IsEntityPlan ? current.EntityTotal != plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities) :
				current.PersonnelLimit != plan.GetLimitForTypeAsInt(PlanLimitTypes.Personnel) || current.UnitsLimit != plan.GetLimitForTypeAsInt(PlanLimitTypes.Units))
				throw new InvalidOperationException("Subscription limits changed during the read.");
			int personnelCap = current.IsEntityPlan ? current.EntityTotal : current.PersonnelLimit;
			int unitCap = current.IsEntityPlan ? current.EntityTotal : current.UnitsLimit;
			int personnel = current.IsEntityPlan ? counts.GetEntitiesCount() : counts.UsersCount;
			int units = current.IsEntityPlan ? counts.GetEntitiesCount() : counts.UnitsCount;
			ConfigurationEvidence Fact(string id, int cap, int count, bool headroom) => cap > 0 && count >= 0
				? new(id, EvidenceState.Known, SourceId, plan.PlanId.ToString(), now, Number: headroom ? cap - count : (decimal)count / cap)
				: new(id, EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "LimitNotQuantified");
			var version = plan.PlanId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			ConfigurationEvidence Count(string id, int value) => new(id, value >= 0 ? EvidenceState.Known : EvidenceState.Unknown, SourceId, version, now, Number: value >= 0 ? value : null);
			return new[] {
				new ConfigurationEvidence("capacityKind", EvidenceState.Known, SourceId, version, now, Code: current.IsEntityPlan ? "entities" : "separate"),
				Count("capacityPersonnelCount", counts.UsersCount), Count("capacityUnitCount", counts.UnitsCount),
				Count("capacityPersonnelLimit", current.PersonnelLimit), Count("capacityUnitLimit", current.UnitsLimit), Count("capacityEntityLimit", current.EntityTotal), Fact("personnelUtilization", personnelCap, personnel, false), Fact("unitUtilization", unitCap, units, false),
				Fact("personnelHeadroom", personnelCap, personnel, true), Fact("unitHeadroom", unitCap, units, true) };
		}
	}
}
