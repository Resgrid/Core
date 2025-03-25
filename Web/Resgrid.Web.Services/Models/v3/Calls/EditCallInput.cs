using System;

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
		public string Pri { get; set; }
		public string Nme { get; set; }
		public string Noc { get; set; }
		public string Not { get; set; }
		public string Add { get; set; }
		public string Geo { get; set; }

		public string Typ { get; set; }
		public string W3W { get; set; }
		public string Dis { get; set; }
		public string CNme { get; set; }
		public string CNum { get; set; }

		public string EId { get; set; }
		public string InI { get; set; }
		public string RId { get; set; }
		public DateTime? Don { get; set; }
		public string Cfd { get; set; }

		public bool RebroadcastCall { get; set; }
	}
}
