using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Declared administrative context. Codes and references only; never clinical scope inferred from a role.</summary>
	[ProtoContract]
	public sealed class DepartmentOperatingProfile : IValidatableObject
	{
		public static readonly string[] ArchetypeCodes = { "fire", "ems", "mental-health", "sar", "emergency-response", "hazmat", "industrial", "security", "mutual-aid" };
		public static readonly string[] WorkforceCodes = { "unknown", "career", "volunteer", "contract", "combination" };
		public static readonly string[] DispatchCodes = { "unknown", "central", "self", "external", "combination" };
		public static readonly string[] HoursCodes = { "unknown", "continuous", "scheduled", "on-call", "seasonal" };
		[ProtoMember(1)] public List<string> Archetypes { get; set; } = new();
		[ProtoMember(2)] public string WorkforceMix { get; set; } = "unknown";
		[ProtoMember(3)] public int? DeclaredMemberCount { get; set; }
		[ProtoMember(4)] public List<string> AuthoritativeSystemReferences { get; set; } = new();
		[ProtoMember(5)] public string DispatchModel { get; set; } = "unknown";
		[ProtoMember(6)] public string OperatingHours { get; set; } = "unknown";
		[ProtoMember(7)] public List<string> SiteGroupReferences { get; set; } = new();
		[ProtoMember(8)] public bool? MutualAid { get; set; }
		[ProtoMember(9)] public List<string> LanguageCodes { get; set; } = new();
		[ProtoMember(10)] public List<string> AccessibilityNeeds { get; set; } = new();
		[ProtoMember(11)] public List<string> StaffingPolicyReferences { get; set; } = new();
		[ProtoMember(12)] public List<string> QualificationPolicyReferences { get; set; } = new();
		[ProtoMember(13)] public List<string> ContinuityProcedureReferences { get; set; } = new();
		[ProtoMember(14)] public string SeasonStartMonthDay { get; set; }
		[ProtoMember(15)] public string SeasonEndMonthDay { get; set; }
		[ProtoMember(16)] public DateTime? ReviewedOnUtc { get; set; }
		[ProtoMember(17)] public long Revision { get; set; }
		[ProtoMember(18)] public int? ExpectedEmailPollIntervalMinutes { get; set; }

		public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
		{
			if (Revision < 0 || Revision == long.MaxValue) yield return new ValidationResult("Invalid profile revision.", new[] { nameof(Revision) });
			if (Archetypes == null || Archetypes.Count > ArchetypeCodes.Length || Archetypes.Distinct().Count() != Archetypes.Count || Archetypes.Any(a => !ArchetypeCodes.Contains(a)))
				yield return new ValidationResult("Invalid operating archetype.", new[] { nameof(Archetypes) });
			if (!WorkforceCodes.Contains(WorkforceMix) || !DispatchCodes.Contains(DispatchModel) || !HoursCodes.Contains(OperatingHours))
				yield return new ValidationResult("Invalid operating context.");
			if (DeclaredMemberCount < 0 || DeclaredMemberCount > 1000000) yield return new ValidationResult("Member count is outside the supported range.", new[] { nameof(DeclaredMemberCount) });
			if (ExpectedEmailPollIntervalMinutes is < 1 or > 10080) yield return new ValidationResult("Expected email polling interval must be between 1 and 10080 minutes, or blank.", new[] { nameof(ExpectedEmailPollIntervalMinutes) });
			var references = new[] { AuthoritativeSystemReferences, SiteGroupReferences, StaffingPolicyReferences, QualificationPolicyReferences, ContinuityProcedureReferences };
			if (references.Any(list => list == null || list.Count > 25 || list.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.')))))
				yield return new ValidationResult("Use at most 25 existing document, group or system reference identifiers per field.");
			if (LanguageCodes == null || LanguageCodes.Count > 20 || LanguageCodes.Any(value => !Resgrid.Model.Helpers.AdminAssistLanguageCodes.IsValid(value)))
				yield return new ValidationResult("Use supported language codes.", new[] { nameof(LanguageCodes) });
			if (AccessibilityNeeds == null || AccessibilityNeeds.Count > 4 || AccessibilityNeeds.Any(value => !new[] { "captions", "screen-reader", "large-text", "plain-language" }.Contains(value)))
				yield return new ValidationResult("Invalid accessibility preference.", new[] { nameof(AccessibilityNeeds) });
			if ((SeasonStartMonthDay != null || SeasonEndMonthDay != null) && (!ValidDay(SeasonStartMonthDay) || !ValidDay(SeasonEndMonthDay)))
				yield return new ValidationResult("Choose a valid month and day for both the season start and the season end, or leave the season blank.");
		}
		private static bool ValidDay(string value) => DateTime.TryParseExact("2000-" + value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
	}
}

namespace Resgrid.Model.Helpers
{
	public static class AdminAssistLanguageCodes
	{
		public static bool IsValid(string code) => new[] { "ar", "de", "el", "en", "es", "fr", "it", "pl", "sv", "uk" }.Contains(code);
	}
}
