namespace Resgrid.Web.Services.Controllers.Version3.Models.Profile
{
	/// <summary>
	/// Information about mobile carriers in the Resgrid system. If you need mobile carriers added contact team@resgrid.com.
	/// </summary>
	public class MobileCarriersResult
	{
		/// <summary>
		/// Mobile Carrier Id
		/// </summary>
		public int Cid { get; set; }

		/// <summary>
		/// Mobile Carrier Name
		/// </summary>
		public string Nme { get; set; }
	}
}