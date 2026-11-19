using System;
using System.ComponentModel;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>How far Resgrid goes with a suggested destination (<see cref="DestinationSuggestionConfig.Mode"/>).</summary>
	public enum DestinationSuggestionModes
	{
		/// <summary>No suggestions (nothing is routed).</summary>
		Off = 0,

		/// <summary>The call form and the status destination lists show the suggested destinations; nothing is filled in.</summary>
		Suggest = 1,

		/// <summary>As Suggest, and a new call's empty destination (and a unit's destination when nothing else is assigned) is filled with the best one.</summary>
		SuggestAndFill = 2
	}

	/// <summary>
	/// ProtoBuf-serialized <see cref="DepartmentSettingTypes.DestinationSuggestions"/> (Mapping → Destination Rules): whether
	/// Resgrid suggests destinations from the department's <see cref="DestinationRule"/>s and the incident location, and how
	/// close a destination with a preferred capability must be to win over the closest. A missing row means off.
	/// </summary>
	[ProtoContract]
	public class DestinationSuggestionConfig
	{
		public const int DefaultPreferredWithinMinutes = 3;
		public const int MaximumPreferredWithinMinutes = 30;

		/// <summary><see cref="DestinationSuggestionModes"/>; Off by default (each suggestion spends routing lookups).</summary>
		[ProtoMember(1)]
		public int Mode { get; set; }

		/// <summary>
		/// A destination offering more of the preferred capabilities is ranked ahead of a closer one only while its drive time is
		/// within this many minutes of the closest that meets the requirements (0 = only on equal times).
		/// </summary>
		[ProtoMember(2)]
		[DefaultValue(DefaultPreferredWithinMinutes)]
		public int PreferredWithinMinutes { get; set; } = DefaultPreferredWithinMinutes;

		/// <summary>The stored mode, or Off when it is not one Resgrid knows.</summary>
		public DestinationSuggestionModes GetMode()
		{
			return Enum.IsDefined(typeof(DestinationSuggestionModes), Mode) ? (DestinationSuggestionModes)Mode : DestinationSuggestionModes.Off;
		}

		/// <summary><see cref="PreferredWithinMinutes"/> clamped to 0..<see cref="MaximumPreferredWithinMinutes"/>.</summary>
		public int GetPreferredWithinMinutes()
		{
			return Math.Clamp(PreferredWithinMinutes, 0, MaximumPreferredWithinMinutes);
		}
	}
}
