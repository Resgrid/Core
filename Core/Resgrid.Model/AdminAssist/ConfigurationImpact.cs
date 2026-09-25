using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Only proposed scalar values are accepted from the caller. All current evidence is read by the server.</summary>
	public sealed record ConfigurationImpactRequest(string SettingId, string ExpectedRevision, bool? Boolean = null, decimal? Number = null);
	public sealed record CapacityImpactRequest(string ExpectedRevision, int ProposedPersonnelCount, int ProposedUnitCount);
	public sealed record ConfigurationImpactMetric(string LabelKey, EvidenceState State, decimal? Before, decimal? After, string ReasonCode = null);
	public sealed record ConfigurationImpactRule(string RuleId, string TitleKey, RuleResult Before, RuleResult After);
	public sealed record ConfigurationImpactReport(string SettingId, string SnapshotRevision, DateTime AsOfUtc,
		string EvaluatorVersion, SettingImpact Profile, IReadOnlyList<ConfigurationImpactMetric> Metrics,
		IReadOnlyList<ConfigurationImpactRule> RuleChanges, IReadOnlyList<string> LimitKeys, string Destination);
	public interface IConfigurationImpactService
	{
		Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, ConfigurationImpactRequest request, CancellationToken cancellationToken = default);
		Task<ConfigurationImpactReport> PreviewCapacityAsync(AdminAssistActor actor, CapacityImpactRequest request, CancellationToken cancellationToken = default);
	}
	public sealed record OperationalImpact(IReadOnlyList<ConfigurationImpactMetric> Metrics, IReadOnlyList<string> LimitKeys, string Version);
	public interface IOperationalImpactProvider
	{
		bool Supports(string settingId);
		Task<OperationalImpact> EvaluateAsync(AdminAssistActor actor, ConfigurationSnapshot snapshot, ConfigurationImpactRequest request, CancellationToken cancellationToken);
	}
}
