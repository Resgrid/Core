using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.UserDefinedFields
{
	/// <summary>Input for creating or updating a UDF definition (creates a new version).</summary>
	public class SaveUdfDefinitionInput
	{
		/// <summary>Entity type: Call=0, Personnel=1, Unit=2, Contact=3</summary>
		[Required]
		public int EntityType { get; set; }

		[Required]
		public List<UdfFieldInput> Fields { get; set; }
	}

	public class UdfFieldInput
	{
		/// <summary>Leave empty to create a new field; provide existing ID to carry it forward.</summary>
		public string UdfFieldId { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Label { get; set; }

		public string Description { get; set; }
		public string Placeholder { get; set; }

		/// <summary>See UdfFieldDataType enum: Text=0, Number=1, Decimal=2, Boolean=3, Date=4, DateTime=5, Dropdown=6, MultiSelect=7, Email=8, Phone=9, Url=10, ComboBox=11 (options are suggestions; a value naming an option by key or label is stored as its key, anything else as free text)</summary>
		[Required]
		public int FieldDataType { get; set; }

		public bool IsRequired { get; set; }
		public bool IsReadOnly { get; set; }
		public string DefaultValue { get; set; }

		/// <summary>JSON-serialized UdfValidationRules.</summary>
		public string ValidationRules { get; set; }

		public int SortOrder { get; set; }
		public string GroupName { get; set; }
		public bool IsVisibleOnMobile { get; set; } = true;
		public bool IsVisibleOnReports { get; set; } = true;
		public bool IsEnabled { get; set; } = true;

		/// <summary>
		/// Call fields: Protected Workflows release sensitivity (0 none, 1 restricted, 2 42 CFR Part 2). Omit it to keep the
		/// current field's value (matched by name).
		/// </summary>
		public int? Sensitivity { get; set; }

		/// <summary>
		/// Visibility setting for this field. 0=Everyone, 1=DepartmentAndGroupAdmins, 2=DepartmentAdminsOnly.
		/// </summary>
		public int Visibility { get; set; } = 0;
	}
}

