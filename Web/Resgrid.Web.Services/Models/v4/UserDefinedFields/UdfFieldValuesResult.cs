using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.UserDefinedFields
{
	/// <summary>Response containing UDF field values for an entity.</summary>
	public class UdfFieldValuesResult : StandardApiResponseV4Base
	{
		public List<UdfFieldValueResultData> Data { get; set; }
	}

	public class UdfFieldValueResultData
	{
		public string UdfFieldValueId { get; set; }
		public string UdfFieldId { get; set; }
		public string UdfDefinitionId { get; set; }
		public string EntityId { get; set; }
		public int EntityType { get; set; }
		public string Value { get; set; }
	}
}

