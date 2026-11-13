using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.RoadClosures
{
	public class GetRoadClosuresResult : StandardApiResponseV4Base
	{
		public List<RoadClosureResultData> Data { get; set; } = new List<RoadClosureResultData>();
	}

	/// <summary>A road closure (M0314). Times are UTC.</summary>
	public class RoadClosureResultData
	{
		public string RoadClosureId { get; set; }

		public string Name { get; set; }

		/// <summary>RoadClosureTypes: 0 other, 1 event, 2 road works, 3 incident, 4 police, 5 weather, 6 hazard, 7 infrastructure.</summary>
		public int ClosureType { get; set; }

		/// <summary>0 full (no through traffic), 1 partial (slowed).</summary>
		public int Severity { get; set; }

		/// <summary>Speed inside as a percentage of normal.</summary>
		public int SpeedPercent { get; set; }

		/// <summary>Bit mask of who it stops: 1 driving, 2 cycling, 4 walking.</summary>
		public int AppliesTo { get; set; }

		/// <summary>0 in effect, 1 scheduled, 2 ended, 3 switched off.</summary>
		public int State { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>The closed areas and road lines as a GeoJSON FeatureCollection.</summary>
		public JToken Geometry { get; set; }

		public List<RoadClosureWindowData> Windows { get; set; } = new List<RoadClosureWindowData>();

		public string Reason { get; set; }

		public string ClosedBy { get; set; }

		public string Authority { get; set; }

		public string ContactName { get; set; }

		public string ContactDetails { get; set; }

		public string Notes { get; set; }

		public DateTime UpdatedOn { get; set; }
	}

	public class RoadClosureWindowData
	{
		public DateTime StartsOn { get; set; }

		/// <summary>Null: until further notice.</summary>
		public DateTime? EndsOn { get; set; }
	}
}
