using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.UserDefinedFields
{
	/// <summary>
	/// Model for the UDF sample/preview modal. Fields are rendered as disabled form controls
	/// so the user can see exactly how they will look without saving any data.
	/// </summary>
	public sealed class UdfPreviewModel
	{
		public UdfEntityType EntityType { get; init; }
		public string EntityTypeName { get; init; } = string.Empty;
		public List<UdfField> Fields { get; init; } = new();

		/// <summary>Localised title for the preview warning banner.</summary>
		public string PreviewWarningTitle { get; init; } = "Preview Only";

		/// <summary>Localised body text for the preview warning banner.</summary>
		public string PreviewWarningBody { get; init; } = "This is a sample view. No data will be saved.";

		/// <summary>Message shown when there are no enabled fields to preview.</summary>
		public string NoFieldsMessage { get; init; } = "No enabled fields to preview.";
	}


	/// <summary>
	/// Summary row shown on the UDF index page for a single entity type.
	/// </summary>
	public sealed class UdfEntitySummaryModel
	{
		public UdfEntityType EntityType { get; init; }
		public string EntityTypeName { get; init; } = string.Empty;
		public UdfDefinition? ActiveDefinition { get; init; }
		public int FieldCount { get; init; }
	}

	/// <summary>
	/// Index page: list of all entity types and their current UDF status.
	/// </summary>
	public sealed class UdfIndexModel
	{
		public List<UdfEntitySummaryModel> Entities { get; init; } = new();
	}

	/// <summary>
	/// Represents a single field being created or edited in the definition form.
	/// </summary>
	public sealed class UdfFieldFormModel
	{
		/// <summary>Existing field ID; empty for new fields.</summary>
		public string UdfFieldId { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string Label { get; set; } = string.Empty;
		public string? Description { get; set; }
		public string? Placeholder { get; set; }
		public int FieldDataType { get; set; }
		public bool IsRequired { get; set; }
		public bool IsReadOnly { get; set; }
		public bool IsEnabled { get; set; } = true;
		public bool IsVisibleOnMobile { get; set; } = true;
		public bool IsVisibleOnReports { get; set; } = true;

		/// <summary>
		/// Controls which roles can see this field. See <see cref="UdfFieldVisibility"/>.
		/// Everyone=0, DepartmentAndGroupAdmins=1, DepartmentAdminsOnly=2.
		/// </summary>
		public int Visibility { get; set; } = (int)UdfFieldVisibility.Everyone;

		public string? DefaultValue { get; set; }
		public string? GroupName { get; set; }
		public int SortOrder { get; set; }

		// Validation rules (flat, serialized to UdfValidationRules on save)
		public int? MinLength { get; set; }
		public int? MaxLength { get; set; }
		public decimal? MinValue { get; set; }
		public decimal? MaxValue { get; set; }
		public string? Regex { get; set; }
		public string? RegexErrorMessage { get; set; }

		/// <summary>
		/// Options for Dropdown / MultiSelect expressed as "key=Label" lines (one per line).
		/// </summary>
		public string? DropdownOptionsRaw { get; set; }
	}

	/// <summary>
	/// Edit/New definition page model.
	/// </summary>
	public sealed class UdfDefinitionEditModel
	{
		public UdfEntityType EntityType { get; set; }
		public string EntityTypeName { get; set; } = string.Empty;

		/// <summary>Existing definition ID; empty when creating a brand-new definition.</summary>
		public string? ExistingDefinitionId { get; set; }

		public List<UdfFieldFormModel> Fields { get; set; } = new();
	}
}

