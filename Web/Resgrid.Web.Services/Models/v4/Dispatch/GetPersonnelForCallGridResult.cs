using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// Data needed for the Dispatch App Modal that sets the state for a unit
	/// </summary>
	public class GetPersonnelForCallGridResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<GetPersonnelForCallGridResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetPersonnelForCallGridResult()
		{
			Data = new List<GetPersonnelForCallGridResultData>();
		}
	}

	/// <summary>
	/// Role entry for the new call dispatch grid
	/// </summary>
	public class GetPersonnelForCallGridResultData
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string Group { get; set; }
		public int GroupId { get; set; }
		public string Status { get; set; }
		public string StatusColor { get; set; }
		public string Staffing { get; set; }
		public string StaffingColor { get; set; }
		public List<string> Roles { get; set; }
		public string Eta { get; set; }
		public int Weight { get; set; }
		public string Location { get; set; }
	}
}
