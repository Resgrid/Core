using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	/// <summary>Exact field selections for a reviewed disclosure. A decision may only withhold content, never introduce or broaden it.</summary>
	public static class DisclosureContentPolicy
	{
		public static JObject Prepare(JObject authorizedContent)
		{
			var content = (JObject)authorizedContent.DeepClone();
			void Expand(JToken token, int depth)
			{
				if (depth > 32) throw new ArgumentException("Disclosure content is too deeply nested.");
				if (token is JObject obj)
					foreach (var property in obj.Properties().ToList())
					{
						if (property.Value.Type == JTokenType.String && property.Name.EndsWith("Json", StringComparison.Ordinal))
						{
							var text = (string)property.Value;
							if (text?.TrimStart().StartsWith("{") == true || text?.TrimStart().StartsWith("[") == true)
								try { property.Value = JToken.Parse(text); } catch (JsonException) { }
						}
						Expand(property.Value, depth + 1);
					}
				else if (token is JArray array) foreach (var item in array) Expand(item, depth + 1);
			}
			Expand(content, 0); return content;
		}

		public static List<RmsDisclosureFieldValue> Fields(JObject content)
		{
			var fields = new List<RmsDisclosureFieldValue>();
			void Walk(JToken token, string path)
			{
				if (token is JObject obj) foreach (var p in obj.Properties()) Walk(p.Value, path + "/" + Escape(p.Name));
				else if (token is JArray array) for (var i = 0; i < array.Count; i++) Walk(array[i], path + "/" + i);
				else if (token.Type != JTokenType.Null) fields.Add(new RmsDisclosureFieldValue { Path = path, Value = token.ToString() });
			}
			Walk(content, ""); return fields;
		}

		public static void Apply(JObject content, string recordId, IEnumerable<RmsDisclosureFieldDecision> decisions, List<RmsRedactionEntry> log)
		{
			var selected = (decisions ?? Enumerable.Empty<RmsDisclosureFieldDecision>()).Where(d => d.Withhold).ToList();
			if (selected.Select(d => d.Path).Distinct(StringComparer.Ordinal).Count() != selected.Count) throw new ArgumentException("A field has duplicate redaction decisions.");
			// Resolve and validate every decision before changing anything; a stale path cannot leave a partial redaction.
			var resolved = selected.Select(d =>
			{
				if (string.IsNullOrWhiteSpace(d.Authority) || string.IsNullOrWhiteSpace(d.Basis)) throw new ArgumentException("Record the applicable authority and reason for each redaction.");
				var token = Resolve(content, d.Path) ?? throw new ArgumentException("A selected field no longer exists. Reload the reviewed revision.");
				return (Decision: d, Token: token);
			}).ToList();
			foreach (var entry in resolved.OrderByDescending(e => e.Decision.Path.Length))
			{
				entry.Token.Replace(new JValue("[WITHHELD]"));
				log.Add(new RmsRedactionEntry { RecordId = recordId, Section = "Record", Field = entry.Decision.Path, Authority = entry.Decision.Authority.Trim(), Basis = entry.Decision.Basis.Trim() });
			}
		}

		private static JToken Resolve(JToken root, string path)
		{
			if (string.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.Length > 2048) throw new ArgumentException("A redaction must name an exact field path.");
			JToken current = root;
			foreach (var encoded in path.Substring(1).Split('/'))
			{
				for (var i = 0; i < encoded.Length; i++) if (encoded[i] == '~' && (++i >= encoded.Length || encoded[i] != '0' && encoded[i] != '1')) throw new ArgumentException("The redaction path is invalid.");
				var part = encoded.Replace("~1", "/").Replace("~0", "~");
				if (current is JObject obj) current = obj.Property(part, StringComparison.Ordinal)?.Value;
				else if (current is JArray array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < array.Count && part == index.ToString(CultureInfo.InvariantCulture)) current = array[index];
				else return null;
				if (current == null) return null;
			}
			return current;
		}
		private static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");
	}
}
