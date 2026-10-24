using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	/// <summary>
	/// One unit's map: its own position (when it reports one) and the positions of the members seated in its active
	/// roles. Either may be missing: a team whose members carry the app but whose unit never runs the Unit app has crew
	/// positions only.
	/// </summary>
	public class GetUnitMapDataResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetUnitMapDataResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetUnitMapDataResult()
		{
			Data = new GetUnitMapDataResultData();
		}
	}

	public class GetUnitMapDataResultData
	{
		public GetUnitMapDataResultData()
		{
			Crew = new List<UnitMapCrewMemberData>();
		}

		public string UnitId { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public string GroupName { get; set; }

		/// <summary>The department's default map center and zoom, for when neither the unit nor its crew has a position.</summary>
		public double CenterLat { get; set; }
		public double CenterLon { get; set; }
		public int ZoomLevel { get; set; }

		/// <summary>
		/// The unit's marker (Id "u{unitId}", Type 1), chosen by the same location TTL and status fallback as the department
		/// map; null when the unit has no current position or the viewer may not see its location.
		/// </summary>
		public MapMakerInfoData UnitMarker { get; set; }

		/// <summary>UTC time of the fix or status the unit marker came from.</summary>
		public DateTime? UnitLocationTimestamp { get; set; }

		/// <summary>The unit's seated members, in the unit's role order.</summary>
		public List<UnitMapCrewMemberData> Crew { get; set; }
	}

	public class UnitMapCrewMemberData
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string RoleName { get; set; }

		/// <summary>
		/// The member's marker (Id "p{userId}", Type 3), titled "Name (Role)"; null when the member has no current position
		/// or the viewer may not see it.
		/// </summary>
		public MapMakerInfoData Marker { get; set; }

		/// <summary>UTC time of the fix or status the marker came from.</summary>
		public DateTime? LocationTimestamp { get; set; }
	}
}
