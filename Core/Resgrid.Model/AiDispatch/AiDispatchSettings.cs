using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Resgrid.Model.AiDispatch
{
	/// <summary>
	/// Per-department AI dispatch settings (M0241, <c>DepartmentAiDispatchConfigs</c>; ai-dispatch-template-plan.md §5, registry §4D).
	/// A dedicated table rather than department setting types, per the Enhanced AI allocation. Absent row = defaults. Revision is
	/// the compare-and-swap token for concurrent admin edits. Enrich mode is the only mode until the ai-dispatch GPU exists.
	/// </summary>
	public sealed class DepartmentAiDispatchConfig : IEntity
	{
		public int DepartmentId { get; set; }
		/// <summary>Null = the host default (AiDispatchConfig.MinimumConfidence). Bounded by <see cref="AiDispatchSettingsPolicy"/>.</summary>
		public decimal? MinimumConfidence { get; set; }
		/// <summary>Newline-separated sender addresses or domains. Empty = every sender that reaches the dispatch address.</summary>
		public string SenderAllowlist { get; set; }
		/// <summary>Monthly tokens AI dispatch may use, inside the department's Enhanced AI budget. Null = no separate cap.</summary>
		public int? MonthlyTokenCap { get; set; }
		public int AuditRetentionDays { get; set; } = AiDispatchSettingsPolicy.DefaultRetentionDays;
		public bool FillCallType { get; set; } = true;
		public bool FillAddress { get; set; } = true;
		public bool FillContact { get; set; } = true;
		public bool FillIncidentNumber { get; set; } = true;
		public bool RenamePlaceholder { get; set; } = true;
		public bool AddSummaryNote { get; set; } = true;
		public bool FlagRelatedCalls { get; set; } = true;
		public long Revision { get; set; }
		public string UpdatedByUserId { get; set; }
		public DateTime? UpdatedOnUtc { get; set; }

		[NotMapped, JsonIgnore] public object IdValue { get => DepartmentId; set => DepartmentId = Convert.ToInt32(value, CultureInfo.InvariantCulture); }
		[NotMapped] public string TableName => "DepartmentAiDispatchConfigs";
		[NotMapped] public string IdName => "DepartmentId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Bounds, parsing and sender matching for <see cref="DepartmentAiDispatchConfig"/>. Pure, so every rule is testable.</summary>
	public static class AiDispatchSettingsPolicy
	{
		public const decimal MinimumConfidenceFloor = 0.5m;
		public const decimal MinimumConfidenceCeiling = 0.95m;
		public const int DefaultRetentionDays = 365;
		public const int MinimumRetentionDays = 30;
		public const int MaximumRetentionDays = 1095;
		public const int MinimumTokenCap = 8192;
		public const int MaximumTokenCap = 50_000_000;
		public const int MaximumAllowlistEntries = 50;

		/// <summary>Validation error codes (localized by the page): ConfidenceOutOfRange, RetentionOutOfRange, CapOutOfRange, AllowlistTooLong, AllowlistInvalid.</summary>
		public static IReadOnlyList<string> Validate(DepartmentAiDispatchConfig config)
		{
			var errors = new List<string>();
			if (config.MinimumConfidence is { } confidence && (confidence < MinimumConfidenceFloor || confidence > MinimumConfidenceCeiling))
				errors.Add("ConfidenceOutOfRange");
			if (config.AuditRetentionDays < MinimumRetentionDays || config.AuditRetentionDays > MaximumRetentionDays)
				errors.Add("RetentionOutOfRange");
			if (config.MonthlyTokenCap is { } cap && (cap < MinimumTokenCap || cap > MaximumTokenCap))
				errors.Add("CapOutOfRange");
			var entries = Split(config.SenderAllowlist);
			if (entries.Count > MaximumAllowlistEntries)
				errors.Add("AllowlistTooLong");
			if (entries.Any(e => Normalize(e) == null))
				errors.Add("AllowlistInvalid");
			return errors;
		}

		/// <summary>The stored form: one normalized entry per line, de-duplicated. Call after <see cref="Validate"/> passes.</summary>
		public static string NormalizeAllowlist(string text)
		{
			var entries = Split(text).Select(Normalize).Where(e => e != null).Distinct(StringComparer.Ordinal).ToList();
			return entries.Count == 0 ? null : string.Join("\n", entries);
		}

		/// <summary>
		/// An empty allowlist allows every sender. Otherwise the sender must match a full address or a domain entry exactly;
		/// an unknown sender is not allowed.
		/// </summary>
		public static bool IsSenderAllowed(string allowlist, string sender)
		{
			var entries = Split(allowlist).Select(Normalize).Where(e => e != null).ToList();
			if (entries.Count == 0)
				return true;
			var address = Normalize(sender);
			if (address == null || !address.Contains('@'))
				return false;
			var domain = address.Substring(address.IndexOf('@') + 1);
			return entries.Any(e => e.Contains('@') ? e == address : e == domain);
		}

		public static double EffectiveMinimumConfidence(DepartmentAiDispatchConfig config, double hostDefault)
		{
			var value = config?.MinimumConfidence is { } configured ? (double)configured : hostDefault;
			return Math.Clamp(value, (double)MinimumConfidenceFloor, (double)MinimumConfidenceCeiling);
		}

		private static List<string> Split(string text) =>
			(text ?? "").Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

		/// <summary>"name@host" or "host" (a leading "@" is accepted), lower-cased; null when neither.</summary>
		private static string Normalize(string entry)
		{
			if (string.IsNullOrWhiteSpace(entry) || entry.Length > 254)
				return null;
			var value = entry.Trim().ToLowerInvariant().TrimStart('@');
			if (value.Contains('@'))
				return MailAddress.TryCreate(value, out var parsed) && parsed.Address == value ? value : null;
			return Uri.CheckHostName(value) == UriHostNameType.Dns && value.Contains('.') ? value : null;
		}
	}

	/// <summary>One audit row for the viewer, with the call numbers it refers to (never names or content).</summary>
	public sealed class AiDispatchAuditListItem
	{
		public AiDispatchAuditRow Audit { get; set; }
		public string CallNumber { get; set; }
		public string RelatedCallNumber { get; set; }
	}

	/// <summary>Why AI dispatch is or is not running for a department; every flag is needed.</summary>
	public sealed record AiDispatchStatus(bool HostEnabled, bool RolledOut, bool Entitled, bool FormatSelected, bool ModelConfigured)
	{
		public bool Running => HostEnabled && RolledOut && Entitled && FormatSelected && ModelConfigured;
	}

	public interface IAiDispatchConfigRepository
	{
		Task<DepartmentAiDispatchConfig> GetAsync(int departmentId, CancellationToken cancellationToken);
		/// <summary>Compare-and-swap on <see cref="DepartmentAiDispatchConfig.Revision"/>; false when another admin saved first.</summary>
		Task<bool> SaveAsync(DepartmentAiDispatchConfig config, long expectedRevision, CancellationToken cancellationToken);
	}

	/// <summary>Department-admin surface for AI dispatch: status, settings and the audit viewer.</summary>
	public interface IAiDispatchAdminService
	{
		Task<AiDispatchStatus> GetStatusAsync(int departmentId);
		Task<DepartmentAiDispatchConfig> GetSettingsAsync(int departmentId, CancellationToken cancellationToken);
		/// <summary>Returns validation error codes, "Conflict" when another admin saved first, or an empty list on success.</summary>
		Task<IReadOnlyList<string>> SaveSettingsAsync(int departmentId, DepartmentAiDispatchConfig settings, long expectedRevision, string userId, CancellationToken cancellationToken);
		Task<List<AiDispatchAuditListItem>> GetAuditAsync(int departmentId, int take, CancellationToken cancellationToken);
		/// <summary>Audit row counts per outcome over the last <paramref name="days"/> days, independent of how many rows the activity list shows.</summary>
		Task<Dictionary<string, int>> GetRecentOutcomeCountsAsync(int departmentId, int days, CancellationToken cancellationToken);
		/// <summary>AI dispatch tokens used by the department this calendar month (UTC).</summary>
		Task<long> GetMonthlyUsageAsync(int departmentId, CancellationToken cancellationToken);
		/// <summary>Whether a message from <paramref name="sender"/> may be enriched (the call is created regardless).</summary>
		Task<bool> IsSenderAllowedAsync(int departmentId, string sender);
	}
}
