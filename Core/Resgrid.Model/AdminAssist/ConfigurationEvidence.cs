using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.AdminAssist
{
	public enum EvidenceState { Known, Unknown, Unavailable, Redacted, NotApplicable }
	public enum FindingSeverity { Information, Warning, Critical }
	public enum RuleResult { Pass, Fail, Unknown, NotApplicable }
	public enum FindingReviewStatus { Unassigned, Assigned, InReview, AcceptedException, Resolved }
	public enum SetupAreaChoice { UseNow, LearnLater, NotApplicable }
	public enum SetupMode { Fresh, Review, Import }

	/// <summary>Only allowlisted scalar metadata enters the engine; no raw configuration, names or secrets.</summary>
	public sealed record ConfigurationEvidence(
		string Id, EvidenceState State, string Source, string Version, DateTime AsOfUtc,
		bool? Boolean = null, decimal? Number = null, string Code = null, string ReasonCode = null)
	{
		public bool IsFresh(DateTime now, TimeSpan lifetime) => State == EvidenceState.Known &&
			AsOfUtc <= now && now - AsOfUtc <= lifetime;
	}

	public sealed record ConfigurationSnapshot(
		int DepartmentId, string ActorId, string Revision, DateTime AsOfUtc, bool Consistent,
		IReadOnlyDictionary<string, ConfigurationEvidence> Evidence)
	{
		public ConfigurationEvidence Find(string id) => Evidence.TryGetValue(id, out var value) ? value :
			new ConfigurationEvidence(id, EvidenceState.Unknown, "not-observed", Revision, AsOfUtc,
				ReasonCode: "EvidenceNotObserved");
	}

	public sealed record ConfigurationFinding(
		string RuleId, string AreaId, FindingSeverity Severity, RuleResult Result,
		string TitleKey, string ExplanationKey, string NextActionKey, string Destination,
		IReadOnlyList<string> EvidenceIds, string SnapshotRevision, DateTime EvaluatedOnUtc,
		string ReasonCode = null, bool ScopeIndependent = false);

	public sealed record ConfigurationReport(ConfigurationSnapshot Snapshot,
		IReadOnlyList<ConfigurationFinding> Findings, IReadOnlyList<string> SelectedAreas)
	{
		public int Verified => Counted.Count(f => f.Result == RuleResult.Pass);
		public int Required => Counted.Count();
		public int Failed => Counted.Count(f => f.Result == RuleResult.Fail);
		public int Unknown => Counted.Count(f => f.Result == RuleResult.Unknown);
		/// <summary>Information-level findings are review suggestions: listed, never counted as required, failed or unknown checks.</summary>
		public int Suggestions => Applicable.Count(f => f.Severity == FindingSeverity.Information && f.Result is RuleResult.Fail or RuleResult.Unknown);
		public IReadOnlyList<string> UncheckedAreaIds => SelectedAreas.Where(area => !Findings.Any(f => f.AreaId == area)).Distinct().ToArray();
		public bool HasCriticalUncertainty => !Snapshot.Consistent || Applicable.Any(f =>
			f.Severity == FindingSeverity.Critical && f.Result == RuleResult.Unknown);
		private IEnumerable<ConfigurationFinding> Applicable => Findings.Where(f =>
			f.Result != RuleResult.NotApplicable && (f.AreaId == "security" || f.ScopeIndependent || SelectedAreas.Contains(f.AreaId)));
		private IEnumerable<ConfigurationFinding> Counted => Applicable.Where(f => f.Severity != FindingSeverity.Information);
	}

	public sealed record AdminAssistActor(int DepartmentId, string UserId, string Locale = "en");
}
