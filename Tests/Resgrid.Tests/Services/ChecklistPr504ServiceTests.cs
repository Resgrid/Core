using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChecklistPr504ServiceTests
	{
		[Test]
		public async Task Audit_and_workflow_displays_never_overlap_policy_reads_and_preserve_order()
		{
			var active = 0; var maximum = 0;
			var policy = new Mock<IDepartmentDataProtectionService>();
			policy.Setup(p => p.IsProtectionEnforcedAsync(77)).Returns(async () =>
			{
				var count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
				try { await Task.Delay(10); return true; }
				finally { Interlocked.Decrement(ref active); }
			});
			var history = new Lazy<IReadinessHistoryProtectionService>(() => new ReadinessHistoryProtectionService(null, policy.Object));
			var audits = Enumerable.Range(1, 8).Select(id => new AuditLog { AuditLogId = id, DepartmentId = 77, LogType = (int)AuditLogTypes.ChecklistOccurrenceMissed, Data = "PRIVATE" }).ToList();
			var auditRepository = new Mock<IAuditLogsRepository>();
			auditRepository.Setup(r => r.GetAllByDepartmentIdAsync(77)).ReturnsAsync(audits);
			auditRepository.Setup(r => r.GetAuditLogsForDepartmentPagedAsync(77, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, 1, 50)).ReturnsAsync(audits);
			var auditService = new AuditService(auditRepository.Object, null, history);
			(await auditService.GetAllAuditLogsForDepartmentAsync(77)).Select(a => a.AuditLogId).Should().Equal(audits.Select(a => a.AuditLogId));
			(await auditService.GetAuditLogsForDepartmentPagedAsync(77, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, 1, 50)).Should().OnlyContain(a => a.Data == ProtectedDataEnvelope.RedactionValue);
			var runs = Enumerable.Range(1, 8).Select(id => new WorkflowRun
			{
				WorkflowRunId = id.ToString(), DepartmentId = 77, TriggerEventType = (int)WorkflowTriggerEventType.ChecklistCompleted, InputPayload = "PRIVATE",
				Logs = new[] { new WorkflowRunLog { WorkflowRunLogId = id + "a", RenderedOutput = "PRIVATE" }, new WorkflowRunLog { WorkflowRunLogId = id + "b", RenderedOutput = "PRIVATE" } }
			}).ToList();
			var runRepository = new Mock<IWorkflowRunRepository>();
			runRepository.Setup(r => r.GetByDepartmentIdPagedAsync(77, 1, 50)).ReturnsAsync(runs);
			var workflows = new WorkflowService(null, null, null, runRepository.Object, null, null, null, null, null, null, null, history: history);
			var display = await workflows.GetRunsByDepartmentIdAsync(77, 1, 50);
			display.Select(r => r.WorkflowRunId).Should().Equal(runs.Select(r => r.WorkflowRunId));
			display.Should().OnlyContain(r => r.InputPayload == ProtectedDataEnvelope.RedactionValue);
			foreach (var run in display)
			{
				run.Logs.Select(l => l.WorkflowRunLogId).Should().Equal(run.WorkflowRunId + "a", run.WorkflowRunId + "b");
				run.Logs.Should().OnlyContain(l => l.RenderedOutput == ProtectedDataEnvelope.RedactionValue);
			}
			maximum.Should().Be(1, "all display work shares one repository connection");
			audits.Should().OnlyContain(a => a.Data == "PRIVATE");
			runs.Should().OnlyContain(r => r.InputPayload == "PRIVATE" && r.Logs.All(l => l.RenderedOutput == "PRIVATE"));
		}

		[TestCase(true), TestCase(false)]
		public async Task Cache_failure_after_commit_still_publishes_audit_and_never_rolls_back(bool failCache)
		{
			var flags = new Mock<IFeatureFlagRepository>();
			flags.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<FeatureFlag>());
			flags.Setup(r => r.SaveOrUpdateAsync(It.IsAny<FeatureFlag>(), It.IsAny<CancellationToken>(), false)).ReturnsAsync((FeatureFlag f, CancellationToken ct, bool first) => f);
			var cache = new Mock<ICacheProvider>();
			if (failCache) cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Cache unavailable"));
			var unit = new Mock<IUnitOfWork>(); var events = new Mock<IEventAggregator>(); var committed = false;
			unit.Setup(u => u.CommitChanges()).Callback(() => committed = true);
			events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback(() => committed.Should().BeTrue());
			var service = new FeatureToggleService(flags.Object, null, null, null, null, cache.Object, events.Object, null, null, unit.Object, Mock.Of<IFeatureFlagMutationObserver>());
			Func<Task> save = () => service.SaveFlagAsync(new FeatureFlag { FlagKey = "Review.Test" }, "actor");
			await save.Should().NotThrowAsync();
			cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Exactly(3));
			unit.Verify(u => u.CommitChanges(), Times.Once);
			unit.Verify(u => u.DiscardChanges(), Times.Never);
			events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.UserId == "actor" && a.Type == AuditLogTypes.FeatureFlagChanged)), Times.Once);
		}
	}
}
