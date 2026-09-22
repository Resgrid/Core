using Resgrid.Framework;

namespace Resgrid.Web.Helpers
{
	public sealed class JsonInputHelp
	{
		public string Field { get; private set; }
		public string Example { get; private set; }
		public string Schema { get; private set; }
		public bool Optional { get; private set; }

		public static JsonInputHelp For<T>(string field, string example, bool optional = false, string schema = null)
			=> new JsonInputHelp { Field = field, Example = example, Optional = optional, Schema = schema ?? JsonInput.Schema<T>() };

		public static string RateMultipliersSchema()
		{
			var properties = new Newtonsoft.Json.Linq.JObject();
			foreach (var code in System.Enum.GetNames<Resgrid.Model.Workforce.PayCodes>())
				properties[code] = new Newtonsoft.Json.Linq.JObject { ["type"] = "number", ["minimum"] = 0 };
			return new Newtonsoft.Json.Linq.JObject
			{
				["$schema"] = "https://json-schema.org/draft/2020-12/schema",
				["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false
			}.ToString(Newtonsoft.Json.Formatting.Indented);
		}
	}
}
