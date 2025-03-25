using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Units
{
	/// <summary>
	/// Multiple Unit infos Result
	/// </summary>
	public class UnitsInfoResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<UnitsInfoResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitsInfoResult()
		{
			Data = new List<UnitsInfoResultData>();
		}
	}

	/// <summary>
	/// The information about a specific unit
	/// </summary>
	public class UnitsInfoResultData
	{
		/// <summary>
		/// Id of the Unit
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// The Id of the department the unit is under
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Name of the Unit
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Department assigned type for the unit
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// Department assigned type id for the unit
		/// </summary>
		public int TypeId { get; set; }

		/// <summary>
		/// Custom Statuses Set Id
		/// </summary>
		public string CustomStatusSetId { get; set; }

		/// <summary>
		/// Station Id of the station housing the unit (0 means no station)
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Name of the station the unit is under
		/// </summary>
		public string GroupName { get; set; }

		/// <summary>
		/// Vehicle Identification Number for the unit
		/// </summary>
		public string Vin { get; set; }

		/// <summary>
		/// Plate Number for the Unit
		/// </summary>
		public string PlateNumber { get; set; }

		/// <summary>
		/// Is the unit 4-Wheel drive
		/// </summary>
		public bool FourWheelDrive { get; set; }

		/// <summary>
		/// Does the unit require a special permit to drive
		/// </summary>
		public bool SpecialPermit { get; set; }

		/// <summary>
		/// Id number of the units current destination (0 means no destination)
		/// </summary>
		public string CurrentDestinationId { get; set; }

		/// <summary>
		/// Name of the units current destination (0 means no destination)
		/// </summary>
		public string CurrentDestinationName { get; set; }

		/// <summary>
		/// The current status/state of the Unit
		/// </summary>
		public string CurrentStatusId { get; set; }

		/// <summary>
		/// The current status/state of the Unit as a name
		/// </summary>
		public string CurrentStatus { get; set; }

		/// <summary>
		/// The current status/state of the Unit color
		/// </summary>
		public string CurrentStatusColor { get; set; }

		/// <summary>
		/// The Timestamp of the status
		/// </summary>
		public DateTime CurrentStatusTimestamp { get; set; }

		/// <summary>
		/// The Timestamp of the status in UTC/GMT
		/// </summary>
		public DateTime CurrentStatusTimestampUtc { get; set; }

		/// <summary>
		/// The units current Latitude
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// The units current Longitude
		/// </summary>
		public string Longitude { get; set; }

		/// <summary>
		/// Current user provide status note
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Units Roles
		/// </summary>
		public List<UnitRoleData> Roles { get; set; }
	}
}
