using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Rules for <see cref="Call.SubjectIdentifiers"/>: a JSON object of string keys to string values, keys matching
	/// <c>^[a-z0-9_]{1,64}$</c>, values of at most 256 characters, and at most 20 keys. Stored canonically (keys sorted).
	/// </summary>
	public static class CallSubjectIdentifiers
	{
		public const int MaxKeys = 20;
		public const int MaxValueLength = 256;

		// Validation codes (v4 returns them; the UI localizes SubjectIdentifiers_{code}).
		public const string TooManyKeys = "subject_identifiers_too_many_keys";
		public const string InvalidKey = "subject_identifiers_invalid_key";
		public const string ValueTooLong = "subject_identifiers_value_too_long";
		public const string InvalidValue = "subject_identifiers_invalid_value";
		public const string NotAnObject = "subject_identifiers_not_an_object";

		/// <summary>Errors for a candidate set of identifiers; empty when valid.</summary>
		public static List<string> Validate(IDictionary<string, string> identifiers)
		{
			var errors = new List<string>();
			if (identifiers == null)
				return errors;

			if (identifiers.Count > MaxKeys)
				errors.Add(TooManyKeys);

			foreach (var pair in identifiers)
			{
				if (pair.Key == null || !ProtectedStepOptions.SubjectKeyPattern.IsMatch(pair.Key))
					errors.Add(InvalidKey);
				if (string.IsNullOrEmpty(pair.Value) || pair.Value.Any(char.IsControl))
					errors.Add(InvalidValue);
				else if (pair.Value.Length > MaxValueLength)
					errors.Add(ValueTooLong);
			}

			return errors.Distinct(StringComparer.Ordinal).ToList();
		}

		/// <summary>
		/// Parses stored plaintext. Null or empty is an empty set. Anything that is not an object of strings fails:
		/// callers must never fall back to the raw text.
		/// </summary>
		public static bool TryParse(string json, out SortedDictionary<string, string> identifiers)
		{
			identifiers = new SortedDictionary<string, string>(StringComparer.Ordinal);
			if (string.IsNullOrWhiteSpace(json))
				return true;

			try
			{
				using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
				if (JToken.ReadFrom(reader) is not JObject obj || reader.Read())
					return false;

				foreach (var property in obj.Properties())
				{
					if (property.Value.Type != JTokenType.String)
						return false;
					identifiers[property.Name] = (string)property.Value;
				}

				return true;
			}
			catch (JsonException)
			{
				identifiers = new SortedDictionary<string, string>(StringComparer.Ordinal);
				return false;
			}
		}

		/// <summary>Canonical storage form (keys sorted), or null for an empty set.</summary>
		public static string Serialize(IDictionary<string, string> identifiers)
		{
			if (identifiers == null || identifiers.Count == 0)
				return null;

			var obj = new JObject();
			foreach (var pair in identifiers.OrderBy(p => p.Key, StringComparer.Ordinal))
				obj[pair.Key] = pair.Value;
			return obj.ToString(Formatting.None);
		}
	}
}
