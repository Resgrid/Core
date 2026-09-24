using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>One protected catalog field a release can allow-list, with the snake_case name it renders under.</summary>
	public sealed class ProtectedWorkflowField
	{
		public ProtectedWorkflowField(string fieldId, string templateName, Func<Call, string> getCallValue)
		{
			FieldId = fieldId;
			TemplateName = templateName;
			GetCallValue = getCallValue;
		}

		/// <summary>Catalog field id ("calls.completednotes").</summary>
		public string FieldId { get; }

		/// <summary>Name under protected.call — the same snake_case name call.* uses ("completed_notes").</summary>
		public string TemplateName { get; }

		/// <summary>Localization key for the checkbox label.</summary>
		public string LabelKey => "Field_" + FieldId.Replace('.', '_');

		public Func<Call, string> GetCallValue { get; }
	}

	/// <summary>
	/// Which triggers a Protected Workflow release supports and which protected fields each can release. v1 ships
	/// call triggers only; the shape (trigger -> entity type -> field list) is what a later trigger family plugs into.
	/// The call field ids mirror the ADP call catalog (ProtectedReadService.CallFieldAccessors — pinned by a parity test).
	/// </summary>
	public static class ProtectedWorkflowFieldCatalog
	{
		/// <summary>Template namespace root for released values: protected.call.completed_notes.</summary>
		public const string NamespaceRoot = "protected";

		/// <summary>protected.call.form: the parsed form data object, available when calls.callformdata is released.</summary>
		public const string ParsedFormName = "form";

		public const string FormDataFieldId = "calls.callformdata";

		/// <summary>External subject and record identifiers (a JSON object). Released whole or per key; renders as protected.call.subject_ids.</summary>
		public const string SubjectIdentifiersFieldId = "calls.subjectidentifiers";

		public const string SubjectIdentifiersTemplateName = "subject_ids";

		/// <summary>A release id for one call custom field: calls.udf#&lt;field name&gt; (renders as protected.call.udf.&lt;name&gt;).</summary>
		public const string UdfFieldPrefix = "calls.udf#";

		public const string UdfTemplateName = "udf";

		/// <summary>The ADP catalog field every custom field value is enveloped under (row key: the UdfFieldValueId).</summary>
		public const string UdfValueCatalogFieldId = "udffieldvalues.value";

		/// <summary>Separates a field id from a sub-field key: calls.subjectidentifiers#ehr_client_id.</summary>
		public const char SubFieldSeparator = '#';

		private static readonly System.Text.RegularExpressions.Regex UdfName =
			new System.Text.RegularExpressions.Regex("^[a-z_][a-z0-9_]{0,99}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

		private static readonly IReadOnlyList<ProtectedWorkflowField> CallFields = new List<ProtectedWorkflowField>
		{
			new ProtectedWorkflowField("calls.name", "name", c => c.Name),
			new ProtectedWorkflowField("calls.type", "type", c => c.Type),
			new ProtectedWorkflowField("calls.natureofcall", "nature", c => c.NatureOfCall),
			new ProtectedWorkflowField("calls.notes", "notes", c => c.Notes),
			new ProtectedWorkflowField("calls.completednotes", "completed_notes", c => c.CompletedNotes),
			new ProtectedWorkflowField("calls.address", "address", c => c.Address),
			new ProtectedWorkflowField("calls.geolocationdata", "geo_location", c => c.GeoLocationData),
			new ProtectedWorkflowField("calls.w3w", "w3w", c => c.W3W),
			new ProtectedWorkflowField("calls.contactname", "contact_name", c => c.ContactName),
			new ProtectedWorkflowField("calls.contactnumber", "contact_number", c => c.ContactNumber),
			new ProtectedWorkflowField("calls.sourceidentifier", "source_identifier", c => c.SourceIdentifier),
			new ProtectedWorkflowField("calls.incidentnumber", "incident_number", c => c.IncidentNumber),
			new ProtectedWorkflowField("calls.externalidentifier", "external_id", c => c.ExternalIdentifier),
			new ProtectedWorkflowField("calls.referencenumber", "reference_number", c => c.ReferenceNumber),
			new ProtectedWorkflowField(FormDataFieldId, "form_data", c => c.CallFormData),
			new ProtectedWorkflowField("calls.deletedreason", "deleted_reason", c => c.DeletedReason),
			new ProtectedWorkflowField(SubjectIdentifiersFieldId, SubjectIdentifiersTemplateName, c => c.SubjectIdentifiers)
		};

		public static bool IsSupportedTrigger(int triggerEventType) =>
			triggerEventType == (int)WorkflowTriggerEventType.CallAdded ||
			triggerEventType == (int)WorkflowTriggerEventType.CallUpdated ||
			triggerEventType == (int)WorkflowTriggerEventType.CallClosed;

		/// <summary>Disclosure entity type for the trigger ("call"), or null when unsupported.</summary>
		public static string EntityTypeFor(int triggerEventType) =>
			IsSupportedTrigger(triggerEventType) ? ProtectedWorkflowDefaults.CallEntityType : null;

		/// <summary>The template sub-namespace for the trigger's entity ("call" -> protected.call).</summary>
		public static string EntityNamespaceFor(int triggerEventType) => EntityTypeFor(triggerEventType);

		public static IReadOnlyList<ProtectedWorkflowField> FieldsFor(int triggerEventType) =>
			IsSupportedTrigger(triggerEventType) ? CallFields : Array.Empty<ProtectedWorkflowField>();

		public static ProtectedWorkflowField Find(int triggerEventType, string fieldId) =>
			FieldsFor(triggerEventType).FirstOrDefault(f => string.Equals(f.FieldId, fieldId, StringComparison.OrdinalIgnoreCase));

		/// <summary>Every call field id, for parity checks against the ADP catalog.</summary>
		public static IReadOnlyList<string> CallFieldIds => CallFields.Select(f => f.FieldId).ToList();

		/// <summary>The release id of one call custom field (lower case, as stored on the release).</summary>
		public static string UdfFieldId(string udfName) => UdfFieldPrefix + (udfName ?? string.Empty).Trim().ToLowerInvariant();

		/// <summary>The release id of one subject identifier key.</summary>
		public static string SubjectIdentifierFieldId(string key) => SubjectIdentifiersFieldId + SubFieldSeparator + (key ?? string.Empty).Trim().ToLowerInvariant();

		/// <summary>The custom field name of a calls.udf#name id, or null.</summary>
		public static string ParseUdfName(string fieldId) =>
			fieldId != null && fieldId.StartsWith(UdfFieldPrefix, StringComparison.OrdinalIgnoreCase) ? fieldId.Substring(UdfFieldPrefix.Length) : null;

		/// <summary>
		/// Splits a release's allow-list into what the runtime reads: whole call columns, the subject identifiers (whole or
		/// per key) and custom fields. Unknown or malformed ids, and a whole-field id listed with its own sub-field ids, are
		/// reported rather than guessed at.
		/// </summary>
		public static ProtectedReleaseFieldPlan Plan(int triggerEventType, IEnumerable<string> fieldIds)
		{
			var plan = new ProtectedReleaseFieldPlan();
			foreach (var id in WorkflowProtectedRelease.NormalizeFieldIds(fieldIds))
			{
				var udf = ParseUdfName(id);
				if (udf != null)
				{
					if (IsSupportedTrigger(triggerEventType) && UdfName.IsMatch(udf))
						plan.UdfNames.Add(udf);
					else
						plan.Unknown.Add(id);
					continue;
				}

				var separator = id.IndexOf(SubFieldSeparator);
				if (separator > 0)
				{
					var baseId = id.Substring(0, separator);
					var key = id.Substring(separator + 1);
					if (baseId == SubjectIdentifiersFieldId && IsSupportedTrigger(triggerEventType) && ProtectedStepOptions.SubjectKeyPattern.IsMatch(key))
						plan.SubjectIdentifierKeys.Add(key);
					else
						plan.Unknown.Add(id);
					continue;
				}

				var field = Find(triggerEventType, id);
				if (field == null)
					plan.Unknown.Add(id);
				else if (field.FieldId == SubjectIdentifiersFieldId)
					plan.SubjectIdentifiersWhole = true;
				else
					plan.Columns.Add(field);
			}

			return plan;
		}
	}

	/// <summary>What a release's allow-list asks the runtime to read (see <see cref="ProtectedWorkflowFieldCatalog.Plan"/>).</summary>
	public sealed class ProtectedReleaseFieldPlan
	{
		/// <summary>Whole call columns (never the subject identifiers, which are projected separately).</summary>
		public List<ProtectedWorkflowField> Columns { get; } = new List<ProtectedWorkflowField>();

		public bool SubjectIdentifiersWhole { get; set; }

		public SortedSet<string> SubjectIdentifierKeys { get; } = new SortedSet<string>(StringComparer.Ordinal);

		/// <summary>Custom field names, lower case.</summary>
		public List<string> UdfNames { get; } = new List<string>();

		public List<string> Unknown { get; } = new List<string>();

		/// <summary>The whole subject identifiers field and some of its keys are both listed: one or the other.</summary>
		public bool HasConflict => SubjectIdentifiersWhole && SubjectIdentifierKeys.Count > 0;

		/// <summary>True when the subject identifiers envelope is decrypted (whole, or to project keys from it).</summary>
		public bool ReadsSubjectIdentifiers => SubjectIdentifiersWhole || SubjectIdentifierKeys.Count > 0;

		public bool IsEmpty => Columns.Count == 0 && !ReadsSubjectIdentifiers && UdfNames.Count == 0;
	}
}
