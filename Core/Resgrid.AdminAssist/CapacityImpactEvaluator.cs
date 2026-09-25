using System;
using System.Collections.Generic;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>Headroom for proposed total personnel/unit counts; no provisioning, price estimate or entitlement change.</summary>
	public static class CapacityImpactEvaluator
	{
		public static ConfigurationImpactReport Evaluate(ConfigurationSnapshot snapshot, CapacityImpactRequest request, DateTime now, TimeSpan maximumAge)
		{
			if (request == null || request.ProposedPersonnelCount < 0 || request.ProposedPersonnelCount > 1000000 || request.ProposedUnitCount < 0 || request.ProposedUnitCount > 1000000)
				throw new ArgumentException("Proposed total counts must be between zero and one million.");
			if (!snapshot.Consistent || snapshot.Revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			decimal? Value(string id) { var value = snapshot.Find(id); return value.IsFresh(now, maximumAge) && value.Number >= 0 ? value.Number : null; }
			var personnel = Value("capacityPersonnelCount"); var units = Value("capacityUnitCount");
			var metrics = new List<ConfigurationImpactMetric>();
			void Add(string label, decimal? before, decimal? after) => metrics.Add(new(label, before.HasValue && after.HasValue ? EvidenceState.Known : EvidenceState.Unknown, before, after));
			Add("Impact.PersonnelTotal", personnel, request.ProposedPersonnelCount);
			Add("Impact.UnitTotal", units, request.ProposedUnitCount);
			var kind = snapshot.Find("capacityKind");
			if (kind.IsFresh(now, maximumAge) && kind.Code == "entities")
			{
				var cap = Value("capacityEntityLimit");
				// Zero is not a verified entity allowance; the owning limit service changes behavior at zero.
				if (cap == 0) cap = null;
				Add("Impact.SharedHeadroom", cap - personnel - units, cap - request.ProposedPersonnelCount - request.ProposedUnitCount);
			}
			else if (kind.IsFresh(now, maximumAge) && kind.Code == "separate")
			{
				Add("Impact.PersonnelHeadroom", Value("capacityPersonnelLimit") - personnel, Value("capacityPersonnelLimit") - request.ProposedPersonnelCount);
				Add("Impact.UnitHeadroom", Value("capacityUnitLimit") - units, Value("capacityUnitLimit") - request.ProposedUnitCount);
			}
			else Add("Impact.CapacityHeadroom", null, null);
			return new("plan.capacity", snapshot.Revision, snapshot.AsOfUtc, "capacity-v1",
				new SettingImpact("Medium", "Impact.Audience", "Area.plans", "Impact.Timing", "Impact.CapacityReversal", "Impact.Verify"),
				metrics.AsReadOnly(), Array.Empty<ConfigurationImpactRule>(), new[] { "Impact.NoMutation", "Impact.CapacityScope", "Impact.CapacityTiming" }, "/User/Subscription/Index");
		}
	}
}
