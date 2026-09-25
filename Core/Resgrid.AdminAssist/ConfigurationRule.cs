using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>Evaluates only observed, fresh scalar metadata. Incomplete evidence cannot become a pass.</summary>
	public sealed class ConfigurationRule(ConfigurationRuleDefinition definition) : IConfigurationRule
	{
		public ConfigurationRuleDefinition Definition { get; } = definition;

		public ConfigurationFinding Evaluate(ConfigurationSnapshot snapshot, DateTime nowUtc, TimeSpan maximumAge)
		{
			var ids = Definition.AppliesWhen.Concat(Definition.FailsWhen).Select(c => c.EvidenceId).Distinct().ToArray();
			RuleResult result;
			bool? applies = null;
			string? reason = null;
			if (!snapshot.Consistent)
			{
				result = RuleResult.Unknown;
				reason = "SnapshotChanged";
			}
			else
			{
				applies = MatchAll(Definition.AppliesWhen, snapshot, nowUtc, maximumAge);
				if (applies == false) result = RuleResult.NotApplicable;
				else if (applies == null) { result = RuleResult.Unknown; reason = "ApplicabilityUnknown"; }
				else
				{
					var fails = MatchAll(Definition.FailsWhen, snapshot, nowUtc, maximumAge);
					result = fails == null ? RuleResult.Unknown : fails.Value ? RuleResult.Fail : RuleResult.Pass;
					if (fails == null) reason = "EvidenceIncompleteOrStale";
				}
			}
			return new ConfigurationFinding(Definition.Id, Definition.AreaId, Definition.Severity, result,
				Definition.TitleKey, Definition.ExplanationKey, Definition.NextActionKey, Definition.Location.Url,
				ids, snapshot.Revision, nowUtc, reason, Definition.Severity == FindingSeverity.Critical &&
					(result == RuleResult.Fail || result == RuleResult.Unknown && applies == true && Definition.AppliesWhen.Count > 0));
		}

		private static bool? MatchAll(IReadOnlyList<EvidenceCondition> conditions, ConfigurationSnapshot snapshot,
			DateTime now, TimeSpan age)
		{
			var unknown = false;
			var anyFalse = false;
			foreach (var condition in conditions)
			{
				var evidence = snapshot.Find(condition.EvidenceId);
				if (!evidence.IsFresh(now, age)) { unknown = true; continue; }
				bool? match = condition.Comparison switch
				{
					EvidenceComparison.IsTrue => evidence.Boolean,
					EvidenceComparison.IsFalse => evidence.Boolean.HasValue ? !evidence.Boolean.Value : null,
					EvidenceComparison.Equal when condition.Code != null => evidence.Code == null ? null : evidence.Code == condition.Code,
					EvidenceComparison.NotEqual when condition.Code != null => evidence.Code == null ? null : evidence.Code != condition.Code,
					_ when !evidence.Number.HasValue || !condition.Number.HasValue => null,
					EvidenceComparison.Equal => evidence.Number == condition.Number,
					EvidenceComparison.NotEqual => evidence.Number != condition.Number,
					EvidenceComparison.Greater => evidence.Number > condition.Number,
					EvidenceComparison.GreaterOrEqual => evidence.Number >= condition.Number,
					EvidenceComparison.Less => evidence.Number < condition.Number,
					EvidenceComparison.LessOrEqual => evidence.Number <= condition.Number,
					_ => null
				};
				if (match == false) anyFalse = true;
				if (!match.HasValue) unknown = true;
			}
			return unknown ? null : !anyFalse;
		}
	}
}
