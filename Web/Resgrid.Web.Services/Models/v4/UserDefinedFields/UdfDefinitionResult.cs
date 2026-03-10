using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.UserDefinedFields
{
	/// <summary>Response containing the active UDF definition and its fields.</summary>
	public class UdfDefinitionResult : StandardApiResponseV4Base
	{
		public UdfDefinitionResultData Data { get; set; }
	}

	public class UdfDefinitionResultData
	{
		public string UdfDefinitionId { get; set; }
		public int DepartmentId { get; set; }
		public int EntityType { get; set; }
		public int Version { get; set; }
		public bool IsActive { get; set; }
		public List<UdfFieldResultData> Fields { get; set; }
	}

	public class UdfFieldResultData
	{
		public string UdfFieldId { get; set; }
		public string UdfDefinitionId { get; set; }
		public string Name { get; set; }
		public string Label { get; set; }
		public string Description { get; set; }
		public string Placeholder { get; set; }
		public int FieldDataType { get; set; }
		public bool IsRequired { get; set; }
		public bool IsReadOnly { get; set; }
		public string DefaultValue { get; set; }
		public string ValidationRules { get; set; }
		public int SortOrder { get; set; }
		public string GroupName { get; set; }
		public bool IsVisibleOnMobile { get; set; }
		public bool IsVisibleOnReports { get; set; }
		public bool IsEnabled { get; set; }

		/// <summary>
		/// Visibility setting for this field. 0=Everyone, 1=DepartmentAndGroupAdmins, 2=DepartmentAdminsOnly.
		/// </summary>
		public int Visibility { get; set; }
	}
}

