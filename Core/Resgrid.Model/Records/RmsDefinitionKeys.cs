using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Stable DefinitionKey values for the locked system definitions (RMS plan section 4.1). These are
	/// the Logs-parity types plus Unit Activity (the UnitLog replacement). Department-owned definitions
	/// (RMS-1B) use their own keys and never collide with these because system keys are reserved.
	/// </summary>
	public static class RmsDefinitionKeys
	{
		public const string Run = "system.run";
		public const string Training = "system.training";
		public const string Work = "system.work";
		public const string Meeting = "system.meeting";
		public const string Coroner = "system.coroner";
		public const string Callback = "system.callback";
		public const string UnitActivity = "system.unit-activity";

		/// <summary>The NERIS incident report definition (RMS-2). Reserved now so no department key can take it.</summary>
		public const string NerisIncidentReport = "system.neris-incident";

		/// <summary>Prefix that every locked system definition key carries; department keys may not use it.</summary>
		public const string SystemPrefix = "system.";

		/// <summary>Current schema version of every locked Logs-parity definition. Bumped only by a product release.</summary>
		public const int LockedDefinitionVersion = 1;

		/// <summary>Restricted-class definitions: permanent retention by default and RecordRestricted_View gates their sections.</summary>
		public static readonly HashSet<string> RestrictedClass = new HashSet<string>(StringComparer.Ordinal)
		{
			Coroner
		};

		/// <summary>Locked Logs-parity keys in display order, with the RmsOperationalRecordType each maps to.</summary>
		public static readonly IReadOnlyDictionary<string, RmsOperationalRecordType> LockedTypes = new Dictionary<string, RmsOperationalRecordType>(StringComparer.Ordinal)
		{
			{ Run, RmsOperationalRecordType.Run },
			{ Training, RmsOperationalRecordType.Training },
			{ Work, RmsOperationalRecordType.Work },
			{ Meeting, RmsOperationalRecordType.Meeting },
			{ Coroner, RmsOperationalRecordType.Coroner },
			{ Callback, RmsOperationalRecordType.Callback },
			{ UnitActivity, RmsOperationalRecordType.UnitActivity }
		};

		/// <summary>Reverse map from the typed enum to its definition key.</summary>
		public static string ForType(RmsOperationalRecordType type)
		{
			switch (type)
			{
				case RmsOperationalRecordType.Run: return Run;
				case RmsOperationalRecordType.Training: return Training;
				case RmsOperationalRecordType.Work: return Work;
				case RmsOperationalRecordType.Meeting: return Meeting;
				case RmsOperationalRecordType.Coroner: return Coroner;
				case RmsOperationalRecordType.Callback: return Callback;
				case RmsOperationalRecordType.UnitActivity: return UnitActivity;
				default: throw new ArgumentOutOfRangeException(nameof(type), type, "Not a locked Logs-parity type.");
			}
		}

		public static bool IsSystemKey(string definitionKey)
		{
			return !string.IsNullOrWhiteSpace(definitionKey) &&
				   definitionKey.StartsWith(SystemPrefix, StringComparison.Ordinal);
		}

		/// <summary>
		/// The lifecycle preset every locked Logs-parity definition ships with. A legacy Log is created
		/// already-final, so Quick Entry is the only preset that preserves the speed of the current flow.
		/// Departments change this per definition in RMS-1B.
		/// </summary>
		public const RmsLifecyclePreset LockedDefaultPreset = RmsLifecyclePreset.QuickEntry;

		/// <summary>Cardinality per locked definition (RMS plan section 5.2.1).</summary>
		public static RmsRecordCardinality CardinalityFor(string definitionKey)
		{
			switch (definitionKey)
			{
				case NerisIncidentReport: return RmsRecordCardinality.SingleAuthoritative;
				case UnitActivity: return RmsRecordCardinality.OnePerSubjectPerCall;
				default: return RmsRecordCardinality.MultiplePerCall;
			}
		}

		/// <summary>Record-number prefix used by the default numbering policy for each locked definition.</summary>
		public static string DefaultNumberPrefix(string definitionKey)
		{
			switch (definitionKey)
			{
				case Run: return "RUN";
				case Training: return "TRN";
				case Work: return "WRK";
				case Meeting: return "MTG";
				case Coroner: return "COR";
				case Callback: return "CBK";
				case UnitActivity: return "UNT";
				default: return "REC";
			}
		}
	}
}
