using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Protocols
{
	/// <summary>Round 10 (Omni): the department's protocol categories (Protocols/GetCategories).</summary>
	public class GetProtocolCategoriesResult : StandardApiResponseV4Base
	{
		public List<ProtocolCategoryResultData> Data { get; set; } = new List<ProtocolCategoryResultData>();
	}

	/// <summary>One protocol category: the name behind ProtocolResultData.CategoryId.</summary>
	public class ProtocolCategoryResultData
	{
		public int CategoryId { get; set; }

		public string Name { get; set; }

		/// <summary>The category this one is nested under; null at the top level.</summary>
		public int? ParentId { get; set; }

		public int SortOrder { get; set; }

		/// <summary>The protocol kind (ProtocolKinds) the category is for; null for any kind.</summary>
		public int? Kind { get; set; }
	}
}
