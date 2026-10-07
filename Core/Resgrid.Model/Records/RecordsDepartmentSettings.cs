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
			Floors = new List<RecordsNumberingFloor>();
			Prefixes = new List<RecordsNumberingPrefix>();
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

		/// <summary>
		/// The department's number pattern (see <see cref="RecordNumberFormat"/>), e.g. "{PREFIX}-{YYYY}-{SEQ}". Null keeps
		/// the pattern IncludeYear and PerGroupSequence describe; when set, those two flags are kept in step with it.
		/// </summary>
		[ProtoMember(6)]
		public string Pattern { get; set; }

		/// <summary>
		/// Raised starting points, one per sequence scope: a department that moved to Resgrid after issuing 2026-0001
		/// through 2026-0152 elsewhere sets that scope's next number to 153. A floor only ever rises.
		/// </summary>
		[ProtoMember(7)]
		public List<RecordsNumberingFloor> Floors { get; set; }

		/// <summary>
		/// The department's own {PREFIX} per system record type, e.g. "FIRE" for Run and "NFIRS" for the incident report. A type with
		/// no entry renders <see cref="RmsDefinitionKeys.DefaultNumberPrefix"/>. Department definitions carry their prefix on the definition.
		/// </summary>
		[ProtoMember(8)]
		public List<RecordsNumberingPrefix> Prefixes { get; set; }

		/// <summary>What {PREFIX} renders for a record type: the department's prefix, or the shipped default when it has none (or a saved one no longer validates).</summary>
		public string PrefixFor(string definitionKey)
		{
			var custom = (Prefixes ?? new List<RecordsNumberingPrefix>()).FirstOrDefault(p => p != null && string.Equals(p.DefinitionKey, definitionKey, StringComparison.Ordinal))?.Prefix;
			return RecordNumberFormat.IsValidPrefix(custom) ? custom : RmsDefinitionKeys.DefaultNumberPrefix(definitionKey);
		}

		/// <summary>Sets a type's prefix; null, or the shipped default, removes the department's own so the type follows the default again.</summary>
		public void SetPrefix(string definitionKey, string prefix)
		{
			Prefixes ??= new List<RecordsNumberingPrefix>();
			Prefixes.RemoveAll(p => p == null || string.Equals(p.DefinitionKey, definitionKey, StringComparison.Ordinal));
			if (prefix != null && !string.Equals(prefix, RmsDefinitionKeys.DefaultNumberPrefix(definitionKey), StringComparison.Ordinal))
				Prefixes.Add(new RecordsNumberingPrefix { DefinitionKey = definitionKey, Prefix = prefix });
		}

		/// <summary>
		/// The sequence to issue next in a scope: one past the highest already issued, but never below its floor.
		/// Numbers that exist always win, so a floor can never cause a duplicate.
		/// </summary>
		public int NextSequence(string scopeKey, int highestIssued)
		{
			return Math.Max(Math.Max(0, highestIssued) + 1, FloorFor(scopeKey));
		}

		public int FloorFor(string scopeKey)
		{
			return (Floors ?? new List<RecordsNumberingFloor>()).Where(f => f != null && string.Equals(f.ScopeKey, scopeKey, StringComparison.Ordinal))
				.Select(f => f.NextSequence).DefaultIfEmpty(1).Max();
		}

		/// <summary>Records a raised floor; a value at or below the current floor changes nothing.</summary>
		public void RaiseFloor(string scopeKey, int nextSequence, string userId, DateTime now)
		{
			if (string.IsNullOrEmpty(scopeKey) || nextSequence <= FloorFor(scopeKey))
				return;

			Floors ??= new List<RecordsNumberingFloor>();
			Floors.RemoveAll(f => f == null || string.Equals(f.ScopeKey, scopeKey, StringComparison.Ordinal));
			Floors.Add(new RecordsNumberingFloor { ScopeKey = scopeKey, NextSequence = nextSequence, SetOn = now, SetByUserId = userId });
		}
	}

	/// <summary>One raised starting point inside <see cref="RecordsNumberingConfig"/>.</summary>
	[ProtoContract]
	public class RecordsNumberingFloor
	{
		/// <summary><see cref="RecordNumberScope.Key"/> of the sequence, e.g. "INC-2026-#".</summary>
		[ProtoMember(1)]
		public string ScopeKey { get; set; }

		/// <summary>The lowest sequence the scope may issue next.</summary>
		[ProtoMember(2)]
		public int NextSequence { get; set; }

		[ProtoMember(3)]
		public DateTime SetOn { get; set; }

		[ProtoMember(4)]
		public string SetByUserId { get; set; }
	}

	/// <summary>One department-set record type prefix inside <see cref="RecordsNumberingConfig"/>.</summary>
	[ProtoContract]
	public class RecordsNumberingPrefix
	{
		/// <summary>The system definition key, e.g. <see cref="RmsDefinitionKeys.Run"/>.</summary>
		[ProtoMember(1)]
		public string DefinitionKey { get; set; }

		/// <summary>2 to 6 upper-case ASCII letters or digits (<see cref="RecordNumberFormat.IsValidPrefix"/>).</summary>
		[ProtoMember(2)]
		public string Prefix { get; set; }
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
