using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	/// <summary>Mapping → Road Closures: every closure of the department on a map and in a list.</summary>
	public class RoadClosuresIndexView
	{
		public List<RoadClosureRow> Closures { get; set; } = new List<RoadClosureRow>();

		public bool CanManage { get; set; }

		public double CenterLatitude { get; set; }

		public double CenterLongitude { get; set; }

		/// <summary>"active", "scheduled", "ended", "disabled" or "all"; which closures the list starts on.</summary>
		public string Filter { get; set; }
	}

	public class RoadClosureRow
	{
		public string RoadClosureId { get; set; }

		public string Name { get; set; }

		public RoadClosureTypes ClosureType { get; set; }

		public RoadClosureSeverities Severity { get; set; }

		public RoadClosureStates State { get; set; }

		public RoadClosureTravelModes AppliesTo { get; set; }

		public string Reason { get; set; }

		public string ClosedBy { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>The window running now, or the next one, in department-local time; null when there is none.</summary>
		public DateTime? WindowStartsLocal { get; set; }

		public DateTime? WindowEndsLocal { get; set; }

		public int WindowCount { get; set; }

		/// <summary>The shape as a GeoJSON FeatureCollection for the list map.</summary>
		public string GeometryJson { get; set; }
	}

	/// <summary>A closure time as the form posts it: department-local wall-clock values ("yyyy-MM-ddTHH:mm").</summary>
	public class RoadClosureWindowInput
	{
		public string StartsOn { get; set; }

		public string EndsOn { get; set; }

		public const string Format = "yyyy-MM-dd'T'HH:mm";

		public static bool TryParse(string value, out DateTime local)
		{
			local = default(DateTime);
			if (string.IsNullOrWhiteSpace(value))
				return false;

			return DateTime.TryParseExact(value.Trim(), new[] { Format, "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture,
				DateTimeStyles.None, out local);
		}
	}

	/// <summary>New / Edit road closure.</summary>
	public class RoadClosureEditView
	{
		public string RoadClosureId { get; set; }

		public string Name { get; set; }

		public int ClosureType { get; set; }

		public int Severity { get; set; }

		public int SpeedPercent { get; set; } = RoadClosure.DefaultSpeedPercent;

		public int DetourMinutes { get; set; } = RoadClosure.DefaultDetourMinutes;

		public bool AppliesToDriving { get; set; } = true;

		public bool AppliesToCycling { get; set; }

		public bool AppliesToWalking { get; set; }

		public string GeometryJson { get; set; }

		public bool IsEnabled { get; set; } = true;

		public string Reason { get; set; }

		public string ClosedBy { get; set; }

		public string Authority { get; set; }

		public string ContactName { get; set; }

		public string ContactDetails { get; set; }

		public string Notes { get; set; }

		public List<RoadClosureWindowInput> Windows { get; set; } = new List<RoadClosureWindowInput>();

		public double CenterLatitude { get; set; }

		public double CenterLongitude { get; set; }

		/// <summary>Who last changed it and when (department-local), for the edit page footer.</summary>
		public string UpdatedBy { get; set; }

		public DateTime? UpdatedOnLocal { get; set; }

		public bool IsNew => string.IsNullOrWhiteSpace(RoadClosureId);

		public RoadClosureTravelModes AppliesTo =>
			(AppliesToDriving ? RoadClosureTravelModes.Driving : RoadClosureTravelModes.None) |
			(AppliesToCycling ? RoadClosureTravelModes.Cycling : RoadClosureTravelModes.None) |
			(AppliesToWalking ? RoadClosureTravelModes.Walking : RoadClosureTravelModes.None);

		public static RoadClosureEditView From(RoadClosure closure, Func<DateTime, DateTime> toLocal)
		{
			var modes = closure.AppliesToModes;
			return new RoadClosureEditView
			{
				RoadClosureId = closure.RoadClosureId,
				Name = closure.Name,
				ClosureType = closure.ClosureType,
				Severity = closure.Severity,
				SpeedPercent = closure.SpeedPercent,
				DetourMinutes = closure.DetourMinutes,
				AppliesToDriving = modes.HasFlag(RoadClosureTravelModes.Driving),
				AppliesToCycling = modes.HasFlag(RoadClosureTravelModes.Cycling),
				AppliesToWalking = modes.HasFlag(RoadClosureTravelModes.Walking),
				GeometryJson = RoadClosureShape.Normalize(closure.GeometryJson) ?? closure.GeometryJson,
				IsEnabled = closure.IsEnabled,
				Reason = closure.Reason,
				ClosedBy = closure.ClosedBy,
				Authority = closure.Authority,
				ContactName = closure.ContactName,
				ContactDetails = closure.ContactDetails,
				Notes = closure.Notes,
				Windows = closure.Windows.Select(w => new RoadClosureWindowInput
				{
					StartsOn = toLocal(w.StartsOn).ToString(RoadClosureWindowInput.Format, CultureInfo.InvariantCulture),
					EndsOn = w.EndsOn.HasValue ? toLocal(w.EndsOn.Value).ToString(RoadClosureWindowInput.Format, CultureInfo.InvariantCulture) : null
				}).ToList()
			};
		}
	}
}
