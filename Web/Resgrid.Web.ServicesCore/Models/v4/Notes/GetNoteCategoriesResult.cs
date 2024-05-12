using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Notes
{
	/// <summary>
	/// Result containing all the note categories
	/// </summary>
	public class GetNoteCategoriesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<NoteCategoryData> Data { get; set; }
	}

	/// <summary>
	/// Data about a note category
	/// </summary>
	public class NoteCategoryData
	{
		/// <summary>
		/// Note Category Identification number
		/// </summary>
		public string NoteCategoryId { get; set; }

		/// <summary>
		/// Category text
		/// </summary>
		public string Category { get; set; }
	}
}
