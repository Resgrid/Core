using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.UserDefinedFields
{
	/// <summary>Input for saving UDF field values for a specific entity.</summary>
	public class SaveUdfFieldValuesInput
	{
		[Required]
		public int EntityType { get; set; }

		[Required]
		public string EntityId { get; set; }

		[Required]
		public List<UdfFieldValueInput> Values { get; set; }
	}

	public class UdfFieldValueInput
	{
		[Required]
		public string UdfFieldId { get; set; }

		public string Value { get; set; }
	}
}

