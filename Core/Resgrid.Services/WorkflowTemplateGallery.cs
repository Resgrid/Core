using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>One step of a gallery template: everything but the credential, which the department picks.</summary>
	public sealed class WorkflowGalleryStep
	{
		public WorkflowActionType ActionType { get; init; }
		public string ActionConfig { get; init; }
		public string OutputTemplate { get; init; }
		public string ConditionExpression { get; init; }
	}

	/// <summary>A ready-made workflow: a trigger and its steps, with placeholder destinations and no real data.</summary>
	public sealed class WorkflowGalleryTemplate
	{
		public string Key { get; init; }
		public WorkflowTriggerEventType Trigger { get; init; }

		/// <summary>Only offered when the department has Protected Workflows enabled (the EHR samples).</summary>
		public bool RequiresProtectedWorkflows { get; init; }

		/// <summary>The release field ids the template reads, for the Draft release created with it.</summary>
		public IReadOnlyList<string> ReleaseFieldIds { get; init; } = Array.Empty<string>();

		public IReadOnlyList<WorkflowGalleryStep> Steps { get; init; } = Array.Empty<WorkflowGalleryStep>();

		/// <summary>Localization keys (Workflows area): Gallery_{Key}_Name and Gallery_{Key}_Description.</summary>
		public string NameKey => $"Gallery_{Key}_Name";
		public string DescriptionKey => $"Gallery_{Key}_Description";
	}

	/// <summary>
	/// The workflow template gallery. Templates escape every value they place in a structured payload (json_escape,
	/// xml_escape, hl7_escape) and use run.idempotency_key so a retried delivery is recognized. The EHR samples (FHIR R4
	/// transaction Bundle, HL7 v2.5.1 MDM^T02) are only offered to departments with Protected Workflows enabled; they read
	/// protected values, so they only ever send under an approved release.
	/// </summary>
	public static class WorkflowTemplateGallery
	{
		public const string WebhookCallAdded = "webhook_call_added";
		public const string EmailCallClosed = "email_call_closed";
		public const string FhirEncounterBundle = "fhir_encounter_bundle";
		public const string Hl7MdmT02 = "hl7_mdm_t02";

		/// <summary>Placeholder destinations: an administrator replaces them before anything can be approved or enabled.</summary>
		public const string FhirPlaceholderUrl = "https://ehr.example.org/fhir/r4";
		public const string Hl7PlaceholderUrl = "https://interface-engine.example.org/hl7/mdm";

		public static readonly IReadOnlyList<WorkflowGalleryTemplate> All = new List<WorkflowGalleryTemplate>
		{
			new WorkflowGalleryTemplate
			{
				Key = WebhookCallAdded,
				Trigger = WorkflowTriggerEventType.CallAdded,
				Steps = new[]
				{
					new WorkflowGalleryStep
					{
						ActionType = WorkflowActionType.CallApiPost,
						ActionConfig = JsonConvert.SerializeObject(new { url = "https://hooks.example.org/resgrid/calls", contentType = "application/json", timeoutSeconds = 30, idempotencyHeader = "Idempotency-Key" }),
						OutputTemplate = WebhookTemplate
					}
				}
			},
			new WorkflowGalleryTemplate
			{
				Key = EmailCallClosed,
				Trigger = WorkflowTriggerEventType.CallClosed,
				Steps = new[]
				{
					new WorkflowGalleryStep
					{
						ActionType = WorkflowActionType.SendEmail,
						ActionConfig = JsonConvert.SerializeObject(new { to = "dispatch-supervisors@example.org", subject = "Call {{ call.number }} closed" }),
						OutputTemplate = EmailTemplate
					}
				}
			},
			new WorkflowGalleryTemplate
			{
				Key = FhirEncounterBundle,
				Trigger = WorkflowTriggerEventType.CallClosed,
				RequiresProtectedWorkflows = true,
				ReleaseFieldIds = new[] { ProtectedWorkflowFieldCatalog.SubjectIdentifierFieldId("ehr_client_id") },
				Steps = new[]
				{
					new WorkflowGalleryStep
					{
						ActionType = WorkflowActionType.CallApiPost,
						ActionConfig = JsonConvert.SerializeObject(new
						{
							url = FhirPlaceholderUrl,
							contentType = "application/fhir+json",
							timeoutSeconds = 30,
							successRule = new { type = ProtectedSuccessRule.FhirOperationOutcome },
							responseCapture = new[] { new { source = ProtectedCaptureEntry.FhirLocationId, expression = "Encounter", key = "ehr_encounter_id" } },
							idempotencyHeader = "Idempotency-Key"
						}),
						OutputTemplate = FhirTemplate
					}
				}
			},
			new WorkflowGalleryTemplate
			{
				Key = Hl7MdmT02,
				Trigger = WorkflowTriggerEventType.CallClosed,
				RequiresProtectedWorkflows = true,
				ReleaseFieldIds = new[] { ProtectedWorkflowFieldCatalog.SubjectIdentifierFieldId("ehr_client_id") },
				Steps = new[]
				{
					new WorkflowGalleryStep
					{
						ActionType = WorkflowActionType.CallApiPost,
						ActionConfig = JsonConvert.SerializeObject(new
						{
							url = Hl7PlaceholderUrl,
							contentType = ProtectedPayloadValidator.Hl7MediaType,
							timeoutSeconds = 30,
							successRule = new { type = ProtectedSuccessRule.Hl7Ack }
						}),
						OutputTemplate = Hl7Template
					}
				}
			}
		};

		public static IReadOnlyList<WorkflowGalleryTemplate> Available(bool protectedWorkflowsEnabled) =>
			All.Where(t => protectedWorkflowsEnabled || !t.RequiresProtectedWorkflows).ToList();

		public static WorkflowGalleryTemplate Find(string key) =>
			All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

		// ── Templates ─────────────────────────────────────────────────────────────────────────────────

		private const string WebhookTemplate =
@"{
  ""event"": ""call.added"",
  ""delivery_id"": ""{{ run.idempotency_key }}"",
  ""department"": ""{{ department.name | json_escape }}"",
  ""call"": {
    ""id"": {{ call.id }},
    ""number"": ""{{ call.number | json_escape }}"",
    ""name"": ""{{ call.name | json_escape }}"",
    ""priority"": ""{{ call.priority_text | json_escape }}"",
    ""address"": ""{{ call.address | json_escape }}"",
    ""logged_on"": ""{{ call.logged_on | fhir_datetime }}""
  }
}";

		private const string EmailTemplate =
