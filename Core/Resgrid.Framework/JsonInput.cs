using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Text.Json;

namespace Resgrid.Framework
{
	/// <summary>Strict validation for JSON supplied by a person, before Json.NET can coerce field types.</summary>
	public static class JsonInput
	{
		private static readonly DefaultContractResolver Contracts = new DefaultContractResolver();
		public const int MaximumLength = 2 * 1024 * 1024;

		public static T Read<T>(string json, string field = "JSON")
		{
			if (string.IsNullOrWhiteSpace(json)) throw new JsonInputException(field + ": enter JSON using the example shown on this form.");
			if (json.Length > MaximumLength) throw new JsonInputException(field + ": reduce the JSON to less than 2 MB.");
			try
			{
				using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
				var errors = new List<string>();
				Validate(document.RootElement, typeof(T), "$", errors, false);
				if (errors.Count > 0) throw new JsonInputException(field + ": " + string.Join(" ", errors));
				return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings { MaxDepth = 64, TypeNameHandling = TypeNameHandling.None, DateParseHandling = DateParseHandling.None });
			}
			catch (System.Text.Json.JsonException ex)
			{
				throw new JsonInputException($"{field}: invalid JSON at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}. Check double quotes, commas, brackets and braces; comments and trailing commas are not allowed.");
			}
			catch (Newtonsoft.Json.JsonException ex)
			{
				var path = (ex as JsonSerializationException)?.Path;
				throw new JsonInputException($"{field}: {path ?? "$"} cannot be read. Use the field type and format shown in the schema.");
			}
		}

