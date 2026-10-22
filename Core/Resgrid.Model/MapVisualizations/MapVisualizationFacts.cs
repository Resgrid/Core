using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Everything <see cref="MapVisualizationBuilder"/> needs to turn a definition into layers, already loaded, filtered to
	/// what the viewer may see and resolved to plain values. The service fills it; the builder never reads a database, so the
	/// map math is tested on its own.
	/// </summary>
	public sealed class MapVisualizationContext
	{
		public MapVisualizationDefinition Definition { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		/// <summary>First moment of the window in the department's time zone.</summary>
		public DateTime StartLocal { get; set; }

		/// <summary>Last moment of the window (inclusive) in the department's time zone.</summary>
		public DateTime EndLocal { get; set; }

		/// <summary>Calls that matched the shared filters, newest first.</summary>
		public List<MapVisualizationCallFact> Calls { get; set; } = new List<MapVisualizationCallFact>();

		/// <summary>More calls matched than were read.</summary>
		public bool CallsTruncated { get; set; }

		/// <summary>True when unit times (on scene / responding) were computed for <see cref="Calls"/>.</summary>
		public bool UnitTimesLoaded { get; set; }

		public List<MapVisualizationStation> Stations { get; set; } = new List<MapVisualizationStation>();

		public List<MapVisualizationStatusFact> ResponderOrigins { get; set; } = new List<MapVisualizationStatusFact>();

		public List<MapVisualizationStatusFact> UnitStatusLocations { get; set; } = new List<MapVisualizationStatusFact>();

		public List<MapVisualizationTrackFact> UnitTracks { get; set; } = new List<MapVisualizationTrackFact>();

		public List<MapVisualizationTrackFact> PersonnelTracks { get; set; } = new List<MapVisualizationTrackFact>();

		/// <summary>Notices per source key (<see cref="MapVisualizationNotices"/>), e.g. a hidden or shortened source.</summary>
		public Dictionary<string, List<string>> SourceNotices { get; set; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

		/// <summary>Rows per source that had no usable location.</summary>
		public Dictionary<string, int> SourceWithoutLocation { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

		public MapVisualizationLookups Lookups { get; set; } = new MapVisualizationLookups();

		public MapVisualizationCallFocus Call { get; set; }

		public double? CenterLatitude { get; set; }

		public double? CenterLongitude { get; set; }

		public void AddNotice(string source, string notice)
		{
			if (!SourceNotices.TryGetValue(source, out var notices))
				SourceNotices[source] = notices = new List<string>();

			if (!notices.Contains(notice))
				notices.Add(notice);
		}
	}

	/// <summary>Names and colors for category labels.</summary>
	public sealed class MapVisualizationLookups
	{
		/// <summary>Priority id → (name, color).</summary>
		public Dictionary<int, (string Name, string Color)> Priorities { get; set; } = new Dictionary<int, (string, string)>();

		/// <summary>Group id → (name, geofence color or null).</summary>
		public Dictionary<int, (string Name, string Color)> Groups { get; set; } = new Dictionary<int, (string, string)>();

		/// <summary>Unit id → (name, unit type name, station group id).</summary>
		public Dictionary<int, (string Name, string Type, int? StationGroupId)> Units { get; set; } = new Dictionary<int, (string, string, int?)>();

		/// <summary>User id → display name.</summary>
		public Dictionary<string, string> People { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Custom status detail id → (text, color).</summary>
		public Dictionary<int, (string Name, string Color)> CustomStatuses { get; set; } = new Dictionary<int, (string, string)>();
	}

	public sealed class MapVisualizationCallFact
	{
		public int CallId { get; set; }

		public string Type { get; set; }

		public int Priority { get; set; }

		public int State { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		public DateTime LoggedOnUtc { get; set; }

		/// <summary><see cref="LoggedOnUtc"/> in the department's time zone.</summary>
		public DateTime LoggedOnLocal { get; set; }

		public int? HomeGroupId { get; set; }

		/// <summary>Minutes from logged to the first unit on scene.</summary>
		public double? ResponseMinutes { get; set; }

		/// <summary>Minutes from logged to the first unit responding / en route.</summary>
		public double? TurnoutMinutes { get; set; }

		public double? DurationMinutes { get; set; }

		public int UnitsDispatched { get; set; }

		public int PersonnelResponded { get; set; }

		/// <summary>Distance to the nearest station in the definition's unit; null without station locations.</summary>
		public double? StationDistance { get; set; }

		/// <summary>Calls in the window at the same location (this one included).</summary>
		public int CallsAtLocation { get; set; } = 1;

		/// <summary>Units that worked the call, visible to the viewer.</summary>
		public List<MapVisualizationUnitResponseFact> Units { get; set; } = new List<MapVisualizationUnitResponseFact>();

		/// <summary>Members who responded to the call, visible to the viewer.</summary>
		public List<string> Responders { get; set; } = new List<string>();
	}

	public sealed class MapVisualizationUnitResponseFact
	{
		public int UnitId { get; set; }

		public double? ResponseMinutes { get; set; }

		public double? TurnoutMinutes { get; set; }

		/// <summary>Distance from the unit's own station (else the nearest station) to the call.</summary>
		public double? StationDistance { get; set; }
	}

	/// <summary>A status a unit or member set, at the place they were when they set it.</summary>
	public sealed class MapVisualizationStatusFact
	{
		public int UnitId { get; set; }

		public string UserId { get; set; }

		/// <summary>The raw status: a built-in state/action type or a custom status detail id.</summary>
		public int Status { get; set; }

		public bool IsUnit { get; set; }

		public DateTime TimestampLocal { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		public int CallId { get; set; }

		public double? StationDistance { get; set; }
	}

	/// <summary>One unit's or member's recorded positions, oldest first.</summary>
	public sealed class MapVisualizationTrackFact
	{
		public int UnitId { get; set; }

		public string UserId { get; set; }

		public List<(double Latitude, double Longitude, DateTime TimestampUtc)> Points { get; set; } = new List<(double, double, DateTime)>();
	}
}
