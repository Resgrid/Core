using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	/// <summary>
	/// Input information to close a call
	/// </summary>
	public class CloseCallInput
	{
		/// <summary>
		/// Call Id of the call to close
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Message of the call to close
		/// </summary>
		public string Msg { get; set; }

		/// <summary>
		/// Type of call closure that is used
		/// </summary>
		public ClosedOnlyCallStates Typ { get; set; }
	}
}
