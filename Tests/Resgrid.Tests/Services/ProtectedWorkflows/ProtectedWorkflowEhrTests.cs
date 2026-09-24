using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Services;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// Protected Workflows for EHR integration, end to end through the real WorkflowService, runtime and write pipeline:
	/// subject identifiers, per-field custom field release with sensitivity tags, response capture, idempotency, payload
	/// validation, the retry policy and the gallery samples.
	/// </summary>
	[TestFixture]
	public class ProtectedWorkflowEhrTests
	{
		private const string ClientSentinel = "CLIENT-SENTINEL-4471";
		private const string CaseSentinel = "CASE-SENTINEL-9902";
		private const string DispositionSentinel = "DISPOSITION-SENTINEL-17";
		private const string SubstanceSentinel = "SUBSTANCE-SENTINEL-83";
		private const string EncounterSentinel = "ENCOUNTER-SENTINEL-5520";

		private ProtectedWorkflowHarness _h;

		[SetUp]
		public void SetUp() => _h = new ProtectedWorkflowHarness();

		private void Configure(string template, object extraConfig = null, string contentType = "text/plain")
		{
			var config = JObject.FromObject(new { Url = ProtectedWorkflowHarness.Url, ContentType = contentType });
			if (extraConfig != null)
				config.Merge(JObject.FromObject(extraConfig));
			_h.Steps[0].ActionConfig = config.ToString(Formatting.None);
			_h.Steps[0].OutputTemplate = template;
		}

		private string Sent => _h.Capturing.Calls.Single().RenderedContent;

		private void AssertNothingPersistedContains(params string[] sentinels)
		{
			var persisted = _h.EverythingPersisted();
			foreach (var sentinel in sentinels)
				persisted.Should().NotContain(sentinel);
		}

		// ── Subject identifiers ─────────────────────────────────────────────────────────────────────

		[Test]
		public async Task only_allow_listed_subject_identifier_keys_reach_the_template()
		{
			_h.SetSubjectIdentifiers($"{{\"dynamics_case_id\":\"{CaseSentinel}\",\"ehr_client_id\":\"{ClientSentinel}\"}}");
			Configure("{{ protected.call.subject_ids.ehr_client_id }}|{{ protected.call.subject_ids.dynamics_case_id }}|{{ protected.call.completed_notes }}");
			_h.ActivateRelease("calls.subjectidentifiers#ehr_client_id");

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Completed, string.Join("; ", _h.Logs.Select(l => l.ErrorMessage)));
			Sent.Should().Be($"{ClientSentinel}||", "a key that is not allow-listed is dropped in memory, and nothing else was released");
			_h.Broker.Calls.Single().Items.Select(i => i.FieldId).Should().Equal("calls.subjectidentifiers");
			WorkflowProtectedRelease.ParseFieldIds(_h.DisclosureRecords.Single().FieldIds).Should().Equal("calls.subjectidentifiers#ehr_client_id");
			AssertNothingPersistedContains(ClientSentinel, CaseSentinel);
		}

		[Test]
		public async Task the_whole_subject_identifiers_field_releases_every_key()
		{
			_h.SetSubjectIdentifiers($"{{\"dynamics_case_id\":\"{CaseSentinel}\",\"ehr_client_id\":\"{ClientSentinel}\"}}");
			Configure("{{ protected.call.subject_ids.ehr_client_id }}|{{ protected.call.subject_ids.dynamics_case_id }}");
			_h.ActivateRelease("calls.subjectidentifiers");

			await _h.RunAsync();

			Sent.Should().Be($"{ClientSentinel}|{CaseSentinel}");
		}

		[Test]
		public async Task malformed_subject_identifiers_fail_the_step_as_failed_projection_and_send_nothing()
		{
			_h.SetSubjectIdentifiers("{\"ehr_client_id\":" + ClientSentinel + "}");
			Configure("{{ protected.call.subject_ids.ehr_client_id }}");
			_h.ActivateRelease("calls.subjectidentifiers#ehr_client_id");

			await _h.RunAsync();

			_h.Capturing.Calls.Should().BeEmpty();
			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedProjection);
			disclosure.Detail.Should().Be(ProtectedWorkflowErrorCodes.ProjectionFailed);
			_h.AttemptRecords.Should().BeEmpty("nothing was about to be sent");
			AssertNothingPersistedContains(ClientSentinel);
		}

		[Test]
		public void subject_identifier_rules_are_enforced()
		{
			CallSubjectIdentifiers.Validate(new Dictionary<string, string> { ["ehr_client_id"] = "123456" }).Should().BeEmpty();
			CallSubjectIdentifiers.Validate(new Dictionary<string, string> { ["EHR-Client"] = "1" }).Should().Contain(CallSubjectIdentifiers.InvalidKey);
			CallSubjectIdentifiers.Validate(new Dictionary<string, string> { [new string('k', 65)] = "1" }).Should().Contain(CallSubjectIdentifiers.InvalidKey);
			CallSubjectIdentifiers.Validate(new Dictionary<string, string> { ["k"] = new string('v', 257) }).Should().Contain(CallSubjectIdentifiers.ValueTooLong);
			CallSubjectIdentifiers.Validate(new Dictionary<string, string> { ["k"] = new string('v', 256) }).Should().BeEmpty();
			CallSubjectIdentifiers.Validate(Enumerable.Range(0, 21).ToDictionary(i => "k" + i, i => "v")).Should().Contain(CallSubjectIdentifiers.TooManyKeys);
			CallSubjectIdentifiers.Validate(Enumerable.Range(0, 20).ToDictionary(i => "k" + i, i => "v")).Should().BeEmpty();

			CallSubjectIdentifiers.Serialize(new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }).Should().Be("{\"a\":\"1\",\"b\":\"2\"}");
			CallSubjectIdentifiers.TryParse("{\"a\":1}", out _).Should().BeFalse("values are strings");
			CallSubjectIdentifiers.TryParse("[\"a\"]", out _).Should().BeFalse();
			CallSubjectIdentifiers.TryParse(null, out var empty).Should().BeTrue();
			empty.Should().BeEmpty();
		}

		[Test]
		public async Task a_release_cannot_list_the_whole_subject_identifiers_field_and_one_of_its_keys()
		{
			var result = await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.subjectidentifiers", "calls.subjectidentifiers#ehr_client_id" }
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });

			result.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.FieldConflict);

			(await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = new[] { "calls.subjectidentifiers#Not-A-Key" }
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA })).ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.UnknownField);
		}

		[Test]
		public async Task subject_identifiers_are_never_in_the_ordinary_call_context()
		{
			_h.SetSubjectIdentifiers($"{{\"ehr_client_id\":\"{ClientSentinel}\"}}");
			Configure("[{{ call.subject_identifiers }}{{ call.subject_ids }}{{ call.part2_consent_on_file }}]");

			await _h.RunAsync(); // no release: an ordinary run

			Sent.Should().Be("[false]");
		}

		// ── Call custom fields and sensitivity ──────────────────────────────────────────────────────

		[Test]
		public async Task only_allow_listed_custom_fields_are_decrypted_and_rendered()
		{
			_h.AddCustomField("disposition", UdfFieldSensitivity.None, DispositionSentinel);
			_h.AddCustomField("substance_use", UdfFieldSensitivity.None, SubstanceSentinel);
			Configure("{{ protected.call.udf.disposition }}|{{ protected.call.udf.substance_use }}");
			_h.ActivateRelease("calls.udf#disposition");

			await _h.RunAsync();

			Sent.Should().Be($"{DispositionSentinel}|");
			_h.Broker.Calls.Single().Items.Should().ContainSingle()
				.Which.Should().Match<ProtectedFieldOperationItem>(i => i.FieldId == "udffieldvalues.value" && i.RowKey == "udfv-disposition");
			AssertNothingPersistedContains(DispositionSentinel, SubstanceSentinel);
		}

		private async Task<ProtectedWorkflowCommandResult> DraftAndRequestAsync(IEnumerable<string> fields, ProtectedSensitiveAttestation sensitive = null)
		{
			var draft = await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, new ProtectedReleaseDraft
			{
				FieldIds = fields.ToList(),
				RecipientType = (int)ProtectedReleaseRecipientType.CoveredEntity,
				RecipientName = "County behavioral health EHR",
				Purpose = "Crisis response encounter documentation"
			}, new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA });
			draft.Success.Should().BeTrue(draft.ErrorCode);

			return await _h.Service.RequestApprovalAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, true,
				ProtectedWorkflowDefaults.WarningTextVersion, _h.StepsFingerprint(), ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminA), sensitive);
		}

		private static ProtectedSensitiveAttestation Attest(bool restricted = false, bool part2 = false) => new ProtectedSensitiveAttestation
		{
			Restricted = restricted,
			RestrictedVersion = ProtectedWorkflowDefaults.RestrictedAttestationVersion,
			Part2 = part2,
			Part2Version = ProtectedWorkflowDefaults.Part2AttestationVersion
		};

		[Test]
		public async Task a_restricted_field_needs_the_restricted_attestation()
		{
			_h.AddCustomField("safety_plan", UdfFieldSensitivity.Restricted, "x");

			(await DraftAndRequestAsync(new[] { "calls.udf#safety_plan" })).ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.RestrictedAttestationRequired);
			(await DraftAndRequestAsync(new[] { "calls.udf#safety_plan" }, new ProtectedSensitiveAttestation { Restricted = true, RestrictedVersion = "PW-RESTRICTED-0" }))
				.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.RestrictedAttestationRequired, "only the current attestation text counts");

			var approved = await DraftAndRequestAsync(new[] { "calls.udf#safety_plan" }, Attest(restricted: true));

			approved.Success.Should().BeTrue(approved.ErrorCode);
			var release = _h.StoredRelease();
			release.AllowsRestricted.Should().BeTrue();
			release.RestrictedAckVersion.Should().Be(ProtectedWorkflowDefaults.RestrictedAttestationVersion);
			release.RestrictedAckByUserId.Should().Be(ProtectedWorkflowHarness.AdminA);
			release.AllowsPart2.Should().BeFalse();
		}

		[Test]
		public async Task a_part2_field_needs_the_part2_attestation_and_consent_on_the_call()
		{
			_h.AddCustomField("substance_use", UdfFieldSensitivity.Part2, SubstanceSentinel);
			Configure("{{ protected.call.udf.substance_use }}");

			(await DraftAndRequestAsync(new[] { "calls.udf#substance_use" }, Attest(restricted: true))).ErrorCode
				.Should().Be(ProtectedWorkflowErrorCodes.Part2AttestationRequired);
			(await DraftAndRequestAsync(new[] { "calls.udf#substance_use" }, Attest(part2: true))).Success.Should().BeTrue();
			_h.StoredRelease().AllowsPart2.Should().BeTrue();

			// No consent on file: blocked before anything is decrypted.
			_h.Calls[ProtectedWorkflowHarness.CallId].Part2ConsentOnFile = false;
			var blocked = await _h.RunAsync();

			_h.Broker.Calls.Should().BeEmpty();
			_h.Capturing.Calls.Should().BeEmpty();
			_h.DisclosureRecords.Single().Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedConsent);
			blocked.Status.Should().Be((int)WorkflowRunStatus.Failed, "a missing consent is not something a retry fixes");

			// Consent on file: sent.
			_h.Calls[ProtectedWorkflowHarness.CallId].Part2ConsentOnFile = true;
			(await _h.RunAsync()).Status.Should().Be((int)WorkflowRunStatus.Completed);
			_h.Capturing.Calls.Single().RenderedContent.Should().Be(SubstanceSentinel);
		}

		[Test]
		public async Task the_approver_makes_the_same_sensitive_attestations()
		{
			_h.Egress.ProtectedWorkflowsRequireSecondApprover = true;
			_h.AddCustomField("substance_use", UdfFieldSensitivity.Part2, "x");
			(await DraftAndRequestAsync(new[] { "calls.udf#substance_use" }, Attest(part2: true))).Success.Should().BeTrue();

			var releaseId = _h.StoredRelease().WorkflowProtectedReleaseId;
			(await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, releaseId, true, ProtectedWorkflowDefaults.WarningTextVersion,
				_h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB))).ErrorCode
				.Should().Be(ProtectedWorkflowErrorCodes.Part2AttestationRequired);

			(await _h.Service.ApproveAsync(ProtectedWorkflowHarness.DepartmentId, releaseId, true, ProtectedWorkflowDefaults.WarningTextVersion,
				_h.StoredRelease().ConfigFingerprint, ProtectedWorkflowHarness.SteppedUp(ProtectedWorkflowHarness.AdminB), Attest(part2: true))).Success.Should().BeTrue();
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Active);
		}

		[Test]
		public async Task retagging_a_released_field_sends_the_release_back_for_approval()
		{
			var field = _h.AddCustomField("disposition", UdfFieldSensitivity.None, DispositionSentinel);
			Configure("{{ protected.call.udf.disposition }}");
			_h.ActivateRelease("calls.udf#disposition");

			field.Sensitivity = (int)UdfFieldSensitivity.Part2;
			await _h.Service.OnCallCustomFieldsChangedAsync(ProtectedWorkflowHarness.DepartmentId, ProtectedWorkflowHarness.AdminB);

			var release = _h.StoredRelease();
			release.ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
			release.SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.ConfigChanged);
			(await _h.RunAsync()).SkipReason.Should().Be("protected_release_pending_approval");
			_h.Broker.Calls.Should().BeEmpty();
		}

		[Test]
		public async Task a_retag_that_bypassed_the_hook_is_caught_at_send_time()
		{
			var field = _h.AddCustomField("disposition", UdfFieldSensitivity.None, DispositionSentinel);
			Configure("{{ protected.call.udf.disposition }}");
			_h.ActivateRelease("calls.udf#disposition");
			field.Sensitivity = (int)UdfFieldSensitivity.Restricted;

			await _h.RunAsync();

			_h.Broker.Calls.Should().BeEmpty();
			_h.DisclosureRecords.Single().Detail.Should().Be(ProtectedWorkflowErrorCodes.ConfigChanged);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
		}

		[Test]
		public async Task a_custom_field_that_does_not_exist_cannot_be_released()
		{
			(await _h.Service.SaveDraftAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId,
				new ProtectedReleaseDraft { FieldIds = new[] { "calls.udf#no_such_field" } },
				new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminA })).ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.UnknownField);
		}

		// ── Response capture ────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task captured_values_land_encrypted_in_the_subject_identifiers_and_only_key_names_are_recorded()
		{
			_h.SetSubjectIdentifiers($"{{\"ehr_client_id\":\"{ClientSentinel}\"}}");
			Configure("{{ protected.call.subject_ids.ehr_client_id }}", new
			{
				ResponseCapture = new[] { new { Source = "header", Expression = "Location", Key = "ehr_encounter_id" } }
			});
			_h.ActivateRelease("calls.subjectidentifiers#ehr_client_id");
			_h.Capturing.Respond = ctx => new WorkflowActionResult
			{
				Success = true,
				ResultMessage = "HTTP 201 Created",
				HttpStatus = 201,
				CapturedValues = new Dictionary<string, string> { ["ehr_encounter_id"] = EncounterSentinel }
			};

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Completed, string.Join("; ", _h.Logs.Select(l => l.ErrorMessage)));
			var stored = _h.Calls[ProtectedWorkflowHarness.CallId].SubjectIdentifiers;
			ProtectedDataEnvelope.HasEnvelopePrefix(stored).Should().BeTrue("the merged identifiers are written through the encrypt lane");
			JToken.DeepEquals(JObject.Parse(_h.StoredSubjectIdentifiersPlaintext()),
				JObject.Parse($"{{\"ehr_client_id\":\"{ClientSentinel}\",\"ehr_encounter_id\":\"{EncounterSentinel}\"}}")).Should().BeTrue();
			_h.Broker.Encrypts.Single().Items.Single().Should().Match<ProtectedFieldOperationItem>(i =>
				i.FieldId == "calls.subjectidentifiers" && i.RowKey == ProtectedWorkflowHarness.CallId.ToString() && i.CatalogVersion == 29);

			var disclosure = _h.DisclosureRecords.Single();
			JArray.Parse(disclosure.CapturedKeys).Select(k => (string)k).Should().Equal("ehr_encounter_id");
			_h.Logs.Single().ActionResult.Should().EndWith("captured=[ehr_encounter_id]");
			AssertNothingPersistedContains(EncounterSentinel, ClientSentinel);
		}

		[Test]
		public async Task existing_keys_are_overwritten_by_a_capture()
		{
			_h.SetSubjectIdentifiers($"{{\"ehr_client_id\":\"{ClientSentinel}\",\"ehr_encounter_id\":\"OLD\"}}");
			Configure("x", new { ResponseCapture = new[] { new { Source = "header", Expression = "Location", Key = "ehr_encounter_id" } } });
			_h.ActivateRelease("calls.subjectidentifiers");
			_h.Capturing.Respond = ctx => new WorkflowActionResult { Success = true, HttpStatus = 201, CapturedValues = new Dictionary<string, string> { ["ehr_encounter_id"] = "NEW" } };

			await _h.RunAsync();

			JObject.Parse(_h.StoredSubjectIdentifiersPlaintext()).Value<string>("ehr_encounter_id").Should().Be("NEW");
		}

		[Test]
		public async Task a_capture_step_needs_a_release_that_reads_the_subject_identifiers()
		{
			Configure("{{ protected.call.completed_notes | json_escape }}", new { ResponseCapture = new[] { new { Source = "header", Expression = "Location", Key = "ehr_encounter_id" } } });

			var result = await DraftAndRequestAsync(new[] { "calls.completednotes" });

			result.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.CaptureRequiresSubjectIdentifiers);
		}

		[Test]
		public async Task a_capture_that_keeps_losing_to_concurrent_edits_fails_without_resending()
		{
			_h.SetSubjectIdentifiers($"{{\"ehr_client_id\":\"{ClientSentinel}\"}}");
			Configure("x", new { ResponseCapture = new[] { new { Source = "header", Expression = "Location", Key = "ehr_encounter_id" } } });
			_h.ActivateRelease("calls.subjectidentifiers");
			_h.SubjectIdentifierWriteConflicts = 10;
			_h.Capturing.Respond = ctx => new WorkflowActionResult { Success = true, HttpStatus = 201, CapturedValues = new Dictionary<string, string> { ["ehr_encounter_id"] = EncounterSentinel } };

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Failed, "the record was delivered; sending it again would duplicate it");
			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.Sent);
			disclosure.Detail.Should().Be(ProtectedWorkflowErrorCodes.ConcurrentChange);
			disclosure.CapturedKeys.Should().BeNull();
			AssertNothingPersistedContains(EncounterSentinel);
		}

		// ── Idempotency ─────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task the_idempotency_key_is_stable_across_retries_and_differs_across_events()
		{
			Configure("{{ run.idempotency_key }}");
			_h.ActivateRelease();
			_h.Capturing.Respond = ctx => new WorkflowActionResult { Success = false, HttpStatus = 503, ErrorDetail = "http_failed: HTTP 503", ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp };

			var first = await _h.RunAsync();
			await _h.RunAsync(first.InputPayload, attempt: 2, runId: first.WorkflowRunId);
			await _h.RunAsync();

			var keys = _h.Capturing.Calls.Select(c => c.IdempotencyKey).ToList();
			keys[0].Should().MatchRegex("^[0-9a-f]{32}$");
			keys[1].Should().Be(keys[0], "a retry is the same delivery");
			keys[2].Should().NotBe(keys[0], "another event is another delivery");
			_h.Capturing.Calls.Select(c => c.RenderedContent).Should().Equal(keys, "run.idempotency_key renders the same value the header carries");
			WorkflowIdempotency.Key("wf", "event-1", "run-1", "step").Should().Be(WorkflowIdempotency.Key("wf", "event-1", "run-2", "step"),
				"with a domain event id the event, not the run, identifies the delivery");
		}

		// ── Payload validation ──────────────────────────────────────────────────────────────────────

		[TestCase("application/json", "{\"closure\":\"{{ protected.call.completed_notes }}\"")]
		[TestCase("application/fhir+json", "{\"closure\":\"{{ protected.call.completed_notes | json_escape }}\"}")]
		[TestCase("application/xml", "<closure>{{ protected.call.completed_notes | xml_escape }}</closure")]
		[TestCase("x-application/hl7-v2+er7", "PID|1|{{ protected.call.completed_notes | hl7_escape }}")]
		public async Task malformed_output_is_blocked_before_any_network_call(string contentType, string template)
		{
			Configure(template, contentType: contentType);
			_h.ActivateRelease();

			var run = await _h.RunAsync();

			_h.Capturing.Calls.Should().BeEmpty();
			_h.AttemptRecords.Should().BeEmpty();
			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedValidation);
			disclosure.ContentType.Should().Be(contentType);
			disclosure.Detail.Should().StartWith("payload_invalid: rule=").And.NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes);
			run.Status.Should().Be((int)WorkflowRunStatus.Failed, "an invalid payload is not retried");
		}

		[Test]
		public async Task a_content_type_outside_the_allowlist_cannot_be_approved()
		{
			Configure("x", contentType: "text/html");

			var result = await DraftAndRequestAsync(new[] { "calls.completednotes" });

			result.ErrorCode.Should().Be(ProtectedWorkflowErrorCodes.ValidationFailed);
			result.ValidationErrors.Select(e => e.Code).Should().Contain(ProtectedStepOptions.ContentTypeNotAllowed);
		}

		[Test]
		public async Task a_protected_value_without_an_escape_helper_is_a_warning_not_a_block()
		{
			Configure("{\"closure\":\"{{ protected.call.completed_notes }}\"}", contentType: "application/json");

			var view = await _h.Service.GetReleaseViewAsync(ProtectedWorkflowHarness.DepartmentId, _h.Workflow.WorkflowId, ProtectedWorkflowHarness.AdminA);
			view.Validation.Warnings.Select(w => w.Code).Should().Contain(ProtectedWorkflowValidator.UnescapedProtectedValue);
			view.Validation.IsValid.Should().BeTrue();

			(await DraftAndRequestAsync(new[] { "calls.completednotes" })).Success.Should().BeTrue();
		}

		// ── Retry policy ────────────────────────────────────────────────────────────────────────────

		[TestCase(503, ProtectedWorkflowDisclosureOutcomes.FailedHttp, WorkflowRunStatus.Retrying)]
		[TestCase(429, ProtectedWorkflowDisclosureOutcomes.FailedHttp, WorkflowRunStatus.Retrying)]
		[TestCase(400, ProtectedWorkflowDisclosureOutcomes.FailedHttp, WorkflowRunStatus.Failed)]
		[TestCase(409, ProtectedWorkflowDisclosureOutcomes.FailedHttp, WorkflowRunStatus.Failed)]
		[TestCase(200, ProtectedWorkflowDisclosureOutcomes.FailedAck, WorkflowRunStatus.Failed)]
		[TestCase(500, ProtectedWorkflowDisclosureOutcomes.FailedAck, WorkflowRunStatus.Retrying)]
		[TestCase(200, ProtectedWorkflowDisclosureOutcomes.FailedResponseTooLarge, WorkflowRunStatus.Failed)]
		public async Task only_transport_failures_5xx_and_429_are_retried(int status, string outcome, WorkflowRunStatus expected)
		{
			_h.ActivateRelease();
			_h.Capturing.Respond = ctx => new WorkflowActionResult { Success = false, HttpStatus = status, ErrorDetail = "detail", ProtectedOutcome = outcome };

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)expected);
			if (expected == WorkflowRunStatus.Failed)
				_h.Notifications.Should().NotBeEmpty("a final failure alerts the administrators at once");
		}

		// ── Fingerprint ─────────────────────────────────────────────────────────────────────────────

		private static readonly (string Name, Func<ProtectedFingerprintExtras, ProtectedFingerprintExtras> Change)[] ExtraChanges =
		{
			("auth method", e => new ProtectedFingerprintExtras { AuthMethod = "private_key_jwt", AllowsRestricted = e.AllowsRestricted, AllowsPart2 = e.AllowsPart2, FieldSensitivities = e.FieldSensitivities }),
			("restricted attestation", e => new ProtectedFingerprintExtras { AuthMethod = e.AuthMethod, AllowsRestricted = true, AllowsPart2 = e.AllowsPart2, FieldSensitivities = e.FieldSensitivities }),
			("part2 attestation", e => new ProtectedFingerprintExtras { AuthMethod = e.AuthMethod, AllowsRestricted = e.AllowsRestricted, AllowsPart2 = true, FieldSensitivities = e.FieldSensitivities }),
			("sensitivity", e => new ProtectedFingerprintExtras { AuthMethod = e.AuthMethod, AllowsRestricted = e.AllowsRestricted, AllowsPart2 = e.AllowsPart2, FieldSensitivities = new Dictionary<string, int> { ["calls.udf#disposition"] = 2 } })
		};

		private static readonly (string Name, object Config)[] StepOptionChanges =
		{
			("content type", new { ContentType = "application/fhir+json" }),
			("success rule", new { SuccessRule = new { Type = "hl7_ack" } }),
			("response capture", new { ResponseCapture = new[] { new { Source = "header", Expression = "Location", Key = "ehr_encounter_id" } } }),
			("idempotency header", new { IdempotencyHeader = "Idempotency-Key" }),
			("if-none-exist", new { IfNoneExist = "identifier=https://resgrid.com/call|{{ call.id }}" })
		};

		[Test]
		public void every_new_fingerprint_input_changes_the_fingerprint()
		{
			var steps = new List<WorkflowStep> { ProtectedWorkflowHarness.Clone(_h.Steps[0]) };
			var fields = new[] { "calls.udf#disposition" };
			var baseline = new ProtectedFingerprintExtras { AuthMethod = "client_secret", FieldSensitivities = new Dictionary<string, int> { ["calls.udf#disposition"] = 0 } };
			var original = ProtectedWorkflowFingerprint.Compute((int)WorkflowTriggerEventType.CallClosed, steps, fields, ProtectedWorkflowHarness.Host, null, baseline);

			foreach (var (name, change) in ExtraChanges)
				ProtectedWorkflowFingerprint.Compute((int)WorkflowTriggerEventType.CallClosed, steps, fields, ProtectedWorkflowHarness.Host, null, change(baseline))
					.Should().NotBe(original, name);

			foreach (var (name, config) in StepOptionChanges)
			{
				var changed = ProtectedWorkflowHarness.Clone(steps[0]);
				var json = JObject.Parse(changed.ActionConfig);
				json.Merge(JObject.FromObject(config));
				changed.ActionConfig = json.ToString(Formatting.None);
				ProtectedWorkflowFingerprint.Compute((int)WorkflowTriggerEventType.CallClosed, new[] { changed }, fields, ProtectedWorkflowHarness.Host, null, baseline)
					.Should().NotBe(original, name);
			}
		}

		[Test]
		public async Task a_step_option_change_suspends_an_active_release()
		{
			_h.ActivateRelease();
			foreach (var (name, config) in StepOptionChanges)
			{
				var step = ProtectedWorkflowHarness.Clone(_h.Steps[0]);
				var json = JObject.Parse(step.ActionConfig);
				json.Merge(JObject.FromObject(config));
				step.ActionConfig = json.ToString(Formatting.None);

				await _h.WorkflowService.SaveWorkflowStepAsync(step);

				_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval, name);
				var release = _h.StoredRelease();
				release.State = (int)ProtectedReleaseState.Active;
				release.SuspendedReason = null;
				_h.Refingerprint(release);
			}
		}

		// ── Acceptance: two protected sends on one call close ───────────────────────────────────────

		[Test]
		public async Task two_protected_workflows_on_call_closed_send_independently_to_their_own_destinations()
		{
			// The DMH case: the Dynamics case write-back (base spec) and a FHIR Bundle to the EHR, both on Call Closed, each
			// with its own release, pinned host and field list.
			const string EhrHost = "ehr.example.org";
			_h.SetSubjectIdentifiers($"{{\"dynamics_case_id\":\"{CaseSentinel}\",\"ehr_client_id\":\"{ClientSentinel}\"}}");
			_h.AddCustomField("disposition", UdfFieldSensitivity.None, DispositionSentinel);
			_h.AddCustomField("substance_use", UdfFieldSensitivity.Part2, SubstanceSentinel);
			var dynamicsRelease = _h.ActivateRelease("calls.completednotes", "calls.callformdata");

			var fhirStep = WorkflowTemplateGallery.Find(WorkflowTemplateGallery.FhirEncounterBundle).Steps.Single();
			var ehr = _h.AddWorkflow("wf-ehr", "EHR encounter", fhirStep.ActionType,
				fhirStep.ActionConfig.Replace(WorkflowTemplateGallery.FhirPlaceholderUrl, $"https://{EhrHost}/fhir/r4"), fhirStep.OutputTemplate);
			var ehrRelease = _h.ActivateReleaseFor(ehr, EhrHost, "calls.subjectidentifiers#ehr_client_id", "calls.udf#disposition");

			_h.Capturing.Respond = ctx => ctx.PinnedHost == EhrHost
				? new WorkflowActionResult { Success = true, HttpStatus = 201, ResultMessage = "HTTP 201 Created", CapturedValues = new Dictionary<string, string> { ["ehr_encounter_id"] = EncounterSentinel } }
				: new WorkflowActionResult { Success = true, HttpStatus = 204, ResultMessage = "HTTP 204 No Content" };

			// The event provider gives every active workflow on the trigger its own run for the same event.
			var payload = _h.ClosedPayload();
			(await _h.RunAsync(payload)).Status.Should().Be((int)WorkflowRunStatus.Completed, string.Join("; ", _h.Logs.Select(l => l.ErrorMessage)));
			(await _h.WorkflowService.ExecuteWorkflowAsync(ehr.WorkflowId, payload, ProtectedWorkflowHarness.DepartmentId, ProtectedWorkflowHarness.DepartmentCode))
				.Status.Should().Be((int)WorkflowRunStatus.Completed, string.Join("; ", _h.Logs.Select(l => l.ErrorMessage)));

			var toDynamics = _h.Capturing.Calls.Single(c => c.PinnedHost == ProtectedWorkflowHarness.Host);
			var toEhr = _h.Capturing.Calls.Single(c => c.PinnedHost == EhrHost);

			toDynamics.RenderedContent.Should().Contain(ProtectedWorkflowHarness.SentinelCompletedNotes)
				.And.NotContain(ClientSentinel).And.NotContain(DispositionSentinel).And.NotContain(SubstanceSentinel);

			ProtectedPayloadValidator.Validate("application/fhir+json", toEhr.RenderedContent).Ok.Should().BeTrue();
			var bundle = JObject.Parse(toEhr.RenderedContent);
			bundle["entry"][0]["resource"]["subject"].Value<string>("reference").Should().Be("Patient/" + ClientSentinel);
			bundle["identifier"].Value<string>("value").Should().Be(toEhr.IdempotencyKey);
			toEhr.RenderedContent.Should().Contain(DispositionSentinel, "the released custom field becomes an Observation")
				.And.NotContain(SubstanceSentinel).And.NotContain(CaseSentinel).And.NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes);
			toEhr.IdempotencyKey.Should().NotBe(toDynamics.IdempotencyKey);

			_h.DisclosureRecords.Select(d => (d.WorkflowProtectedReleaseId, d.DestinationHost)).Should().BeEquivalentTo(new[]
			{
				(dynamicsRelease.WorkflowProtectedReleaseId, ProtectedWorkflowHarness.Host),
				(ehrRelease.WorkflowProtectedReleaseId, EhrHost)
			});
			JObject.Parse(_h.StoredSubjectIdentifiersPlaintext()).Value<string>("ehr_encounter_id").Should().Be(EncounterSentinel,
				"the EHR's encounter id is stored on the call, encrypted");
			ProtectedDataEnvelope.HasEnvelopePrefix(_h.Calls[ProtectedWorkflowHarness.CallId].SubjectIdentifiers).Should().BeTrue();
			AssertNothingPersistedContains(ClientSentinel, CaseSentinel, DispositionSentinel, SubstanceSentinel, EncounterSentinel,
				ProtectedWorkflowHarness.SentinelCompletedNotes);
		}

		// ── Gallery samples ─────────────────────────────────────────────────────────────────────────

		private static string RenderGallery(string key, IEnumerable<string> releasedFields, out string idempotencyKey)
		{
			var template = WorkflowTemplateGallery.Find(key);
			var sample = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(template.Trigger);
			idempotencyKey = (string)((ScriptObject)sample["run"])["idempotency_key"];
			var harness = new ProtectedWorkflowHarness();
			var values = harness.Runtime.BuildSampleValues((int)template.Trigger, releasedFields);

			var context = new TemplateContext { EnableRelaxedTargetAccess = true };
			context.PushGlobal(sample);
			context.PushGlobal((ScriptObject)values.Namespace);
			context.PushGlobal(new ScriptObject());
			var step = template.Steps.Single();
			var rendered = Template.Parse(step.OutputTemplate).Render(context);
			var options = ProtectedStepOptions.Read(step.ActionConfig, out var errors);
			errors.Should().BeEmpty();
			return ProtectedPayloadValidator.NormalizeBody(options.MediaType, rendered);
		}

		[Test]
		public void the_fhir_sample_renders_a_valid_transaction_bundle()
		{
			var body = RenderGallery(WorkflowTemplateGallery.FhirEncounterBundle,
				new[] { "calls.subjectidentifiers#ehr_client_id", "calls.udf#disposition", "calls.udf#follow_up" }, out var key);

			ProtectedPayloadValidator.Validate("application/fhir+json", body).Ok.Should().BeTrue(body);
			var bundle = JObject.Parse(body);
			bundle.Value<string>("resourceType").Should().Be("Bundle");
			bundle["identifier"].Value<string>("value").Should().Be(key);
			var entries = (JArray)bundle["entry"];
			entries.Should().HaveCount(3, "the Encounter plus one Observation per released custom field");
			var encounter = entries[0]["resource"];
			encounter.Value<string>("resourceType").Should().Be("Encounter");
			encounter["class"].Value<string>("code").Should().Be("FLD");
			encounter.Value<string>("status").Should().Be("finished");
			encounter["subject"].Value<string>("reference").Should().Be("Patient/SAMPLE-CLIENT-000123");
			entries[0]["request"].Value<string>("ifNoneExist").Should().Be("identifier=https://resgrid.com/call|1001");
			entries[0].Value<string>("fullUrl").Should().MatchRegex("^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
			entries.Skip(1).Select(e => e["resource"].Value<string>("valueString")).Should()
				.Equal("SAMPLE disposition (synthetic test data)", "SAMPLE follow_up (synthetic test data)");
		}

		[Test]
		public void the_hl7_sample_renders_a_valid_mdm_t02()
		{
			var body = RenderGallery(WorkflowTemplateGallery.Hl7MdmT02,
				new[] { "calls.subjectidentifiers#ehr_client_id", "calls.udf#disposition" }, out var key);

			ProtectedPayloadValidator.Validate(ProtectedPayloadValidator.Hl7MediaType, body).Ok.Should().BeTrue(body.Replace('\r', '\n'));
			var segments = body.Split('\r');
			segments.Select(s => s.Substring(0, 3)).Should().Equal("MSH", "EVN", "PID", "PV1", "TXA", "OBX");
			var msh = segments[0].Split('|');
			msh[8].Should().Be("MDM^T02^MDM_T02");
			msh[9].Should().Be(key, "MSH-10 is the idempotency key");
			segments[2].Split('|')[3].Should().Be("SAMPLE-CLIENT-000123^^^EHR^MR", "PID-3 is the EHR client id");
			segments[4].Split('|')[2].Should().Be("CFR^Crisis Field Response");
			segments[5].Split('|')[5].Should().Be("SAMPLE disposition (synthetic test data)");
		}

		[Test]
		public void the_ehr_samples_are_only_offered_with_protected_workflows_enabled()
		{
			WorkflowTemplateGallery.Available(false).Select(t => t.Key).Should().NotContain(new[] { WorkflowTemplateGallery.FhirEncounterBundle, WorkflowTemplateGallery.Hl7MdmT02 });
			WorkflowTemplateGallery.Available(true).Select(t => t.Key).Should().Contain(new[] { WorkflowTemplateGallery.FhirEncounterBundle, WorkflowTemplateGallery.Hl7MdmT02 });
			foreach (var template in WorkflowTemplateGallery.All.Where(t => t.RequiresProtectedWorkflows))
				template.Steps.Should().OnlyContain(s => !ProtectedWorkflowValidator.HasUnescapedProtectedReference(s.OutputTemplate), template.Key);
			foreach (var template in WorkflowTemplateGallery.All)
				template.Steps.Should().NotBeEmpty().And.OnlyContain(s => !string.IsNullOrWhiteSpace(s.OutputTemplate) && !string.IsNullOrWhiteSpace(s.ActionConfig), template.Key);
		}
	}
}
