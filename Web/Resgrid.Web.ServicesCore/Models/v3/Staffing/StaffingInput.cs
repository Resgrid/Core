namespace Resgrid.Web.Services.Controllers.Version3.Models.Staffing
{
	public class StaffingInput
	{
		/// <summary>
		/// UserId (GUID/UUID) of the User to set. This field will be ignored if the input is used on a 
		/// function that is setting status for the current user.
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// The state/staffing level of the user to set for the user.
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Note for the staffing level
		/// </summary>
		public string Not { get; set; }
	}
}