		private static void Validate(JsonElement value, Type type, string path, List<string> errors, bool allowNull)
		{
			if (errors.Count >= 20) return;
			var underlying = Nullable.GetUnderlyingType(type);
			if (value.ValueKind == JsonValueKind.Null)
			{
				if (!allowNull && underlying == null) errors.Add(path + ": null is not allowed; supply a value of the documented type.");
				return;
			}
			type = underlying ?? type;
			var contract = Contracts.ResolveContract(type);
			if (contract is JsonDictionaryContract dictionary)
			{
				if (!Expect(value, JsonValueKind.Object, path, "an object { ... }", errors)) return;
				var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var entry in value.EnumerateObject())
				{
					if (errors.Count >= 20) break;
					var entryPath = path + "[" + JsonConvert.ToString(entry.Name) + "]";
					if (!names.Add(entry.Name)) errors.Add(entryPath + ": duplicate field; keep only one value.");
					if (dictionary.DictionaryKeyType == typeof(int) && (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var key) || key < 1))
						errors.Add(entryPath + ": use a positive whole-number version as the key, for example \"1\".");
					Validate(entry.Value, dictionary.DictionaryValueType, entryPath, errors, false);
				}
			}
			else if (contract is JsonArrayContract array)
			{
				if (!Expect(value, JsonValueKind.Array, path, "an array [ ... ]", errors)) return;
				var index = 0;
				foreach (var item in value.EnumerateArray()) Validate(item, array.CollectionItemType, path + "[" + index++ + "]", errors, false);
			}
			else if (contract is JsonObjectContract obj)
			{
				if (!Expect(value, JsonValueKind.Object, path, "an object { ... }", errors)) return;
				var properties = obj.Properties.Where(p => !p.Ignored && p.Writable).ToList();
				var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var entry in value.EnumerateObject())
				{
					if (errors.Count >= 20) break;
					if (!names.Add(entry.Name)) errors.Add(path + "." + entry.Name + ": duplicate field; keep only one value.");
					var property = properties.FirstOrDefault(p => string.Equals(p.PropertyName, entry.Name, StringComparison.OrdinalIgnoreCase));
					if (property == null) { errors.Add(path + "." + entry.Name + ": unknown field; remove it or use a field name from the schema."); continue; }
					Validate(entry.Value, property.PropertyType, path + "." + entry.Name, errors, AllowsNull(property));
				}
				foreach (var property in properties)
				{
					var required = property.AttributeProvider.GetAttributes(typeof(RequiredAttribute), true).Any();
					if (required && !value.EnumerateObject().Any(p => string.Equals(p.Name, property.PropertyName, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null && (p.Value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(p.Value.GetString()))))
						errors.Add(path + "." + property.PropertyName + ": required; supply a non-empty value.");
				}
			}
			else if (type.IsEnum)
			{
				var valid = value.ValueKind == JsonValueKind.String && Enum.GetNames(type).Contains(value.GetString());
				if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) valid = Enum.IsDefined(type, number);
				if (!valid) errors.Add(path + ": choose one of " + string.Join(", ", Enum.GetNames(type)) + " (or its integer value in the schema).");
			}
			else if (type == typeof(string)) Expect(value, JsonValueKind.String, path, "a string in double quotes", errors);
			else if (type == typeof(bool))
			{
				if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) errors.Add(path + ": expected true or false, without quotes.");
			}
			else if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
			{
				if (value.ValueKind != JsonValueKind.String || !value.TryGetDateTime(out _)) errors.Add(path + ": use an ISO date such as \"2026-09-22\" or date/time such as \"2026-09-22T14:30:00Z\".");
			}
			else if (type == typeof(int) || type == typeof(long))
			{
				if (value.ValueKind != JsonValueKind.Number || (type == typeof(int) ? !value.TryGetInt32(out _) : !value.TryGetInt64(out _))) errors.Add(path + ": expected a whole number within " + (type == typeof(int) ? "-2147483648 to 2147483647" : "the 64-bit integer range") + ", without quotes or decimals.");
			}
			else if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
			{
				if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out _)) errors.Add(path + ": expected a number such as 12.50, without quotes, currency symbols or thousands separators.");
			}
			else throw new InvalidOperationException("Unsupported JSON input type: " + type.FullName);
		}

		private static bool AllowsNull(Newtonsoft.Json.Serialization.JsonProperty property) => !property.PropertyType.IsValueType
			&& property.Required != Required.DisallowNull && property.Required != Required.Always
			&& !property.AttributeProvider.GetAttributes(typeof(RequiredAttribute), true).Any();

		private static bool Expect(JsonElement value, JsonValueKind kind, string path, string expected, List<string> errors)
		{
			if (value.ValueKind == kind) return true;
			errors.Add(path + ": expected " + expected + "; received " + value.ValueKind.ToString().ToLowerInvariant() + ".");
			return false;
		}

		/// <summary>Documentation uses the same CLR contracts as validation, including recursive record rules.</summary>
		public static string Schema<T>()
		{
			var definitions = new JObject();
			var schema = Describe(typeof(T), definitions, false);
			schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
			schema["$defs"] = definitions;
			return schema.ToString(Formatting.Indented);
		}

		private static JObject Describe(Type type, JObject definitions, bool allowNull)
		{
			var underlying = Nullable.GetUnderlyingType(type);
			if (underlying != null || allowNull) return new JObject { ["anyOf"] = new JArray(Describe(underlying ?? type, definitions, false), new JObject { ["type"] = "null" }) };
			var contract = Contracts.ResolveContract(type);
			if (contract is JsonDictionaryContract dictionary)
			{
				var result = new JObject { ["type"] = "object", ["additionalProperties"] = Describe(dictionary.DictionaryValueType, definitions, false) };
				if (dictionary.DictionaryKeyType == typeof(int)) result["propertyNames"] = new JObject { ["pattern"] = "^[1-9][0-9]*$" };
				return result;
			}
			if (contract is JsonArrayContract array) return new JObject { ["type"] = "array", ["items"] = Describe(array.CollectionItemType, definitions, false) };
			if (contract is JsonObjectContract obj)
			{
				var key = type.Name;
				if (definitions[key] == null)
				{
					var properties = new JObject();
					var required = new JArray();
					definitions[key] = new JObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
					foreach (var property in obj.Properties.Where(p => !p.Ignored && p.Writable))
					{
						var isRequired = property.AttributeProvider.GetAttributes(typeof(RequiredAttribute), true).Any();
						properties[property.PropertyName] = Describe(property.PropertyType, definitions, AllowsNull(property));
						if (isRequired) { required.Add(property.PropertyName); if (property.PropertyType == typeof(string)) properties[property.PropertyName]["minLength"] = 1; }
					}
				}
				return new JObject { ["$ref"] = "#/$defs/" + key };
			}
			if (type.IsEnum) return new JObject { ["enum"] = new JArray(Enum.GetNames(type).Cast<object>().Concat(Enum.GetValues(type).Cast<object>().Select(v => (object)Convert.ToInt32(v)))) };
			if (type == typeof(string)) return new JObject { ["type"] = "string" };
			if (type == typeof(bool)) return new JObject { ["type"] = "boolean" };
			if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return new JObject { ["type"] = "string", ["description"] = "ISO 8601 date or date/time, e.g. 2026-09-22 or 2026-09-22T14:30:00Z" };
			if (type == typeof(int) || type == typeof(long)) return new JObject { ["type"] = "integer", ["minimum"] = type == typeof(int) ? int.MinValue : long.MinValue, ["maximum"] = type == typeof(int) ? int.MaxValue : long.MaxValue };
			return new JObject { ["type"] = "number" };
		}
	}

	public sealed class JsonInputException : InvalidOperationException
	{
		public JsonInputException(string message) : base(message) { }
	}
}
