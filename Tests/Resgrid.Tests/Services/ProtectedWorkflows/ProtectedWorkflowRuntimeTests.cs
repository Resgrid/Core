using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// The unattended Protected Workflows path end to end through the real WorkflowService: what reaches a template,
	/// when a send is refused, what the broker is asked for, and — above all — that plaintext never lands in anything
	/// the platform writes.
	/// </summary>
	[TestFixture]
	public class ProtectedWorkflowRuntimeTests
	{
		private ProtectedWorkflowHarness _h;

		[SetUp]
		public void SetUp() => _h = new ProtectedWorkflowHarness();

		// ── Template context ────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task without_a_release_protected_namespace_is_empty_and_call_values_are_redacted()
		{
			_h.Steps[0].OutputTemplate = "{{ call.completed_notes }}|{{ protected.call.completed_notes }}|{{ call.notes }}|{{ protected.call.form.outcome }}";

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Completed, string.Join("; ", _h.Logs.Select(l => l.ErrorMessage)));
			_h.Capturing.Calls.Should().ContainSingle();
			_h.Capturing.Calls[0].RenderedContent.Should().Be("REDACTED||REDACTED|", "protected.* renders as an empty string, however deep the reference");
			_h.Capturing.Calls[0].ProtectedMode.Should().BeFalse("a department that never used the feature sees no behaviour change");
			_h.Broker.Calls.Should().BeEmpty();
			_h.Disclosures.Should().BeEmpty();
		}

		[Test]
		public async Task with_an_active_release_only_allowlisted_fields_appear_under_protected()
		{
			_h.Steps[0].OutputTemplate =
				"{{ call.completed_notes }}|{{ protected.call.completed_notes }}|{{ call.notes }}|{{ protected.call.notes }}|{{ protected.call.form.outcome }}|{{ call.nature }}";
			_h.UsePlainText();
			_h.ActivateRelease("calls.completednotes", "calls.callformdata");

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Completed);
			var sent = _h.Capturing.Calls.Single();
			sent.ProtectedMode.Should().BeTrue();
			sent.PinnedHost.Should().Be(ProtectedWorkflowHarness.Host);
			sent.RenderedContent.Should().Be(
				$"REDACTED|{ProtectedWorkflowHarness.SentinelCompletedNotes}|REDACTED||{ProtectedWorkflowHarness.SentinelFormOutcome}|REDACTED",
				"call.* stays REDACTED, and a protected field that is not allow-listed is simply absent");

			var brokerCall = _h.Broker.Calls.Single();
			brokerCall.Purpose.Should().Be("protected-workflow");
			brokerCall.Items.Select(i => i.FieldId).Should().BeEquivalentTo(new[] { "calls.completednotes", "calls.callformdata" },
				"minimum necessary: only the ticked fields are ever sent to the broker");
			brokerCall.Items.Should().OnlyContain(i => i.RowKey == ProtectedWorkflowHarness.CallId.ToString() && i.CatalogVersion == 29);
		}

		[Test]
		public async Task condition_expressions_cannot_see_plaintext()
		{
			_h.Steps[0].ConditionExpression = "{{ protected.call.completed_notes == \"" + ProtectedWorkflowHarness.SentinelCompletedNotes + "\" }}";
			_h.ActivateRelease();

			await _h.RunAsync();

			_h.Capturing.Calls.Should().BeEmpty("the condition evaluates against the ordinary context, where protected.* does not exist");
			_h.Logs.Single().Status.Should().Be((int)WorkflowRunStatus.Skipped);
			_h.Broker.Calls.Should().BeEmpty("a skipped step decrypts nothing");
		}

		[Test]
		public async Task a_step_cannot_leave_plaintext_behind_for_the_next_step()
		{
			_h.Steps[0].OutputTemplate = "{{ call.notes = protected.call.completed_notes }}{{ leaked = protected.call.completed_notes }}ok";
			_h.UsePlainText();
			_h.Steps.Add(new WorkflowStep
			{
				WorkflowStepId = "step-2",
				WorkflowId = _h.Workflow.WorkflowId,
				ActionType = (int)WorkflowActionType.CallApiPost,
				StepOrder = 2,
				IsEnabled = true,
				WorkflowCredentialId = ProtectedWorkflowHarness.CredentialId,
				ActionConfig = JsonConvert.SerializeObject(new { Url = ProtectedWorkflowHarness.Url + "?n={{ call.notes }}{{ leaked }}", ContentType = "text/plain" }),
				OutputTemplate = "{{ call.notes }}|{{ leaked }}",
				CreatedByUserId = ProtectedWorkflowHarness.AdminA
			});
			_h.ActivateRelease();

			await _h.RunAsync();

			_h.Capturing.Calls.Should().HaveCount(2);
			_h.Capturing.Calls[1].RenderedContent.Should().Be("REDACTED|");
			_h.Capturing.Calls[1].ActionConfigJson.Should().NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes);
		}

		// ── Run gate ────────────────────────────────────────────────────────────────────────────────

		[TestCase(ProtectedReleaseState.Draft, "protected_release_draft")]
		[TestCase(ProtectedReleaseState.PendingApproval, "protected_release_pending_approval")]
		[TestCase(ProtectedReleaseState.Suspended, "protected_release_suspended")]
		[TestCase(ProtectedReleaseState.Expired, "protected_release_expired")]
		[TestCase(ProtectedReleaseState.Revoked, "protected_release_revoked")]
		public async Task a_release_that_is_not_active_skips_the_run_and_never_runs_it_unprotected(ProtectedReleaseState state, string reason)
		{
			var release = _h.ActivateRelease();
			_h.StoredRelease(release.WorkflowProtectedReleaseId).State = (int)state;

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Skipped);
			run.SkipReason.Should().Be(reason);
			_h.Capturing.Calls.Should().BeEmpty();
			_h.Broker.Calls.Should().BeEmpty();
		}

		// ── Preconditions ───────────────────────────────────────────────────────────────────────────

		[TestCase(DepartmentDataProtectionState.Failed)]
		[TestCase(DepartmentDataProtectionState.Encrypting)]
		public async Task adp_not_enabled_blocks_the_send(DepartmentDataProtectionState state)
		{
			_h.ActivateRelease();
			_h.Policy.State = (int)state;

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedDepartment, ProtectedWorkflowErrorCodes.AdpNotEnabled);
		}

		[Test]
		public async Task department_toggle_off_blocks_the_send()
		{
			_h.ActivateRelease();
			_h.Egress.ProtectedWorkflowsEnabled = false;

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedDepartment, ProtectedWorkflowErrorCodes.DepartmentDisabled);
		}

		[Test]
		public async Task an_expired_release_blocks_the_send_and_is_marked_expired()
		{
			var release = _h.ActivateRelease();
			_h.StoredRelease(release.WorkflowProtectedReleaseId).ExpiresOn = DateTime.UtcNow.AddMinutes(-1);

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ReleaseExpired);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.Expired);
			_h.AdminEvents.Should().Contain(e => e.EventType == ProtectedWorkflowAdminEventTypes.ReleaseExpired);
		}

		[Test]
		public async Task a_config_change_that_bypassed_the_save_hook_is_caught_at_send_time()
		{
			_h.ActivateRelease();
			_h.Steps[0].OutputTemplate += " ";

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ConfigChanged);
			_h.StoredRelease().ReleaseState.Should().Be(ProtectedReleaseState.PendingApproval);
			_h.StoredRelease().SuspendedReason.Should().Be(ProtectedWorkflowSuspendReasons.ConfigChanged);
		}

		[Test]
		public async Task a_non_api_step_blocks_the_send()
		{
			var release = _h.ActivateRelease();
			_h.Steps[0].ActionType = (int)WorkflowActionType.SendEmail;
			_h.Refingerprint(release);

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ActionNotAllowed);
		}

		[Test]
		public async Task a_credential_other_than_the_pinned_one_blocks_the_send()
		{
			var release = _h.ActivateRelease();
			_h.Credentials.Add(new WorkflowCredential { WorkflowCredentialId = "cred-2", DepartmentId = ProtectedWorkflowHarness.DepartmentId, CredentialType = (int)WorkflowCredentialType.HttpBearer, EncryptedData = "enc:{}" });
			_h.Steps[0].WorkflowCredentialId = "cred-2";
			_h.Refingerprint(release);

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.CredentialNotAllowed);
		}

		// ── Host pinning ────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task a_host_other_than_the_pinned_one_blocks_the_send()
		{
			var release = _h.ActivateRelease();
			_h.Steps[0].ActionConfig = JsonConvert.SerializeObject(new { Url = "https://attacker.example.com/collect" });
			_h.Refingerprint(release);

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.HostMismatch);
		}

		[Test]
		public async Task an_http_url_blocks_the_send()
		{
			var release = _h.ActivateRelease();
			_h.Steps[0].ActionConfig = JsonConvert.SerializeObject(new { Url = "http://" + ProtectedWorkflowHarness.Host + "/api" });
			_h.Refingerprint(release);

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.SchemeNotHttps);
		}

		[Test]
		public async Task a_templated_url_that_renders_to_a_different_host_blocks_the_send()
		{
			var release = _h.ActivateRelease();
			// The literal part is the pinned host; the rendered URL is not.
			_h.Steps[0].ActionConfig = JsonConvert.SerializeObject(new { Url = "https://" + ProtectedWorkflowHarness.Host + "{{ '.attacker.example' }}/api" });
			_h.Refingerprint(release);

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.HostMismatch);
		}

		[Test]
		public async Task a_redirect_reported_by_the_executor_is_recorded_as_blocked_host()
		{
			_h.ActivateRelease();
			_h.Capturing.Respond = ctx => new WorkflowActionResult
			{
				Success = false,
				ResultMessage = "HTTP 302 Found",
				ErrorDetail = ProtectedWorkflowErrorCodes.Redirected,
				HttpStatus = 302,
				ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.BlockedHost
			};

			await _h.RunAsync();

			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedHost);
			disclosure.HttpStatus.Should().Be(302);
		}

		// ── Broker ──────────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task broker_purpose_denial_fails_the_step_without_sending()
		{
			_h.ActivateRelease();
			_h.Broker.FailWith = "workload_purpose_denied";

			await _h.RunAsync();

			AssertFailedBroker();
		}

		[Test]
		public async Task a_kms_failure_fails_the_step_without_sending()
		{
			_h.ActivateRelease();
			_h.Broker.FailWith = "kms_unavailable";

			await _h.RunAsync();

			AssertFailedBroker();
		}

		[Test]
		public async Task a_broker_outage_fails_the_step_without_sending()
		{
			_h.ActivateRelease();
			_h.Broker.Throw = true;

			await _h.RunAsync();

			AssertFailedBroker();
		}

		[Test]
		public async Task one_undecryptable_field_fails_the_whole_send_never_a_partial_payload()
		{
			_h.ActivateRelease();
			_h.Broker.FailFields.Add("calls.callformdata");

			await _h.RunAsync();

			AssertFailedBroker();
		}

		// ── Outbound guard ──────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task an_envelope_in_the_rendered_output_is_caught_and_the_step_fails()
		{
			_h.ActivateRelease();
			// A value that is itself still ciphertext (double-enveloped, or a template quoting one).
			_h.Broker.Plaintext[ProtectedWorkflowHarness.EnvelopeCompletedNotes] = "rgdp:1:2:U1RJTExFTkNSWVBURUQ=";

			await _h.RunAsync();

			_h.Capturing.Calls.Should().BeEmpty();
			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.BlockedGuard);
			disclosure.Detail.Should().Be(ProtectedWorkflowErrorCodes.EnvelopeInPayload);
			_h.Logs.Single().Status.Should().Be((int)WorkflowRunStatus.Failed);
		}

		// ── Disclosure record ───────────────────────────────────────────────────────────────────────

		[Test]
		public async Task a_successful_send_writes_one_value_free_disclosure()
		{
			_h.ActivateRelease();

			await _h.RunAsync();

			var disclosure = _h.DisclosureRecords.Single();
			var sent = _h.Capturing.Calls.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.Sent);
			disclosure.EntityType.Should().Be("call");
			disclosure.EntityId.Should().Be(ProtectedWorkflowHarness.CallId.ToString());
			WorkflowProtectedRelease.ParseFieldIds(disclosure.FieldIds).Should().BeEquivalentTo(new[] { "calls.callformdata", "calls.completednotes" });
			disclosure.DestinationHost.Should().Be(ProtectedWorkflowHarness.Host);
			disclosure.HttpStatus.Should().Be(204);
			disclosure.PayloadBytes.Should().Be(System.Text.Encoding.UTF8.GetByteCount(sent.RenderedContent));
			disclosure.PayloadSha256.Should().Be(ProtectedWorkflowDisclosureChain.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(sent.RenderedContent)));
			disclosure.BrokerRequestId.Should().Be(_h.Broker.Calls.Single().RequestId);
			disclosure.IsTest.Should().BeFalse();
		}

		[Test]
		public async Task the_attempt_is_chained_before_the_request_leaves()
		{
			_h.ActivateRelease();
			var attemptsAtSend = -1;
			_h.Capturing.Respond = ctx =>
			{
				attemptsAtSend = _h.AttemptRecords.Count();
				return new WorkflowActionResult { Success = true, ResultMessage = "HTTP 204 No Content", HttpStatus = 204 };
			};

			await _h.RunAsync();

			attemptsAtSend.Should().Be(1, "the audit record exists before the request does");
			var attempt = _h.AttemptRecords.Single();
			var outcome = _h.DisclosureRecords.Single();
			attempt.ChainSequence.Should().BeLessThan(outcome.ChainSequence);
			attempt.WorkflowRunId.Should().Be(outcome.WorkflowRunId);
			attempt.PayloadSha256.Should().Be(ProtectedWorkflowDisclosureChain.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(_h.Capturing.Calls.Single().RenderedContent)));
			attempt.FieldIds.Should().Be(outcome.FieldIds);
			attempt.HttpStatus.Should().BeNull();
		}

		[Test]
		public async Task when_the_attempt_cannot_be_chained_nothing_is_sent()
		{
			_h.ActivateRelease();
			_h.FailDisclosureAppends = true;

			var run = await _h.RunAsync();

			_h.Capturing.Calls.Should().BeEmpty("an unrecordable disclosure is never made");
			_h.Disclosures.Should().BeEmpty();
			_h.Logs.Single().ErrorMessage.Should().Be(ProtectedWorkflowErrorCodes.DisclosureUnavailable);
			run.Status.Should().Be((int)WorkflowRunStatus.Retrying, "the chain being briefly unavailable is worth another attempt");
		}

		// ── Values come from the stored call, never from the queued event ───────────────────────────

		[Test]
		public async Task the_queued_event_is_redacted_and_the_released_values_come_from_the_stored_call()
		{
			_h.ActivateRelease();
			var payload = _h.ClosedPayload();
			payload.Should().Contain(ProtectedDataEnvelope.RedactionValue)
				.And.NotContain(ProtectedWorkflowHarness.EnvelopeCompletedNotes, "the workflow queue only ever carries the safe projection");

			await _h.RunAsync(payload);

			_h.Capturing.Calls.Single().RenderedContent.Should().Contain(ProtectedWorkflowHarness.SentinelCompletedNotes)
				.And.Contain(ProtectedWorkflowHarness.SentinelFormOutcome).And.NotContain("\"closure\":\"REDACTED\"");
			_h.Broker.Calls.Single().Items.Select(i => i.Value).Should().BeEquivalentTo(
				new[] { ProtectedWorkflowHarness.EnvelopeCompletedNotes, ProtectedWorkflowHarness.EnvelopeForm },
				"the broker decrypts the stored envelopes, not the placeholders in the event");
		}

		[Test]
		public async Task a_stored_value_that_is_only_a_placeholder_fails_closed()
		{
			_h.ActivateRelease();
			_h.Calls[ProtectedWorkflowHarness.CallId].CompletedNotes = ProtectedDataEnvelope.RedactionValue;

			await _h.RunAsync();

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.FailedBroker, ProtectedWorkflowErrorCodes.EntityUnavailable);
		}

		[Test]
		public async Task a_call_that_no_longer_exists_or_belongs_to_another_department_is_never_decrypted()
		{
			_h.ActivateRelease();
			var payload = _h.ClosedPayload();
			_h.Calls[ProtectedWorkflowHarness.CallId].DepartmentId = 99;

			await _h.RunAsync(payload);

			AssertBlocked(ProtectedWorkflowDisclosureOutcomes.FailedBroker, ProtectedWorkflowErrorCodes.EntityUnavailable);

			var gone = new ProtectedWorkflowHarness();
			gone.ActivateRelease();
			var goneRun = gone.ClosedPayload();
			gone.Calls.Clear();
			await gone.RunAsync(goneRun);
			gone.Capturing.Calls.Should().BeEmpty();
			gone.Broker.Calls.Should().BeEmpty();
		}

		// ── Nothing persisted contains plaintext ────────────────────────────────────────────────────

		[Test]
		public async Task no_persisted_field_or_log_line_contains_a_decrypted_value_on_success()
		{
			_h.ActivateRelease("calls.completednotes", "calls.callformdata", "calls.notes");
			_h.Steps[0].OutputTemplate = _h.Steps[0].OutputTemplate.Replace("\"number\":", "\"notes\":\"{{ protected.call.notes }}\",\"number\":");
			_h.Refingerprint(_h.StoredRelease());

			var captured = await CaptureConsoleAsync(() => _h.RunAsync());

			_h.Capturing.Calls.Single().RenderedContent.Should().Contain(ProtectedWorkflowHarness.SentinelCompletedNotes, "the destination does receive the plaintext");
			AssertNoSentinel(_h.EverythingPersisted() + captured);
			_h.Logs.Single().RenderedOutput.Should().StartWith("[protected payload] sha256=");
		}

		[Test]
		public async Task no_persisted_field_or_log_line_contains_a_decrypted_value_when_the_send_throws()
		{
			_h.ActivateRelease();
			// An exception whose message quotes the request content must not carry it into the run log or the logs.
			_h.Capturing.Throw = new InvalidOperationException("could not post body: " + ProtectedWorkflowHarness.SentinelCompletedNotes);

			var captured = await CaptureConsoleAsync(() => _h.RunAsync());

			_h.Logs.Single().Status.Should().Be((int)WorkflowRunStatus.Failed);
			_h.Logs.Single().ErrorMessage.Should().StartWith(ProtectedWorkflowErrorCodes.StepError);
			AssertNoSentinel(_h.EverythingPersisted() + captured);
		}

		[Test]
		public async Task no_persisted_field_contains_a_decrypted_value_when_the_endpoint_echoes_it_back()
		{
			_h.ActivateRelease();
			_h.Capturing.Respond = ctx => new WorkflowActionResult
			{
				Success = false,
				ResultMessage = "HTTP 400 Bad Request",
				ErrorDetail = "http_failed: HTTP 400",
				HttpStatus = 400,
				ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp
			};

			var captured = await CaptureConsoleAsync(() => _h.RunAsync());

			_h.DisclosureRecords.Single().Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedHttp);
			AssertNoSentinel(_h.EverythingPersisted() + captured);
		}

		// ── Retries ─────────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task each_retry_re_decrypts_with_a_fresh_request_id_and_nothing_plaintext_is_kept_for_it()
		{
			_h.ActivateRelease();
			_h.Capturing.Respond = ctx => new WorkflowActionResult { Success = false, ResultMessage = "HTTP 503 Service Unavailable", HttpStatus = 503, ErrorDetail = "http_failed: HTTP 503", ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp };

			var first = await _h.RunAsync();
			first.Status.Should().Be((int)WorkflowRunStatus.Retrying);
			var second = await _h.RunAsync(first.InputPayload, attempt: 2, runId: first.WorkflowRunId);

			second.Status.Should().Be((int)WorkflowRunStatus.Retrying);
			_h.Broker.Calls.Should().HaveCount(2);
			_h.Broker.Calls.Select(c => c.RequestId).Distinct().Should().HaveCount(2, "a reused request id would be refused as a replay");
			_h.Capturing.Calls.Should().HaveCount(2);
			_h.DisclosureRecords.Should().HaveCount(2, "one disclosure per send attempt");
			first.InputPayload.Should().NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes, "the retry carries the event as it arrived: ids and envelopes, never plaintext");
		}

		[Test]
		public async Task a_suspension_between_attempts_blocks_the_retry()
		{
			var release = _h.ActivateRelease();
			_h.Capturing.Respond = ctx => new WorkflowActionResult { Success = false, ResultMessage = "HTTP 503 Service Unavailable", HttpStatus = 503, ErrorDetail = "http_failed: HTTP 503", ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp };
			var first = await _h.RunAsync();

			var suspended = await _h.Service.SuspendAsync(ProtectedWorkflowHarness.DepartmentId, release.WorkflowProtectedReleaseId,
				new ProtectedWorkflowActor { UserId = ProtectedWorkflowHarness.AdminB });
			suspended.Success.Should().BeTrue();

			var retry = await _h.RunAsync(first.InputPayload, attempt: 2, runId: first.WorkflowRunId);

			retry.Status.Should().Be((int)WorkflowRunStatus.Skipped);
			retry.SkipReason.Should().Be("protected_release_suspended");
			_h.Capturing.Calls.Should().HaveCount(1, "only the first attempt ever sent");
			_h.Broker.Calls.Should().HaveCount(1);
		}

		[Test]
		public async Task a_final_failure_notifies_administrators_with_a_generic_message()
		{
			_h.Workflow.MaxRetryCount = 1;
			_h.ActivateRelease();
			_h.Broker.FailWith = "kms_unavailable";

			var run = await _h.RunAsync();

			run.Status.Should().Be((int)WorkflowRunStatus.Failed);
			_h.Notifications.Should().HaveCount(2);
			_h.Notifications.Should().OnlyContain(n => n.Contains(run.WorkflowRunId) && n.Contains(ProtectedWorkflowErrorCodes.BrokerFailed));
			AssertNoSentinel(string.Join("\n", _h.Notifications));
		}

		// ── Test send ───────────────────────────────────────────────────────────────────────────────

		[Test]
		public async Task a_test_send_uses_synthetic_values_never_decrypts_and_is_labelled_test()
		{
			_h.ActivateRelease();

			var result = await _h.WorkflowService.SendProtectedTestAsync(ProtectedWorkflowHarness.DepartmentId, ProtectedWorkflowHarness.DepartmentCode, _h.Workflow.WorkflowId);

			result.Success.Should().BeTrue();
			_h.Broker.Calls.Should().BeEmpty();
			_h.Capturing.Calls.Single().RenderedContent.Should().Contain("synthetic test data").And.NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes);
			_h.Capturing.Calls.Single().ProtectedMode.Should().BeTrue();
			_h.DisclosureRecords.Single().IsTest.Should().BeTrue();
		}

		// ── Helpers ─────────────────────────────────────────────────────────────────────────────────

		private void AssertBlocked(string outcome, string code)
		{
			_h.Capturing.Calls.Should().BeEmpty("a failed precondition never reaches the executor");
			_h.Broker.Calls.Should().BeEmpty("a failed precondition never decrypts");
			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(outcome);
			disclosure.Detail.Should().Be(code);
			disclosure.PayloadSha256.Should().BeNull();
			_h.Logs.Single().Status.Should().Be((int)WorkflowRunStatus.Failed);
			_h.Logs.Single().ErrorMessage.Should().Be(code);
		}

		private void AssertFailedBroker()
		{
			_h.Capturing.Calls.Should().BeEmpty("a broker failure never sends, not even a redacted or partial payload");
			var disclosure = _h.DisclosureRecords.Single();
			disclosure.Outcome.Should().Be(ProtectedWorkflowDisclosureOutcomes.FailedBroker);
			disclosure.Detail.Should().StartWith(ProtectedWorkflowErrorCodes.BrokerFailed);
			disclosure.BrokerRequestId.Should().NotBeNullOrEmpty();
			_h.Logs.Single().Status.Should().Be((int)WorkflowRunStatus.Failed);
		}

		private static void AssertNoSentinel(string text)
		{
			text.Should().NotContain(ProtectedWorkflowHarness.SentinelCompletedNotes);
			text.Should().NotContain(ProtectedWorkflowHarness.SentinelFormOutcome);
			text.Should().NotContain(ProtectedWorkflowHarness.SentinelNotes);
		}

		private static async Task<string> CaptureConsoleAsync(Func<Task> action)
		{
			var originalOut = Console.Out;
			var originalError = Console.Error;
			using var writer = new StringWriter();
			Console.SetOut(writer);
			Console.SetError(writer);
			try
			{
				await action();
			}
			finally
			{
				Console.SetOut(originalOut);
				Console.SetError(originalError);
			}

			return writer.ToString();
		}
	}
}
