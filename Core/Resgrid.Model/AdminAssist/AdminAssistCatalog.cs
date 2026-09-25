using System;
using System.Collections.Generic;

namespace Resgrid.Model.AdminAssist
{
	public sealed record CatalogLocation(string Controller, string Action, string Field = null)
	{
		public string Url => $"/User/{Uri.EscapeDataString(Controller)}/{Uri.EscapeDataString(Action)}" +
			(string.IsNullOrWhiteSpace(Field) ? "" : "?aa=" + Uri.EscapeDataString(Field));
	}
	public sealed record SettingImpact(string Risk, string AudienceKey, string OperationKey,
		string TimingKey, string ReversibilityKey, string VerificationKey);
	public sealed record SettingCatalogEntry(string Id, string AreaId, string LabelKey, string HelpKey,
		string Binding, string ValueType, string Classification, bool Secret, CatalogLocation Location,
		string Owner, DateTime ReviewedOn, SettingImpact Impact, IReadOnlyList<string> Requires,
		IReadOnlyList<string> Affects, IReadOnlyList<string> Conflicts, string DocumentationId,
		string DefaultValue = null, string AllowedValues = null, string EvidenceSource = null,
		string Availability = "available", string ReviewGap = null, DateTime? ReviewGapExpiresOn = null);
	public sealed record CapabilityRequirement(string Kind, string Id);
	public sealed record CapabilitySetupDefinition(string EvidenceId, decimal Minimum, IReadOnlyList<string> RuleIds, string GuidanceKey);
	/// <summary>
	/// A feature inside a module. <c>Key</c> features are what Setup Wizard, Setup Report and Explore teach and verify;
	/// <c>Detail</c> features (page actions, secondary views, niche settings screens) stay in the catalog for Ask and reference.
	/// </summary>
	public sealed record ProductCapability(string Id, string AreaId, string LabelKey, string PurposeKey,
		string ValueKey, string ExampleKey, string AdoptionKey, string ReleaseStatus,
		CatalogLocation Location, IReadOnlyList<CapabilityRequirement> Requirements,
		IReadOnlyList<string> SettingIds, IReadOnlyList<string> RuleIds, CapabilitySetupDefinition Setup = null,
		string Prominence = ProductCapability.Detail, string DocsPath = null)
	{
		public const string Key = "Key";
		public const string Detail = "Detail";
		public bool IsKey => Prominence == Key;
	}
	/// <summary>
	/// A product module (historically "area"; the id is still referenced as AreaId by settings, features, rules and scope).
	/// Tier orders setup priority; an add-on module names the <see cref="PlanAddonTypes"/> it needs; a global module
	/// (for example Advanced Data Protection) applies across every other module.
	/// </summary>
	public sealed record ProductArea(string Id, string LabelKey, string PurposeKey, int Order,
		IReadOnlyList<string> Archetypes, string Tier = ProductArea.Optional, string Addon = null, bool Global = false,
		string ValueKey = null, string ExampleKey = null, string AdoptionKey = null, int MinimumMinutes = 0, int MaximumMinutes = 0,
		string DocsPath = null)
	{
		/// <summary>Public documentation site; catalog entries store only a validated path on it.</summary>
		public const string DocsOrigin = "https://docs.resgrid.com";
		public const string Core = "Core";
		public const string Recommended = "Recommended";
		public const string Optional = "Optional";
		public const string AddOn = "AddOn";
	}
	public sealed record CapabilityAccess(string CapabilityId, EvidenceState State,
		IReadOnlyList<string> ReasonCodes, bool CanConfigure, string Destination, DateTime AsOfUtc, string SubscriptionDestination = null,
		EvidenceState? CommercialState = null);
	public sealed record OperatingPack(string Id, string LabelKey, string PurposeKey,
		IReadOnlyList<string> AreaIds, IReadOnlyList<string> RuleIds, IReadOnlyList<string> PrerequisiteKeys);
	public sealed record KnowledgeArticle(string Id, string Locale, string TitleKey, string Body,
		string SourcePath, string Anchor, string PackVersion, string Owner, DateTime ReviewedOn);

	public interface IAdminAssistCatalog
	{
		string Version { get; }
		IReadOnlyList<SettingCatalogEntry> Settings { get; }
		IReadOnlyList<ProductArea> Areas { get; }
		IReadOnlyList<ProductCapability> Capabilities { get; }
		IReadOnlyList<OperatingPack> Packs { get; }
		IReadOnlyList<ConfigurationRuleDefinition> Rules { get; }
		IReadOnlyList<KnowledgeArticle> Articles { get; }
	}

	public enum EvidenceComparison { IsTrue, IsFalse, Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }
	public sealed record EvidenceCondition(string EvidenceId, EvidenceComparison Comparison,
		decimal? Number = null, string Code = null);
	/// <summary>Fixed AND predicates: applicability first, then failure. No executable catalog expressions.</summary>
	public sealed record ConfigurationRuleDefinition(string Id, string AreaId, FindingSeverity Severity,
		string TitleKey, string ExplanationKey, string NextActionKey, CatalogLocation Location,
		IReadOnlyList<EvidenceCondition> AppliesWhen, IReadOnlyList<EvidenceCondition> FailsWhen);
}
