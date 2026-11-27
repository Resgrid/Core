using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Protocols P3 services: block drafts and "Convert to blocks" in the library lifecycle (old versions keep their hash), step-through
	/// log persistence with its optimistic revision, the offline manifest and the staleness answer, the PDF book (TOC, stamps, the seal
	/// binding and the fail-closed unsealed path), and content translations (fallback, a hash of their own, approval bound to it).
	/// </summary>
	[TestFixture]
	public class ProtocolFieldToolsServiceTests
	{
		private const int Dept = ProtocolLibraryWorld.DepartmentId;
		private const string Author = ProtocolLibraryWorld.ManagerId;

		private static string BlockJson() => ProtocolBlocks.Normalize(new ProtocolContentDocument
		{
			Schema = ProtocolContentDocument.BlockSchema,
			Blocks = new List<ProtocolContentBlock>
			{
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Heading, Key = "h1", Text = "Entry" },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Steps, Key = "s1", Items = new List<ProtocolStepItem> { new ProtocolStepItem { Key = "a", Html = "Check air" }, new ProtocolStepItem { Key = "b", Html = "Buddy check" } } },
				new ProtocolContentBlock { Type = ProtocolBlockTypes.Medication, Key = "m1", Medication = new ProtocolMedication { Drug = "Atropine", DosePerKg = "0.02 mg/kg" } }
			}
		}).ToJson();

		#region Library: block drafts and conversion

		[Test]
		public async Task A_block_draft_is_rendered_by_the_server_and_its_hash_is_the_hash_of_that_html()
		{
			var world = new ProtocolLibraryWorld();
			world.AddProtocol(1);
			world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, "<p>Old</p>");

			var result = await world.Service.SaveDraftAsync(ProtocolLibraryWorld.Manager(), 1, new ProtocolDraftInput { ContentJson = BlockJson(), ExpectedRowVersion = 3 });

			result.Succeeded.Should().BeTrue();
			var stored = world.Versions[100];
			ProtocolContentDocument.Parse(stored.ContentJson, null).IsBlockModel.Should().BeTrue();
			stored.ContentHtml.Should().StartWith(Resgrid.Framework.StructuredHtml.DocumentMarker).And.Contain("id=\"b-s1-a\"");
			stored.ContentHash.Should().Be(ProtocolContentHash.Compute(stored.ContentHtml));
		}

		[Test]
		public async Task Convert_to_blocks_changes_only_the_draft_and_leaves_the_published_version_and_its_hash_alone()
		{
			var world = new ProtocolLibraryWorld();
			const string published = "<h2>Entry</h2><ol><li>Check air</li></ol>";
			world.AddProtocol(1, ProtocolStatuses.Published, 50);
			var current = world.AddVersion(50, 1, ProtocolVersionStatuses.Published, published, 1, 0);
			var hashBefore = current.ContentHash;
			world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, published);

			var result = await world.Service.ConvertDraftToBlocksAsync(ProtocolLibraryWorld.Manager(), 1, 3);

			result.Succeeded.Should().BeTrue();
			world.Versions[50].ContentHtml.Should().Be(published);
			world.Versions[50].ContentHash.Should().Be(hashBefore);
			var draft = world.Versions[100];
			ProtocolContentDocument.Parse(draft.ContentJson, null).Blocks.Select(b => b.Type).Should().Equal(ProtocolBlockTypes.Heading, ProtocolBlockTypes.Steps);
			draft.ContentHash.Should().Be(ProtocolContentHash.Compute(draft.ContentHtml));
			world.Events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.ProtocolContentConverted)), Times.Once);
		}

		[Test]
		public async Task Convert_to_blocks_is_refused_for_a_locked_draft_and_a_stale_copy()
		{
			var world = new ProtocolLibraryWorld();
			world.AddProtocol(1);
			world.AddVersion(100, 1, ProtocolVersionStatuses.Draft, "<p>x</p>");

			(await world.Service.ConvertDraftToBlocksAsync(ProtocolLibraryWorld.Manager(), 1, 2)).Error.Should().Be(ProtocolLifecycleErrors.StaleCopy);

			world.Versions[100].Status = (int)ProtocolVersionStatuses.InReview;
			(await world.Service.ConvertDraftToBlocksAsync(ProtocolLibraryWorld.Manager(), 1, 3)).Error.Should().Be(ProtocolLifecycleErrors.NotDraft);
			world.Versions[100].ContentHtml.Should().Be("<p>x</p>");
		}

		[Test]
		public async Task A_new_protocol_from_the_wizard_starts_as_a_block_document()
		{
			var world = new ProtocolLibraryWorld();
			world.ProtocolsService.Setup(x => x.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatchProtocol p, CancellationToken _) => { p.DispatchProtocolId = 77; return p; });

			var created = await world.Service.CreateAsync(new ProtocolCreateRequest
			{
				DepartmentId = Dept, UserId = Author, AsBlocks = true, ContentHtml = "<h2>Intro</h2><p>Text</p>",
				Protocol = new DispatchProtocol { Name = "Entry", Code = "EN", Kind = (int)ProtocolKinds.Procedure }
			});

			created.Succeeded.Should().BeTrue();
			ProtocolContentDocument.Parse(created.Version.ContentJson, null).IsBlockModel.Should().BeTrue();
			created.Version.ContentHtml.Should().Contain("rgp-heading");
		}

		#endregion Library: block drafts and conversion

		#region Step-through

		private sealed class StepWorld
		{
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<ICallsService> Calls = new Mock<ICallsService>();
			public readonly Mock<ICallProtocolsRepository> Rows = new Mock<ICallProtocolsRepository>();
			public readonly Mock<IProtocolAccessService> Access = new Mock<IProtocolAccessService>();
			public readonly ProtocolStepThroughService Service;
			public CallProtocol Row;
			public ProtocolVersion Version;
			public int Conflicts;
			public readonly List<CallProtocolStepNote> Notes = new List<CallProtocolStepNote>();
			public readonly Mock<ICallProtocolStepNotesRepository> NoteRows = new Mock<ICallProtocolStepNotesRepository>();

			public StepWorld(ProtocolKinds kind = ProtocolKinds.Procedure, IProtectedReadService read = null, IProtectedWriteService write = null, IProtectedGrantContext grant = null)
			{
				// The note rows (M0403): the unique index on (call protocol, client step id) is enforced like the database does.
				NoteRows.Setup(x => x.InsertAsync(It.IsAny<CallProtocolStepNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((CallProtocolStepNote n, CancellationToken _, bool __) =>
					{
						if (n.ClientStepId != null && Notes.Any(x => x.CallProtocolId == n.CallProtocolId && x.ClientStepId == n.ClientStepId))
							throw new InvalidOperationException("duplicate key");
						Notes.Add(n);
						return n;
					});
				NoteRows.Setup(x => x.GetByClientStepIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
					.ReturnsAsync((int d, int cp, string cid) => Notes.FirstOrDefault(x => x.DepartmentId == d && x.CallProtocolId == cp && x.ClientStepId == cid));
				NoteRows.Setup(x => x.GetByIdsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>()))
					.ReturnsAsync((int d, IReadOnlyCollection<string> ids) => Notes.Where(x => x.DepartmentId == d && ids.Contains(x.CallProtocolStepNoteId))
						.Select(x => new CallProtocolStepNote { CallProtocolStepNoteId = x.CallProtocolStepNoteId, DepartmentId = x.DepartmentId, CallId = x.CallId, CallProtocolId = x.CallProtocolId,
							StepId = x.StepId, ClientStepId = x.ClientStepId, UserId = x.UserId, Note = x.Note, IsProtected = x.IsProtected, CreatedOn = x.CreatedOn }).ToList());
				NoteRows.Setup(x => x.DeleteAsync(It.IsAny<CallProtocolStepNote>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((CallProtocolStepNote n, CancellationToken _) => Notes.RemoveAll(x => x.CallProtocolStepNoteId == n.CallProtocolStepNoteId) > 0);
				var json = BlockJson();
				Version = new ProtocolVersion { ProtocolVersionId = 5, DepartmentId = Dept, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Published, ContentJson = json, ContentHtml = ProtocolContentDocument.Parse(json, null).Render(), Major = 1 };
				Row = new CallProtocol { CallProtocolId = 300, CallId = 40, DispatchProtocolId = 9, ProtocolVersionId = 5 };
				Calls.Setup(x => x.GetCallByIdAsync(40, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 40, DepartmentId = Dept });
				Protocols.Setup(x => x.GetProtocolHeaderByIdAsync(9)).ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 9, DepartmentId = Dept, Kind = (int)kind, Code = "RIT", Name = "RIT" });
				Protocols.Setup(x => x.GetCitedVersionsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(() => new List<ProtocolVersion> { Version });
				Rows.Setup(x => x.GetForDepartmentCallAsync(Dept, 40, 300)).ReturnsAsync(() => new CallProtocol
				{
					CallProtocolId = Row.CallProtocolId, CallId = Row.CallId, DispatchProtocolId = Row.DispatchProtocolId, ProtocolVersionId = Row.ProtocolVersionId,
					StepLogJson = Row.StepLogJson, StepLogRevision = Row.StepLogRevision, RemovedOn = Row.RemovedOn
				});
				Rows.Setup(x => x.TryUpdateStepLogAsync(300, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int id, string log, int expected, CancellationToken _) =>
					{
						if (Conflicts > 0)
						{
							// Someone else wrote in between: the stored revision moved on.
							Conflicts--;
							Row.StepLogRevision++;
							return false;
						}
						if (expected != Row.StepLogRevision)
							return false;
						Row.StepLogJson = log;
						Row.StepLogRevision = expected + 1;
						return true;
					});
				Service = new ProtocolStepThroughService(Protocols.Object, Calls.Object, Rows.Object, Access.Object, NoteRows.Object,
					new Lazy<IProtectedReadService>(() => read ?? Mock.Of<IProtectedReadService>()), new Lazy<IProtectedWriteService>(() => write ?? Mock.Of<IProtectedWriteService>()),
					grant ?? Mock.Of<IProtectedGrantContext>());
			}
		}

		[Test]
		public async Task A_step_is_saved_to_the_call_protocol_log_with_the_server_time_and_who()
		{
			var world = new StepWorld();
			var before = DateTime.UtcNow;

			var result = await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, " ok ", null, "u-7");

			result.Succeeded.Should().BeTrue();
			var log = ProtocolStepLog.Parse(world.Row.StepLogJson);
			log.ProtocolVersionId.Should().Be(5);
			log.Entries.Should().ContainSingle();
			log.Entries[0].StepId.Should().Be("s1-a");
			log.Entries[0].UserId.Should().Be("u-7");
			log.Entries[0].Note.Should().BeNull("the note lives in its own row (M0403); the log keeps the pointer");
			world.Notes.Single().Note.Should().Be("ok");
			log.Entries[0].NoteId.Should().Be(world.Notes.Single().CallProtocolStepNoteId);
			result.Checklist.Steps.Single(s => s.Step.Id == "s1-a").Last.Note.Should().Be("ok", "a read fills the note from its row");
			log.Entries[0].At.Should().BeOnOrAfter(before);
			result.Checklist.Steps.Single(s => s.Step.Id == "s1-a").State.Should().Be(ProtocolStepStates.Done);
			result.Checklist.DoneCount.Should().Be(1);
		}

		[Test]
		public async Task Adp_a_step_note_is_sealed_in_its_own_row_the_log_stays_operational_and_it_reads_redacted_without_a_grant()
		{
			// Round 8 (catalog 39, M0403): a step note is call narrative. The log keeps step, state, time and who; the note is sealed in its own
			// row before it is written, an offline replay finds that row, and a reader sees it only with a grant.
			using var kit = new Resgrid.Tests.Adp.AdpTestKit(departmentId: Dept);
			var world = new StepWorld(read: kit.Seam, write: kit.Seam, grant: kit.Attended(false));

			var result = await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, "BP 180/110 SYNTHETIC-CANARY", null, "u-7", CancellationToken.None, "dev-1:9");

			result.Succeeded.Should().BeTrue("a protected department steps through exactly as any other");
			world.Row.StepLogJson.Should().NotContain("CANARY").And.NotContain("rgdp:", "the log is operational and appendable without a key");
			var log = ProtocolStepLog.Parse(world.Row.StepLogJson);
			log.Entries.Single().Should().Match<ProtocolStepLogEntry>(e => e.StepId == "s1-a" && e.UserId == "u-7" && e.NoteId != null && e.Note == null);
			var row = world.Notes.Single();
			ProtectedDataEnvelope.HasEnvelopePrefix(row.Note).Should().BeTrue("the note is sealed before it is stored");
			row.IsProtected.Should().BeTrue();
			result.Checklist.Steps.Single(s => s.Step.Id == "s1-a").Last.Note.Should().Be(ProtectedDataEnvelope.RedactionValue, "no grant on this read");
			kit.Decrypted.Should().BeEmpty();

			(await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, "BP 180/110 SYNTHETIC-CANARY", null, "u-7", CancellationToken.None, "dev-1:9"))
				.Succeeded.Should().BeTrue();
			world.Notes.Should().ContainSingle("an offline replay finds its row instead of writing a second one");

			var granted = new StepWorld(read: kit.Seam, write: kit.Seam, grant: kit.Attended(true));
			granted.Row.StepLogJson = world.Row.StepLogJson;
			granted.Notes.AddRange(world.Notes);
			(await granted.Service.GetCallChecklistAsync(Dept, 40, 300)).Steps.Single(s => s.Step.Id == "s1-a").Last.Note.Should().Be("BP 180/110 SYNTHETIC-CANARY",
				"a grant holder (the reveal flow, or the v4 grant header) reads it");
		}

		[Test]
		public async Task An_offline_replay_with_the_same_client_step_id_is_recorded_once()
		{
			// Round 5F: an offline queue may send the same event again (a lost response, a retry); the device's own id makes it idempotent.
			var world = new StepWorld();
			var device = DateTime.UtcNow.AddMinutes(-10);

			(await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, "first", device, "u-1", CancellationToken.None, " dev-1:42 ")).Succeeded.Should().BeTrue();
			var replay = await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, "first", device, "u-1", CancellationToken.None, "dev-1:42");
			replay.Succeeded.Should().BeTrue("a replay is not an error");
			replay.Checklist.Steps.Single(s => s.Step.Id == "s1-a").State.Should().Be(ProtocolStepStates.Done);

			// A different event (another id, or none from the web) is recorded as usual.
			await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Pending, null, device, "u-1", CancellationToken.None, "dev-1:43");
			await world.Service.RecordStepAsync(Dept, 40, 300, "s1-b", ProtocolStepStates.Done, null, null, "u-2");

			var log = ProtocolStepLog.Parse(world.Row.StepLogJson);
			log.Entries.Select(e => e.ClientStepId).Should().Equal("dev-1:42", "dev-1:43", null);
			log.Entries.Should().HaveCount(3);

			// Even once the log is finished, a replay of an event it already holds is still a success, not "completed".
			await world.Service.CompleteAsync(Dept, 40, 300, "u-1");
			(await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, "first", device, "u-1", CancellationToken.None, "dev-1:42")).Succeeded.Should().BeTrue();
			(await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, null, device, "u-1", CancellationToken.None, "dev-1:99")).Error
				.Should().Be(ProtocolStepErrors.Completed);
		}

		[Test]
		public async Task A_concurrent_write_is_retried_on_the_fresh_log_so_no_step_is_lost()
		{
			var world = new StepWorld { Conflicts = 1 };
			await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, null, null, "u-1");
			world.Conflicts = 2;

			var result = await world.Service.RecordStepAsync(Dept, 40, 300, "s1-b", ProtocolStepStates.Skipped, null, null, "u-2");

			result.Succeeded.Should().BeTrue();
			ProtocolStepLog.Parse(world.Row.StepLogJson).Entries.Select(e => e.StepId).Should().Equal("s1-a", "s1-b");
		}

		[Test]
		public async Task Unknown_steps_finished_logs_and_protocols_without_steps_are_refused()
		{
			var world = new StepWorld();
			(await world.Service.RecordStepAsync(Dept, 40, 300, "nope", ProtocolStepStates.Done, null, null, "u")).Error.Should().Be(ProtocolStepErrors.UnknownStep);

			(await world.Service.CompleteAsync(Dept, 40, 300, "u")).Succeeded.Should().BeTrue();
			(await world.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, null, null, "u")).Error.Should().Be(ProtocolStepErrors.Completed);

			var sog = new StepWorld(ProtocolKinds.Sog);
			(await sog.Service.RecordStepAsync(Dept, 40, 300, "s1-a", ProtocolStepStates.Done, null, null, "u")).Error.Should().Be(ProtocolStepErrors.NoSteps);
			(await world.Service.RecordStepAsync(Dept + 1, 40, 300, "s1-a", ProtocolStepStates.Done, null, null, "u")).Error.Should().Be(ProtocolStepErrors.NotFound);
		}

		[Test]
		public async Task Starting_an_sop_attaches_it_as_a_manual_pick_and_opens_the_log_on_the_cited_version()
		{
			var world = new StepWorld();
			world.Rows.Setup(x => x.GetCallProtocolsByCallIdAsync(40)).ReturnsAsync(new List<CallProtocol>());
			world.Access.Setup(x => x.GetReadableAsync(Dept, "u-1", 9)).ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 9, DepartmentId = Dept });
			world.Protocols.Setup(x => x.PrepareCallProtocolsAsync(Dept, It.IsAny<Call>(), It.IsAny<IEnumerable<ProtocolAttachRequest>>(), "u-1", It.IsAny<IEnumerable<CallProtocol>>(), true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtocolAttachResult { Attached = new List<CallProtocol> { new CallProtocol { CallId = 40, DispatchProtocolId = 9, Source = (int)CallProtocolSources.Manual, ProtocolVersionId = 5 } } });
			world.Rows.Setup(x => x.InsertAsync(It.IsAny<CallProtocol>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((CallProtocol r, CancellationToken _, bool __) => { r.CallProtocolId = 300; return r; });

			var result = await world.Service.StartAsync(Dept, 40, 9, "u-1");

			result.Succeeded.Should().BeTrue();
			world.Rows.Verify(x => x.InsertAsync(It.Is<CallProtocol>(r => r.Source == (int)CallProtocolSources.Manual), It.IsAny<CancellationToken>(), true), Times.Once);
			var log = ProtocolStepLog.Parse(world.Row.StepLogJson);
			log.StartedByUserId.Should().Be("u-1");
			log.ProtocolVersionId.Should().Be(5);
		}

		#endregion Step-through

		#region Offline manifest and staleness

		private sealed class OfflineWorld
		{
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<IProtocolAccessService> Access = new Mock<IProtocolAccessService>();
			public readonly Mock<IProtocolVersionsRepository> Versions = new Mock<IProtocolVersionsRepository>();
			public readonly Mock<IDispatchProtocolAttachmentRepository> Attachments = new Mock<IDispatchProtocolAttachmentRepository>();
			public readonly Mock<IProtocolVersionFilesRepository> Files = new Mock<IProtocolVersionFilesRepository>();
			public readonly Mock<IProtocolVersionTranslationsRepository> Translations = new Mock<IProtocolVersionTranslationsRepository>();
			public readonly ProtocolOfflineService Service;
			public readonly DispatchProtocol Protocol = new DispatchProtocol { DispatchProtocolId = 9, DepartmentId = Dept, Code = "RIT", Name = "RIT", Kind = (int)ProtocolKinds.Procedure, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 6 };
			public readonly List<ProtocolVersion> All;

			public OfflineWorld()
			{
				var effective = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
				All = new List<ProtocolVersion>
				{
					new ProtocolVersion { ProtocolVersionId = 5, DepartmentId = Dept, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Superseded, Major = 3, ContentHash = new string('a', 64) },
					new ProtocolVersion { ProtocolVersionId = 6, DepartmentId = Dept, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Published, Major = 4, EffectiveOn = effective, ContentHash = new string('b', 64), ContentJson = BlockJson(), ContentHtml = "x" },
					new ProtocolVersion { ProtocolVersionId = 7, DepartmentId = Dept, DispatchProtocolId = 9, Status = (int)ProtocolVersionStatuses.Approved, Major = 5, EffectiveOn = effective.AddDays(30), ContentHash = new string('c', 64) }
				};
				Protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(Dept, true)).ReturnsAsync(new List<DispatchProtocol> { Protocol });
				Access.Setup(x => x.GetReaderAsync(Dept, "u")).ReturnsAsync(new ProtocolReader { DepartmentId = Dept, UserId = "u", IsCurrentMember = true });
				Access.Setup(x => x.FilterReadableAsync(It.IsAny<ProtocolReader>(), It.IsAny<IEnumerable<DispatchProtocol>>())).ReturnsAsync((ProtocolReader _, IEnumerable<DispatchProtocol> p) => p.ToList());
				Access.Setup(x => x.GetReadableAsync(Dept, "u", 9)).ReturnsAsync(Protocol);
				Versions.Setup(x => x.GetHeadersForDepartmentAsync(Dept)).ReturnsAsync(All);
				Versions.Setup(x => x.GetForProtocolAsync(Dept, 9, false)).ReturnsAsync(All);
				Versions.Setup(x => x.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(All.Where(v => v.ProtocolVersionId == 6).ToList());
				Files.Setup(x => x.GetMetadataForVersionsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<ProtocolVersionFile>());
				Attachments.Setup(x => x.GetAttachmentHashesByDepartmentIdAsync(Dept)).ReturnsAsync(new List<DispatchProtocolAttachment>
				{
					new DispatchProtocolAttachment { DispatchProtocolAttachmentId = 1, DispatchProtocolId = 9, FileName = "rit.pdf", FileType = "application/pdf", Size = 10, Sha256 = new string('d', 64) }
				});
				Translations.Setup(x => x.GetApprovedHeadersForVersionsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<ProtocolVersionTranslation>
				{
					new ProtocolVersionTranslation { ProtocolVersionTranslationId = 70, DepartmentId = Dept, ProtocolVersionId = 6, Locale = "de", Status = 1, ContentHash = new string('e', 64), SourceContentHash = new string('b', 64) },
					new ProtocolVersionTranslation { ProtocolVersionTranslationId = 71, DepartmentId = Dept, ProtocolVersionId = 6, Locale = "fr", Status = 1, ContentHash = new string('f', 64), SourceContentHash = new string('0', 64) }
				});
				Service = new ProtocolOfflineService(Protocols.Object, Access.Object, Versions.Object, Attachments.Object, Files.Object, Translations.Object);
			}
		}

		[Test]
		public async Task The_offline_manifest_lists_the_version_hash_file_hashes_current_translations_and_what_is_scheduled()
		{
			var world = new OfflineWorld();

			var manifest = await world.Service.GetManifestAsync(Dept, "u");

			var item = manifest.Items.Single();
			item.VersionId.Should().Be(6);
			item.ContentHash.Should().Be(new string('b', 64));
			item.HasSteps.Should().BeTrue();
			item.Files.Single().Sha256.Should().Be(new string('d', 64));
			item.Translations.Select(t => t.Locale).Should().Equal("de"); // the fr one was made from older content
			item.Scheduled.VersionId.Should().Be(7);
		}

		[Test]
		public async Task Staleness_answers_v3_viewed_v4_in_force_since_and_current_when_up_to_date()
		{
			var world = new OfflineWorld();

			var stale = await world.Service.GetStalenessAsync(Dept, "u", 9, 5);
			stale.IsStale.Should().BeTrue();
			stale.ViewedLabel.Should().Be("3.0");
			stale.CurrentLabel.Should().Be("4.0");
			stale.CurrentEffectiveOn.Should().Be(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));

			(await world.Service.GetStalenessAsync(Dept, "u", 9, 6)).IsStale.Should().BeFalse();
			(await world.Service.GetStalenessAsync(Dept, "u", 9, null, new string('a', 64))).IsStale.Should().BeTrue("the hash the device holds is not the one in force");

			world.Protocol.Status = (int)ProtocolStatuses.Retired;
			(await world.Service.GetStalenessAsync(Dept, "u", 9, 6)).IsRetired.Should().BeTrue();
			(await world.Service.GetStalenessAsync(Dept, "other", 9, 6)).Should().BeNull("a reader who cannot read the protocol gets nothing");
		}

		#endregion Offline manifest and staleness

		#region PDF book

		private sealed class BookWorld
		{
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<IProtocolAccessService> Access = new Mock<IProtocolAccessService>();
			public readonly Mock<IProtocolVersionsRepository> Versions = new Mock<IProtocolVersionsRepository>();
			public readonly Mock<IProtocolVersionApprovalsRepository> Approvals = new Mock<IProtocolVersionApprovalsRepository>();
			public readonly Mock<IProtocolCategoriesRepository> Categories = new Mock<IProtocolCategoriesRepository>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public readonly Mock<IDocumentSealingService> Sealing = new Mock<IDocumentSealingService>();
			public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
			public readonly ProtocolBookService Service;
			public SealHtmlRequest Request;

			public BookWorld()
			{
				var effective = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
				Protocols.Setup(x => x.GetProtocolSummariesForDepartmentAsync(Dept, true)).ReturnsAsync(new List<DispatchProtocol>
				{
					new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = Dept, Code = "B", Name = "Burns", Number = "200", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 21, CategoryId = 1 },
					new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = Dept, Code = "A", Name = "Airway", Number = "100", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 11, CategoryId = 1 },
					new DispatchProtocol { DispatchProtocolId = 3, DepartmentId = Dept, Code = "D", Name = "Draft only", Status = (int)ProtocolStatuses.Draft },
					new DispatchProtocol { DispatchProtocolId = 4, DepartmentId = Dept, Code = "H", Name = "Hidden", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 41, CategoryId = 1 }
				});
				Access.Setup(x => x.GetReaderAsync(Dept, "u")).ReturnsAsync(new ProtocolReader { DepartmentId = Dept, UserId = "u", IsCurrentMember = true });
				Access.Setup(x => x.FilterReadableAsync(It.IsAny<ProtocolReader>(), It.IsAny<IEnumerable<DispatchProtocol>>()))
					.ReturnsAsync((ProtocolReader _, IEnumerable<DispatchProtocol> list) => list.Where(p => p.DispatchProtocolId != 4).ToList());
				Versions.Setup(x => x.GetByIdsAsync(Dept, It.IsAny<IEnumerable<int>>(), true)).ReturnsAsync(new List<ProtocolVersion>
				{
					new ProtocolVersion { ProtocolVersionId = 11, DepartmentId = Dept, DispatchProtocolId = 1, Status = (int)ProtocolVersionStatuses.Published, Major = 2, Minor = 1, EffectiveOn = effective, ContentHtml = "<p>Open the airway</p>", ContentHash = new string('1', 64) },
					new ProtocolVersion { ProtocolVersionId = 21, DepartmentId = Dept, DispatchProtocolId = 2, Status = (int)ProtocolVersionStatuses.Published, Major = 1, EffectiveOn = effective, ContentHtml = "<p>Cool the burn<script>x()</script></p>", ContentHash = new string('2', 64) }
				});
				Approvals.Setup(x => x.GetForVersionsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<ProtocolVersionApproval>
				{
					new ProtocolVersionApproval { ProtocolVersionId = 11, Round = 1, Decision = (int)ProtocolApprovalDecisions.Approved, DecidedByUserId = "md-1", DecidedOn = effective }
				});
				Categories.Setup(x => x.GetForDepartmentAsync(Dept)).ReturnsAsync(new List<ProtocolCategory> { new ProtocolCategory { ProtocolCategoryId = 1, DepartmentId = Dept, Name = "Medical" } });
				Departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "md-1", FirstName = "Dr Medical", LastName = "Director" } });
				Departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station Nine" });
				// Sealing is not configured: the service records it pending and hands back the unsealed rendering (fails closed).
				Sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((SealHtmlRequest r, CancellationToken _) =>
					{
						Request = r;
						return new SealOutcome { Bytes = new byte[] { 37, 80, 68, 70 }, Document = new SealedDocument { Status = (int)SealedDocumentStatuses.Pending, FileName = r.FileName }, Reason = "sealing_sidecar_not_configured" };
					});
				Service = new ProtocolBookService(Protocols.Object, Access.Object, Versions.Object, Approvals.Object, Categories.Object, Departments.Object, Sealing.Object, Events.Object,
					Mock.Of<IDepartmentSettingsRepository>());
			}
		}

		[Test]
		public async Task The_book_has_a_table_of_contents_and_stamps_each_readable_protocol_in_force()
		{
			var world = new BookWorld();

			var book = await world.Service.BuildAsync(Dept, "u", null, new ProtocolBookText());

			book.Entries.Select(e => e.Protocol.DispatchProtocolId).Should().Equal(1, 2);
			book.Html.Should().Contain("Contents");
			book.Html.Should().Contain("href=\"#protocol-1\"").And.Contain("id=\"protocol-1\"");
			book.Html.Should().Contain("v2.1").And.Contain("2026-10-01").And.Contain("Dr Medical Director");
			book.Html.Should().NotContain("<script").And.NotContain("Hidden").And.NotContain("Draft only");
			book.BookHash.Should().MatchRegex("^[0-9a-f]{64}$");
			(await world.Service.BuildAsync(Dept, "u", null, new ProtocolBookText())).BookHash.Should().Be(book.BookHash, "the same versions give the same book hash");
		}

		[Test]
		public async Task Without_sealing_the_book_is_returned_unsealed_and_recorded_pending_bound_to_the_book_hash()
		{
			var world = new BookWorld();

			var export = await world.Service.ExportPdfAsync(Dept, "u", 1, new ProtocolBookText(), "en", "Letter");

			export.Pdf.Should().NotBeEmpty();
			export.Sealed.Should().BeFalse();
			export.SealingPending.Should().BeTrue();
			world.Request.SubjectKind.Should().Be(SignatureSubjectKinds.ProtocolBook);
			world.Request.SubjectId.Should().Be("category-1");
			world.Request.RevisionSha256.Should().Be(export.Book.BookHash);
			world.Events.Verify(x => x.SendMessage(It.Is<AuditEvent>(e => e.Type == AuditLogTypes.ProtocolBookExported)), Times.Once);
		}

		#endregion PDF book

		#region Translations

		private sealed class TranslationWorld
		{
			public readonly ProtocolLibraryWorld Library = new ProtocolLibraryWorld();
			public readonly Mock<IProtocolVersionTranslationsRepository> Repository = new Mock<IProtocolVersionTranslationsRepository>();
			public readonly Mock<IProtocolAccessService> Access = new Mock<IProtocolAccessService>();
			public readonly Mock<ISignatureService> Signatures = new Mock<ISignatureService>();
			public readonly Dictionary<int, ProtocolVersionTranslation> Rows = new Dictionary<int, ProtocolVersionTranslation>();
			public readonly Mock<IProtocolsService> Protocols = new Mock<IProtocolsService>();
			public readonly Mock<ISearchProjectionService> Search = new Mock<ISearchProjectionService>();
			public readonly Mock<IKnowledgeIndexQueue> Knowledge = new Mock<IKnowledgeIndexQueue>();
			public readonly ProtocolTranslationService Service;
			public SignatureRequest SignatureRequest;
			public ProtocolVersion Version;

			public TranslationWorld()
			{
				Library.AddProtocol(9, ProtocolStatuses.Published, 6);
				var json = BlockJson();
				Version = Library.AddVersion(6, 9, ProtocolVersionStatuses.Published, "x", 1, 0);
				Version.ContentJson = json;
				Version.ContentHtml = ProtocolContentDocument.Parse(json, null).Render();
				Version.ContentHash = ProtocolContentHash.Compute(Version.ContentHtml);
				Access.Setup(x => x.GetReadableAsync(Dept, It.IsAny<string>(), 9)).ReturnsAsync(() => Library.Protocols[9]);
				Protocols.Setup(x => x.GetProtocolHeaderByIdAsync(9)).ReturnsAsync(() => Library.Protocols[9]);
				Repository.Setup(x => x.GetForVersionAsync(Dept, 6, It.IsAny<bool>())).ReturnsAsync(() => Rows.Values.ToList());
				Repository.Setup(x => x.GetByIdForDepartmentAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Rows.TryGetValue(id, out var t) ? Clone(t) : null);
				Repository.Setup(x => x.InsertAsync(It.IsAny<ProtocolVersionTranslation>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((ProtocolVersionTranslation t, CancellationToken _, bool __) => { t.ProtocolVersionTranslationId = 70 + Rows.Count; Rows[t.ProtocolVersionTranslationId] = Clone(t); return t; });
				Repository.Setup(x => x.TryUpdateAsync(It.IsAny<ProtocolVersionTranslation>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ProtocolVersionTranslation t, long expected, CancellationToken _) =>
					{
						if (Rows[t.ProtocolVersionTranslationId].RowVersion != expected) return false;
						t.RowVersion = expected + 1;
						Rows[t.ProtocolVersionTranslationId] = Clone(t);
						return true;
					});
				Signatures.Setup(x => x.SignAsync(It.IsAny<SignatureRequest>(), It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((SignatureRequest r, SignatureProof _, CancellationToken __) =>
					{
						SignatureRequest = r;
						return new SignatureResult { Outcome = SignatureOutcomes.Signed, Record = new SignatureRecord { SignatureRecordId = 900, ArtifactSha256 = r.ArtifactSha256 } };
					});
				Service = new ProtocolTranslationService(Repository.Object, Library.Service, Access.Object, Signatures.Object, Library.Events.Object, Protocols.Object,
					new Lazy<ISearchProjectionService>(() => Search.Object), Knowledge.Object);
			}

			private static ProtocolVersionTranslation Clone(ProtocolVersionTranslation t) => Newtonsoft.Json.JsonConvert.DeserializeObject<ProtocolVersionTranslation>(Newtonsoft.Json.JsonConvert.SerializeObject(t));
		}

		[Test]
		public async Task A_translation_has_its_own_hash_and_approval_and_never_changes_the_source_hash()
		{
			var world = new TranslationWorld();
			var sourceHash = world.Version.ContentHash;
			var reader = ProtocolLibraryWorld.Manager();

			var started = await world.Service.StartAsync(reader, 6, "de");
			started.Succeeded.Should().BeTrue();
			var translated = ProtocolContentDocument.Parse(started.Translation.ContentJson, null);
			translated.Blocks[0].Text = "Einstieg";
			translated.Blocks[2].Medication.DosePerKg = "5 mg/kg";
			var saved = await world.Service.SaveAsync(reader, started.Translation.ProtocolVersionTranslationId, translated.ToJson(), null, started.Translation.RowVersion);
			saved.Succeeded.Should().BeTrue();
			saved.Translation.ContentHtml.Should().Contain("Einstieg").And.Contain("0.02 mg/kg").And.NotContain("5 mg/kg");
			saved.Translation.ContentHtml.Should().Contain("Medikament", "the block labels are written in the translation's language");

			var translationEvents = new List<ProtocolWorkflowEvent>();
			world.Library.Events.Setup(x => x.SendMessage(It.IsAny<ProtocolWorkflowEvent>())).Callback((ProtocolWorkflowEvent e) => translationEvents.Add(e));
			var approved = await world.Service.ApproveAsync(reader, saved.Translation.ProtocolVersionTranslationId, ProtocolLibraryWorld.StepUp());

			approved.Succeeded.Should().BeTrue();
			// Round 7C, workflow trigger 249: once, with the locale and ids; a second approval of the approved translation is refused.
			(await world.Service.ApproveAsync(reader, saved.Translation.ProtocolVersionTranslationId, ProtocolLibraryWorld.StepUp())).Succeeded.Should().BeFalse();
			translationEvents.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ProtocolTranslationApproved && e.Locale == "de" && e.ProtocolId == 9 &&
				e.TranslationId == saved.Translation.ProtocolVersionTranslationId && e.ProtocolVersionId == 6);
			world.SignatureRequest.Use.Should().Be(SignatureUse.ProtocolTranslationApproval);
			world.SignatureRequest.ArtifactSha256.Should().Be(ProtocolContentHash.Compute(saved.Translation.ContentHtml)).And.NotBe(sourceHash);
			world.Library.Versions[6].ContentHash.Should().Be(sourceHash);
			world.Library.Versions[6].ContentHtml.Should().NotContain("Einstieg");

			// Round 5F: the approved translation reaches Unified Search (the protocol's row) and the K1 index (a locale variant).
			world.Search.Verify(x => x.ProjectProtocolAsync(It.Is<DispatchProtocol>(p => p.DispatchProtocolId == 9), It.IsAny<CancellationToken>()), Times.Once);
			world.Knowledge.Verify(x => x.EnqueueAsync(Dept, KnowledgeSourceKinds.Protocol, "9", It.IsAny<CancellationToken>()), Times.Once);
			await world.Service.WithdrawAsync(reader, saved.Translation.ProtocolVersionTranslationId);
			world.Search.Verify(x => x.ProjectProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()), Times.Exactly(2), "a withdrawn translation leaves search again");
		}

		[Test]
		public async Task Readers_get_the_approved_translation_in_their_language_and_otherwise_fall_back_to_the_source()
		{
			var world = new TranslationWorld();
			var reader = ProtocolLibraryWorld.Manager();
			var started = await world.Service.StartAsync(reader, 6, "de");
			var version = world.Library.Versions[6];

			var draftOnly = await world.Service.GetLocalizedAsync(Dept, version, "de");
			draftOnly.Translation.Should().BeNull("a draft translation is never shown");
			draftOnly.IsFallback.Should().BeTrue();
			draftOnly.ContentHtml.Should().Be(version.ContentHtml);

			await world.Service.ApproveAsync(reader, started.Translation.ProtocolVersionTranslationId, ProtocolLibraryWorld.StepUp());
			var german = await world.Service.GetLocalizedAsync(Dept, version, "de-AT");
			german.Translation.Should().NotBeNull();
			german.Locale.Should().Be("de");
			german.AvailableLocales.Should().Equal("en", "de");

			(await world.Service.GetLocalizedAsync(Dept, version, "en")).Translation.Should().BeNull("the source language is the source");
			(await world.Service.GetLocalizedAsync(Dept, version, "pl")).IsFallback.Should().BeTrue();

			world.Rows[started.Translation.ProtocolVersionTranslationId].SourceContentHash = new string('0', 64);
			(await world.Service.GetLocalizedAsync(Dept, version, "de")).Translation.Should().BeNull("a translation made from other content is not shown");
		}

		[Test]
		public async Task Only_locked_versions_are_translated_and_the_source_language_is_refused()
		{
			var world = new TranslationWorld();
			var reader = ProtocolLibraryWorld.Manager();

			(await world.Service.StartAsync(reader, 6, "en")).Error.Should().Be(ProtocolTranslationErrors.InvalidLocale);
			world.Library.Versions[6].Status = (int)ProtocolVersionStatuses.Draft;
			(await world.Service.StartAsync(reader, 6, "de")).Error.Should().Be(ProtocolTranslationErrors.SourceNotLocked);
			(await world.Service.StartAsync(new ProtocolReader { DepartmentId = Dept, UserId = "m", IsCurrentMember = true }, 6, "de")).Error
				.Should().Be(ProtocolTranslationErrors.NotAuthorized);
		}

		#endregion Translations
	}
}
