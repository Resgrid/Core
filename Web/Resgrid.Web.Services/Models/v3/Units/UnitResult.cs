namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	/// <summary>
	///  Basic information on a Unit (Apparatus or piece of equipment of groups of personnel) in the System
	/// </summary>
	public class UnitResult
	{
		/// <summary>
		/// Id For the Unit
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Department Id the unit is under
		/// </summary>
		public int DepartmentId { get; set; }

		/// <summary>
		/// Name of the unit
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Type of unit (department defined)
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// The station id the group is housed at
		/// </summary>
		public int? StationId { get; set; }

		/// <summary>
		/// Vehicle Identifcation Number for the Unit (if applicable) 
		/// </summary>
		public string VIN { get; set; }

		/// <summary>
		/// The Plate numbber for the Unit (if applicable) 
		/// </summary>
		public string PlateNumber { get; set; }

		/// <summary>
		/// Can the unit go off-road
		/// </summary>
		public bool? FourWheel { get; set; }

		/// <summary>
		/// Is a special permit or certification required to drive the unit (if applicable) 
		/// </summary>
		public bool? SpecialPermit { get; set; }
	}
}
