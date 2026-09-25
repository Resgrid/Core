using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class DispatchTraceTests
	{
		[SetUp, TearDown] public void Drain() { while (DispatchTraceTelemetry.Reader.TryRead(out _)) { } }
		[Test]
		public async Task Saturated_or_unconsumed_trace_storage_does_not_stop_sends()
		{
			var before = DispatchTraceTelemetry.Dropped; var sends = 0;
			using (DispatchTraceTelemetry.Begin(7, 1, 19, true))
			{
				for (int i = 0; i < 2200; i++) DispatchTraceTelemetry.Observe(DispatchTraceStage.Selected, recipientId: "member");
				var result = await DispatchTraceTelemetry.AttemptAsync(DispatchTraceChannel.Sms, "member", () => { sends++; return Task.FromResult(true); });
				Assert.That(result, Is.True);
			}
			Assert.That(sends, Is.EqualTo(1)); Assert.That(DispatchTraceTelemetry.Dropped, Is.GreaterThan(before));
		}
		[Test]
		public async Task Exceptions_preserve_sender_behavior_and_do_not_record_exception_content()
		{
			using (DispatchTraceTelemetry.Begin(7, 1, 19, true))
			{
				Assert.ThrowsAsync<InvalidOperationException>(async () => await DispatchTraceTelemetry.AttemptAsync(DispatchTraceChannel.Email, "member", () => throw new InvalidOperationException("sensitive provider response")));
				await DispatchTraceTelemetry.AttemptAsync(DispatchTraceChannel.Push, "member", () => Task.FromResult(true));
			}
			var rows = new List<DispatchTraceObservation>(); while (DispatchTraceTelemetry.Reader.TryRead(out var row)) rows.Add(row);
			Assert.That(rows.Select(r => r.Stage), Is.EqualTo(new[] { DispatchTraceStage.Attempted, DispatchTraceStage.Failed, DispatchTraceStage.Attempted, DispatchTraceStage.ServiceCompleted }));
			Assert.That(rows.Select(r => r.AttemptId).Distinct().Count(), Is.EqualTo(1));
			Assert.That(rows.All(r => r.DepartmentId == 7 && r.QueueItemId == 19), Is.True);
			Assert.That(System.Text.Json.JsonSerializer.Serialize(rows), Does.Not.Contain("sensitive provider response"));
		}
		[Test]
		public void Disabled_capture_and_scope_disposal_leave_no_observations()
		{
			using (DispatchTraceTelemetry.Begin(7, 1, 19, false)) DispatchTraceTelemetry.Observe(DispatchTraceStage.Selected);
			DispatchTraceTelemetry.Observe(DispatchTraceStage.Selected);
			Assert.That(DispatchTraceTelemetry.Reader.TryRead(out _), Is.False);
		}
		[Test]
		public async Task Provider_handoff_has_a_logical_message_id_and_never_implies_delivery()
		{
			using (DispatchTraceTelemetry.Begin(7, 1, 19, true))
				await DispatchTraceTelemetry.AttemptAsync(DispatchTraceChannel.Sms, "member", () => {
					DispatchTraceTelemetry.ProviderResult(DispatchTraceProvider.Twilio, DispatchTraceChannel.Sms, "SM" + new string('a',32), true);
					return Task.FromResult(true);
				});
			var rows = new List<DispatchTraceObservation>(); while (DispatchTraceTelemetry.Reader.TryRead(out var row)) rows.Add(row);
			Assert.That(rows.Select(r => r.Stage), Is.EqualTo(new[] { DispatchTraceStage.Attempted, DispatchTraceStage.ProviderAccepted, DispatchTraceStage.ServiceCompleted }));
			Assert.That(rows.Select(r => r.LogicalMessageId).Distinct().Count(), Is.EqualTo(1));
			Assert.That(Guid.TryParse(rows[0].LogicalMessageId, out _), Is.True);
			Assert.That(rows.Select(r => r.Sequence), Is.EqualTo(new[] { 1,2,3 }));
			Assert.That(rows[1].ProviderMessageId, Does.StartWith("SM"));
		}
		[Test]
		public async Task Arbitrary_provider_response_content_is_not_captured_as_an_identifier()
		{
			using (DispatchTraceTelemetry.Begin(7, 1, 19, true))
				await DispatchTraceTelemetry.AttemptAsync(DispatchTraceChannel.Email, "member", () => {
					DispatchTraceTelemetry.ProviderResult(DispatchTraceProvider.Postmark, DispatchTraceChannel.Email, "private@example.test secret response", true);
					return Task.FromResult(true);
				});
			var rows = new List<DispatchTraceObservation>(); while (DispatchTraceTelemetry.Reader.TryRead(out var row)) rows.Add(row);
			Assert.That(rows[1].Stage, Is.EqualTo(DispatchTraceStage.ProviderResultUnknown)); Assert.That(rows[1].ProviderMessageId, Is.Null);
			Assert.That(System.Text.Json.JsonSerializer.Serialize(rows), Does.Not.Contain("private@example.test").And.Not.Contain("secret response"));
			DispatchTraceTelemetry.ProviderResult(DispatchTraceProvider.Postmark, DispatchTraceChannel.Email, Guid.NewGuid().ToString("D"), true);
			Assert.That(DispatchTraceTelemetry.Reader.TryRead(out _), Is.False);
		}
		[Test]
		public void Completed_observation_reports_capture_loss_when_the_per_broadcast_limit_is_reached()
		{
			using (DispatchTraceTelemetry.Begin(7, 1, 19, true))
			{
				for (var i = 0; i < 10002; i++) { DispatchTraceTelemetry.Observe(DispatchTraceStage.Selected); while (DispatchTraceTelemetry.Reader.TryRead(out _)) { } }
				DispatchTraceTelemetry.Observe(DispatchTraceStage.BroadcastCompleted);
			}
			Assert.That(DispatchTraceTelemetry.Reader.TryRead(out var terminal), Is.True);
			Assert.That(terminal.Sequence, Is.EqualTo(10003)); Assert.That(terminal.PriorDropped, Is.EqualTo(2));
		}
		[TestCase("queued", true)] [TestCase("completed", true)] [TestCase("failed", false)] [TestCase("busy", false)]
		public void Provider_creation_states_are_normalized_without_changing_the_send_result(string status, bool expected) =>
			Assert.That(DispatchProviderOutcome.CreationStatus(status), Is.EqualTo(expected));
		[Test]
		public void Missing_and_new_provider_states_remain_unknown()
		{
			Assert.That(DispatchProviderOutcome.CreationStatus(null), Is.Null);
			Assert.That(DispatchProviderOutcome.CreationStatus("provider-added-new-state"), Is.Null);
		}
	}
}
