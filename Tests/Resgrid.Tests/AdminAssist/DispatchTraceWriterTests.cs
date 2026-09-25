using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class DispatchTraceWriterTests
	{
		private Mock<IAdminAssistTraceStore> _store;
		private Mock<IProtectedWriteService> _write;
		private Mock<IDepartmentDataProtectionService> _protection;
		private AdminAssistTraceWriter _writer;
		private Mock<IFeatureToggleService> _flags;
		private bool _wasCaptureEnabled;
		[SetUp]
		public void SetUp()
		{
			_store = new(); _write = new(); _protection = new();
			_wasCaptureEnabled = Resgrid.Config.AdminAssistConfig.CaptureDispatchTraces;
			Resgrid.Config.AdminAssistConfig.CaptureDispatchTraces = true;
			_flags = new(); _flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminAssist, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
			_store.Setup(s => s.TraceDepartmentExistsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_protection.Setup(p => p.GetPinnedCatalogVersionAsync(7)).ReturnsAsync(30);
			SetWrite(false); _writer = new(_store.Object, _write.Object, _protection.Object, _flags.Object);
		}
		[TearDown] public void Reset() => Resgrid.Config.AdminAssistConfig.CaptureDispatchTraces = _wasCaptureEnabled;
		[TestCase(false, true)] [TestCase(true, false)]
		public async Task New_trace_requires_both_host_capture_and_department_assist_rollout(bool capture, bool enabled)
		{
			Resgrid.Config.AdminAssistConfig.CaptureDispatchTraces = capture;
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminAssist, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = enabled });
			var observation = JsonSerializer.Deserialize<DispatchTraceObservation>(DispatchTraceQueueTests.Row().Content);
			Assert.That(await _writer.PrepareAsync(observation, CancellationToken.None), Is.Null);
			await _writer.PersistAsync(observation, CancellationToken.None);
			_write.VerifyNoOtherCalls(); _store.VerifyNoOtherCalls(); _protection.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Flag_store_failure_does_not_create_new_durable_trace_evidence()
		{
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminAssist, 7)).ThrowsAsync(new InvalidOperationException("Unavailable"));
			Assert.That(await _writer.PrepareAsync(JsonSerializer.Deserialize<DispatchTraceObservation>(DispatchTraceQueueTests.Row().Content), CancellationToken.None), Is.Null);
			_write.VerifyNoOtherCalls(); _store.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Disabling_rollout_does_not_discard_already_queued_evidence_or_bypass_protection()
		{
			Resgrid.Config.AdminAssistConfig.CaptureDispatchTraces = false;
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminAssist, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(7)).ReturnsAsync(true); SetWrite(true);
			await _writer.PersistPreparedAsync(DispatchTraceQueueTests.Row(), CancellationToken.None);
			_store.Verify(s => s.SaveTraceAsync(It.Is<AdminAssistDispatchTraceRow>(r => r.IsProtected), It.IsAny<CancellationToken>()), Times.Once);
		}
		private void SetWrite(bool encrypt, bool fail = false)
		{
			_write.Setup(w => w.PrepareRecordsEntityWriteAsync(7, It.IsAny<AdminAssistDispatchTraceRow>(), null, It.IsAny<string>(),
				It.IsAny<IReadOnlyDictionary<string, (Func<AdminAssistDispatchTraceRow, string>, Action<AdminAssistDispatchTraceRow, string>)>>(),
				It.IsAny<Action>(), null, null, true, It.IsAny<CancellationToken>()))
				.Callback(new InvocationAction(i =>
				{
					if (encrypt && !fail) { ((AdminAssistDispatchTraceRow)i.Arguments[1]).Content = "rgdp:1:1:encrypted-test-payload"; ((Action)i.Arguments[5])(); }
				})).ReturnsAsync(fail ? ProtectedWriteResult.Blocked("broker_unavailable") : ProtectedWriteResult.Allowed(encrypt));
		}
		[Test]
		public async Task Preparing_for_the_queue_encrypts_before_any_persistence()
		{
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(7)).ReturnsAsync(true); SetWrite(true);
			var row = DispatchTraceQueueTests.Row();
			var prepared = await _writer.PrepareAsync(JsonSerializer.Deserialize<DispatchTraceObservation>(row.Content), CancellationToken.None);
			Assert.That(prepared.IsProtected, Is.True); Assert.That(prepared.Content, Does.Not.Contain("member-reference"));
			_store.Verify(s => s.SaveTraceAsync(It.IsAny<AdminAssistDispatchTraceRow>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public async Task Enrollment_after_queueing_rechecks_protection_before_storage()
		{
			var source = DispatchTraceQueueTests.Row();
			var prepared = await _writer.PrepareAsync(JsonSerializer.Deserialize<DispatchTraceObservation>(source.Content), CancellationToken.None);
			Assert.That(prepared.IsProtected, Is.False);
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(7)).ReturnsAsync(true); SetWrite(true);
			await _writer.PersistPreparedAsync(prepared, CancellationToken.None);
			_store.Verify(s => s.SaveTraceAsync(It.Is<AdminAssistDispatchTraceRow>(r => r.IsProtected && r.ProtectedCatalogVersion == 30 && r.Content.StartsWith("rgdp:")), It.IsAny<CancellationToken>()), Times.Once);
		}
		[Test]
		public void A_protection_outage_does_not_fall_back_to_plaintext()
		{
			SetWrite(true, true);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await _writer.PersistPreparedAsync(DispatchTraceQueueTests.Row(), CancellationToken.None));
			_store.Verify(s => s.SaveTraceAsync(It.IsAny<AdminAssistDispatchTraceRow>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public void Old_protection_catalog_requires_upgrade_before_queueing()
		{
			_protection.Setup(p => p.ShouldEncryptNewWritesAsync(7)).ReturnsAsync(true); _protection.Setup(p => p.GetPinnedCatalogVersionAsync(7)).ReturnsAsync(29);
			Assert.ThrowsAsync<InvalidOperationException>(async () => await _writer.PrepareAsync(JsonSerializer.Deserialize<DispatchTraceObservation>(DispatchTraceQueueTests.Row().Content), CancellationToken.None));
			_write.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Late_queue_delivery_does_not_recreate_deleted_department_evidence()
		{
			_store.Setup(s => s.TraceDepartmentExistsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			await _writer.PersistPreparedAsync(DispatchTraceQueueTests.Row(), CancellationToken.None);
			_write.VerifyNoOtherCalls(); _store.Verify(s => s.SaveTraceAsync(It.IsAny<AdminAssistDispatchTraceRow>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
