using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Links
{
	public class LinkedCallResult
	{
		/// <summary>
		/// Id of the call
		/// </summary>
		public int Cid { get; set; }

		//public string Unm { get; set; }

		/// <summary>
		/// Priority of the call (Low		= 0, Medium = 1, High	= 2, Emergency = 3)
		/// </summary>
		public int Pri { get; set; }

		/// <summary>
		/// Is Call Critical
		/// </summary>
		public bool Ctl { get; set; }

		/// <summary>
		/// Name of the Call
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// Nature of the Call
		/// </summary>
		public string Noc { get; set; }

		/// <summary>
		/// Map Location of the Call
		/// </summary>
		public string Map { get; set; }

		/// <summary>
		/// High level note for the Call
		/// </summary>
		public string Not { get; set; }

		/// <summary>
		/// Call Address
		/// </summary>
		public string Add { get; set; }

		/// <summary>
		/// Gelocation Coordinates
		/// </summary>
		public string Geo { get; set; }

		/// <summary>
		/// When was the call Logged On
		/// </summary>
		public DateTime Lon { get; set; }

		/// <summary>
		/// State of the call (Active	= 0, Closed = 1, Cancelled = 2, Unfounded = 3)
		/// </summary>
		public int Ste { get; set; }

		/// <summary>
		/// Call Number, will be the 2 digit year (i.e. 15 for 2015) and an auto incrementing number for the call in the year. So 15-43 is the 43'rd call in 2015.
		/// </summary>
		public string Num { get; set; }

		/// <summary>
		/// The amount of notes the call has
		/// </summary>
		public int Nts { get; set; }

		/// <summary>
		/// The amount of audio the call has
		/// </summary>
		public int Aud { get; set; }

		/// <summary>
		/// The amount of images the call has
		/// </summary>
		public int Img { get; set; }

		/// <summary>
		/// The amount of files the call has
		/// </summary>
		public int Fls { get; set; }

		/// <summary>
		/// What 3 Words Address
		/// </summary>
		public string w3w { get; set; }

		public string Color { get; set; }
		public string Priority { get; set; }
		public string PriorityCss { get; set; }
		public string State { get; set; }
		public string StateCss { get; set; }
	}
}