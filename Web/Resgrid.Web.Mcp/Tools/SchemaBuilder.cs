using System.Collections.Generic;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Helper class for building MCP tool schemas
	/// </summary>
	public static class SchemaBuilder
	{
		public static Dictionary<string, object> BuildObjectSchema(
			Dictionary<string, PropertySchema> properties,
			string[] required = null)
		{
			var schema = new Dictionary<string, object>
			{
				["type"] = "object",
				["properties"] = BuildProperties(properties)
			};

			if (required != null && required.Length > 0)
			{
				schema["required"] = required;
			}

			return schema;
		}

		private static Dictionary<string, object> BuildProperties(Dictionary<string, PropertySchema> properties)
		{
			var props = new Dictionary<string, object>();
			foreach (var kvp in properties)
			{
				props[kvp.Key] = new Dictionary<string, object>
				{
					["type"] = kvp.Value.Type,
					["description"] = kvp.Value.Description
				};

				if (kvp.Value.Items != null)
				{
					props[kvp.Key] = new Dictionary<string, object>
					{
						["type"] = kvp.Value.Type,
						["items"] = new Dictionary<string, object> { ["type"] = kvp.Value.Items },
						["description"] = kvp.Value.Description
					};
				}
			}
			return props;
		}

		public sealed class PropertySchema
		{
			public string Type { get; set; }
			public string Description { get; set; }
			public string Items { get; set; } // For array types
		}
	}
}

