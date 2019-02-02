namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	/// <summary>
	/// Input into the API to update an existing call. Only specific information can be updated. 
	/// </summary>
	public class EditCallInput
	{
		/// <summary>
		/// Id of the call being updated
		/// </summary>
		public int Cid { get; set; }

		/// <summary>
		/// Updated name of the call
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// Updated Nature of the Call
		/// </summary>
		public string Noc { get; set; }

		/// <summary>
		/// Updated Call Address
		/// </summary>
		public string Add { get; set; }
	}
}