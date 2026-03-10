using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Pure validation functions for User Defined Field values.
	/// Used by the service layer, API controllers, and the rendering service to emit
	/// matching client-side validation attributes.
	/// </summary>
	public static class UdfValidationHelper
	{
		/// <summary>
		/// Regex that a UDF machine name must satisfy:
		/// starts with a letter or underscore, followed by letters, digits, or underscores only.
		/// </summary>
		public static readonly System.Text.RegularExpressions.Regex MachineNameRegex =
			new System.Text.RegularExpressions.Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$",
				System.Text.RegularExpressions.RegexOptions.Compiled);

		/// <summary>
		/// Returns true when <paramref name="name"/> is a valid UDF machine name.
		/// </summary>
		public static bool IsValidMachineName(string name) =>
			!string.IsNullOrWhiteSpace(name) && MachineNameRegex.IsMatch(name);

		/// <summary>
		/// Validates that every field in <paramref name="fields"/> has a valid, unique machine name
		/// (case-insensitive).  Returns a list of error messages; empty means all names are valid.
		/// </summary>
		public static List<string> ValidateFieldNamesUnique(IEnumerable<UdfField> fields)
		{
			var errors  = new List<string>();
			var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var field in fields ?? Enumerable.Empty<UdfField>())
			{
				var name = field.Name?.Trim() ?? string.Empty;

				if (!IsValidMachineName(name))
				{
					errors.Add(
						$"'{name}' is not a valid machine name. Use letters, digits, and underscores only, starting with a letter or underscore.");
					continue;
				}

				if (!seen.Add(name))
					duplicates.Add(name);
			}

			foreach (var dup in duplicates)
				errors.Add($"Machine name '{dup}' is used more than once. Field machine names must be unique within an entity type.");

			return errors;
		}
		/// </summary>
		/// <param name="field">The UDF field definition containing type and rules.</param>
		/// <param name="value">The raw string value to validate.</param>
		/// <returns>A list of error messages. Empty list means the value is valid.</returns>
		public static List<string> ValidateFieldValue(UdfField field, string value)
		{
			var errors = new List<string>();

			if (field == null)
				return errors;

			var isEmpty = string.IsNullOrWhiteSpace(value);

			// Required check
			if (field.IsRequired && isEmpty)
			{
				errors.Add(string.IsNullOrWhiteSpace(field.ValidationRules)
					? $"{field.Label} is required."
					: GetCustomOrDefault(field.ValidationRules, $"{field.Label} is required."));
				return errors; // No point validating further if value is missing
			}

			if (isEmpty)
				return errors; // Not required and empty — valid

			// Parse validation rules if present
			UdfValidationRules rules = null;
			if (!string.IsNullOrWhiteSpace(field.ValidationRules))
			{
				try { rules = JsonConvert.DeserializeObject<UdfValidationRules>(field.ValidationRules); }
				catch { /* Malformed rules JSON — skip rule validation */ }
			}

			var dataType = (UdfFieldDataType)field.FieldDataType;

			switch (dataType)
			{
				case UdfFieldDataType.Text:
					ValidateTextRules(field.Label, value, rules, errors);
					break;

				case UdfFieldDataType.Number:
					if (!long.TryParse(value, out var longVal))
						errors.Add($"{field.Label} must be a whole number.");
					else if (rules != null)
						ValidateNumericRange(field.Label, (decimal)longVal, rules, errors);
					break;

				case UdfFieldDataType.Decimal:
					if (!decimal.TryParse(value, System.Globalization.NumberStyles.Any,
						System.Globalization.CultureInfo.InvariantCulture, out var decVal))
						errors.Add($"{field.Label} must be a valid decimal number.");
					else if (rules != null)
						ValidateNumericRange(field.Label, decVal, rules, errors);
					break;

				case UdfFieldDataType.Boolean:
					var lower = value.ToLowerInvariant();
					if (lower != "true" && lower != "false" && lower != "1" && lower != "0" &&
						lower != "yes" && lower != "no")
						errors.Add($"{field.Label} must be a boolean value (true/false).");
					break;

				case UdfFieldDataType.Date:
					if (!DateTime.TryParse(value, out _))
						errors.Add($"{field.Label} must be a valid date.");
					break;

				case UdfFieldDataType.DateTime:
					if (!DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
						errors.Add($"{field.Label} must be a valid date and time.");
					break;

				case UdfFieldDataType.Email:
					if (!IsValidEmail(value))
						errors.Add($"{field.Label} must be a valid email address.");
					else if (rules != null)
						ValidateTextRules(field.Label, value, rules, errors);
					break;

				case UdfFieldDataType.Phone:
					if (!IsValidPhone(value))
						errors.Add($"{field.Label} must be a valid phone number.");
					else if (rules != null)
						ValidateTextRules(field.Label, value, rules, errors);
					break;

				case UdfFieldDataType.Url:
					if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
						(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
						errors.Add($"{field.Label} must be a valid URL (http or https).");
					else if (rules != null)
						ValidateTextRules(field.Label, value, rules, errors);
					break;

				case UdfFieldDataType.Dropdown:
					if (rules?.Options != null && rules.Options.Count > 0)
					{
						if (!rules.Options.Any(o => o.Key == value))
							errors.Add(rules.CustomErrorMessage ?? $"{field.Label} must be one of the allowed options.");
					}
					break;

				case UdfFieldDataType.MultiSelect:
					if (rules?.Options != null && rules.Options.Count > 0)
					{
						var selected = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
							.Select(v => v.Trim()).ToList();
						var validKeys = rules.Options.Select(o => o.Key).ToHashSet();
						var invalid = selected.Where(s => !validKeys.Contains(s)).ToList();
						if (invalid.Any())
							errors.Add(rules.CustomErrorMessage ??
								$"{field.Label} contains invalid options: {string.Join(", ", invalid)}.");
					}
					break;
			}

			// Additional regex check (applies on top of type-specific checks)
			if (rules?.Regex != null && errors.Count == 0)
			{
				try
				{
					if (!Regex.IsMatch(value, rules.Regex, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
						errors.Add(rules.RegexErrorMessage ?? $"{field.Label} does not match the required format.");
				}
				catch (RegexMatchTimeoutException)
				{
					errors.Add(rules.RegexErrorMessage ?? $"{field.Label} validation timed out; the value could not be validated against the required format.");
				}
				catch (ArgumentException)
				{
					errors.Add(rules.RegexErrorMessage ?? $"{field.Label} could not be validated due to an invalid pattern configuration.");
				}
			}

			return errors;
		}

		/// <summary>
		/// Validates a list of field values against their corresponding field definitions.
		/// </summary>
		public static Dictionary<string, List<string>> ValidateAllFieldValues(
			IEnumerable<UdfField> fields,
			IEnumerable<UdfFieldValue> values)
		{
			var errors = new Dictionary<string, List<string>>();
			var valueMap = values?
				.GroupBy(v => v.UdfFieldId)
				.ToDictionary(g => g.Key, g => g.Last().Value)
				?? new Dictionary<string, string>();

			foreach (var field in fields ?? Enumerable.Empty<UdfField>())
			{
				valueMap.TryGetValue(field.UdfFieldId, out var val);
				var fieldErrors = ValidateFieldValue(field, val);
				if (fieldErrors.Count > 0)
					errors[field.UdfFieldId] = fieldErrors;
			}

			return errors;
		}

		/// <summary>
		/// Returns HTML5 input attributes as a dictionary for the given field, based on its
		/// data type and validation rules. Used by <see cref="IUdfRenderingService"/> to emit
		/// matching client-side validation attributes.
		/// </summary>
		public static Dictionary<string, string> GetHtmlValidationAttributes(UdfField field)
		{
			var attrs = new Dictionary<string, string>();

			if (field.IsRequired)
				attrs["required"] = "required";

			if (field.IsReadOnly)
				attrs["readonly"] = "readonly";

			UdfValidationRules rules = null;
			if (!string.IsNullOrWhiteSpace(field.ValidationRules))
			{
				try { rules = JsonConvert.DeserializeObject<UdfValidationRules>(field.ValidationRules); }
				catch { /* skip */ }
			}

			if (rules == null) return attrs;

			if (rules.MinLength.HasValue) attrs["minlength"] = rules.MinLength.Value.ToString();
			if (rules.MaxLength.HasValue) attrs["maxlength"] = rules.MaxLength.Value.ToString();
			if (rules.MinValue.HasValue) attrs["min"] = rules.MinValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (rules.MaxValue.HasValue) attrs["max"] = rules.MaxValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (!string.IsNullOrWhiteSpace(rules.Regex)) attrs["pattern"] = rules.Regex;

			return attrs;
		}

		// ── Private helpers ──────────────────────────────────────────────────────

		private static void ValidateTextRules(string label, string value, UdfValidationRules rules, List<string> errors)
		{
			if (rules == null) return;

			if (rules.MinLength.HasValue && value.Length < rules.MinLength.Value)
				errors.Add(rules.CustomErrorMessage ?? $"{label} must be at least {rules.MinLength.Value} characters.");

			if (rules.MaxLength.HasValue && value.Length > rules.MaxLength.Value)
				errors.Add(rules.CustomErrorMessage ?? $"{label} must not exceed {rules.MaxLength.Value} characters.");
		}

		private static void ValidateNumericRange(string label, decimal value, UdfValidationRules rules, List<string> errors)
		{
			if (rules.MinValue.HasValue && value < rules.MinValue.Value)
				errors.Add(rules.CustomErrorMessage ?? $"{label} must be at least {rules.MinValue.Value}.");

			if (rules.MaxValue.HasValue && value > rules.MaxValue.Value)
				errors.Add(rules.CustomErrorMessage ?? $"{label} must not exceed {rules.MaxValue.Value}.");
		}

		private static bool IsValidEmail(string value)
		{
			try
			{
				var addr = new System.Net.Mail.MailAddress(value);
				return addr.Address == value;
			}
			catch { return false; }
		}

		private static bool IsValidPhone(string value)
		{
			// Allows digits, spaces, dashes, dots, parentheses, and leading +
			return Regex.IsMatch(value, @"^\+?[\d\s\-\.\(\)]{7,20}$");
		}

		private static string GetCustomOrDefault(string rulesJson, string defaultMessage)
		{
			try
			{
				var rules = JsonConvert.DeserializeObject<UdfValidationRules>(rulesJson);
				return rules?.CustomErrorMessage ?? defaultMessage;
			}
			catch { return defaultMessage; }
		}
	}
}

