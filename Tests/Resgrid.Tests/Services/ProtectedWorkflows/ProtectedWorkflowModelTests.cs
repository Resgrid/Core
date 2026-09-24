using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>The pure pieces: the configuration fingerprint, the disclosure hash chain, the validator and the field catalog.</summary>
	[TestFixture]
	public class ProtectedWorkflowModelTests
	{
		private static List<WorkflowStep> Steps() => new List<WorkflowStep>
		{
			new WorkflowStep
			{
				WorkflowStepId = "s1", StepOrder = 1, IsEnabled = true, ActionType = (int)WorkflowActionType.CallApiPut,
				OutputTemplate = "{{ protected.call.completed_notes }}", ConditionExpression = null, WorkflowCredentialId = "cred-1",
				ActionConfig = "{\"Url\":\"https://org.crm.dynamics.com/api\",\"Headers\":{\"b\":\"2\",\"a\":\"1\"}}"
			}
		};

		private static readonly string[] Fields = { "calls.completednotes" };

		private static string Fingerprint(List<WorkflowStep> steps, IEnumerable<string> fields = null, string host = "org.crm.dynamics.com",
			string tokenHost = null, int trigger = (int)WorkflowTriggerEventType.CallClosed) =>
			ProtectedWorkflowFingerprint.Compute(trigger, steps, fields ?? Fields, host, tokenHost);

		// ── Fingerprint ─────────────────────────────────────────────────────────────────────────────

		[Test]
		public void every_fingerprint_input_changes_the_fingerprint()
		{
			var baseline = Fingerprint(Steps());

			var variants = new Dictionary<string, string>
			{
				["trigger"] = Fingerprint(Steps(), trigger: (int)WorkflowTriggerEventType.CallUpdated),
				["action type"] = Fingerprint(Mutate(s => s.ActionType = (int)WorkflowActionType.CallApiPost)),
				["step order"] = Fingerprint(Mutate(s => s.StepOrder = 2)),
				["output template"] = Fingerprint(Mutate(s => s.OutputTemplate += "!")),
				["condition"] = Fingerprint(Mutate(s => s.ConditionExpression = "{{ true }}")),
				["url"] = Fingerprint(Mutate(s => s.ActionConfig = s.ActionConfig.Replace("/api", "/api2"))),
				["header"] = Fingerprint(Mutate(s => s.ActionConfig = s.ActionConfig.Replace("\"1\"", "\"9\""))),
				["credential id"] = Fingerprint(Mutate(s => s.WorkflowCredentialId = "cred-2")),
				["enabled"] = Fingerprint(Mutate(s => s.IsEnabled = false)),
				["fields"] = Fingerprint(Steps(), new[] { "calls.completednotes", "calls.callformdata" }),
				["destination host"] = Fingerprint(Steps(), host: "other.crm.dynamics.com"),
				["token host"] = Fingerprint(Steps(), tokenHost: "login.microsoftonline.com")
			};

			foreach (var variant in variants)
				variant.Value.Should().NotBe(baseline, variant.Key);
		}

		[Test]
		public void the_fingerprint_ignores_json_key_order_host_casing_and_line_endings()
		{
			var reordered = Mutate(s =>
			{
				s.ActionConfig = "{\"Headers\":{\"a\":\"1\",\"b\":\"2\"},\"Url\":\"https://org.crm.dynamics.com/api\"}";
			});
			Fingerprint(reordered, new[] { "CALLS.COMPLETEDNOTES" }, "ORG.crm.dynamics.com").Should().Be(Fingerprint(Steps()));

			Fingerprint(Mutate(s => s.OutputTemplate = "line1\r\nline2")).Should().Be(Fingerprint(Mutate(s => s.OutputTemplate = "line1\nline2")));
		}

		[Test]
		public void date_looking_values_are_fingerprinted_verbatim()
		{
			// Parsed into a local DateTime, a date-looking header would make the fingerprint depend on the host's time
			// zone, and web and worker hosts must agree byte for byte.
			var dated = Mutate(s => s.ActionConfig = "{\"Url\":\"https://org.crm.dynamics.com/api\",\"Headers\":{\"X-Since\":\"2026-09-24T10:00:00+02:00\"}}");
			var utc = Mutate(s => s.ActionConfig = "{\"Url\":\"https://org.crm.dynamics.com/api\",\"Headers\":{\"X-Since\":\"2026-09-24T08:00:00Z\"}}");

			Fingerprint(dated).Should().MatchRegex("^[0-9a-f]{64}$");
			Fingerprint(dated).Should().NotBe(Fingerprint(utc), "the same instant written differently is a different configuration");
		}

		private static List<WorkflowStep> Mutate(Action<WorkflowStep> change)
		{
			var steps = Steps();
			change(steps[0]);
			return steps;
		}

		// ── Disclosure chain ────────────────────────────────────────────────────────────────────────

		private static List<ProtectedWorkflowDisclosure> Chain(int count)
		{
			var rows = new List<ProtectedWorkflowDisclosure>();
			for (var i = 0; i < count; i++)
			{
				var row = new ProtectedWorkflowDisclosure
				{
					ProtectedWorkflowDisclosureId = "d" + i,
					DepartmentId = 42,
					RecordType = ProtectedWorkflowRecordTypes.Disclosure,
					WorkflowId = "wf-1",
					EntityType = "call",
					EntityId = (1000 + i).ToString(),
					FieldIds = "[\"calls.completednotes\"]",
					DestinationHost = "org.crm.dynamics.com",
					PayloadSha256 = new string('a', 64),
					PayloadBytes = 120 + i,
					HttpStatus = 204,
					Outcome = ProtectedWorkflowDisclosureOutcomes.Sent,
					OccurredOn = new DateTime(2026, 9, 24, 10, 0, i, 123, DateTimeKind.Utc).AddTicks(4567)
				};
				ProtectedWorkflowDisclosureChain.Link(row, rows.LastOrDefault());
				rows.Add(row);
			}
			return rows;
		}

		[Test]
		public void hashes_chain_from_the_genesis_value()
		{
			var rows = Chain(4);

			rows[0].PrevHash.Should().Be(ProtectedWorkflowDisclosureChain.GenesisHash);
			rows[0].ChainSequence.Should().Be(1);
			for (var i = 1; i < rows.Count; i++)
			{
				rows[i].PrevHash.Should().Be(rows[i - 1].Hash);
				rows[i].ChainSequence.Should().Be(i + 1);
			}
			rows.Should().OnlyContain(r => r.OccurredOn.Ticks % TimeSpan.TicksPerMillisecond == 0, "stored at the precision it is hashed at");
			ProtectedWorkflowDisclosureChain.Verify(rows).Should().BeNull();
		}

		[Test]
		public void a_chain_read_back_from_a_database_still_verifies()
		{
			// Unspecified kind (as Dapper hands back a datetime2) must hash identically.
			var rows = Chain(3).Select(ProtectedWorkflowHarness.Clone).ToList();
			foreach (var row in rows)
				row.OccurredOn = DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Unspecified);

			ProtectedWorkflowDisclosureChain.Verify(rows).Should().BeNull();
		}

		private static readonly (string Name, Action<ProtectedWorkflowDisclosure> Tamper)[] Tampers =
		{
			("outcome", r => r.Outcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp),
			("http status", r => r.HttpStatus = 200),
			("field ids", r => r.FieldIds = "[\"calls.completednotes\",\"calls.notes\"]"),
			("host", r => r.DestinationHost = "evil.example"),
			("payload hash", r => r.PayloadSha256 = new string('b', 64)),
			("bytes", r => r.PayloadBytes = 1),
			("entity", r => r.EntityId = "9999"),
			("time", r => r.OccurredOn = r.OccurredOn.AddSeconds(1)),
			("test flag", r => r.IsTest = true),
			("actor", r => r.ActorUserId = "someone"),
			("detail", r => r.Detail = "x"),
			("prev hash", r => r.PrevHash = ProtectedWorkflowDisclosureChain.GenesisHash)
		};

		private static string[] TamperNames() => Tampers.Select(t => t.Name).ToArray();

		[TestCaseSource(nameof(TamperNames))]
		public void tampering_with_any_row_breaks_verification_at_that_row(string field)
		{
			var rows = Chain(5);
			Tampers.Single(t => t.Name == field).Tamper(rows[2]);

			ProtectedWorkflowDisclosureChain.Verify(rows).Should().Be(3, field);
		}

		[Test]
		public void removing_or_reordering_rows_breaks_verification()
		{
			var removed = Chain(5);
			removed.RemoveAt(2);
			ProtectedWorkflowDisclosureChain.Verify(removed).Should().Be(4);

			var truncatedHead = Chain(3);
			truncatedHead.RemoveAt(0);
			ProtectedWorkflowDisclosureChain.Verify(truncatedHead).Should().Be(2);
		}

		// ── Validator ───────────────────────────────────────────────────────────────────────────────

		private static readonly IReadOnlyDictionary<string, int> Credentials = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["cred-1"] = (int)WorkflowCredentialType.HttpBearer,
			["basic"] = (int)WorkflowCredentialType.HttpBasic,
			["oauth"] = (int)WorkflowCredentialType.OAuth2ClientCredentials,
			["smtp"] = (int)WorkflowCredentialType.Smtp
		};

		private static IEnumerable<string> Errors(List<WorkflowStep> steps, bool allowBasic = false, int trigger = (int)WorkflowTriggerEventType.CallClosed) =>
			ProtectedWorkflowValidator.Validate(trigger, steps, Credentials, allowBasic).Errors.Select(e => e.Code);

		[Test]
		public void a_valid_workflow_pins_one_host_and_one_credential()
		{
			var result = ProtectedWorkflowValidator.Validate((int)WorkflowTriggerEventType.CallClosed, Steps(), Credentials, false);

			result.IsValid.Should().BeTrue();
			result.DestinationHost.Should().Be("org.crm.dynamics.com");
			result.WorkflowCredentialId.Should().Be("cred-1");
		}

		[Test]
		public void only_api_post_or_put_steps_are_allowed_even_when_disabled()
		{
			var steps = Steps();
			steps.Add(new WorkflowStep { WorkflowStepId = "mail", StepOrder = 2, IsEnabled = false, ActionType = (int)WorkflowActionType.SendEmail });

			Errors(steps).Should().Contain(ProtectedWorkflowValidator.ActionNotAllowed);
			Errors(Mutate(s => s.ActionType = (int)WorkflowActionType.CallApiGet)).Should().Contain(ProtectedWorkflowValidator.ActionNotAllowed);
		}

		[Test]
		public void destinations_must_be_one_literal_https_host()
		{
			Errors(Mutate(s => s.ActionConfig = "{\"Url\":\"http://org.crm.dynamics.com/api\"}")).Should().Contain(ProtectedWorkflowValidator.SchemeNotHttps);
			Errors(Mutate(s => s.ActionConfig = "{\"Url\":\"https://{{ call.external_id }}.crm.dynamics.com/api\"}")).Should().Contain(ProtectedWorkflowValidator.HostNotLiteral);
			Errors(Mutate(s => s.ActionConfig = "{\"Url\":\"https://user@org.crm.dynamics.com/api\"}")).Should().Contain(ProtectedWorkflowValidator.HostNotLiteral);
			Errors(Mutate(s => s.ActionConfig = "{}")).Should().Contain(ProtectedWorkflowValidator.UrlRequired);

			var twoHosts = Steps();
			twoHosts.Add(new WorkflowStep { WorkflowStepId = "s2", StepOrder = 2, IsEnabled = true, ActionType = (int)WorkflowActionType.CallApiPost, WorkflowCredentialId = "cred-1", ActionConfig = "{\"Url\":\"https://other.example.com/x\"}" });
			Errors(twoHosts).Should().Contain(ProtectedWorkflowValidator.HostMismatch);
		}

		[Test]
		public void credentials_must_be_present_shared_and_of_an_allowed_type()
		{
			Errors(Mutate(s => s.WorkflowCredentialId = null)).Should().Contain(ProtectedWorkflowValidator.CredentialRequired);
			Errors(Mutate(s => s.WorkflowCredentialId = "smtp")).Should().Contain(ProtectedWorkflowValidator.CredentialNotAllowed);
			Errors(Mutate(s => s.WorkflowCredentialId = "missing")).Should().Contain(ProtectedWorkflowValidator.CredentialMissing);
			Errors(Mutate(s => s.WorkflowCredentialId = "basic")).Should().Contain(ProtectedWorkflowValidator.CredentialNotAllowed, "Basic is off unless configured");
			Errors(Mutate(s => s.WorkflowCredentialId = "basic"), allowBasic: true).Should().BeEmpty();
			Errors(Mutate(s => s.WorkflowCredentialId = "oauth")).Should().BeEmpty();

			var mixed = Steps();
			mixed.Add(new WorkflowStep { WorkflowStepId = "s2", StepOrder = 2, IsEnabled = true, ActionType = (int)WorkflowActionType.CallApiPost, WorkflowCredentialId = "oauth", ActionConfig = "{\"Url\":\"https://org.crm.dynamics.com/x\"}" });
			Errors(mixed).Should().Contain(ProtectedWorkflowValidator.CredentialMismatch);
		}

		[Test]
		public void protected_values_are_refused_in_conditions_urls_and_headers()
		{
			Errors(Mutate(s => s.ConditionExpression = "{{ protected.call.notes != '' }}")).Should().Contain(ProtectedWorkflowValidator.ProtectedInCondition);
			Errors(Mutate(s => s.ActionConfig = "{\"Url\":\"https://org.crm.dynamics.com/{{ protected.call.external_id }}\"}")).Should().Contain(ProtectedWorkflowValidator.ProtectedInActionConfig);
			Errors(Mutate(s => s.ActionConfig = "{\"Url\":\"https://org.crm.dynamics.com/x\",\"Headers\":{\"X\":\"{{ protected[\\\"call\\\"] }}\"}}")).Should().Contain(ProtectedWorkflowValidator.ProtectedInActionConfig);
		}

		[Test]
		public void the_word_protected_in_plain_text_is_not_a_template_reference()
		{
			ProtectedWorkflowValidator.ReferencesProtectedNamespace("{\"note\":\"This field is protected.\"}").Should().BeFalse();
			ProtectedWorkflowValidator.ReferencesProtectedNamespace("{{ call.is_protected }}").Should().BeFalse();
			ProtectedWorkflowValidator.ReferencesProtectedNamespace("{{ protected.call.notes }}").Should().BeTrue();
		}

		[Test]
		public void only_call_triggers_are_supported_in_v1()
		{
			Errors(Steps(), trigger: (int)WorkflowTriggerEventType.UnitStatusChanged).Should().Contain(ProtectedWorkflowValidator.TriggerNotSupported);
			ProtectedWorkflowFieldCatalog.FieldsFor((int)WorkflowTriggerEventType.UnitStatusChanged).Should().BeEmpty();
		}

		// ── Catalog ─────────────────────────────────────────────────────────────────────────────────

		[Test]
		public void the_release_catalog_matches_the_adp_call_catalog_and_the_call_template_names()
		{
			ProtectedWorkflowFieldCatalog.CallFieldIds.Should().BeEquivalentTo(ProtectedReadService.CallFieldAccessors.Keys,
				"a field the ADP catalog protects must be releasable, and nothing else");

			var call = new Call { Name = "n", Type = "t", NatureOfCall = "na", Notes = "no", CompletedNotes = "cn", Address = "a", GeoLocationData = "g", W3W = "w",
				ContactName = "cna", ContactNumber = "cnu", SourceIdentifier = "si", IncidentNumber = "in", ExternalIdentifier = "ei", ReferenceNumber = "rn",
				CallFormData = "fd", DeletedReason = "dr" };
			foreach (var field in ProtectedWorkflowFieldCatalog.FieldsFor((int)WorkflowTriggerEventType.CallAdded))
				field.GetCallValue(call).Should().Be(ProtectedReadService.CallFieldAccessors[field.FieldId].Get(call), field.FieldId);

			ProtectedWorkflowFieldCatalog.Find((int)WorkflowTriggerEventType.CallClosed, "calls.completednotes").TemplateName.Should().Be("completed_notes");
			ProtectedWorkflowFieldCatalog.Find((int)WorkflowTriggerEventType.CallClosed, "calls.callformdata").TemplateName.Should().Be("form_data");
		}

		[Test]
		public void call_form_data_is_read_from_the_form_builder_field_array()
		{
			var fields = Newtonsoft.Json.Linq.JArray.Parse(
				"[{\"type\":\"text\",\"name\":\"outcome\",\"userData\":[\"Referred\"]}," +
				"{\"type\":\"checkbox-group\",\"name\":\"services\",\"userData\":[\"a\",\"b\"]}," +
				"{\"type\":\"text\",\"name\":\"blank\"}," +
				"{\"type\":\"header\",\"label\":\"No name\"}]");

			var values = ProtectedWorkflowRuntime.FormValues(fields);

			values["outcome"].Should().Be("Referred");
			((Scriban.Runtime.ScriptArray)values["services"]).Cast<object>().Should().Equal("a", "b");
			values["blank"].Should().Be(string.Empty);
			values.Count.Should().Be(3, "a field without a name has nothing to address it by");
		}

		[Test]
		public void the_broker_accepts_the_protected_workflow_purpose()
		{
			Resgrid.Config.DataProtectionConfig.BrokerWorkloadPurposes.Split(',').Should().Contain(ProtectedWorkflowDefaults.WorkloadPurpose);
		}

		[Test]
		public void log_text_carries_a_code_and_a_type_never_a_message_unless_asked_and_scrubbed()
		{
			var ex = new InvalidOperationException("secret body rgdp:1:1:QUJDRA==");

			ProtectedWorkflowLogText.Error("code", ex).Should().Be("code: System.InvalidOperationException");
			ProtectedWorkflowLogText.Error("code", ex, includeMessage: true).Should().NotContain("rgdp:1:1:QUJDRA==");
		}
	}
}
