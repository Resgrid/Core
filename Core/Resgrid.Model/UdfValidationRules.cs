using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Strongly-typed representation of the JSON validation rules stored on a <see cref="UdfField"/>.
	/// </summary>
	public class UdfValidationRules
	{
		/// <summary>Minimum character length for Text / Email / Phone / Url fields.</summary>
		public int? MinLength { get; set; }

		/// <summary>Maximum character length for Text / Email / Phone / Url fields.</summary>
		public int? MaxLength { get; set; }

		/// <summary>Regular expression pattern the value must match.</summary>
		public string Regex { get; set; }

		/// <summary>Error message shown when the regex does not match.</summary>
		public string RegexErrorMessage { get; set; }

		/// <summary>Minimum value for Number / Decimal fields.</summary>
		public decimal? MinValue { get; set; }

		/// <summary>Maximum value for Number / Decimal fields.</summary>
		public decimal? MaxValue { get; set; }

		/// <summary>
		/// Allowed options for Dropdown and MultiSelect fields.
		/// The Key is stored in <see cref="UdfFieldValue.Value"/>; the Label is shown to users.
		/// </summary>
		public List<UdfDropdownOption> Options { get; set; } = new List<UdfDropdownOption>();

		/// <summary>Overrides the default validation error message.</summary>
		public string CustomErrorMessage { get; set; }
	}

	/// <summary>
	/// A single option in a Dropdown or MultiSelect UDF field.
	/// </summary>
	public class UdfDropdownOption
	{
		public string Key { get; set; }
		public string Label { get; set; }
	}
}

