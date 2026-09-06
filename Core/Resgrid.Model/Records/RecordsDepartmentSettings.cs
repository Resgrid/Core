using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 72 (RecordsNumberingConfig): department-wide numbering defaults applied when a
	/// definition does not declare its own policy. RMS plan section 4.1, "Numbering".
	/// </summary>
	[ProtoContract]
	public class RecordsNumberingConfig
	{
		public RecordsNumberingConfig()
		{
			NumberAssignment = (int)RmsNumberAssignment.OnFinalize;
			ResetYearly = true;
			SequenceWidth = 4;
			IncludeYear = true;
		}

		/// <summary>RmsNumberAssignment value; default OnFinalize so abandoned drafts leave no gaps.</summary>
		[ProtoMember(1)]
		public int NumberAssignment { get; set; }

		/// <summary>Restart the sequence each calendar year (department time zone).</summary>
		[ProtoMember(2)]
		public bool ResetYearly { get; set; }

		/// <summary>Zero-padded width of the sequence part, e.g. 4 gives 0184.</summary>
		[ProtoMember(3)]
		public int SequenceWidth { get; set; }

		/// <summary>Render the year between prefix and sequence: TRN-2026-0184.</summary>
		[ProtoMember(4)]
		public bool IncludeYear { get; set; }

		/// <summary>Scope the sequence per station/group instead of department-wide.</summary>
		[ProtoMember(5)]
		public bool PerGroupSequence { get; set; }
	}

	/// <summary>Department setting 73 (RecordsSearchConfig): index scope and the protected degrade mode notice.</summary>
	[ProtoContract]
	public class RecordsSearchConfig
	{
		public RecordsSearchConfig()
		{
			IndexNarrative = true;
			IncludeLegacyHistory = true;
		}

		/// <summary>Index free-text narrative for unprotected departments. Withdrawn automatically on Protected Data enrollment.</summary>
		[ProtoMember(1)]
		public bool IndexNarrative { get; set; }

		/// <summary>Include LegacyLog/LegacyUnitLog documents in the records index.</summary>
		[ProtoMember(2)]
		public bool IncludeLegacyHistory { get; set; }
	}

	/// <summary>One per-definition retention override inside <see cref="RecordsRetentionPolicy"/>.</summary>
	[ProtoContract]
	public class RecordsRetentionOverride
	{
		[ProtoMember(1)]
		public string DefinitionKey { get; set; }

		/// <summary>0 = permanent.</summary>
		[ProtoMember(2)]
		public int RetentionYears { get; set; }

		/// <summary>Prospective: applies to Records whose latest revision is on or after this date.</summary>
		[ProtoMember(3)]
		public DateTime AppliesFrom { get; set; }
	}

	/// <summary>
	/// Department setting 74 (RecordsRetentionPolicy). Resolution for any Record, first match wins:
	/// legal hold, then a per-definition override, then the department default (standard-class
	/// definitions only), then the shipped class default in <see cref="ResolveYears"/>.
	/// </summary>
	[ProtoContract]
	public class RecordsRetentionPolicy
	{
		/// <summary>Shipped floor for standard operational and NERIS classes.</summary>
		public const int StandardClassDefaultYears = 7;

		/// <summary>Permanent: no automatic purge.</summary>
		public const int Permanent = 0;

		public RecordsRetentionPolicy()
		{
			Overrides = new List<RecordsRetentionOverride>();
		}

		/// <summary>Null = system class default.</summary>
		[ProtoMember(1)]
		public int? DepartmentDefaultYears { get; set; }

		[ProtoMember(2)]
		public List<RecordsRetentionOverride> Overrides { get; set; }

		[ProtoMember(3)]
		public string LastChangedByUserId { get; set; }

		[ProtoMember(4)]
		public DateTime? LastChangedOn { get; set; }

		[ProtoMember(5)]
		public List<RecordsRetentionPolicyVersion> History { get; set; } = new List<RecordsRetentionPolicyVersion>();

		/// <summary>The policy in force when this revision became official. Unknown pre-history is retained permanently.</summary>
		public int ResolveYears(string definitionKey, DateTime revisionOn)
		{
			var applicable = this;
			if (LastChangedOn.HasValue && revisionOn < LastChangedOn.Value)
			{
				applicable = (History ?? new List<RecordsRetentionPolicyVersion>()).Where(v => v.EffectiveOn <= revisionOn)
					.OrderByDescending(v => v.EffectiveOn).Select(v => v.Policy).FirstOrDefault();
				if (applicable == null) return Permanent;
			}
			var rule = applicable.Overrides?.Where(o => o.DefinitionKey == definitionKey && o.AppliesFrom <= revisionOn)
				.OrderByDescending(o => o.AppliesFrom).FirstOrDefault();
			if (rule != null) return Math.Max(Permanent, rule.RetentionYears);
			return RmsDefinitionKeys.RestrictedClass.Contains(definitionKey ?? string.Empty) ? Permanent
				: Math.Max(Permanent, applicable.DepartmentDefaultYears ?? StandardClassDefaultYears);
		}

		/// <summary>Called while holding the department write lock; caller-supplied history is never accepted.</summary>
		public void PreserveHistory(RecordsRetentionPolicy previous, DateTime now)
		{
			previous ??= new RecordsRetentionPolicy();
			History = new List<RecordsRetentionPolicyVersion>(previous.History ?? new List<RecordsRetentionPolicyVersion>());
			History.Add(new RecordsRetentionPolicyVersion
			{
				EffectiveOn = previous.LastChangedOn ?? DateTime.MinValue,
				Policy = new RecordsRetentionPolicy { DepartmentDefaultYears = previous.DepartmentDefaultYears,
					Overrides = (previous.Overrides ?? new List<RecordsRetentionOverride>()).Select(o => new RecordsRetentionOverride
					{ DefinitionKey = o.DefinitionKey, RetentionYears = o.RetentionYears, AppliesFrom = o.AppliesFrom }).ToList(),
					LastChangedByUserId = previous.LastChangedByUserId }
			});
			LastChangedOn = now;
			foreach (var rule in Overrides ?? new List<RecordsRetentionOverride>())
			{
				var old = previous.Overrides?.FirstOrDefault(o => o.DefinitionKey == rule.DefinitionKey && o.RetentionYears == rule.RetentionYears);
				rule.AppliesFrom = old?.AppliesFrom ?? now;
			}
		}

		/// <summary>
		/// Retention years for a definition under this policy (legal hold is evaluated by the caller
		/// first). Restricted-class definitions never inherit the department default; they need an
		/// explicit override, which the Records Settings screen only writes after confirmation.
		/// </summary>
		public int ResolveYears(string definitionKey)
		{
			if (Overrides != null)
			{
				foreach (var o in Overrides)
				{
					if (string.Equals(o.DefinitionKey, definitionKey, StringComparison.Ordinal))
						return o.RetentionYears < 0 ? Permanent : o.RetentionYears;
				}
			}

			if (RmsDefinitionKeys.RestrictedClass.Contains(definitionKey ?? string.Empty))
				return Permanent;

			if (DepartmentDefaultYears.HasValue)
				return DepartmentDefaultYears.Value < 0 ? Permanent : DepartmentDefaultYears.Value;

			return StandardClassDefaultYears;
		}
	}

	[ProtoContract]
	public class RecordsRetentionPolicyVersion
	{
		[ProtoMember(1)] public DateTime EffectiveOn { get; set; }
		[ProtoMember(2)] public RecordsRetentionPolicy Policy { get; set; }
	}

	/// <summary>Department setting 77 (RecordsDisclosureConfig). RMS-3 consumes it; the shape ships now so the value is claimed.</summary>
	[ProtoContract]
	public class RecordsDisclosureConfig
	{
		public RecordsDisclosureConfig()
		{
			StatutoryClockDays = 10;
		}

		[ProtoMember(1)]
		public int StatutoryClockDays { get; set; }

		[ProtoMember(2)]
		public string DefaultRedactionProfile { get; set; }

		[ProtoMember(3)]
		public string ReleaseApproverUserId { get; set; }
	}
}
