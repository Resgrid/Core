using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A road, street or area that is closed or slowed for a time (M0314): drawn on a map, with what closed it, who and
	/// why, who to contact, and the periods it is in effect. Dispatch ranking routes around full closures and slows routes
	/// through partial ones, for the travel modes it applies to. No foreign keys: the department purge removes the rows.
	/// </summary>
	[Table("RoadClosures")]
	public class RoadClosure : IEntity
	{
		public const int MaxNameLength = 200;
		public const int MaxTextLength = 200;
		public const int MaxReasonLength = 1000;
		public const int MaxNotesLength = 4000;
		public const int MaxContactDetailsLength = 500;

		/// <summary>Speed inside a closure, as a percentage of normal, when none is set (half speed).</summary>
		public const int DefaultSpeedPercent = 50;
		public const int MinimumSpeedPercent = 5;
		public const int MaximumSpeedPercent = 95;

		/// <summary>Extra minutes a route through a full closure is charged when the router could not go around it.</summary>
		public const int DefaultDetourMinutes = 5;
		public const int MaximumDetourMinutes = 120;

		[Key]
		[Required]
		public string RoadClosureId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(MaxNameLength)]
		public string Name { get; set; }

		/// <summary>A <see cref="RoadClosureTypes"/> value.</summary>
		public int ClosureType { get; set; }

		/// <summary>A <see cref="RoadClosureSeverities"/> value: full (go around) or partial (slow through).</summary>
		public int Severity { get; set; }

		/// <summary>
		/// Travel speed inside the closure as a percentage of normal: the slowdown of a partial closure, and the speed a unit
		/// still makes inside a full closure it has to enter because it starts or ends inside it.
		/// </summary>
		public int SpeedPercent { get; set; } = DefaultSpeedPercent;

		/// <summary>
		/// Minutes added to a route that crosses a full closure when the routing provider can't go around it (Google, OSRM);
		/// the route is flagged as crossing the closure either way.
		/// </summary>
		public int DetourMinutes { get; set; } = DefaultDetourMinutes;

		/// <summary>The travel modes it stops, a <see cref="RoadClosureTravelModes"/> mask; driving only by default.</summary>
		public int AppliesTo { get; set; } = (int)RoadClosureTravelModes.Driving;

		/// <summary>The drawn areas and road lines as GeoJSON (<see cref="RoadClosureShape"/>).</summary>
		public string GeometryJson { get; set; }

		/// <summary>The periods it is in effect, as JSON <see cref="RoadClosureWindow"/>s in UTC; none means always (while enabled).</summary>
		public string WindowsJson { get; set; }

		/// <summary>Off switches the closure out of routing and maps without deleting it.</summary>
		public bool IsEnabled { get; set; } = true;

		[MaxLength(MaxReasonLength)]
		public string Reason { get; set; }

		/// <summary>Who closed the road (a police force, the city, an event organiser).</summary>
		[MaxLength(MaxTextLength)]
		public string ClosedBy { get; set; }

		/// <summary>The authority responsible for the closure or that issued the permit.</summary>
		[MaxLength(MaxTextLength)]
		public string Authority { get; set; }

		[MaxLength(MaxTextLength)]
		public string ContactName { get; set; }

		/// <summary>Phone, radio channel, email: however the contact is reached.</summary>
		[MaxLength(MaxContactDetailsLength)]
		public string ContactDetails { get; set; }

		[MaxLength(MaxNotesLength)]
		public string Notes { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public List<RoadClosureWindow> Windows
		{
			get
			{
				if (string.IsNullOrWhiteSpace(WindowsJson))
					return new List<RoadClosureWindow>();

				try
				{
					return RoadClosureWindows.Normalize(JsonConvert.DeserializeObject<List<RoadClosureWindow>>(WindowsJson));
				}
				catch (JsonException)
				{
					return new List<RoadClosureWindow>();
				}
			}
			set
			{
				var windows = RoadClosureWindows.Normalize(value);
				WindowsJson = windows.Count == 0 ? null : JsonConvert.SerializeObject(windows);
			}
		}

		[NotMapped]
		[JsonIgnore]
		public RoadClosureSeverities SeverityValue =>
			Enum.IsDefined(typeof(RoadClosureSeverities), Severity) ? (RoadClosureSeverities)Severity : RoadClosureSeverities.Full;

		[NotMapped]
		[JsonIgnore]
		public RoadClosureTravelModes AppliesToModes => (RoadClosureTravelModes)AppliesTo & RoadClosureTravelModes.All;

		/// <summary>Where it stands at <paramref name="utc"/>.</summary>
		public RoadClosureStates StateAt(DateTime utc) => RoadClosureWindows.StateAt(IsEnabled, Windows, utc);

		/// <summary>True when it is in effect at <paramref name="utc"/> for a unit travelling by <paramref name="mode"/>.</summary>
		public bool AppliesAt(DateTime utc, UnitTravelModes mode)
		{
			var flag = RoadClosureTravelModeMapping.For(mode);
			return flag != RoadClosureTravelModes.None && (AppliesToModes & flag) == flag && StateAt(utc) == RoadClosureStates.Active;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RoadClosureId; }
			set { RoadClosureId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RoadClosures";

		[NotMapped]
		public string IdName => "RoadClosureId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Windows", "SeverityValue", "AppliesToModes" };
	}
}
