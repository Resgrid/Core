using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Json.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Providers.Neris
{
	/// <summary>The same pinned schema powers guided controls and complete destination-payload validation.</summary>
	public sealed class NerisContractCatalog
	{
		private static readonly Lazy<NerisContractCatalog> Embedded = new Lazy<NerisContractCatalog>(Load);
		private readonly JObject _schemas;
		private readonly ConcurrentDictionary<string, JsonSchema> _compiled = new ConcurrentDictionary<string, JsonSchema>(StringComparer.Ordinal);
		private NerisContractCatalog(JObject schemas) { _schemas = schemas; }
		public static NerisContractCatalog Instance => Embedded.Value;
		public string SchemasJson => _schemas.ToString(Formatting.None);
		public JObject GetSchema(string name) => _schemas[name]?.DeepClone() as JObject;

		public List<RmsValidationIssue> Validate(string schemaName, string json, int departmentId, string recordId, bool allowPendingIncident = false)
		{
			var issues = new List<RmsValidationIssue>();
			void Add(string path, string keyword)
			{
				var message = keyword switch
				{
					"required" => "Complete the required fields in this section.",
					"additionalProperties" => "This section contains a field that is not supported by the reporting contract.",
					"enum" or "const" => "Choose a supported reporting value.",
					"type" => "Enter a value of the required type.",
					"format" or "pattern" => "Enter a value in the required format.",
					"minItems" => "Add the required entries to this section.",
					"maxItems" or "uniqueItems" => "Remove duplicate or excess entries from this section.",
					_ => "This value does not meet the reporting contract's requirements."
				};
				issues.Add(new RmsValidationIssue { RmsValidationIssueId = Guid.NewGuid().ToString(), DepartmentId = departmentId, RecordId = recordId,
					RuleKey = "neris.schema." + keyword, FieldPath = string.IsNullOrEmpty(path) ? "/" : path, Message = message,
					Severity = (int)RmsValidationSeverity.Error, Source = (int)RmsValidationSource.Local,
					ProfileVersion = NerisValueSetCatalog.Instance.ContractVersion, CreatedOn = DateTime.UtcNow });
			}
			try
			{
				using var instance = JsonDocument.Parse(json);
				var key = allowPendingIncident && schemaName == "IncidentAnalysisPayload" ? "PendingIncidentAnalysisPayload" : schemaName;
				var evaluation = _compiled.GetOrAdd(key, Compile).Evaluate(instance.RootElement,
					new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical, RequireFormatValidation = true });
				if (evaluation.IsValid) return issues;
				FindMissingFields(GetSchema(schemaName), JToken.Parse(json), "", Add, allowPendingIncident);
				void Visit(EvaluationResults result)
				{
					if (result.IsValid) return;
					if (result.Errors != null)
						foreach (var error in result.Errors)
							if (!string.IsNullOrEmpty(error.Key) && !new[] { "required", "properties", "items", "anyOf", "oneOf", "$ref" }.Contains(error.Key))
								Add(result.InstanceLocation.ToString(), error.Key);
					foreach (var detail in result.Details ?? new List<EvaluationResults>()) Visit(detail);
				}
				Visit(evaluation);
				if (issues.Count == 0) Add("/", "contract");
			}
			catch (System.Text.Json.JsonException) { Add("/", "type"); }
			return issues.GroupBy(i => (i.FieldPath, i.RuleKey)).Select(g => g.First()).ToList();
		}

		private void FindMissingFields(JObject schema, JToken value, string path, Action<string, string> add, bool pending, int depth = 0)
		{
			if (schema == null || depth > 40 || value == null || value.Type == JTokenType.Null) return;
			if (schema["$ref"] is JValue reference)
			{
				FindMissingFields(GetSchema(((string)reference).Split('/').Last()), value, path, add, pending, depth + 1);
				return;
			}
			if ((schema["anyOf"] ?? schema["oneOf"]) is JArray alternatives)
			{
				var choices = alternatives.OfType<JObject>().Select(s => s["$ref"] == null ? s : GetSchema(((string)s["$ref"]).Split('/').Last())).Where(s => (string)s["type"] != "null").ToList();
				var discriminator = (string)schema["discriminator"]?["propertyName"] ?? "type";
				var actual = (value as JObject)?[discriminator];
				var chosen = choices.Count == 1 ? choices[0] : choices.FirstOrDefault(s =>
					actual != null && (JToken.DeepEquals(s["properties"]?[discriminator]?["const"], actual) || (s["properties"]?[discriminator]?["enum"] as JArray)?.Any(v => JToken.DeepEquals(v, actual)) == true));
				if (chosen != null) FindMissingFields(chosen, value, path, add, pending, depth + 1);
				return;
			}
			if (value is JObject obj)
			{
				foreach (var field in (schema["required"] as JArray ?? new JArray()).Values<string>())
					if (obj[field] == null && !(pending && path == "/base" && field == "neris_id_incident")) add(path + "/" + field, "required");
				foreach (var property in (schema["properties"] as JObject ?? new JObject()).Properties())
					if (obj[property.Name] != null) FindMissingFields(property.Value as JObject, obj[property.Name], path + "/" + property.Name, add, pending, depth + 1);
			}
			if (value is JArray array)
				for (var i = 0; i < array.Count; i++) FindMissingFields(schema["items"] as JObject, array[i], path + "/" + i, add, pending, depth + 1);
		}

		private JsonSchema Compile(string schemaName)
		{
			var pendingIncident = schemaName == "PendingIncidentAnalysisPayload";
			if (pendingIncident) schemaName = "IncidentAnalysisPayload";
			if (_schemas[schemaName] == null) throw new ArgumentException("Unknown pinned NERIS section.", nameof(schemaName));
			// OpenAPI components become JSON Schema definitions. All references stay inside the embedded document.
			var definitions = JObject.Parse(SchemasJson.Replace("#/components/schemas/", "#/$defs/"));
			// A local analysis may be signed while its parent is awaiting a destination ID. Only this one required
			// field is deferred; submission validation always uses the unmodified contract, including its pattern.
			if (pendingIncident)
				definitions["IncidentAnalysisBasePayload"]["required"] = new JArray(((JArray)definitions["IncidentAnalysisBasePayload"]["required"]).Where(t => (string)t != "neris_id_incident"));
			var schema = new JObject { ["$schema"] = "https://json-schema.org/draft/2020-12/schema", ["$defs"] = definitions, ["$ref"] = "#/$defs/" + schemaName };
			return JsonSchema.FromText(schema.ToString(Formatting.None));
		}

		private static NerisContractCatalog Load()
		{
			using var stream = typeof(NerisContractCatalog).Assembly.GetManifestResourceStream("Resgrid.Providers.Neris.Contract.openapi.json")
				?? throw new InvalidOperationException("The pinned NERIS contract is not embedded.");
			using var reader = new StreamReader(stream);
			var contract = JObject.Parse(reader.ReadToEnd());
			if ((string)contract["info"]?["version"] != NerisValueSetCatalog.Instance.ContractVersion)
				throw new InvalidOperationException("The NERIS schema and value-set versions do not match.");
			return new NerisContractCatalog((JObject)contract["components"]["schemas"]);
		}
	}
}
