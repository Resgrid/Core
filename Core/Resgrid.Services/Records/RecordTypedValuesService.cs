using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The typed value seam for department definitions (RMS plan section 5.3, RMS-1B/1C). Every posted value is parsed
	/// against the pinned field type into exactly one populated column group of RmsRecordValues; repeating sections
	/// become RmsRecordValueGroups rows; references are validated against the department and carry a server-authored
	/// snapshot. The bounded rule language (equals, sets, empty, ranges, AND/OR) only controls visibility and
	/// requiredness. Locked system definitions never come through here.
	/// </summary>
	public class RecordTypedValuesService : IRecordTypedValuesService
	{
		public const int MaxLongText = 100000;
		public const int MaxShortText = 400;
		public const int MaxRowsPerSection = 500;
		public const int MaxSummaryLength = 120;
		public const string Redacted = "REDACTED";

		private readonly IRmsRecordValuesRepository _values;
		private readonly IRmsRecordValueGroupsRepository _groups;
		private readonly IRmsRecordAttachmentsRepository _attachments;
		private readonly IDepartmentsService _departments;
		private readonly IUnitsService _units;
		private readonly IDepartmentGroupsService _departmentGroups;
		private readonly IContactsService _contacts;
		private readonly ICallsService _calls;
		private readonly IInventoryService _inventory;
		private readonly IRecordsProtectionService _protection;

		public RecordTypedValuesService(IRmsRecordValuesRepository values, IRmsRecordValueGroupsRepository groups, IRmsRecordAttachmentsRepository attachments,
			IDepartmentsService departments, IUnitsService units, IDepartmentGroupsService departmentGroups, IContactsService contacts, ICallsService calls, IInventoryService inventory,
			IRecordsProtectionService protection)
		{
			_values = values;
			_groups = groups;
			_attachments = attachments;
			_departments = departments;
			_units = units;
			_departmentGroups = departmentGroups;
			_contacts = contacts;
			_calls = calls;
			_inventory = inventory;
			_protection = protection;
		}

		// ------------------------------------------------------------------------------------------------
		// Validation and parsing
		// ------------------------------------------------------------------------------------------------

		public async Task<RecordValueValidation> ValidateAsync(int departmentId, RmsRecordDefinitionVersion version, List<RecordValueInput> inputs, bool finalizing)
		{
			var context = new ParseContext(departmentId, null, version?.Schema ?? new RecordDefinitionSchema(), this);
			var parsed = await ParseAllAsync(context, inputs ?? new List<RecordValueInput>(), null);
			if (finalizing)
				ApplyFinalizationRules(context, parsed);
			return new RecordValueValidation { Issues = context.Issues };
		}

		public RecordRuleEvaluation EvaluateRules(RecordDefinitionSchema schema, RecordValueSet values)
		{
			var evaluation = new RecordRuleEvaluation();
			schema = schema ?? new RecordDefinitionSchema();
			values = values ?? new RecordValueSet();
			foreach (var section in schema.Sections)
			{
				// Section rules see scalars only (the validator enforces it), so a section shows or hides as a whole.
				var visible = section.Rules.Where(r => r.Effect == RmsRuleEffect.Show).All(r => Evaluate(r.Condition, schema, values, 0, null));
				if (!visible)
				{
					evaluation.HiddenSectionKeys.Add(section.Key);
					foreach (var field in section.Fields) evaluation.HiddenFieldKeys.Add(field.Key);
					continue;
				}
				if (!section.Repeating)
				{
					foreach (var field in section.Fields)
					{
						if (!field.Rules.Where(r => r.Effect == RmsRuleEffect.Show).All(r => Evaluate(r.Condition, schema, values, 0, null)))
						{
							evaluation.HiddenFieldKeys.Add(field.Key);
							continue;
						}
						if (field.Required || field.RequiredToFinalize || field.Rules.Where(r => r.Effect == RmsRuleEffect.Require).Any(r => Evaluate(r.Condition, schema, values, 0, null)))
							evaluation.RequiredFieldKeys.Add(field.Key);
					}
					continue;
				}

				// Repeating sections evaluate per row: a field rule may reference its own row's cells (per-row rules) and
				// any scalar; the outcome lands on the row so two rows of the same section can differ.
				var rows = values.Section(section.Key)?.Rows ?? new List<RecordValueRow>();
				foreach (var field in section.Fields)
				{
					if (field.Required || field.RequiredToFinalize) evaluation.RequiredFieldKeys.Add(field.Key);
					if (field.Rules.Count == 0) continue;
					if (rows.Count == 0)
					{
						// No rows yet: evaluate against scalars alone so an empty section still renders its default state.
						if (!field.Rules.Where(r => r.Effect == RmsRuleEffect.Show).All(r => Evaluate(r.Condition, schema, values, 0, null))) evaluation.HiddenFieldKeys.Add(field.Key);
						else if (field.Rules.Where(r => r.Effect == RmsRuleEffect.Require).Any(r => Evaluate(r.Condition, schema, values, 0, null))) evaluation.RequiredFieldKeys.Add(field.Key);
						continue;
					}
					foreach (var row in rows)
					{
						var outcome = evaluation.Row(section.Key, row.RowKey ?? row.GroupId);
						if (!field.Rules.Where(r => r.Effect == RmsRuleEffect.Show).All(r => Evaluate(r.Condition, schema, values, 0, row)))
						{
							outcome.HiddenFieldKeys.Add(field.Key);
							continue;
						}
						if (field.Rules.Where(r => r.Effect == RmsRuleEffect.Require).Any(r => Evaluate(r.Condition, schema, values, 0, row)))
							outcome.RequiredFieldKeys.Add(field.Key);
					}
				}
			}
			return evaluation;
		}

		private static bool Evaluate(RecordConditionSchema condition, RecordDefinitionSchema schema, RecordValueSet values, int depth, RecordValueRow row)
		{
			if (condition == null) return true;
			if (depth > RecordDefinitionSchema.MaxRuleDepth) throw new InvalidOperationException("Rule nesting exceeds the supported depth.");
			switch (condition.Operator)
			{
				case RmsRuleOperator.And:
					return (condition.Conditions ?? new List<RecordConditionSchema>()).All(c => Evaluate(c, schema, values, depth + 1, row));
				case RmsRuleOperator.Or:
					return (condition.Conditions ?? new List<RecordConditionSchema>()).Any(c => Evaluate(c, schema, values, depth + 1, row));
			}

			// The row's own cell wins when the referenced field lives in the same repeating section; everything else is a scalar.
			var cell = row?.Cell(condition.FieldKey) ?? values.Scalar(condition.FieldKey);
			var field = schema.FindField(condition.FieldKey);
			var current = cell?.Value;
			var currentSet = cell?.Values ?? (current == null ? new List<string>() : new List<string> { current });
			switch (condition.Operator)
			{
				case RmsRuleOperator.IsEmpty:
					return string.IsNullOrWhiteSpace(current) && currentSet.Count == 0 && cell?.ReferenceId == null;
				case RmsRuleOperator.IsNotEmpty:
					return !(string.IsNullOrWhiteSpace(current) && currentSet.Count == 0 && cell?.ReferenceId == null);
				case RmsRuleOperator.Equals:
					return currentSet.Any(v => ValueEquals(field, v, condition.Value)) || cell?.ReferenceId != null && string.Equals(cell.ReferenceId, condition.Value, StringComparison.OrdinalIgnoreCase);
				case RmsRuleOperator.NotEquals:
					return !(currentSet.Any(v => ValueEquals(field, v, condition.Value)) || cell?.ReferenceId != null && string.Equals(cell.ReferenceId, condition.Value, StringComparison.OrdinalIgnoreCase));
				case RmsRuleOperator.InSet:
					return currentSet.Any(v => (condition.Values ?? new List<string>()).Any(x => ValueEquals(field, v, x)));
				case RmsRuleOperator.NotInSet:
					return !currentSet.Any(v => (condition.Values ?? new List<string>()).Any(x => ValueEquals(field, v, x)));
				case RmsRuleOperator.InRange:
					if (cell?.Number.HasValue == true)
						return (!condition.Min.HasValue || cell.Number >= condition.Min) && (!condition.Max.HasValue || cell.Number <= condition.Max);
					if (DateTime.TryParse(current, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
						return (!condition.MinDate.HasValue || when >= condition.MinDate) && (!condition.MaxDate.HasValue || when <= condition.MaxDate);
					return false;
			}
			return false;
		}

		private static bool ValueEquals(RecordFieldSchema field, string a, string b)
		{
			if (a == null || b == null) return a == b;
			if (field != null && (field.Type == RmsFieldType.Integer || field.Type == RmsFieldType.Decimal || field.Type == RmsFieldType.Currency || field.Type == RmsFieldType.Quantity)
				&& decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
				return x == y;
			if (field != null && field.Type == RmsFieldType.Boolean)
				return ParseBool(a) == ParseBool(b);
			return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
		}

		private sealed class ParseContext
		{
			public ParseContext(int departmentId, string recordId, RecordDefinitionSchema schema, RecordTypedValuesService owner)
			{
				DepartmentId = departmentId; RecordId = recordId; Schema = schema; Owner = owner;
			}
			public int DepartmentId { get; }
			public string RecordId { get; }
			public RecordDefinitionSchema Schema { get; }
			public RecordTypedValuesService Owner { get; }
			public List<RecordValueIssue> Issues { get; } = new List<RecordValueIssue>();
			public string UserId { get; set; }
			public DateTime Now { get; set; } = DateTime.UtcNow;
			public List<PersonName> Names { get; set; }
			public HashSet<string> AttachmentIds { get; set; }
			public void Error(RecordValueInput input, string code, string message) => Issues.Add(new RecordValueIssue { SectionKey = input?.SectionKey, FieldKey = input?.FieldKey, RowKey = input?.RowKey, Code = code, Message = message });
			public void Error(string sectionKey, string fieldKey, string rowKey, string code, string message) => Issues.Add(new RecordValueIssue { SectionKey = sectionKey, FieldKey = fieldKey, RowKey = rowKey, Code = code, Message = message });
		}

		/// <summary>A parsed row set: groups (repeating rows) and the value rows that point at them.</summary>
		private sealed class ParsedValues
		{
			public List<RmsRecordValueGroup> Groups { get; } = new List<RmsRecordValueGroup>();
			public List<RmsRecordValue> Rows { get; } = new List<RmsRecordValue>();
		}

		private async Task<ParsedValues> ParseAllAsync(ParseContext context, List<RecordValueInput> inputs, RmsRecordDefinitionVersion version)
		{
			var parsed = new ParsedValues();
			var schema = context.Schema;
			var groupsByKey = new Dictionary<string, RmsRecordValueGroup>(StringComparer.OrdinalIgnoreCase);
			var seenScalars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var input in inputs.Where(i => i != null))
			{
				var fieldKey = RecordDefinitionKeys.NormalizeKey(input.FieldKey);
				var field = schema.FindField(fieldKey);
				if (field == null)
				{
					context.Error(input, "unknown_field", $"'{input.FieldKey}' is not a field of this definition version.");
					continue;
				}
				var section = schema.SectionOf(fieldKey);
				if (!string.IsNullOrWhiteSpace(input.SectionKey) && !string.Equals(section.Key, input.SectionKey, StringComparison.OrdinalIgnoreCase))
				{
					context.Error(input, "wrong_section", $"'{input.FieldKey}' belongs to section '{section.Key}'.");
					continue;
				}

				string groupId = null;
				if (section.Repeating)
				{
					var rowKey = string.IsNullOrWhiteSpace(input.RowKey) ? "row-" + input.Ordinal : input.RowKey.Trim();
					var groupKey = section.Key + "|" + rowKey;
					if (!groupsByKey.TryGetValue(groupKey, out var group))
					{
						if (groupsByKey.Count(g => string.Equals(g.Value.SectionKey, section.Key, StringComparison.OrdinalIgnoreCase)) >= Math.Min(MaxRowsPerSection, section.MaxRows ?? MaxRowsPerSection))
						{
							context.Error(input, "too_many_rows", $"Section '{section.Label ?? section.Key}' accepts at most {section.MaxRows ?? MaxRowsPerSection} rows.");
							continue;
						}
						group = new RmsRecordValueGroup
						{
							RmsRecordValueGroupId = Guid.NewGuid().ToString(),
							DepartmentId = context.DepartmentId,
							ProtectionId = Guid.NewGuid().ToString(),
							RecordId = context.RecordId,
							RecordKind = (int)RmsRecordKind.Operational,
							RmsRecordDefinitionVersionId = version?.RmsRecordDefinitionVersionId,
							SectionKey = section.Key,
							Ordinal = input.Ordinal,
							ClientRowKey = rowKey.Length > 64 ? rowKey.Substring(0, 64) : rowKey,
							CreatedOn = context.Now,
							ModifiedOn = context.Now,
							RowVersion = 1
						};
						groupsByKey[groupKey] = group;
						parsed.Groups.Add(group);
					}
					groupId = group.RmsRecordValueGroupId;
				}
				else if (!seenScalars.Add(fieldKey))
				{
					context.Error(input, "duplicate_value", $"'{input.FieldKey}' was posted more than once.");
					continue;
				}

				var rows = await ParseFieldAsync(context, field, input);
				if (rows == null) continue;
				var ordinal = 0;
				foreach (var row in rows)
				{
					row.RmsRecordValueId = Guid.NewGuid().ToString();
					row.DepartmentId = context.DepartmentId;
					row.ProtectionId = Guid.NewGuid().ToString();
					row.RecordId = context.RecordId;
					row.RecordKind = (int)RmsRecordKind.Operational;
					row.RmsRecordDefinitionVersionId = version?.RmsRecordDefinitionVersionId;
					row.FieldKey = field.Key;
					row.RmsRecordValueGroupId = groupId;
					row.Ordinal = rows.Count > 1 ? ordinal++ : 0;
					row.ValueType = (int)field.Type;
					// ADP catalog v11: a Protected-classified field's row is sealed by the seam and swept by the engine.
					row.ProtectionRequired = field.Classification == RmsFieldClassification.Protected;
					row.CreatedOn = context.Now;
					row.ModifiedOn = context.Now;
					row.RowVersion = 1;
					if (row.PopulatedColumnGroups() != 1)
						throw new InvalidOperationException($"Field '{field.Key}' produced {row.PopulatedColumnGroups()} column groups; exactly one is allowed.");
					parsed.Rows.Add(row);
				}
			}

			// Ordinals of repeating rows follow the posted order, densely.
			foreach (var sectionGroups in parsed.Groups.GroupBy(g => g.SectionKey, StringComparer.OrdinalIgnoreCase))
			{
				var ordinal = 0;
				foreach (var group in sectionGroups.OrderBy(g => g.Ordinal).ThenBy(g => g.ClientRowKey, StringComparer.Ordinal))
					group.Ordinal = ordinal++;
			}
			return parsed;
		}

		private static bool IsBlank(RecordValueInput input) => string.IsNullOrWhiteSpace(input.Value) && (input.Values == null || input.Values.All(string.IsNullOrWhiteSpace)) && string.IsNullOrWhiteSpace(input.ReferenceId);

		/// <summary>Null = the input was blank (nothing stored) or invalid (issue recorded); otherwise the rows to store.</summary>
		private async Task<List<RmsRecordValue>> ParseFieldAsync(ParseContext context, RecordFieldSchema field, RecordValueInput input)
		{
			if (IsBlank(input)) return null;
			var value = input.Value?.Trim();
			var row = new RmsRecordValue();
			switch (field.Type)
			{
				case RmsFieldType.ShortText:
				{
					var max = Math.Min(field.MaxLength ?? MaxShortText, MaxShortText);
					if (value.Length > max) { context.Error(input, "too_long", $"'{field.Label ?? field.Key}' accepts at most {max} characters."); return null; }
					row.TextValue = value;
					return One(row);
				}
				case RmsFieldType.LongText:
				{
					var max = Math.Min(field.MaxLength ?? MaxLongText, MaxLongText);
					if (value.Length > max) { context.Error(input, "too_long", $"'{field.Label ?? field.Key}' accepts at most {max} characters."); return null; }
					row.LongTextValue = value;
					return One(row);
				}
				case RmsFieldType.Integer:
				{
					if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { context.Error(input, "not_integer", $"'{field.Label ?? field.Key}' must be a whole number."); return null; }
					if (!InRange(field, n)) { context.Error(input, "out_of_range", RangeMessage(field)); return null; }
					row.NumberValue = n;
					return One(row);
				}
				case RmsFieldType.Decimal:
				{
					if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) { context.Error(input, "not_number", $"'{field.Label ?? field.Key}' must be a number."); return null; }
					if (!InRange(field, d)) { context.Error(input, "out_of_range", RangeMessage(field)); return null; }
					row.NumberValue = d;
					return One(row);
				}
				case RmsFieldType.Boolean:
				{
					var b = ParseBool(value);
					if (b == null) { context.Error(input, "not_boolean", $"'{field.Label ?? field.Key}' must be yes or no."); return null; }
					row.BoolValue = b;
					return One(row);
				}
				case RmsFieldType.Date:
				{
					if (!DateTime.TryParseExact(value, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { context.Error(input, "not_date", $"'{field.Label ?? field.Key}' must be a date (yyyy-MM-dd)."); return null; }
					row.DateTimeValue = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
					return One(row);
				}
				case RmsFieldType.DateTime:
				{
					if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)) { context.Error(input, "not_datetime", $"'{field.Label ?? field.Key}' must be a date and time."); return null; }
					row.DateTimeValue = when.UtcDateTime;
					row.DateTimeOffsetMinutes = input.OffsetMinutes ?? (int)when.Offset.TotalMinutes;
					return One(row);
				}
				case RmsFieldType.Duration:
				{
					var seconds = ParseDuration(value);
					if (seconds == null || seconds < 0) { context.Error(input, "not_duration", $"'{field.Label ?? field.Key}' must be a duration (hh:mm, minutes, or ISO 8601)."); return null; }
					if (!InRange(field, seconds.Value)) { context.Error(input, "out_of_range", RangeMessage(field)); return null; }
					row.DurationSeconds = seconds;
					return One(row);
				}
				case RmsFieldType.SingleSelect:
				{
					var option = field.Options.FirstOrDefault(o => string.Equals(o.Key, value, StringComparison.OrdinalIgnoreCase));
					if (option == null) { context.Error(input, "unknown_option", $"'{value}' is not an option of '{field.Label ?? field.Key}'."); return null; }
					row.OptionKey = option.Key;
					return One(row);
				}
				case RmsFieldType.MultiSelect:
				{
					var keys = (input.Values ?? new List<string>()).Concat(string.IsNullOrWhiteSpace(value) ? new string[0] : value.Split(',')).Select(k => k?.Trim()).Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					var rows = new List<RmsRecordValue>();
					foreach (var key in keys)
					{
						var option = field.Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
						if (option == null) { context.Error(input, "unknown_option", $"'{key}' is not an option of '{field.Label ?? field.Key}'."); return null; }
						rows.Add(new RmsRecordValue { OptionKey = option.Key });
					}
					return rows.Count == 0 ? null : rows;
				}
				case RmsFieldType.Address:
				{
					if (value != null && value.Length > 2000) { context.Error(input, "too_long", "Address text accepts at most 2000 characters."); return null; }
					// One column group: the text is the value; coordinates ride in the reference snapshot (not a counted column).
					row.LongTextValue = value ?? string.Empty;
					if (!string.IsNullOrWhiteSpace(input.ReferenceId))
					{
						if (!TryParseCoordinates(input.ReferenceId, out var lat, out var lng)) { context.Error(input, "not_coordinates", "Coordinates must be 'latitude, longitude'."); return null; }
						row.ReferenceType = "geo";
						row.ReferenceSnapshotJson = Snapshot(new { coordinates = lat.ToString("0.######", CultureInfo.InvariantCulture) + "," + lng.ToString("0.######", CultureInfo.InvariantCulture) });
					}
					return One(row);
				}
				case RmsFieldType.Person:
				{
					var id = input.ReferenceId ?? value;
					context.Names ??= await _departments.GetAllPersonnelNamesForDepartmentAsync(context.DepartmentId) ?? new List<PersonName>();
					var person = context.Names.FirstOrDefault(n => string.Equals(n.UserId, id, StringComparison.OrdinalIgnoreCase));
					if (person == null) { context.Error(input, "unknown_person", $"'{field.Label ?? field.Key}' must name a member of this department."); return null; }
					row.ReferenceType = "user"; row.ReferenceId = person.UserId; row.ReferenceSnapshotJson = Snapshot(new { name = person.Name });
					return One(row);
				}
				case RmsFieldType.Unit:
				{
					if (!int.TryParse(input.ReferenceId ?? value, out var unitId)) { context.Error(input, "unknown_unit", $"'{field.Label ?? field.Key}' must reference a unit."); return null; }
					var unit = await _units.GetUnitByIdAsync(unitId);
					if (unit == null || unit.DepartmentId != context.DepartmentId) { context.Error(input, "unknown_unit", $"'{field.Label ?? field.Key}' must reference a unit of this department."); return null; }
					row.ReferenceType = "unit"; row.ReferenceId = unitId.ToString(CultureInfo.InvariantCulture); row.ReferenceSnapshotJson = Snapshot(new { name = unit.Name });
					return One(row);
				}
				case RmsFieldType.Group:
				{
					if (!int.TryParse(input.ReferenceId ?? value, out var groupId)) { context.Error(input, "unknown_group", $"'{field.Label ?? field.Key}' must reference a group or station."); return null; }
					var group = await _departmentGroups.GetGroupByIdAsync(groupId);
					if (group == null || group.DepartmentId != context.DepartmentId) { context.Error(input, "unknown_group", $"'{field.Label ?? field.Key}' must reference a group of this department."); return null; }
					row.ReferenceType = "group"; row.ReferenceId = groupId.ToString(CultureInfo.InvariantCulture); row.ReferenceSnapshotJson = Snapshot(new { name = group.Name });
					return One(row);
				}
				case RmsFieldType.Contact:
				{
					var contactId = input.ReferenceId ?? value;
					var contact = await _contacts.GetContactByIdAsync(contactId);
					if (contact == null || contact.DepartmentId != context.DepartmentId) { context.Error(input, "unknown_contact", $"'{field.Label ?? field.Key}' must reference a contact or site of this department."); return null; }
					var display = string.Join(" ", new[] { contact.FirstName, contact.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
					row.ReferenceType = "contact"; row.ReferenceId = contact.ContactId; row.ReferenceSnapshotJson = Snapshot(new { name = string.IsNullOrWhiteSpace(display) ? contact.CompanyName : display });
					return One(row);
				}
				case RmsFieldType.Attachment:
				{
					var attachmentId = input.ReferenceId ?? value;
					if (context.RecordId != null)
					{
						context.AttachmentIds ??= new HashSet<string>((await _attachments.GetMetadataForRecordAsync(context.DepartmentId, context.RecordId))?.Select(a => a.RmsRecordAttachmentId) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
						if (!context.AttachmentIds.Contains(attachmentId)) { context.Error(input, "unknown_attachment", $"'{field.Label ?? field.Key}' must reference an attachment of this Record."); return null; }
					}
					row.ReferenceType = "attachment"; row.ReferenceId = attachmentId; row.ReferenceSnapshotJson = Snapshot(new { });
					return One(row);
				}
				case RmsFieldType.Signature:
				{
					// An acknowledgement: who signed, when, and the statement they accepted. The signer is the acting user
					// unless the field names another member (a customer acknowledgement records the name as text).
					var signer = input.ReferenceId ?? context.UserId;
					context.Names ??= await _departments.GetAllPersonnelNamesForDepartmentAsync(context.DepartmentId) ?? new List<PersonName>();
					var person = signer == null ? null : context.Names.FirstOrDefault(n => string.Equals(n.UserId, signer, StringComparison.OrdinalIgnoreCase));
					row.ReferenceType = "signature";
					row.ReferenceId = person?.UserId ?? "external";
					row.ReferenceSnapshotJson = Snapshot(new { name = person?.Name ?? value, statement = value, signed_on = context.Now, method = "web" });
					return One(row);
				}
				case RmsFieldType.ExternalReference:
				{
					var identifier = (input.ReferenceId ?? value)?.Trim();
					if (string.IsNullOrEmpty(identifier) || identifier.Length > 200) { context.Error(input, "bad_reference", $"'{field.Label ?? field.Key}' must be an identifier of at most 200 characters."); return null; }
					var scheme = (input.ReferenceType ?? field.ReferenceType ?? "external").Trim();
					row.ReferenceType = "external:" + scheme; row.ReferenceId = identifier; row.ReferenceSnapshotJson = Snapshot(new { scheme });
					return One(row);
				}
				case RmsFieldType.Currency:
				{
					if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) { context.Error(input, "not_number", $"'{field.Label ?? field.Key}' must be an amount."); return null; }
					var code = (input.CurrencyCode ?? field.DefaultCurrency ?? "USD").ToUpperInvariant();
					if (!RmsCurrencies.IsSupported(code)) { context.Error(input, "unknown_currency", $"'{code}' is not a supported currency."); return null; }
					if (!InRange(field, amount)) { context.Error(input, "out_of_range", RangeMessage(field)); return null; }
					row.NumberValue = decimal.Round(amount, 2); row.CurrencyCode = code;
					return One(row);
				}
				case RmsFieldType.Quantity:
				{
					if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)) { context.Error(input, "not_number", $"'{field.Label ?? field.Key}' must be a number."); return null; }
					var unitCode = input.UnitCode ?? field.DefaultUnit;
					var unit = RmsUnits.Find(unitCode);
					if (unit == null || !string.IsNullOrWhiteSpace(field.UnitFamily) && !string.Equals(unit.Family, field.UnitFamily, StringComparison.OrdinalIgnoreCase)) { context.Error(input, "unknown_unit", $"'{unitCode}' is not a unit of {field.UnitFamily ?? "this field"}."); return null; }
					var canonical = RmsUnits.Canonicalize(quantity, unit.Code);
					if (!InRange(field, canonical?.Value ?? quantity)) { context.Error(input, "out_of_range", RangeMessage(field)); return null; }
					row.NumberValue = quantity; row.UnitCode = unit.Code; row.CanonicalNumberValue = canonical?.Value; row.CanonicalUnitCode = canonical?.Unit;
					return One(row);
				}
				case RmsFieldType.CountrySubdivision:
				{
					var code = value.ToUpperInvariant();
					if (!RmsCountrySubdivisions.IsValid(code)) { context.Error(input, "unknown_subdivision", $"'{value}' is not a country/subdivision code (CC or CC-SUB)."); return null; }
					row.TextValue = code;
					return One(row);
				}
				case RmsFieldType.CallReference:
				{
					if (!int.TryParse(input.ReferenceId ?? value, out var callId)) { context.Error(input, "unknown_call", $"'{field.Label ?? field.Key}' must reference a call."); return null; }
					var call = await _calls.GetCallByIdAsync(callId);
					if (call == null || call.DepartmentId != context.DepartmentId) { context.Error(input, "unknown_call", $"'{field.Label ?? field.Key}' must reference a call of this department."); return null; }
					row.ReferenceType = "call"; row.ReferenceId = callId.ToString(CultureInfo.InvariantCulture); row.ReferenceSnapshotJson = Snapshot(new { number = call.Number, name = call.Name });
					return One(row);
				}
				case RmsFieldType.InventoryReference:
				{
					if (!int.TryParse(input.ReferenceId ?? value, out var inventoryId)) { context.Error(input, "unknown_item", $"'{field.Label ?? field.Key}' must reference an inventory item."); return null; }
					var item = await _inventory.GetInventoryByIdAsync(inventoryId);
					if (item == null || item.DepartmentId != context.DepartmentId) { context.Error(input, "unknown_item", $"'{field.Label ?? field.Key}' must reference an inventory item of this department."); return null; }
					row.ReferenceType = "inventory-item"; row.ReferenceId = inventoryId.ToString(CultureInfo.InvariantCulture); row.ReferenceSnapshotJson = Snapshot(new { name = item.Type?.Type ?? ("Inventory " + inventoryId) });
					return One(row);
				}
				case RmsFieldType.ChecklistWorkOrderReference:
				{
					var type = (input.ReferenceType ?? field.ReferenceType ?? "checklist").Trim().ToLowerInvariant();
					if (type != "checklist" && type != "workorder") { context.Error(input, "bad_reference", $"'{field.Label ?? field.Key}' must reference a checklist or work order."); return null; }
					var id = (input.ReferenceId ?? value)?.Trim();
					if (string.IsNullOrEmpty(id) || id.Length > 64) { context.Error(input, "bad_reference", $"'{field.Label ?? field.Key}' must carry the referenced identifier."); return null; }
					row.ReferenceType = type; row.ReferenceId = id; row.ReferenceSnapshotJson = Snapshot(new { type });
					return One(row);
				}
			}
			context.Error(input, "unsupported_type", $"Field type {field.Type} is not supported on this server.");
			return null;
		}

		private static List<RmsRecordValue> One(RmsRecordValue row) => new List<RmsRecordValue> { row };
		private static string Snapshot(object o) => JsonConvert.SerializeObject(o);
		private static bool InRange(RecordFieldSchema field, decimal n) => (!field.Min.HasValue || n >= field.Min.Value) && (!field.Max.HasValue || n <= field.Max.Value);
		private static string RangeMessage(RecordFieldSchema field) => $"'{field.Label ?? field.Key}' must be between {field.Min?.ToString(CultureInfo.InvariantCulture) ?? "any"} and {field.Max?.ToString(CultureInfo.InvariantCulture) ?? "any"}.";

		public static bool? ParseBool(string value)
		{
			switch ((value ?? string.Empty).Trim().ToLowerInvariant())
			{
				case "true": case "1": case "yes": case "on": return true;
				case "false": case "0": case "no": case "off": return false;
				default: return null;
			}
		}

		public static long? ParseDuration(string value)
		{
			value = (value ?? string.Empty).Trim();
			if (value.Length == 0) return null;
			if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return minutes * 60;
			if (value.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
			{
				try { return (long)System.Xml.XmlConvert.ToTimeSpan(value).TotalSeconds; } catch (FormatException) { return null; }
			}
			if (TimeSpan.TryParseExact(value, new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss", @"d\.h\:mm" }, CultureInfo.InvariantCulture, out var span)) return (long)span.TotalSeconds;
			return null;
		}

		private static bool TryParseCoordinates(string text, out double lat, out double lng)
		{
			lat = lng = 0;
			var parts = (text ?? string.Empty).Split(',');
			return parts.Length == 2 && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lng)
				&& lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;
		}

		private void ApplyFinalizationRules(ParseContext context, ParsedValues parsed)
		{
			var values = Shape(context.Schema, parsed.Groups, parsed.Rows, true);
			var evaluation = EvaluateRules(context.Schema, values);
			foreach (var section in context.Schema.Sections)
			{
				if (evaluation.HiddenSectionKeys.Contains(section.Key)) continue;
				var set = values.Section(section.Key);
				var rows = set?.Rows ?? new List<RecordValueRow>();
				if (section.Repeating && section.MinRows.HasValue && rows.Count < section.MinRows.Value)
					context.Error(section.Key, null, null, "too_few_rows", $"Section '{section.Label ?? section.Key}' needs at least {section.MinRows.Value} row(s).");
				foreach (var field in section.Fields)
				{
					if (!section.Repeating)
					{
						if (evaluation.IsRequired(section.Key, null, field.Key) && values.Scalar(field.Key)?.Display == null)
							context.Error(section.Key, field.Key, null, "required", $"'{field.Label ?? field.Key}' is required before finalization.");
					}
					else
						foreach (var row in rows)
							if (evaluation.IsRequired(section.Key, row.RowKey ?? row.GroupId, field.Key) && row.Cell(field.Key)?.Display == null)
								context.Error(section.Key, field.Key, row.RowKey, "required", $"'{field.Label ?? field.Key}' is required on every row of '{section.Label ?? section.Key}'.");
				}
			}
		}

		// ------------------------------------------------------------------------------------------------
		// Storage
		// ------------------------------------------------------------------------------------------------

		public async Task<RecordValueSet> SaveDraftValuesAsync(int departmentId, string userId, string recordId, RmsRecordDefinitionVersion version, List<RecordValueInput> inputs, CancellationToken cancellationToken = default)
		{
			if (version == null) throw new ArgumentNullException(nameof(version));
			var context = new ParseContext(departmentId, recordId, version.Schema, this) { UserId = userId };
			var parsed = await ParseAllAsync(context, inputs ?? new List<RecordValueInput>(), version);
			if (context.Issues.Any(i => i.Severity == "error"))
				throw new ArgumentException(string.Join(" ", context.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

			await CarryForwardSealedRowsAsync(departmentId, recordId, version, parsed, context.Now);

			// Seal Protected-classified rows in place for storage; the caller gets its plaintext back afterwards.
			var snapshots = parsed.Rows.Where(r => r.ProtectionRequired && !r.IsSealed).Select(r => PlaintextSnapshot<RmsRecordValue>.Take(r, RmsProtectedFields.Values)).ToList();
			await _protection.ProtectValuesAsync(departmentId, parsed.Rows, userId, cancellationToken);

			await _values.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
			await _groups.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
			foreach (var group in parsed.Groups)
				await _groups.InsertAsync(group, cancellationToken, true);
			foreach (var row in parsed.Rows)
				await _values.InsertAsync(row, cancellationToken, true);
			foreach (var snapshot in snapshots)
				snapshot.Restore();

			return Shape(version.Schema, parsed.Groups, parsed.Rows, true);
		}

		/// <summary>
		/// The sentinel policy for typed values (ADP catalog v11): a sealed row the editor could not reveal posts nothing
		/// back (the cell renders withheld and disabled), and dropping it would erase a value the editor never saw. Such
		/// rows are carried into the new draft under their own identity, so the envelope's row-key binding still holds;
		/// a posted value for the same field replaces the row, and a removed repeating row takes its cells with it.
		/// </summary>
		private async Task CarryForwardSealedRowsAsync(int departmentId, string recordId, RmsRecordDefinitionVersion version, ParsedValues parsed, DateTime now)
		{
			var existing = (await _values.GetForRecordAsync(departmentId, recordId, null))?.Where(v => v.IsSealed).ToList() ?? new List<RmsRecordValue>();
			if (existing.Count == 0)
				return;
			var oldGroups = (await _groups.GetForRecordAsync(departmentId, recordId, null))?.ToList() ?? new List<RmsRecordValueGroup>();
			foreach (var sealedRow in existing)
			{
				string newGroupId = null;
				if (sealedRow.RmsRecordValueGroupId != null)
				{
					var oldGroup = oldGroups.FirstOrDefault(g => g.RmsRecordValueGroupId == sealedRow.RmsRecordValueGroupId);
					var newGroup = oldGroup == null ? null : parsed.Groups.FirstOrDefault(g => string.Equals(g.SectionKey, oldGroup.SectionKey, StringComparison.OrdinalIgnoreCase) && string.Equals(g.ClientRowKey, oldGroup.ClientRowKey, StringComparison.Ordinal));
					if (newGroup == null)
						continue;
					newGroupId = newGroup.RmsRecordValueGroupId;
				}
				if (parsed.Rows.Any(r => string.Equals(r.FieldKey, sealedRow.FieldKey, StringComparison.OrdinalIgnoreCase) && r.RmsRecordValueGroupId == newGroupId))
					continue;
				if (version.Schema.FindField(sealedRow.FieldKey) == null)
					continue;
				sealedRow.RmsRecordValueGroupId = newGroupId;
				sealedRow.RmsRecordDefinitionVersionId = version.RmsRecordDefinitionVersionId;
				sealedRow.ModifiedOn = now;
				parsed.Rows.Add(sealedRow);
			}
		}

		public async Task<RecordValueSet> HydrateAsync(int departmentId, string recordId, string revisionId, RmsRecordDefinitionVersion version, bool canViewRestricted)
		{
			if (version == null) return new RecordValueSet();
			var groups = (await _groups.GetForRecordAsync(departmentId, recordId, revisionId))?.ToList() ?? new List<RmsRecordValueGroup>();
			var rows = (await _values.GetForRecordAsync(departmentId, recordId, revisionId))?.ToList() ?? new List<RmsRecordValue>();
			await RevealSealedAsync(departmentId, rows);
			var set = Shape(version.Schema, groups, rows, canViewRestricted);
			set.DefinitionKey = version.DefinitionKey;
			set.DefinitionVersion = version.Version;
			set.DefinitionVersionId = version.RmsRecordDefinitionVersionId;
			return set;
		}

		/// <summary>Ambient reveal of sealed rows (catalog v11); a refused row keeps its envelope and shapes as the withheld cell.</summary>
		private async Task<ProtectedReadResult> RevealSealedAsync(int departmentId, List<RmsRecordValue> rows)
		{
			var sealedRows = rows.Where(r => r.IsSealed).ToList();
			return sealedRows.Count == 0 ? new ProtectedReadResult() : await _protection.RevealValuesAsync(departmentId, sealedRows);
		}

		/// <summary>
		/// A copied row gets a new identity, and the envelope's AAD is bound to the row key, so a sealed source must be
		/// revealed first and the copy re-sealed under its own key (the caller's grant carries finalize under enforcement).
		/// </summary>
		private async Task ResealCopiesAsync(int departmentId, string userId, List<RmsRecordValue> sources, List<RmsRecordValue> copies, string operation, CancellationToken cancellationToken)
		{
			(await RevealSealedAsync(departmentId, sources)).RequireRevealed(operation);
			foreach (var copy in copies)
			{
				var source = sources.FirstOrDefault(s => s.RmsRecordValueId == copy.RmsRecordValueId);
				if (source == null || !source.IsSealed) continue;
				RmsRecordValuePack.Unpack(copy, RmsRecordValuePack.Pack(source));
				copy.ProtectedEnvelope = null; copy.IsProtected = false; copy.ProtectedCatalogVersion = 0;
			}
			await _protection.ProtectValuesAsync(departmentId, copies, userId, cancellationToken);
		}

		public async Task CopyDraftToRevisionAsync(int departmentId, string recordId, string revisionId, CancellationToken cancellationToken = default)
		{
			var groups = (await _groups.GetForRecordAsync(departmentId, recordId, null))?.ToList() ?? new List<RmsRecordValueGroup>();
			var rows = (await _values.GetForRecordAsync(departmentId, recordId, null))?.ToList() ?? new List<RmsRecordValue>();
			var now = DateTime.UtcNow;
			var copies = rows.Select(Clone).ToList();
			await ResealCopiesAsync(departmentId, null, rows, copies, "finalize", cancellationToken);
			var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var group in groups)
			{
				var copy = Clone(group);
				copy.RmsRecordValueGroupId = Guid.NewGuid().ToString();
				copy.ProtectionId = Guid.NewGuid().ToString();
				copy.RevisionId = revisionId;
				copy.CreatedOn = now; copy.ModifiedOn = now; copy.RowVersion = 1;
				groupMap[group.RmsRecordValueGroupId] = copy.RmsRecordValueGroupId;
				await _groups.InsertAsync(copy, cancellationToken, true);
			}
			foreach (var copy in copies)
			{
				var row = rows.First(r => r.RmsRecordValueId == copy.RmsRecordValueId);
				copy.RmsRecordValueId = Guid.NewGuid().ToString();
				copy.ProtectionId = Guid.NewGuid().ToString();
				copy.RevisionId = revisionId;
				copy.RmsRecordValueGroupId = row.RmsRecordValueGroupId != null && groupMap.TryGetValue(row.RmsRecordValueGroupId, out var mapped) ? mapped : null;
				copy.CreatedOn = now; copy.ModifiedOn = now; copy.RowVersion = 1;
			}
			await SealAndInsertAsync(departmentId, null, copies, cancellationToken);
		}

		private async Task SealAndInsertAsync(int departmentId, string userId, List<RmsRecordValue> copies, CancellationToken cancellationToken)
		{
			await _protection.ProtectValuesAsync(departmentId, copies, userId, cancellationToken);
			foreach (var copy in copies)
				await _values.InsertAsync(copy, cancellationToken, true);
		}

		public async Task RestoreDraftFromRevisionAsync(int departmentId, string userId, string recordId, string revisionId, RmsRecordDefinitionVersion version, CancellationToken cancellationToken = default)
		{
			var groups = (await _groups.GetForRecordAsync(departmentId, recordId, revisionId))?.ToList() ?? new List<RmsRecordValueGroup>();
			var rows = (await _values.GetForRecordAsync(departmentId, recordId, revisionId))?.ToList() ?? new List<RmsRecordValue>();
			var copies = rows.Select(Clone).ToList();
			await ResealCopiesAsync(departmentId, userId, rows, copies, "restore draft", cancellationToken);
			await _values.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
			await _groups.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
			var now = DateTime.UtcNow;
			var groupMap = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var group in groups)
			{
				var copy = Clone(group);
				copy.RmsRecordValueGroupId = Guid.NewGuid().ToString();
				copy.ProtectionId = Guid.NewGuid().ToString();
				copy.RevisionId = null;
				copy.CreatedOn = now; copy.ModifiedOn = now; copy.RowVersion = 1;
				groupMap[group.RmsRecordValueGroupId] = copy.RmsRecordValueGroupId;
				await _groups.InsertAsync(copy, cancellationToken, true);
			}
			foreach (var copy in copies)
			{
				var row = rows.First(r => r.RmsRecordValueId == copy.RmsRecordValueId);
				copy.RmsRecordValueId = Guid.NewGuid().ToString();
				copy.ProtectionId = Guid.NewGuid().ToString();
				copy.RevisionId = null;
				copy.RmsRecordValueGroupId = row.RmsRecordValueGroupId != null && groupMap.TryGetValue(row.RmsRecordValueGroupId, out var mapped) ? mapped : null;
				copy.CreatedOn = now; copy.ModifiedOn = now; copy.RowVersion = 1;
			}
			await SealAndInsertAsync(departmentId, userId, copies, cancellationToken);
		}

		public async Task<int> DeleteDraftAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
		{
			var count = await _values.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
			await _groups.DeleteDraftForRecordAsync(departmentId, recordId, cancellationToken);
			return count;
		}

		private static T Clone<T>(T entity) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(entity));

		// ------------------------------------------------------------------------------------------------
		// Shaping and projections
		// ------------------------------------------------------------------------------------------------

		/// <summary>Rows -> sections/rows/cells against the pinned schema. Unknown keys (a value from a field later removed) are ignored; labels are the version's.</summary>
		public static RecordValueSet Shape(RecordDefinitionSchema schema, IEnumerable<RmsRecordValueGroup> groups, IEnumerable<RmsRecordValue> rows, bool canViewRestricted)
		{
			schema = schema ?? new RecordDefinitionSchema();
			var set = new RecordValueSet();
			var groupList = (groups ?? Enumerable.Empty<RmsRecordValueGroup>()).ToList();
			var rowList = (rows ?? Enumerable.Empty<RmsRecordValue>()).ToList();
			foreach (var section in schema.Sections)
			{
				var sectionSet = new RecordValueSectionSet { SectionKey = section.Key, Label = section.Label ?? section.Key, Repeating = section.Repeating };
				if (section.Repeating)
				{
					foreach (var group in groupList.Where(g => string.Equals(g.SectionKey, section.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Ordinal))
					{
						var row = new RecordValueRow { GroupId = group.RmsRecordValueGroupId, RowKey = group.ClientRowKey ?? group.RmsRecordValueGroupId, Ordinal = group.Ordinal };
						foreach (var field in section.Fields)
							row.Cells.Add(ToCell(section, field, rowList.Where(v => v.RmsRecordValueGroupId == group.RmsRecordValueGroupId && string.Equals(v.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(v => v.Ordinal).ToList(), row, canViewRestricted, set));
						sectionSet.Rows.Add(row);
					}
				}
				else
				{
					var row = new RecordValueRow { Ordinal = 0 };
					foreach (var field in section.Fields)
						row.Cells.Add(ToCell(section, field, rowList.Where(v => v.RmsRecordValueGroupId == null && string.Equals(v.FieldKey, field.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(v => v.Ordinal).ToList(), row, canViewRestricted, set));
					sectionSet.Rows.Add(row);
				}
				set.Sections.Add(sectionSet);
			}
			return set;
		}

		private static RecordValueCell ToCell(RecordSectionSchema section, RecordFieldSchema field, List<RmsRecordValue> stored, RecordValueRow row, bool canViewRestricted, RecordValueSet set)
		{
			var cell = new RecordValueCell { SectionKey = section.Key, FieldKey = field.Key, Label = field.Label ?? field.Key, Type = field.Type, Classification = field.Classification, GroupId = row.GroupId, RowKey = row.RowKey, Ordinal = row.Ordinal };
			if (stored.Count == 0) return cell;
			if (field.Classification == RmsFieldClassification.Restricted && !canViewRestricted)
			{
				cell.Withheld = true; cell.Display = Redacted;
				if (!set.WithheldFieldKeys.Contains(field.Key)) set.WithheldFieldKeys.Add(field.Key);
				return cell;
			}
			if (stored.Any(v => v.IsProtected && !string.IsNullOrEmpty(v.ProtectedEnvelope) && v.TextValue == null && v.LongTextValue == null))
			{
				// Sealed under ADP and not revealed for this caller (catalog v11): the sentinel, never ciphertext.
				cell.Withheld = true; cell.Display = Redacted;
				if (!set.WithheldFieldKeys.Contains(field.Key)) set.WithheldFieldKeys.Add(field.Key);
				return cell;
			}
			var first = stored[0];
			switch (field.Type)
			{
				case RmsFieldType.MultiSelect:
					cell.Values = stored.Select(v => v.OptionKey).ToList();
					cell.Value = string.Join(",", cell.Values);
					cell.Display = string.Join(", ", stored.Select(v => field.Options.FirstOrDefault(o => o.Key == v.OptionKey)?.Label ?? v.OptionKey));
					break;
				case RmsFieldType.SingleSelect:
					cell.Value = first.OptionKey;
					cell.Display = field.Options.FirstOrDefault(o => o.Key == first.OptionKey)?.Label ?? first.OptionKey;
					break;
				case RmsFieldType.ShortText:
				case RmsFieldType.CountrySubdivision:
					cell.Value = first.TextValue;
					cell.Display = field.Type == RmsFieldType.CountrySubdivision ? RmsCountrySubdivisions.Label(first.TextValue) : first.TextValue;
					break;
				case RmsFieldType.LongText:
					cell.Value = first.LongTextValue; cell.Display = first.LongTextValue;
					break;
				case RmsFieldType.Address:
				{
					var coordinates = AddressCoordinates(first);
					cell.Value = first.LongTextValue; cell.ReferenceId = coordinates;
					cell.Display = string.IsNullOrWhiteSpace(coordinates) ? first.LongTextValue : (string.IsNullOrWhiteSpace(first.LongTextValue) ? coordinates : first.LongTextValue + " (" + coordinates + ")");
					break;
				}
				case RmsFieldType.Integer:
				case RmsFieldType.Decimal:
					cell.Number = first.NumberValue;
					cell.Value = first.NumberValue?.ToString(field.Type == RmsFieldType.Integer ? "0" : "0.############", CultureInfo.InvariantCulture);
					cell.Display = cell.Value + (string.IsNullOrWhiteSpace(field.FixedUnitLabel) ? string.Empty : " " + field.FixedUnitLabel);
					break;
				case RmsFieldType.Currency:
					cell.Number = first.NumberValue; cell.CurrencyCode = first.CurrencyCode;
					cell.Value = first.NumberValue?.ToString("0.00", CultureInfo.InvariantCulture);
					cell.Display = first.NumberValue.HasValue ? RmsCurrencies.Format(first.NumberValue.Value, first.CurrencyCode) : null;
					break;
				case RmsFieldType.Quantity:
					cell.Number = first.NumberValue; cell.UnitCode = first.UnitCode; cell.CanonicalNumber = first.CanonicalNumberValue; cell.CanonicalUnitCode = first.CanonicalUnitCode;
					cell.Value = first.NumberValue?.ToString("0.############", CultureInfo.InvariantCulture);
					cell.Display = cell.Value + " " + first.UnitCode;
					break;
				case RmsFieldType.Boolean:
					cell.Value = first.BoolValue == true ? "true" : "false"; cell.Display = first.BoolValue == true ? "Yes" : "No";
					break;
				case RmsFieldType.Date:
					cell.Value = first.DateTimeValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); cell.Display = cell.Value;
					break;
				case RmsFieldType.DateTime:
				{
					var utc = first.DateTimeValue.HasValue ? DateTime.SpecifyKind(first.DateTimeValue.Value, DateTimeKind.Utc) : (DateTime?)null;
					cell.OffsetMinutes = first.DateTimeOffsetMinutes;
					cell.Value = utc?.ToString("O", CultureInfo.InvariantCulture);
					if (utc.HasValue)
					{
						var local = new DateTimeOffset(utc.Value).ToOffset(TimeSpan.FromMinutes(first.DateTimeOffsetMinutes ?? 0));
						cell.Display = local.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
					}
					break;
				}
				case RmsFieldType.Duration:
				{
					var seconds = first.DurationSeconds ?? 0;
					cell.Number = seconds;
					cell.Value = (seconds / 60).ToString(CultureInfo.InvariantCulture);
					cell.Display = TimeSpan.FromSeconds(seconds).ToString(seconds >= 86400 ? @"d\.hh\:mm" : @"hh\:mm", CultureInfo.InvariantCulture);
					break;
				}
				default:
				{
					cell.ReferenceType = first.ReferenceType; cell.ReferenceId = first.ReferenceId; cell.Value = first.ReferenceId;
					cell.Display = ReferenceDisplay(first);
					break;
				}
			}
			return cell;
		}

		private static string AddressCoordinates(RmsRecordValue row)
		{
			if (string.IsNullOrWhiteSpace(row.ReferenceSnapshotJson)) return row.TextValue;
			try { return JsonConvert.DeserializeObject<Dictionary<string, string>>(row.ReferenceSnapshotJson)?.GetValueOrDefault("coordinates") ?? row.TextValue; }
			catch (JsonException) { return row.TextValue; }
		}

		private static string ReferenceDisplay(RmsRecordValue row)
		{
			if (string.IsNullOrWhiteSpace(row.ReferenceSnapshotJson)) return row.ReferenceId;
			try
			{
				var snapshot = JsonConvert.DeserializeObject<Dictionary<string, object>>(row.ReferenceSnapshotJson) ?? new Dictionary<string, object>();
				if (row.ReferenceType == "signature")
					return string.Join(" ", new[] { snapshot.TryGetValue("name", out var n) ? n?.ToString() : null, snapshot.TryGetValue("signed_on", out var s) && s != null ? "signed " + Convert.ToDateTime(s, CultureInfo.InvariantCulture).ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
				if (row.ReferenceType == "call")
					return string.Join(" ", new[] { snapshot.TryGetValue("number", out var num) ? num?.ToString() : null, snapshot.TryGetValue("name", out var nm) ? nm?.ToString() : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
				if (row.ReferenceType != null && row.ReferenceType.StartsWith("external:", StringComparison.Ordinal))
					return row.ReferenceId + " (" + row.ReferenceType.Substring(9) + ")";
				return snapshot.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name?.ToString()) ? name.ToString() : row.ReferenceId;
			}
			catch (JsonException)
			{
				return row.ReferenceId;
			}
		}

		public string ToSearchText(RecordDefinitionSchema schema, RecordValueSet values)
		{
			if (schema == null || values == null) return string.Empty;
			var parts = new List<string>();
			foreach (var cell in values.AllCells())
			{
				var field = schema.FindField(cell.FieldKey);
				if (field == null || !field.Searchable || field.Classification != RmsFieldClassification.Standard || cell.Withheld || cell.Display == null) continue;
				// Long text never enters the projection text (plan section 5.10); everything else searchable does.
				if (field.Type == RmsFieldType.LongText || field.Type == RmsFieldType.Address || field.Type == RmsFieldType.Signature) continue;
				parts.Add(cell.Display);
			}
			var text = string.Join(" ", parts);
			return text.Length > 4000 ? text.Substring(0, 4000) : text;
		}

		public Dictionary<string, object> ToWorkflowBlock(RecordDefinitionSchema schema, RecordValueSet values)
		{
			var block = new Dictionary<string, object>(StringComparer.Ordinal);
			if (schema == null || values == null) return block;
			foreach (var section in values.Sections)
			{
				var sectionSchema = schema.FindSection(section.SectionKey);
				if (sectionSchema == null) continue;
				if (!section.Repeating)
				{
					foreach (var cell in section.Rows.SelectMany(r => r.Cells))
					{
						var field = schema.FindField(cell.FieldKey);
						if (field == null || !field.WorkflowExposed || field.Classification != RmsFieldClassification.Standard || cell.Withheld) continue;
						block[cell.FieldKey] = WorkflowValue(field, cell);
					}
				}
				else
				{
					var exposed = sectionSchema.Fields.Where(f => f.WorkflowExposed && f.Classification == RmsFieldClassification.Standard).ToList();
					if (exposed.Count == 0) continue;
					var rows = new List<Dictionary<string, object>>();
					foreach (var row in section.Rows)
					{
						var item = new Dictionary<string, object>(StringComparer.Ordinal);
						foreach (var field in exposed)
						{
							var cell = row.Cell(field.Key);
							if (cell != null && !cell.Withheld) item[field.Key] = WorkflowValue(field, cell);
						}
						rows.Add(item);
					}
					block[section.SectionKey] = rows;
					block[section.SectionKey + "_count"] = rows.Count;
				}
			}
			return block;
		}

		private static object WorkflowValue(RecordFieldSchema field, RecordValueCell cell)
		{
			switch (field.Type)
			{
				case RmsFieldType.Integer: return cell.Number.HasValue ? (object)(long)cell.Number.Value : null;
				case RmsFieldType.Decimal: case RmsFieldType.Currency: case RmsFieldType.Quantity: return cell.Number;
				case RmsFieldType.Boolean: return cell.Value == "true";
				case RmsFieldType.MultiSelect: return cell.Values ?? new List<string>();
				default: return cell.Display;
			}
		}

		public Dictionary<string, object> ToSnapshot(RecordDefinitionSchema schema, RecordValueSet values)
		{
			var snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
			if (values == null) return snapshot;
			foreach (var section in values.Sections)
			{
				var label = section.Label ?? section.SectionKey;
				if (!section.Repeating)
				{
					var fields = new Dictionary<string, object>(StringComparer.Ordinal);
					foreach (var cell in section.Rows.SelectMany(r => r.Cells).Where(c => c.Display != null))
						fields[SnapshotKey(cell)] = cell.Withheld ? Redacted : cell.Display;
					if (fields.Count > 0) snapshot[label] = fields;
				}
				else
				{
					var rows = new List<Dictionary<string, object>>();
					foreach (var row in section.Rows)
					{
						var fields = new Dictionary<string, object>(StringComparer.Ordinal);
						foreach (var cell in row.Cells.Where(c => c.Display != null))
							fields[SnapshotKey(cell)] = cell.Withheld ? Redacted : cell.Display;
						rows.Add(fields);
					}
					if (rows.Count > 0) snapshot[label] = rows;
				}
			}
			return snapshot;
		}

		/// <summary>Restricted cells carry a label suffix in snapshots so revisions, diffs and prints can withhold them without the schema.</summary>
		private static string SnapshotKey(RecordValueCell cell) => (cell.Label ?? cell.FieldKey) + (cell.Classification == RmsFieldClassification.Restricted ? RecordSnapshotSerializer.RestrictedValueSuffix : string.Empty);

		public string ToDisplaySummary(RecordDefinitionSchema schema, RecordValueSet values)
		{
			if (schema == null || values == null) return null;
			var parts = new List<string>();
			foreach (var cell in values.AllCells())
			{
				var field = schema.FindField(cell.FieldKey);
				if (field == null || cell.Withheld || cell.Display == null || field.Classification != RmsFieldClassification.Standard) continue;
				if (field.Type != RmsFieldType.ShortText && field.Type != RmsFieldType.SingleSelect && field.Type != RmsFieldType.Date && field.Type != RmsFieldType.Contact && field.Type != RmsFieldType.Unit && field.Type != RmsFieldType.ExternalReference) continue;
				if (!field.Searchable) continue;
				parts.Add(cell.Display);
				if (parts.Count >= 3) break;
			}
			var summary = string.Join(" · ", parts);
			return summary.Length > MaxSummaryLength ? summary.Substring(0, MaxSummaryLength - 1) + "…" : (summary.Length == 0 ? null : summary);
		}
	}
}
