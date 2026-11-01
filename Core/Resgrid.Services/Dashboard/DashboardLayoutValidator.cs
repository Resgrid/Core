using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	/// <summary>
	/// Normalizes and validates dashboard layouts (home-dashboard-redesign-plan.md §5.1): known schema version, widget cap,
	/// unique ids, sizes from the widget's allow-list, settings stripped to the allow-list and clamped, single-instance
	/// widgets at most once. Unknown keys are preserved verbatim (never rendered) so a retired widget or a missing
	/// entitlement never destroys a member's layout.
	/// </summary>
	public static class DashboardLayoutValidator
	{
		public static DashboardLayout Deserialize(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				return Upgrade(JsonConvert.DeserializeObject<DashboardLayout>(json));
			}
			catch (Newtonsoft.Json.JsonException)
			{
				return null;
			}
		}

		public static string Serialize(DashboardLayout layout) => JsonConvert.SerializeObject(layout);

		/// <summary>Steps a stored layout up to the current schema (plan §5.1). Version 1 is current; nothing to do yet.</summary>
		public static DashboardLayout Upgrade(DashboardLayout layout)
		{
			if (layout == null)
				return null;

			if (layout.SchemaVersion <= 0)
				layout.SchemaVersion = DashboardLayout.CurrentSchemaVersion;

			return layout;
		}

		/// <summary>Returns a normalized copy, or null when the layout cannot be accepted (wrong schema, too many widgets).</summary>
		public static DashboardLayout Normalize(DashboardLayout layout)
		{
			if (layout == null || layout.SchemaVersion > DashboardLayout.CurrentSchemaVersion)
				return null;

			var widgets = layout.Widgets ?? new List<DashboardLayoutWidget>();
			if (widgets.Count > DashboardLayout.MaxWidgets)
				return null;

			var result = new DashboardLayout
			{
				SchemaVersion = DashboardLayout.CurrentSchemaVersion,
				Density = layout.Density == DashboardDensities.Compact ? DashboardDensities.Compact : DashboardDensities.Comfortable
			};

			var ids = new HashSet<string>(StringComparer.Ordinal);
			var singles = new HashSet<string>(StringComparer.Ordinal);

			foreach (var widget in widgets)
			{
				if (widget == null || string.IsNullOrWhiteSpace(widget.Key) || widget.Key.Length > 100)
					continue;

				var id = NormalizeId(widget.Id);
				if (id == null || !ids.Add(id))
					id = Guid.NewGuid().ToString();
				ids.Add(id);

				var definition = DashboardWidgetCatalog.Find(widget.Key);
				if (definition == null)
				{
					// Preserved-unknown: keep it as stored (plan §5.1), but bounded.
					result.Widgets.Add(new DashboardLayoutWidget
					{
						Id = id,
						Key = widget.Key,
						Size = DashboardWidgetSizes.IsKnown(widget.Size) ? widget.Size : DashboardWidgetSizes.M,
						Settings = CopyUnknownSettings(widget.Settings),
						Collapsed = widget.Collapsed
					});
					continue;
				}

				if (!definition.SupportsMultipleInstances && !singles.Add(definition.Key))
					continue;

				result.Widgets.Add(new DashboardLayoutWidget
				{
					Id = id,
					Key = definition.Key,
					Size = definition.AllowedSizes.Contains(widget.Size) ? widget.Size : definition.DefaultSize,
					Settings = NormalizeSettings(definition, widget.Settings),
					Collapsed = widget.Collapsed
				});
			}

			return result;
		}

		/// <summary>Settings reduced to the widget's allow-list, typed and clamped; missing ones are left out (defaults apply).</summary>
		public static Dictionary<string, object> NormalizeSettings(DashboardWidgetDefinition definition, IDictionary<string, object> settings)
		{
			var result = new Dictionary<string, object>(StringComparer.Ordinal);
			if (definition == null || settings == null)
				return result;

			foreach (var setting in definition.Settings)
			{
				if (!settings.TryGetValue(setting.Name, out var raw) || raw == null)
					continue;

				var value = Coerce(setting, raw);
				if (value != null)
					result[setting.Name] = value;
			}

			return result;
		}

		/// <summary>The effective settings a provider sees: defaults overlaid with the stored values.</summary>
		public static Dictionary<string, object> WithDefaults(DashboardWidgetDefinition definition, IDictionary<string, object> settings)
		{
			var result = new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (var setting in definition.Settings)
				if (setting.Default != null)
					result[setting.Name] = setting.Default;

			foreach (var pair in NormalizeSettings(definition, settings))
				result[pair.Key] = pair.Value;

			return result;
		}

		private static object Coerce(DashboardWidgetSetting setting, object raw)
		{
			raw = Unwrap(raw);

			switch (setting.Type)
			{
				case DashboardSettingTypes.Number:
				{
					if (!TryNumber(raw, out var number))
						return null;

					if (setting.Min.HasValue) number = Math.Max(setting.Min.Value, number);
					if (setting.Max.HasValue) number = Math.Min(setting.Max.Value, number);
					return number;
				}
				case DashboardSettingTypes.Toggle:
					return raw switch
					{
						bool b => b,
						string s when bool.TryParse(s, out var parsed) => parsed,
						_ => null
					};
				case DashboardSettingTypes.Select:
				{
					var text = raw as string ?? (raw is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : null);
					if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
						return null;

					// Fixed options must match; department sources are re-checked by the provider (plan §9.7).
					if (setting.OptionsSource == null && setting.Options.Count > 0 && !setting.Options.Contains(text))
						return null;

					return text;
				}
				case DashboardSettingTypes.MultiSelect:
				{
					var values = ToStrings(raw).Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 100).Distinct(StringComparer.Ordinal);
					if (setting.OptionsSource == null && setting.Options.Count > 0)
						values = values.Where(v => setting.Options.Contains(v));

					return values.Take(50).ToArray();
				}
				case DashboardSettingTypes.Text:
				{
					var text = raw as string;
					if (text == null)
						return null;

					var max = setting.Max ?? 500;
					return text.Length > max ? text.Substring(0, max) : text;
				}
				default:
					return null;
			}
		}

		private static object Unwrap(object raw)
		{
			switch (raw)
			{
				case JValue jValue:
					return jValue.Value;
				case JArray jArray:
					return jArray.Select(t => t.Type == JTokenType.String || t.Type == JTokenType.Integer ? t.ToString() : null).Where(s => s != null).ToArray();
				case JsonElement element:
					return element.ValueKind switch
					{
						JsonValueKind.String => element.GetString(),
						JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
						JsonValueKind.True => true,
						JsonValueKind.False => false,
						JsonValueKind.Array => element.EnumerateArray()
							.Where(e => e.ValueKind == JsonValueKind.String || e.ValueKind == JsonValueKind.Number)
							.Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()).ToArray(),
						_ => null
					};
				default:
					return raw;
			}
		}

		private static bool TryNumber(object raw, out int number)
		{
			switch (raw)
			{
				case int i: number = i; return true;
				case long l: number = (int)Math.Clamp(l, int.MinValue, int.MaxValue); return true;
				case double d when !double.IsNaN(d): number = (int)Math.Clamp(d, int.MinValue, int.MaxValue); return true;
				case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed): number = parsed; return true;
				default: number = 0; return false;
			}
		}

		private static IEnumerable<string> ToStrings(object raw)
		{
			return raw switch
			{
				string s => new[] { s },
				IEnumerable<string> list => list,
				System.Collections.IEnumerable items => items.Cast<object>().Select(o => Convert.ToString(Unwrap(o), CultureInfo.InvariantCulture)),
				_ => Array.Empty<string>()
			};
		}

		private static Dictionary<string, object> CopyUnknownSettings(IDictionary<string, object> settings)
		{
			var result = new Dictionary<string, object>(StringComparer.Ordinal);
			if (settings == null)
				return result;

			foreach (var pair in settings.Take(20))
			{
				var value = Unwrap(pair.Value);
				if (pair.Key != null && pair.Key.Length <= 50 && (value is string s ? s.Length <= 500 : value is bool || value is long || value is int || value is double))
					result[pair.Key] = value;
			}

			return result;
		}

		private static string NormalizeId(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			id = id.Trim();
			return id.Length <= 36 && id.All(c => char.IsLetterOrDigit(c) || c == '-') ? id : null;
		}
	}
}
