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
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Models.v4.Checklists;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[Test]
		public async Task Offline_start_preserves_its_published_version_and_rejects_a_version_from_another_definition()
		{
			var first = await Start(); var version = (await _service.GetRunAsync(_actor, first.Run)).Completion.VersionId;
			var changed = Form(); changed.Name = "New published instructions";
			await _service.SaveDefinitionAsync(_actor, first.Definition, 2, changed); await _service.PublishAsync(_actor, first.Definition, 3);
			var id = Guid.NewGuid().ToString(); await _service.StartPinnedAsync(_actor, first.Definition, version, "77", id);
			(await _service.GetRunAsync(_actor, id)).Form.Name.Should().Be(first.Form.Name);
			(await _service.StartPinnedAsync(_actor, first.Definition, version, "77", id.ToUpperInvariant())).Should().Be(id);
			var other = await Start(); var otherVersion = (await _service.GetRunAsync(_actor, other.Run)).Completion.VersionId;
			Func<Task> invalid = () => _service.StartPinnedAsync(_actor, first.Definition, otherVersion, "77", Guid.NewGuid().ToString());
			(await invalid.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			await _service.RetireAsync(_actor, first.Definition, 4);
			Func<Task> retired = () => _service.StartPinnedAsync(_actor, first.Definition, version, "77", Guid.NewGuid().ToString());
			(await retired.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
		}
		[Test]
		public async Task Lost_progress_and_final_responses_replay_without_duplicate_revisions_or_events_but_changed_payload_conflicts()
		{
			var setup = await Start(); var input = Answers(setup.Form);
			(await _service.SaveRunAsync(_actor, setup.Run, input, false)).Should().Be(2);
			var audits = _audits.Invocations.Count;
			(await _service.SaveRunAsync(_actor, setup.Run, input, false)).Should().Be(2); _audits.Invocations.Count.Should().Be(audits);
			input.Note = "Changed while offline";
			Func<Task> stale = () => _service.SaveRunAsync(_actor, setup.Run, input, false);
			(await stale.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			input.Revision = 2; input.ClientCompletedOn = DateTime.UtcNow;
			(await _service.SaveRunAsync(_actor, setup.Run, input, true)).Should().Be(3);
			(await _service.SaveRunAsync(_actor, setup.Run, input, true)).Should().Be(3);
			_events.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ChecklistCompleted);
			input.Note = "Different final content"; (await stale.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
		}
		[Test]
		public async Task Occurrence_preview_is_read_only_and_the_client_GUID_wins_once()
		{
			var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime); setup.Clock.Now = setup.Clock.Now.AddHours(1);
			var occurrence = (await _store.ListAsync<ChecklistOccurrence>(77)).OrderBy(o => o.PeriodStartUtc).First();
			var preview = await _service.PreviewOccurrenceAsync(_actor, occurrence.Id); preview.Completion.Revision.Should().Be(0); preview.Form.Sections.Should().NotBeEmpty();
			(await _store.ListAsync<ChecklistCompletion>(77)).Should().BeEmpty();
			var id = Guid.NewGuid().ToString(); (await _service.StartOccurrenceWithIdAsync(_actor, occurrence.Id, id)).Should().Be(id);
			(await _service.StartOccurrenceWithIdAsync(_actor, occurrence.Id, id)).Should().Be(id);
			Func<Task> competitor = () => _service.StartOccurrenceWithIdAsync(_actor, occurrence.Id, Guid.NewGuid().ToString());
			(await competitor.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			(await _store.ListAsync<ChecklistCompletion>(77)).Should().ContainSingle();
			(await _service.PreviewOccurrenceAsync(_actor, occurrence.Id)).Completion.Revision.Should().Be(1);
		}
		[Test]
		public async Task Upload_retry_is_deduplicated_but_cannot_rebase_over_a_later_answer_edit()
		{
			var setup = await Start(); byte[] bytes;
			using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2))
			using (var stream = new System.IO.MemoryStream()) { image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder()); bytes = stream.ToArray(); }
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });
			var item = setup.Form.Sections[0].Items[0].Id;
			await _service.AddFileAtRevisionAsync(_actor, setup.Run, item, 1, "photo.png", "image/png", bytes);
			await _service.AddFileAtRevisionAsync(_actor, setup.Run, item, 1, "photo.png", "image/png", bytes);
			var view = await _service.GetRunAsync(_actor, setup.Run); view.Files.Should().ContainSingle(); view.Completion.Revision.Should().Be(2);
			var input = Answers(setup.Form); input.Revision = 2; await _service.SaveRunAsync(_actor, setup.Run, input, false);
			Func<Task> retry = () => _service.AddFileAtRevisionAsync(_actor, setup.Run, item, 1, "photo.png", "image/png", bytes);
			(await retry.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			Func<Task> remove = () => _service.DeleteFileAtRevisionAsync(_actor, view.Files.Single().Id, 2);
			(await remove.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(409);
			await _service.DeleteFileAtRevisionAsync(_actor, view.Files.Single().Id, 3);
			(await _service.GetRunAsync(_actor, setup.Run)).Files.Should().BeEmpty();
		}
		[Test]
		public async Task Mobile_due_pages_filter_before_pagination_and_never_offer_a_managers_draft_form()
		{
			_authorization.Setup(a => a.TargetsAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistTargetType>())).ReturnsAsync(new List<ChecklistTarget> { new ChecklistTarget { Type = ChecklistTargetType.Department, Id = "77", Name = "Department" } });
			for (var i = 0; i < 52; i++)
			{
				var form = Form(); form.Name = "Published " + i; var id = await _service.SaveDefinitionAsync(_actor, null, 0, form); await _service.PublishAsync(_actor, id, 1);
				form.Name = "SENSITIVE DRAFT " + i; await _service.SaveDefinitionAsync(_actor, id, 2, form);
			}
			var first = await _service.MobileDueAsync(_actor, new ChecklistMobileQuery()); first.Definitions.Should().HaveCount(50); first.HasMoreDefinitions.Should().BeTrue();
			var next = await _service.MobileDueAsync(_actor, new ChecklistMobileQuery { Page = 1 }); next.Definitions.Should().HaveCount(2); next.HasMoreDefinitions.Should().BeFalse();
			first.Definitions.Concat(next.Definitions).Should().OnlyContain(d => d.Definition.Form.Name.StartsWith("Published "));
			Newtonsoft.Json.JsonConvert.SerializeObject(first).Should().NotContain("SENSITIVE DRAFT");
		}
		[Test]
		public async Task Mobile_replay_rechecks_revoked_membership_and_ADP_before_returning_an_existing_run()
		{
			var setup = await Start(); var version = (await _service.GetRunAsync(_actor, setup.Run)).Completion.VersionId;
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "checklistcompletions.content" } }));
			Func<Task> retry = () => _service.StartPinnedAsync(_actor, setup.Definition, version, "77", setup.Run);
			(await retry.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			Func<Task> upload = () => _service.AddFileAtRevisionAsync(_actor, setup.Run, setup.Form.Sections[0].Items[0].Id, 1, "private.png", "image/png", new byte[] { 1 });
			(await upload.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			_scanner.Verify(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
			_authorization.Setup(a => a.RequireMemberAsync(It.IsAny<ChecklistActor>())).ThrowsAsync(new ChecklistException(403, "Active department membership is required."));
			Func<Task> save = () => _service.SaveRunAsync(_actor, setup.Run, Answers(setup.Form), true);
			(await save.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			_events.Where(e => e.Trigger.HasValue).Should().BeEmpty();
		}
		[Test]
		public async Task API_projection_excludes_storage_envelopes_binary_data_and_raw_JSON()
		{
			var setup = await Start(); var view = await _service.GetRunAsync(_actor, setup.Run);
			view.Completion.Content = "SENSITIVE RAW JSON"; view.Completion.ProtectedScoreEnvelope = "rgdp:secret";
			view.Files.Add(new ChecklistCompletionFile { Data = new byte[] { 1, 2, 3 }, Content = "image.png" });
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(ChecklistRunData.From(view, "author"));
			json.Should().NotContain("SENSITIVE RAW JSON").And.NotContain("rgdp:").And.NotContain("AQID").And.NotContain("ProtectedScoreEnvelope");
		}
	}
}
