using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Autofills
{
	/// <summary>
	/// Autofills in the Resgrid system
	/// </summary>
	public class GetAutofillsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<AutofillResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetAutofillsResult()
		{
			Data = new List<AutofillResultData>();
		}
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class AutofillResultData
	{
		/// <summary>
		/// Identifier for the Autofill
		/// </summary>
		public string AutofillId { get; set; }

		/// <summary>
		/// Department Identifier
		/// </summary>
		public int DepartmentId { get; set; }

		/// <summary>
		/// Type of this autofill
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// Sort order
		/// </summary>
		public int Sort { get; set; }

		/// <summary>
		/// Name for this Autofill
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Data (text) for this Autofill
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// Added by User Identifier
		/// </summary>
		public string AddedByUserId { get; set; }

		/// <summary>
		/// Added on as UTC
		/// </summary>
		public string AddedOn { get; set; }
	}
}
