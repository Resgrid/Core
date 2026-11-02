using System;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// Department setting 122 (MappingNavigationConfig): defaults for in-app turn-by-turn navigation and route deviation
	/// thresholds (maplibre-mapping-routing-navigation-plan.md §9, §10.4). A department with no saved value gets the
	/// documented defaults. Every member defaults to 0/false so protobuf round-trips the defaults (true and non-zero
	/// initializers lose their false/0 values).
	/// </summary>
	[ProtoContract]
	public class MappingNavigationConfig
	{
		public const int DefaultArrivalRadiusMeters = 50;
		public const int DefaultDeviationThresholdMinutes = 15;

		/// <summary>True turns spoken guidance off by default (crews can still turn it on).</summary>
		[ProtoMember(1)]
		public bool VoiceGuidanceOff { get; set; }

		/// <summary><see cref="NavigationDistanceUnits"/>: 0 follows the device locale.</summary>
		[ProtoMember(2)]
		public int DistanceUnits { get; set; }

		/// <summary>Arrival radius in metres; 0 is <see cref="DefaultArrivalRadiusMeters"/>.</summary>
		[ProtoMember(3)]
		public int ArrivalRadiusMeters { get; set; }

		/// <summary>True stops navigation offering to set On Scene on arrival at a call.</summary>
		[ProtoMember(4)]
		public bool OnScenePromptOff { get; set; }

		/// <summary><see cref="RouteAvoidOptions"/> flags applied to unit navigation by default.</summary>
		[ProtoMember(5)]
		public int DefaultAvoidOptions { get; set; }

		/// <summary>Minutes late (arrival) or over the estimated dwell before a deviation is recorded; 0 is <see cref="DefaultDeviationThresholdMinutes"/>.</summary>
		[ProtoMember(6)]
		public int DeviationThresholdMinutes { get; set; }

		/// <summary>True records no late-arrival or long-dwell deviations.</summary>
		[ProtoMember(7)]
		public bool TimeDeviationsOff { get; set; }

		public int EffectiveArrivalRadiusMeters()
		{
			return ArrivalRadiusMeters > 0 ? Math.Min(ArrivalRadiusMeters, 1000) : DefaultArrivalRadiusMeters;
		}

		public TimeSpan EffectiveDeviationThreshold()
		{
			return TimeSpan.FromMinutes(DeviationThresholdMinutes > 0 ? Math.Min(DeviationThresholdMinutes, 1440) : DefaultDeviationThresholdMinutes);
		}
	}

	/// <summary>Distance units for navigation instructions (<see cref="MappingNavigationConfig.DistanceUnits"/>).</summary>
	public enum NavigationDistanceUnits
	{
		Device = 0,
		Imperial = 1,
		Metric = 2
	}
}
