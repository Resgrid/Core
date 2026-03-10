using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resgrid.Services
{
	public class UdfRenderingService : IUdfRenderingService
	{
		public string GenerateHtmlFormFields(UdfDefinition definition, List<UdfField> fields, List<UdfFieldValue> existingValues)
		{
			if (definition == null || fields == null || fields.Count == 0)
				return string.Empty;

			var valueMap = (existingValues ?? new List<UdfFieldValue>())
				.ToDictionary(v => v.UdfFieldId, v => v.Value ?? string.Empty);

			var sb = new StringBuilder();
			sb.AppendLine($"<div class=\"udf-section\" data-definition-id=\"{definition.UdfDefinitionId}\" data-entity-type=\"{definition.EntityType}\">");

			var groups = fields
				.Where(f => f.IsEnabled)
				.OrderBy(f => f.SortOrder)
				.GroupBy(f => string.IsNullOrWhiteSpace(f.GroupName) ? string.Empty : f.GroupName);

			foreach (var group in groups)
			{
				if (!string.IsNullOrWhiteSpace(group.Key))
				{
					// Use a <fieldset> to semantically group related fields
					sb.AppendLine($"  <fieldset class=\"udf-group\">");
					sb.AppendLine($"    <legend class=\"udf-group-legend\">{HtmlEncode(group.Key)}</legend>");
				}

				foreach (var field in group)
				{
					valueMap.TryGetValue(field.UdfFieldId, out var currentValue);
					sb.AppendLine(RenderFormField(field, currentValue ?? field.DefaultValue ?? string.Empty));
				}

				if (!string.IsNullOrWhiteSpace(group.Key))
				{
					sb.AppendLine($"  </fieldset>");
				}
			}

			sb.AppendLine("</div>");
			return sb.ToString();
		}

		public string GenerateReactNativeSchema(UdfDefinition definition, List<UdfField> fields, List<UdfFieldValue> existingValues)
		{
			if (definition == null || fields == null || fields.Count == 0)
				return JsonConvert.SerializeObject(new { definitionId = (string)null, entityType = 0, fields = new object[0] });

			var valueMap = (existingValues ?? new List<UdfFieldValue>())
				.ToDictionary(v => v.UdfFieldId, v => v.Value);

			var schemaFields = fields
				.Where(f => f.IsEnabled && f.IsVisibleOnMobile)
				.OrderBy(f => f.SortOrder)
				.Select(f =>
				{
					valueMap.TryGetValue(f.UdfFieldId, out var currentValue);

					UdfValidationRules rules = null;
					if (!string.IsNullOrWhiteSpace(f.ValidationRules))
					{
						try { rules = JsonConvert.DeserializeObject<UdfValidationRules>(f.ValidationRules); }
						catch { /* skip */ }
					}

					return new
					{
						fieldId = f.UdfFieldId,
						name = f.Name,
						label = f.Label,
						description = f.Description,
						placeholder = f.Placeholder,
						type = ((UdfFieldDataType)f.FieldDataType).ToString().ToLowerInvariant(),
						isRequired = f.IsRequired,
						isReadOnly = f.IsReadOnly,
						defaultValue = f.DefaultValue,
						currentValue = currentValue,
						group = f.GroupName,
						sortOrder = f.SortOrder,
						validation = rules == null ? null : new
						{
							minLength = rules.MinLength,
							maxLength = rules.MaxLength,
							regex = rules.Regex,
							regexErrorMessage = rules.RegexErrorMessage,
							minValue = rules.MinValue,
							maxValue = rules.MaxValue,
							customErrorMessage = rules.CustomErrorMessage,
							options = rules.Options?.Select(o => new { key = o.Key, label = o.Label }).ToList()
						}
					};
				})
				.ToList();

			var schema = new
			{
				definitionId = definition.UdfDefinitionId,
				entityType = definition.EntityType,
				version = definition.Version,
				fields = schemaFields
			};

			return JsonConvert.SerializeObject(schema, Formatting.None);
		}

		public string GenerateReadOnlyHtml(UdfDefinition definition, List<UdfField> fields, List<UdfFieldValue> values)
		{
			if (definition == null || fields == null || fields.Count == 0)
				return string.Empty;

			var valueMap = (values ?? new List<UdfFieldValue>())
				.ToDictionary(v => v.UdfFieldId, v => v.Value ?? string.Empty);

			var sb = new StringBuilder();
			sb.AppendLine($"<div class=\"udf-readonly-section\" data-definition-id=\"{definition.UdfDefinitionId}\">");

			var groups = fields
				.Where(f => f.IsEnabled)
				.OrderBy(f => f.SortOrder)
				.GroupBy(f => string.IsNullOrWhiteSpace(f.GroupName) ? string.Empty : f.GroupName);

			foreach (var group in groups)
			{
				if (!string.IsNullOrWhiteSpace(group.Key))
				{
					// Named groups use a panel matching site's ibox style
					sb.AppendLine($"  <div class=\"panel panel-default udf-readonly-group\">");
					sb.AppendLine($"    <div class=\"panel-heading\"><h5 class=\"panel-title\">{HtmlEncode(group.Key)}</h5></div>");
					sb.AppendLine($"    <div class=\"panel-body\">");
					sb.AppendLine($"      <dl class=\"dl-horizontal\">");
				}
				else
				{
					// Ungrouped fields use a dl-horizontal directly, matching ViewCall/ViewPerson style
					sb.AppendLine($"  <dl class=\"dl-horizontal\">");
				}

				foreach (var field in group)
				{
					valueMap.TryGetValue(field.UdfFieldId, out var currentValue);
					var displayValue = GetDisplayValue(field, currentValue);
					sb.AppendLine($"    <dt>{HtmlEncode(field.Label)}:</dt>");
					sb.AppendLine($"    <dd>{HtmlEncode(displayValue)}</dd>");
				}

				if (!string.IsNullOrWhiteSpace(group.Key))
				{
					sb.AppendLine($"      </dl>");
					sb.AppendLine($"    </div>");
					sb.AppendLine($"  </div>");
				}
				else
				{
					sb.AppendLine($"  </dl>");
				}
			}

			sb.AppendLine("</div>");
			return sb.ToString();
		}

		// ── Private helpers ──────────────────────────────────────────────────────

		private static string RenderFormField(UdfField field, string currentValue)
		{
			var attrs = UdfValidationHelper.GetHtmlValidationAttributes(field);
			var attrsHtml = BuildAttrsHtml(attrs);
			var fieldId = $"udf_{field.UdfFieldId}";
			// fieldName must be "udf_{UdfFieldId}" so that the save controllers can extract
			// the field ID via k.Substring(4) when iterating IFormCollection keys.
			var fieldName = fieldId;
			var dataType = (UdfFieldDataType)field.FieldDataType;
			var requiredMark = field.IsRequired ? " <span class=\"text-danger\">*</span>" : "";

			var sb = new StringBuilder();
			// Bootstrap 3 form-horizontal: form-group with col-sm-2 label / col-sm-10 input
			sb.AppendLine($"  <div class=\"form-group udf-field\" data-field-id=\"{field.UdfFieldId}\">");

			if (dataType == UdfFieldDataType.Boolean)
			{
				sb.AppendLine($"    <label class=\"col-sm-2 control-label\">{HtmlEncode(field.Label)}{requiredMark}</label>");
				sb.AppendLine($"    <div class=\"col-sm-10\">");
				sb.AppendLine($"      <div class=\"checkbox checkbox-primary\">");
				var boolChecked = (currentValue == "true" || currentValue == "1" || currentValue?.ToLower() == "yes") ? "checked" : "";
				sb.AppendLine($"        <input type=\"checkbox\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"true\" {boolChecked} {attrsHtml} />");
				sb.AppendLine($"        <input type=\"hidden\" name=\"{fieldName}_exists\" value=\"1\" />");
				sb.AppendLine($"        <label for=\"{fieldId}\"></label>");
				sb.AppendLine($"      </div>");
				if (!string.IsNullOrWhiteSpace(field.Description))
					sb.AppendLine($"      <p class=\"help-block\">{HtmlEncode(field.Description)}</p>");
				sb.AppendLine($"    </div>");
			}
			else
			{
				if (!string.IsNullOrWhiteSpace(field.Description))
					sb.AppendLine($"    <label class=\"col-sm-2 control-label\" for=\"{fieldId}\">{HtmlEncode(field.Label)}{requiredMark} <i class=\"fa fa-info-circle\" title=\"{HtmlEncode(field.Description)}\"></i></label>");
				else
					sb.AppendLine($"    <label class=\"col-sm-2 control-label\" for=\"{fieldId}\">{HtmlEncode(field.Label)}{requiredMark}</label>");

				sb.AppendLine($"    <div class=\"col-sm-10\">");

				switch (dataType)
				{
					case UdfFieldDataType.Dropdown:
						sb.AppendLine($"      <select class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" style=\"width: 200px;\" {attrsHtml}>");
						sb.AppendLine($"        <option value=\"\">{HtmlEncode(field.Placeholder ?? "-- Select --")}</option>");
						var dropRules = ParseRules(field.ValidationRules);
						if (dropRules?.Options != null)
							foreach (var opt in dropRules.Options)
							{
								var sel = opt.Key == currentValue ? "selected" : "";
								sb.AppendLine($"        <option value=\"{HtmlEncode(opt.Key)}\" {sel}>{HtmlEncode(opt.Label)}</option>");
							}
						sb.AppendLine($"      </select>");
						break;

					case UdfFieldDataType.MultiSelect:
						sb.AppendLine($"      <select class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" multiple style=\"width: 100%;\" {attrsHtml}>");
						var msRules = ParseRules(field.ValidationRules);
						var selectedKeys = (currentValue ?? string.Empty).Split(',').Select(v => v.Trim()).ToHashSet();
						if (msRules?.Options != null)
							foreach (var opt in msRules.Options)
							{
								var sel = selectedKeys.Contains(opt.Key) ? "selected" : "";
								sb.AppendLine($"        <option value=\"{HtmlEncode(opt.Key)}\" {sel}>{HtmlEncode(opt.Label)}</option>");
							}
						sb.AppendLine($"      </select>");
						break;

					case UdfFieldDataType.Date:
						sb.AppendLine($"      <input type=\"date\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder)}\" style=\"width: 200px;\" {attrsHtml} />");
						break;

					case UdfFieldDataType.DateTime:
						sb.AppendLine($"      <input type=\"datetime-local\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" style=\"width: 250px;\" {attrsHtml} />");
						break;

					case UdfFieldDataType.Number:
						sb.AppendLine($"      <input type=\"number\" step=\"1\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder)}\" style=\"width: 150px;\" {attrsHtml} />");
						break;

					case UdfFieldDataType.Decimal:
						sb.AppendLine($"      <input type=\"number\" step=\"any\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder)}\" style=\"width: 150px;\" {attrsHtml} />");
						break;

					case UdfFieldDataType.Email:
						sb.AppendLine($"      <input type=\"email\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder ?? "email@example.com")}\" {attrsHtml} />");
						break;

					case UdfFieldDataType.Phone:
						sb.AppendLine($"      <input type=\"tel\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder)}\" style=\"width: 200px;\" {attrsHtml} />");
						break;

					case UdfFieldDataType.Url:
						sb.AppendLine($"      <input type=\"url\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder ?? "https://")}\" {attrsHtml} />");
						break;

					default: // Text
						sb.AppendLine($"      <input type=\"text\" class=\"form-control\" id=\"{fieldId}\" name=\"{fieldName}\" value=\"{HtmlEncode(currentValue)}\" placeholder=\"{HtmlEncode(field.Placeholder)}\" {attrsHtml} />");
						break;
				}

				if (!string.IsNullOrWhiteSpace(field.Description))
					sb.AppendLine($"      <p class=\"help-block\">{HtmlEncode(field.Description)}</p>");

				sb.AppendLine($"    </div>");
			}

			sb.AppendLine($"  </div>");
			return sb.ToString();
		}

		private static string GetDisplayValue(UdfField field, string rawValue)
		{
			if (string.IsNullOrWhiteSpace(rawValue))
				return string.Empty;

			var dataType = (UdfFieldDataType)field.FieldDataType;

			if (dataType == UdfFieldDataType.Boolean)
				return rawValue == "true" || rawValue == "1" || rawValue.ToLower() == "yes" ? "Yes" : "No";

			if (dataType == UdfFieldDataType.Dropdown || dataType == UdfFieldDataType.MultiSelect)
			{
				var rules = ParseRules(field.ValidationRules);
				if (rules?.Options != null)
				{
					var keys = rawValue.Split(',').Select(v => v.Trim()).ToHashSet();
					var labels = rules.Options.Where(o => keys.Contains(o.Key)).Select(o => o.Label);
					return string.Join(", ", labels);
				}
			}

			return rawValue;
		}

		private static string BuildAttrsHtml(Dictionary<string, string> attrs)
		{
			if (attrs == null || attrs.Count == 0) return string.Empty;
			return string.Join(" ", attrs.Select(kv => $"{kv.Key}=\"{HtmlEncode(kv.Value)}\""));
		}

		private static UdfValidationRules ParseRules(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try { return JsonConvert.DeserializeObject<UdfValidationRules>(json); }
			catch { return null; }
		}

		private static string HtmlEncode(string value)
		{
			if (string.IsNullOrEmpty(value)) return string.Empty;
			return System.Net.WebUtility.HtmlEncode(value);
		}
	}
}

