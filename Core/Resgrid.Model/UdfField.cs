using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UdfFields")]
	public class UdfField : IEntity
	{
		public string UdfFieldId { get; set; }

		public string UdfDefinitionId { get; set; }

		/// <summary>
		/// Internal machine name (no spaces, used as form field name).
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Human-readable display label shown to users.
		/// </summary>
		public string Label { get; set; }

		/// <summary>
		/// Optional tooltip / help text shown alongside the field.
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Optional placeholder text shown inside the input.
		/// </summary>
		public string Placeholder { get; set; }

		/// <summary>
		/// The data type of this field. See <see cref="UdfFieldDataType"/>.
		/// </summary>
		public int FieldDataType { get; set; }

		public bool IsRequired { get; set; }

		/// <summary>
		/// When true the field is displayed but cannot be edited by users (e.g. populated by integrations).
		/// </summary>
		public bool IsReadOnly { get; set; }

		public string DefaultValue { get; set; }

		/// <summary>
		/// JSON-serialized <see cref="UdfValidationRules"/> describing validation constraints.
		/// </summary>
		public string ValidationRules { get; set; }

		public int SortOrder { get; set; }

		/// <summary>
		/// Optional group / section name used to visually cluster related fields.
		/// </summary>
		public string GroupName { get; set; }

		public bool IsVisibleOnMobile { get; set; }

		public bool IsVisibleOnReports { get; set; }

		/// <summary>
		/// Soft-disable: when false the field is hidden from new forms without being deleted.
		/// </summary>
		public bool IsEnabled { get; set; }

		/// <summary>
		/// Controls which user roles can see and interact with this field.
		/// See <see cref="UdfFieldVisibility"/>.
		/// Everyone=0, DepartmentAndGroupAdmins=1, DepartmentAdminsOnly=2.
		/// </summary>
		public int Visibility { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UdfFieldId; }
			set { UdfFieldId = (string)value; }
		}

		[NotMapped]
		public string TableName => "UdfFields";

		[NotMapped]
		public string IdName => "UdfFieldId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

