using System;
using System.Collections.Generic;
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