@"<p>Call <strong>{{ call.number | xml_escape }}</strong> ({{ call.name | xml_escape }}) was closed.</p>
<p>Logged {{ call.logged_on }}, closed {{ call.closed_on }}.</p>";

		/// <summary>
		/// A FHIR R4 transaction: the Encounter (class FLD, finished, dispatch to close) conditionally created on the call's
		/// identifier, plus one Observation per released call custom field. The Bundle identifier is run.idempotency_key.
		/// </summary>
		private const string FhirTemplate =
@"{{ k = run.idempotency_key }}{{ encounter = ""urn:uuid:"" + (k | string.slice 0 8) + ""-"" + (k | string.slice 8 4) + ""-"" + (k | string.slice 12 4) + ""-"" + (k | string.slice 16 4) + ""-"" + (k | string.slice 20 12) }}{
  ""resourceType"": ""Bundle"",
  ""type"": ""transaction"",
  ""identifier"": { ""system"": ""https://resgrid.com/workflow-delivery"", ""value"": ""{{ k }}"" },
  ""entry"": [
    {
      ""fullUrl"": ""{{ encounter }}"",
      ""resource"": {
        ""resourceType"": ""Encounter"",
        ""identifier"": [ { ""system"": ""https://resgrid.com/call"", ""value"": ""{{ call.id }}"" } ],
        ""status"": ""finished"",
        ""class"": { ""system"": ""http://terminology.hl7.org/CodeSystem/v3-ActCode"", ""code"": ""FLD"", ""display"": ""field"" },
        ""type"": [ { ""text"": ""Crisis field response"" } ],
        ""subject"": { ""reference"": ""Patient/{{ protected.call.subject_ids.ehr_client_id | json_escape }}"" },
        ""period"": { ""start"": ""{{ (call.dispatch_on ?? call.logged_on) | fhir_datetime }}"", ""end"": ""{{ call.closed_on | fhir_datetime }}"" }
      },
      ""request"": { ""method"": ""POST"", ""url"": ""Encounter"", ""ifNoneExist"": ""identifier=https://resgrid.com/call|{{ call.id }}"" }
    }{{ for field in protected.call.udf }},
    {
      ""resource"": {
        ""resourceType"": ""Observation"",
        ""status"": ""final"",
        ""code"": { ""text"": ""{{ field.key | json_escape }}"" },
        ""subject"": { ""reference"": ""Patient/{{ protected.call.subject_ids.ehr_client_id | json_escape }}"" },
        ""encounter"": { ""reference"": ""{{ encounter }}"" },
        ""effectiveDateTime"": ""{{ call.closed_on | fhir_datetime }}"",
        ""valueString"": ""{{ field.value | json_escape }}""
      },
      ""request"": { ""method"": ""POST"", ""url"": ""Observation"" }
    }{{ end }}
  ]
}";

		/// <summary>
		/// HL7 v2.5.1 MDM^T02 for an interface engine: MSH-10 is the idempotency key, PID-3 the EHR client id, PV1 the
		/// encounter class and times, TXA the "Crisis Field Response" document, one OBX per released call custom field.
		/// Every value goes through hl7_escape. Line breaks between segments become CR when the payload is prepared.
		/// </summary>
		/// <remarks>A computed property, not a field: <see cref="All"/> is initialized first and reads it.</remarks>
		private static string Hl7Template => string.Join("\n", new[]
		{
			Segment("MSH", "^~\\&", "RESGRID", "{{ department.code | hl7_escape }}", "EHR", "EHR", "{{ timestamp.utc_now | hl7_ts }}", "", "MDM^T02^MDM_T02",
				"{{ run.idempotency_key }}", "P", "2.5.1"),
			Segment("EVN", "T02", "{{ timestamp.utc_now | hl7_ts }}"),
			Segment("PID", "1", "", "{{ protected.call.subject_ids.ehr_client_id | hl7_escape }}^^^EHR^MR"),
			Pv1(),
			Txa(),
			"{{ n = 0 }}{{ for field in protected.call.udf }}{{ n = n + 1 }}" +
				Segment("OBX", "{{ n }}", "TX", "{{ field.key | hl7_escape }}", "", "{{ field.value | hl7_escape }}", "", "", "", "", "", "F"),
			"{{ end }}"
		});

		private static string Segment(string id, params string[] fields) => id + "|" + string.Join("|", fields);

		/// <summary>PV1: class E (emergency), the call number as the visit number (PV1-19), dispatch (PV1-44) and close (PV1-45).</summary>
		private static string Pv1()
		{
			var fields = Enumerable.Repeat(string.Empty, 45).ToArray();
			fields[0] = "1";
			fields[1] = "E";
			fields[18] = "{{ call.number | hl7_escape }}";
			fields[43] = "{{ (call.dispatch_on ?? call.logged_on) | hl7_ts }}";
			fields[44] = "{{ call.closed_on | hl7_ts }}";
			return Segment("PV1", fields);
		}

		/// <summary>TXA: document type (TXA-2), content presentation TX (TXA-3), activity time (TXA-4), unique document number (TXA-12), completion AU (TXA-17).</summary>
		private static string Txa()
		{
			var fields = Enumerable.Repeat(string.Empty, 17).ToArray();
			fields[0] = "1";
			fields[1] = "CFR^Crisis Field Response";
			fields[2] = "TX";
			fields[3] = "{{ call.closed_on | hl7_ts }}";
			fields[11] = "{{ run.idempotency_key }}";
			fields[16] = "AU";
			return Segment("TXA", fields);
		}
	}
}
