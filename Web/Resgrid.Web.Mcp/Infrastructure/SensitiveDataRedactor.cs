using System.Text.Json;
using System.Text.Json.Nodes;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Provides functionality to redact sensitive fields from JSON payloads before logging
	/// </summary>
	public static class SensitiveDataRedactor
	{
		private static readonly string[] SensitiveFields = new[]
		{
			"password",
			"username",
			"token",
			"ssn",
			"email",
			"apikey",
			"api_key",
			"secret",
			"authorization",
			"auth",
			"credentials",
			"credit_card",
			"creditcard",
			"cvv",
			"pin"
		};

		private const string RedactedValue = "***REDACTED***";

		/// <summary>
		/// Redacts sensitive fields from a JSON string
		/// </summary>
		/// <param name="jsonString">The JSON string to redact</param>
		/// <returns>A redacted version of the JSON string, or metadata if parsing fails</returns>
		public static string RedactSensitiveFields(string jsonString)
		{
			if (string.IsNullOrWhiteSpace(jsonString))
			{
				return string.Empty;
			}

			try
			{
				var jsonNode = JsonNode.Parse(jsonString);
				if (jsonNode == null)
				{
					return GetMetadataOnly(jsonString);
				}

				RedactNode(jsonNode);
				return jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
			}
			catch (JsonException)
			{
				// If parsing fails, return only metadata
				return GetMetadataOnly(jsonString);
			}
		}

		/// <summary>
		/// Extracts only metadata from a JSON string without sensitive data
		/// </summary>
		/// <param name="jsonString">The JSON string</param>
		/// <returns>A string containing only metadata</returns>
		private static string GetMetadataOnly(string jsonString)
		{
			try
			{
				using var doc = JsonDocument.Parse(jsonString);
				var root = doc.RootElement;

				var metadata = new
				{
					method = root.TryGetProperty("method", out var method) ? method.GetString() : null,
					id = root.TryGetProperty("id", out var id) ? id.ToString() : null,
					jsonrpc = root.TryGetProperty("jsonrpc", out var jsonrpc) ? jsonrpc.GetString() : null,
					hasParams = root.TryGetProperty("params", out _),
					hasResult = root.TryGetProperty("result", out _),
					hasError = root.TryGetProperty("error", out _)
				};

				return JsonSerializer.Serialize(metadata);
			}
			catch
			{
				return $"[Length: {jsonString.Length} bytes]";
			}
		}

		/// <summary>
		/// Recursively redacts sensitive fields in a JSON node
		/// </summary>
		/// <param name="node">The JSON node to redact</param>
		private static void RedactNode(JsonNode node)
		{
			if (node is JsonObject jsonObject)
			{
				// Create a list of properties to avoid modifying while iterating
				var propertiesToProcess = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, JsonNode>>();
				foreach (var property in jsonObject)
				{
					propertiesToProcess.Add(new System.Collections.Generic.KeyValuePair<string, JsonNode>(property.Key, property.Value));
				}

				foreach (var property in propertiesToProcess)
				{
					var propertyName = property.Key.ToLowerInvariant();

					// Check if this property name matches a sensitive field
					var isSensitive = false;
					foreach (var sensitiveField in SensitiveFields)
					{
						if (propertyName.Contains(sensitiveField))
						{
							isSensitive = true;
							break;
						}
					}

					if (isSensitive)
					{
						jsonObject[property.Key] = RedactedValue;
					}
					else if (property.Value != null)
					{
						RedactNode(property.Value);
					}
				}
			}
			else if (node is JsonArray jsonArray)
			{
				for (int i = 0; i < jsonArray.Count; i++)
				{
					var item = jsonArray[i];
					if (item != null)
					{
						RedactNode(item);
					}
				}
			}
		}
	}
}



