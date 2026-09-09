using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public partial class ChecklistWorkflowTests
	{
		private readonly ChecklistActor _actor = new ChecklistActor { DepartmentId = 77, UserId = "author" };
		private MemoryStore _store;
		private Mock<IReadinessAccessService> _access;
		private Mock<IChecklistAuthorizationService> _authorization;
		private Mock<IAuditLogsRepository> _audits;
		private Mock<IDomainEventOutboxService> _outbox;
		private Mock<IProtectedWriteService> _write;
		private Mock<IProtectedReadService> _read;
		private Mock<IRecordAttachmentScanner> _scanner;
		private Mock<IUnitOfWork> _uow;
		private ChecklistsService _service;
		private List<DomainEventEnvelope> _events;
		[SetUp]
		public void Setup()
		{
			_store = new MemoryStore(); _events = new List<DomainEventEnvelope>();
			_access = new Mock<IReadinessAccessService>(); _access.Setup(s => s.CanUseChecklistsAsync(77)).ReturnsAsync(true);
			_authorization = new Mock<IChecklistAuthorizationService>();
			_authorization.Setup(s => s.RequireMemberAsync(It.IsAny<ChecklistActor>())).Returns(Task.CompletedTask);
			_authorization.Setup(s => s.CanManageAsync(It.IsAny<ChecklistActor>())).ReturnsAsync(true);
			_authorization.Setup(s => s.CanReadAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistCompletion>())).ReturnsAsync((ChecklistActor a, ChecklistCompletion c) => c != null && c.DepartmentId == a.DepartmentId && (c.CreatedBy == a.UserId || c.WitnessUserId == a.UserId));
			_authorization.Setup(s => s.ReadFilterAsync(It.IsAny<ChecklistActor>())).ReturnsAsync((ChecklistActor a) => new Func<ChecklistCompletion, Task<bool>>(c => _authorization.Object.CanReadAsync(a, c)));
			_authorization.Setup(s => s.TargetAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistTargetType>(), It.IsAny<string>())).ReturnsAsync((ChecklistActor a, ChecklistTargetType t, string id) => new ChecklistTarget { Type = t, Id = id, Name = "Test target" });
			_audits = new Mock<IAuditLogsRepository>();
			_audits.Setup(s => s.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((AuditLog log, CancellationToken ct, bool first) => { log.AuditLogId = 1; return log; });
			_outbox = new Mock<IDomainEventOutboxService>();
			_outbox.Setup(s => s.EnqueueAsync(77, "Checklists", It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, string p, DomainEventEnvelope e, CancellationToken c) => { _events.Add(e); return new DomainEventOutboxEntry { DomainEventOutboxId = _events.Count }; });
			_read = new Mock<IProtectedReadService>(); _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
			_write = new Mock<IProtectedWriteService>(); _write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
			_scanner = new Mock<IRecordAttachmentScanner>();
			_uow = new Mock<IUnitOfWork>(); _uow.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { _store.Begin(); return (DbConnection)null; });
			_uow.Setup(u => u.DiscardChanges()).Callback(() => _store.Rollback());
			_service = new ChecklistsService(_store, _authorization.Object, _access.Object, _uow.Object, _audits.Object, _outbox.Object, new Lazy<IProtectedReadService>(() => _read.Object), new Lazy<IProtectedWriteService>(() => _write.Object), _scanner.Object);
		}
		private static ChecklistForm Form(bool witness = false) => new ChecklistForm { Name = "Shift readiness", RequiresIndependentWitness = witness, Sections = { new ChecklistSection { Name = "Safety", Items = { new ChecklistItem { Name = "Equipment works", Critical = true }, new ChecklistItem { Name = "Fuel adequate" } } } } };
		private async Task<(string Definition, string Run, ChecklistForm Form)> Start(bool witness = false)
		{
			var form = Form(witness); var definition = await _service.SaveDefinitionAsync(_actor, null, 0, form);
			await _service.PublishAsync(_actor, definition, 1);
			var run = await _service.StartAsync(_actor, definition, "77", Guid.NewGuid().ToString()); return (definition, run, form);
		}
		private static ChecklistRunInput Answers(ChecklistForm form, string value = "pass") => new ChecklistRunInput { Revision = 1,
			Answers = form.Sections.SelectMany(s => s.Items).Select(i => new ChecklistAnswer { ItemId = i.Id, Status = ChecklistAnswerStatus.Answered, Value = value, Note = value == "fail" ? "Removed from service; supervisor notified" : null }).ToList() };

		[Test]
		public async Task Protected_writes_cannot_persist_fields_skipped_by_an_older_catalog()
		{
			_write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed(isProtected: true)));
			Func<Task> save = () => _service.SaveDefinitionAsync(_actor, null, 0, Form());
			(await save.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			(await _store.ListAsync<ChecklistDefinition>(77)).Should().BeEmpty();
		}

		[Test]
		public async Task History_cannot_read_unmigrated_protected_outcomes_and_audits_do_not_copy_them()
		{
			var run = await Start(); await _service.SaveRunAsync(_actor, run.Run, Answers(run.Form, "fail"), true);
			_audits.Verify(s => s.InsertAsync(It.Is<AuditLog>(l => l.Data.Contains("Score") || l.Data.Contains("Passed") || l.Data.Contains("FailedItemIds")), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { IsProtected = true }));
			Func<Task> history = () => _service.HistoryAsync(_actor, run.Definition);
			(await history.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
		}
		[Test]
		public async Task Published_version_is_pinned_and_draft_edits_do_not_change_a_started_run()
		{
			var run = await Start(); var view = await _service.GetRunAsync(_actor, run.Run); var version = view.Completion.VersionId;
			run.Form.Name = "Changed draft"; run.Form.TargetType = ChecklistTargetType.Personnel;
			await _service.SaveDefinitionAsync(_actor, run.Definition, 2, run.Form);
			var detail = await _service.GetDefinitionAsync(_actor, run.Definition);
			detail.Form.TargetType.Should().Be(ChecklistTargetType.Personnel); detail.PublishedForm.TargetType.Should().Be(ChecklistTargetType.Department);
			await _service.PublishAsync(_actor, run.Definition, 3);
			view = await _service.GetRunAsync(_actor, run.Run); view.Form.Name.Should().Be("Shift readiness"); view.Completion.VersionId.Should().Be(version);
		}
		[Test]
		public async Task Create_edit_run_fail_history_audit_and_workflow_are_connected()
		{
			var run = await Start(); var input = Answers(run.Form, "fail");
			await _service.SaveRunAsync(_actor, run.Run, input, true);
			var result = await _service.GetRunAsync(_actor, run.Run);
			result.Completion.State.Should().Be((int)ChecklistRunState.Submitted); result.Completion.Score.Should().Be(0); result.Completion.Passed.Should().BeFalse();
			result.Input.Answers.Should().HaveCount(2); (await _service.HistoryAsync(_actor, run.Definition)).Should().ContainSingle();
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.ChecklistFailed).Should().Be(2); _events.Count(e => e.Trigger == WorkflowTriggerEventType.ChecklistCompleted).Should().Be(1);
			JsonConvert.SerializeObject(_events).Should().NotContain("Removed from service").And.NotContain("Equipment works");
			_audits.Verify(a => a.InsertAsync(It.Is<AuditLog>(l => l.LogType == (int)AuditLogTypes.ChecklistCompletionSubmitted && l.ObjectId == run.Run), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}
		[Test]
		public async Task A_failed_required_note_leaves_no_partial_submission_or_events()
		{
			var run = await Start(); var input = Answers(run.Form, "fail"); input.Answers[0].Note = null;
			Func<Task> submit = () => _service.SaveRunAsync(_actor, run.Run, input, true);
			(await submit.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(400);
			var current = await _service.GetRunAsync(_actor, run.Run); current.Completion.State.Should().Be(0); current.Input.Answers.Should().BeEmpty(); _events.Where(e => e.Trigger.HasValue).Should().BeEmpty();
		}
		[Test]
		public async Task Stale_progress_cannot_overwrite_newer_answers()
		{
			var run = await Start(); var input = Answers(run.Form); await _service.SaveRunAsync(_actor, run.Run, input, false);
			input.Answers[0].Value = "fail";
			Func<Task> stale = () => _service.SaveRunAsync(_actor, run.Run, input, false);
			(await stale.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			(await _service.GetRunAsync(_actor, run.Run)).Input.Answers[0].Value.Should().Be("pass");
		}
		[Test]
		public async Task Identical_terminal_retry_is_idempotent_and_changed_payload_conflicts()
		{
			var run = await Start(); var input = Answers(run.Form); var revision = await _service.SaveRunAsync(_actor, run.Run, input, true);
			(await _service.SaveRunAsync(_actor, run.Run, input, true)).Should().Be(revision); _events.Where(e => e.Trigger.HasValue).Should().ContainSingle();
			input.Note = "Changed after submission";
			Func<Task> changed = () => _service.SaveRunAsync(_actor, run.Run, input, true);
			(await changed.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
		}
		[Test]
		public async Task Repeated_start_returns_the_same_occurrence_but_cannot_change_target()
		{
			var run = await Start(); (await _service.StartAsync(_actor, run.Definition, "77", run.Run)).Should().Be(run.Run);
			(await _store.ListAsync<ChecklistOccurrence>(77)).Should().ContainSingle();
			Func<Task> changed = () => _service.StartAsync(_actor, run.Definition, "different", run.Run);
			(await changed.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
		}
		[Test]
		public async Task Author_cannot_witness_and_a_distinct_authenticated_witness_finalizes_once()
		{
			var run = await Start(true); await _service.SaveRunAsync(_actor, run.Run, Answers(run.Form), true);
			var current = await _service.GetRunAsync(_actor, run.Run); current.Completion.State.Should().Be(1); _events.Where(e => e.Trigger.HasValue).Should().BeEmpty();
			Func<Task> own = () => _service.WitnessAsync(_actor, run.Run, current.Completion.SubmissionHash, "I verified the count.");
			(await own.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			var witness = new ChecklistActor { DepartmentId = 77, UserId = "witness" };
			Func<Task> unauthorized = () => _service.WitnessAsync(witness, run.Run, current.Completion.SubmissionHash, "Verified");
			(await unauthorized.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
			_authorization.Setup(a => a.CanReadAsync(witness, It.IsAny<ChecklistCompletion>())).ReturnsAsync(true);
			await _service.WitnessAsync(witness, run.Run, current.Completion.SubmissionHash, "I independently verified the count.");
			await _service.WitnessAsync(witness, run.Run, current.Completion.SubmissionHash, "I independently verified the count.");
			current = await _service.GetRunAsync(_actor, run.Run); current.Completion.WitnessUserId.Should().Be("witness"); current.Completion.State.Should().Be(2); _events.Where(e => e.Trigger.HasValue).Should().ContainSingle();
		}
		[Test]
		public async Task Historical_reads_survive_flag_disable_but_writes_stop()
		{
			var run = await Start(); _access.Setup(s => s.CanUseChecklistsAsync(77)).ReturnsAsync(false);
			(await _service.GetRunAsync(_actor, run.Run)).Should().NotBeNull(); (await _service.HistoryAsync(_actor, run.Definition)).Should().ContainSingle();
			Func<Task> write = () => _service.SaveRunAsync(_actor, run.Run, Answers(run.Form), false);
			(await write.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
		}
		[Test]
		public async Task Another_department_cannot_fetch_run_or_definition_by_known_id()
		{
			var run = await Start(); var foreign = new ChecklistActor { DepartmentId = 88, UserId = "author" };
			Func<Task> read = () => _service.GetRunAsync(foreign, run.Run); (await read.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
			read = () => _service.GetDefinitionAsync(foreign, run.Definition); (await read.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
		}
		[Test]
		public async Task Audit_failure_rolls_back_answers_and_completion_without_dispatch()
		{
			var run = await Start(); _outbox.Invocations.Clear();
			_audits.Setup(a => a.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("Audit unavailable"));
			Func<Task> save = () => _service.SaveRunAsync(_actor, run.Run, Answers(run.Form), false); await save.Should().ThrowAsync<InvalidOperationException>();
			var current = await _service.GetRunAsync(_actor, run.Run); current.Completion.Revision.Should().Be(1); current.Input.Answers.Should().BeEmpty();
			_outbox.Verify(o => o.DispatchAfterCommitAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public async Task Protected_write_denial_does_not_persist_plaintext()
		{
			_write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Blocked("broker_unavailable")));
			Func<Task> save = () => _service.SaveDefinitionAsync(_actor, null, 0, Form()); (await save.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			(await _store.ListAsync<ChecklistDefinition>(77)).Should().BeEmpty();
		}
		[Test]
		public async Task Redacted_definition_cannot_be_round_tripped_as_an_edit()
		{
			var run = await Start(); _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistdefinitions.content" } }));
			Func<Task> save = () => _service.SaveDefinitionAsync(_actor, run.Definition, 2, run.Form); (await save.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
		}
		[Test]
		public async Task Retirement_stops_new_runs_but_preserves_versions_and_existing_runs()
		{
			var run = await Start(); await _service.RetireAsync(_actor, run.Definition, 2);
			Func<Task> start = () => _service.StartAsync(_actor, run.Definition, "77", Guid.NewGuid().ToString()); (await start.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			await _service.SaveRunAsync(_actor, run.Run, Answers(run.Form), true); (await _service.GetRunAsync(_actor, run.Run)).Completion.State.Should().Be(2);
		}
		[Test]
		public async Task Images_need_a_clean_scan_and_scan_rejection_leaves_no_file()
		{
			var run = await Start(); byte[] png;
			using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1))
			using (var stream = new System.IO.MemoryStream()) { image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder()); png = stream.ToArray(); }
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Skipped });
			Func<Task> upload = () => _service.AddFileAsync(_actor, run.Run, run.Form.Sections[0].Items[0].Id, "evidence.png", "image/png", png);
			(await upload.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409); (await _store.ListAsync<ChecklistCompletionFile>(77)).Should().BeEmpty();
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });
			await upload(); await upload(); (await _store.ListAsync<ChecklistCompletionFile>(77)).Should().ContainSingle();
			var view = await _service.GetRunAsync(_actor, run.Run); var input = Answers(run.Form); input.Revision = view.Completion.Revision; await _service.SaveRunAsync(_actor, run.Run, input, true);
			Func<Task> remove = () => _service.DeleteFileAsync(_actor, view.Files[0].Id); (await remove.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
		}
		internal sealed partial class MemoryStore : IChecklistRepository
		{
			private Dictionary<(Type, string), string> _rows = new Dictionary<(Type, string), string>();
			private Dictionary<(Type, string), string> _backup;
			public void Begin() => _backup = new Dictionary<(Type, string), string>(_rows);
			public void Rollback() { if (_backup != null) _rows = new Dictionary<(Type, string), string>(_backup); }
			public Task<List<ChecklistOccurrence>> ReportOccurrencesAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, int skip, CancellationToken ct = default) => Task.FromResult(_rows.Where(p => p.Key.Item1 == typeof(ChecklistOccurrence)).Select(p => JsonConvert.DeserializeObject<ChecklistOccurrence>(p.Value)).Where(r => r.DepartmentId == departmentId && (r.PeriodStartUtc ?? r.CreatedOn) >= fromUtc && (r.PeriodStartUtc ?? r.CreatedOn) < untilUtc).OrderBy(r => r.CreatedOn).ThenBy(r => r.Id).Skip(skip).Take(500).ToList());
			public Task LockDepartmentAsync(int departmentId, CancellationToken ct = default) => Task.CompletedTask;
			public Task<T> GetAsync<T>(int departmentId, string id, CancellationToken ct = default) where T : ChecklistRow
			{
				var row = id != null && _rows.TryGetValue((typeof(T), id), out var json) ? JsonConvert.DeserializeObject<T>(json) : null;
				return Task.FromResult(row?.DepartmentId == departmentId ? row : null);
			}
			public Task<List<T>> ListAsync<T>(int departmentId, string parentId = null, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow => Task.FromResult(_rows.Where(p => p.Key.Item1 == typeof(T)).Select(p => JsonConvert.DeserializeObject<T>(p.Value)).Where(r => r.DepartmentId == departmentId && (parentId == null || r.ParentId == parentId)).OrderByDescending(r => r.CreatedOn).Skip(skip).Take(take).ToList());
			public int ChildQueries { get; private set; }
			public Task<List<T>> ListChildrenAsync<T>(int departmentId, IReadOnlyCollection<string> parentIds, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow
			{
				ChildQueries++;
				var rows = _rows.Where(p => p.Key.Item1 == typeof(T)).Select(p => JsonConvert.DeserializeObject<T>(p.Value))
					.Where(r => r.DepartmentId == departmentId && parentIds.Contains(r.ParentId)).OrderByDescending(r => r.CreatedOn).ThenBy(r => r.Id).Skip(skip).Take(take).ToList();
				foreach (var file in rows.OfType<ChecklistCompletionFile>()) file.Data = null;
				return Task.FromResult(rows);
			}
			public Task WriteAsync<T>(T row, bool insert, CancellationToken ct = default) where T : ChecklistRow { var key = (typeof(T), row.Id); if (insert && _rows.ContainsKey(key)) throw new InvalidOperationException("Duplicate ID"); _rows[key] = JsonConvert.SerializeObject(row); return Task.CompletedTask; }
			public async Task ReplaceAnswersAsync(int departmentId, string completionId, IEnumerable<ChecklistCompletionItem> items, CancellationToken ct = default)
			{ foreach (var old in await ListAsync<ChecklistCompletionItem>(departmentId, completionId, take: 500)) _rows.Remove((typeof(ChecklistCompletionItem), old.Id)); foreach (var item in items) await WriteAsync(item, true, ct); }
			public Task DeleteFileAsync(int departmentId, string id, CancellationToken ct = default) { _rows.Remove((typeof(ChecklistCompletionFile), id)); return Task.CompletedTask; }
			public async Task<ChecklistCompletionFile> GetFileMetadataAsync(int departmentId, string id) { var file = await GetAsync<ChecklistCompletionFile>(departmentId, id); if (file != null) file.Data = null; return file; }
		}
	}
}
