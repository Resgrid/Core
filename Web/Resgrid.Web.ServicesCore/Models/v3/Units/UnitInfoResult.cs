using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	/// <summary>
	/// The information about a specific unit
	/// </summary>
	public class UnitInfoResult
	{
		/// <summary>
		/// Id of the Unit
		/// </summary>
		public int Uid { get; set; }

		/// <summary>
		/// The Id of the department the unit is under
		/// </summary>
		public int Did { get; set; }

		/// <summary>
		/// Name of the Unit
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// Department assigned type for the unit
		/// </summary>
		public string Typ { get; set; }

		/// <summary>
		/// Custom Statuses Set Id
		/// </summary>
		public int Cid { get; set; }

		/// <summary>
		/// Station Id of the station housing the unit (0 means no station)
		/// </summary>
		public int Sid { get; set; }

		/// <summary>
		/// Name of the station the unit is under
		/// </summary>
		public string Snm { get; set; }

		/// <summary>
		/// Vehicle Identification Number for the unit
		/// </summary>
		public string Vin { get; set; }

		/// <summary>
		/// Plate Number for the Unit
		/// </summary>
		public string Pnm { get; set; }

		/// <summary>
		/// Is the unit 4-Wheel drive
		/// </summary>
		public bool Fwl { get; set; }

		/// <summary>
		/// Does the unit require a special permit to drive
		/// </summary>
		public bool Spm { get; set; }

		/// <summary>
		/// Id number of the units current destionation (0 means no destination)
		/// </summary>
		public int Cdi { get; set; }

		/// <summary>
		/// The current status/state of the Unit
		/// </summary>
		public int Ste { get; set; }

		/// <summary>
		/// The Timestamp of the status
		/// </summary>
		public DateTime Tms { get; set; }

		/// <summary>
		/// The units current Latitude
		/// </summary>
		public string Lat { get; set; }

		/// <summary>
		/// The units current Longitude
		/// </summary>
		public string Lon { get; set; }

		/// <summary>
		/// Current user provide status note
		/// </summary>
		public string Not { get; set; }
	}
}
