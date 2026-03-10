using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Generates UI representations of UDF fields for Razor views and React Native apps.
	/// </summary>
	public interface IUdfRenderingService
	{
		/// <summary>
		/// Generates Bootstrap-compatible HTML form inputs with HTML5 client-side validation
		/// attributes for use in Razor views via Html.Raw().
		/// Fields are grouped by GroupName into fieldset sections.
		/// </summary>
		string GenerateHtmlFormFields(UdfDefinition definition, List<UdfField> fields, List<UdfFieldValue> existingValues);

		/// <summary>
		/// Generates a JSON schema string describing each field, its type, validation rules,
		/// current value, and dropdown options. Consumed by React Native apps to dynamically
		/// render and validate form controls.
		/// </summary>
		string GenerateReactNativeSchema(UdfDefinition definition, List<UdfField> fields, List<UdfFieldValue> existingValues);

		/// <summary>
		/// Generates read-only HTML for display/detail pages (e.g. ViewCall).
		/// </summary>
		string GenerateReadOnlyHtml(UdfDefinition definition, List<UdfField> fields, List<UdfFieldValue> values);
	}
}

