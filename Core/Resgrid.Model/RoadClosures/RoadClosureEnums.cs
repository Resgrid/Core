using System;

namespace Resgrid.Model
{
	/// <summary>What closed the road. Persisted on <c>RoadClosures.ClosureType</c>, so values are never renumbered.</summary>
	public enum RoadClosureTypes
	{
		Other = 0,

		/// <summary>A planned event: marathon, cycle race, festival, parade, market.</summary>
		Event = 1,

		/// <summary>Road works or construction.</summary>
		RoadWorks = 2,

		/// <summary>An emergency scene: collision, fire, rescue.</summary>
		Incident = 3,

		/// <summary>A police cordon or restriction.</summary>
		Police = 4,

		/// <summary>Flooding, snow, ice or storm damage.</summary>
		Weather = 5,

		/// <summary>A hazard on the road: spill, fallen tree, debris.</summary>
		Hazard = 6,

		/// <summary>A bridge, tunnel or rail crossing out of use.</summary>
		Infrastructure = 7
	}

	/// <summary>How closed the road is. Persisted on <c>RoadClosures.Severity</c>, so values are never renumbered.</summary>
	public enum RoadClosureSeverities
	{
		/// <summary>No through traffic: routes go around it.</summary>
		Full = 0,

		/// <summary>Open but slow (lane closure, contraflow, walking pace through a crowd): routes may use it at reduced speed.</summary>
		Partial = 1
	}

	/// <summary>
	/// Who the closure stops. Persisted as a bit mask on <c>RoadClosures.AppliesTo</c>, so values are never changed. Air travel
	/// never meets a road closure.
	/// </summary>
	[Flags]
	public enum RoadClosureTravelModes
	{
		None = 0,
		Driving = 1,
		Cycling = 2,
		Walking = 4,
		All = Driving | Cycling | Walking
	}

	/// <summary>Where a closure stands right now, for lists and maps (never stored).</summary>
	public enum RoadClosureStates
	{
		/// <summary>In effect now.</summary>
		Active = 0,

		/// <summary>Has a window still to come but none running now.</summary>
		Scheduled = 1,

		/// <summary>Every window has ended.</summary>
		Ended = 2,

		/// <summary>Switched off by the department; never applied.</summary>
		Disabled = 3
	}

	public static class RoadClosureTravelModeMapping
	{
		/// <summary>The closure flag a unit's travel mode is matched on; air travel has none.</summary>
		public static RoadClosureTravelModes For(UnitTravelModes mode)
		{
			switch (mode)
			{
				case UnitTravelModes.Driving:
					return RoadClosureTravelModes.Driving;
				case UnitTravelModes.Cycling:
					return RoadClosureTravelModes.Cycling;
				case UnitTravelModes.Walking:
					return RoadClosureTravelModes.Walking;
				default:
					return RoadClosureTravelModes.None;
			}
		}
	}
}
