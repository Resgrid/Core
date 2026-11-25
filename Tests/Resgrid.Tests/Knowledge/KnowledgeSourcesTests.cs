using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// K1 corpus (§16.1): what is indexed and what never is. Drafts, in-review, retired, not-yet-effective and expired versions, items marked
	/// "no AI" or licensed, quiz questions and answer keys, learner data, and Documents without the opt-in or under Advanced Data Protection.
	/// </summary>
	[TestFixture]
	public class KnowledgeSourcesTests
	{
		private const int DepartmentId = 7;
		private const string Member = "member-1";
		private const string Html = "<h2>Scene safety</h2><p>Stage uphill.</p><h2>Airway</h2><p>Head tilt.</p>";

		#region Protocols

		private Mock<IKnowledgeIndexRepository> _index;
		private Mock<IProtocolVersionsRepository> _versions;
		private Mock<IProtocolAudiencesRepository> _audiences;
		private Mock<IKnowledgePageTextSource> _pages;
		private List<DispatchProtocol> _protocols;
		private List<ProtocolVersion> _protocolVersions;
		private Mock<IProtocolVersionTranslationsRepository> _translations;
		private List<ProtocolVersionTranslation> _protocolTranslations = new List<ProtocolVersionTranslation>();

		private ProtocolKnowledgeSource Protocols(IProtocolAccessService access = null)
		{
			_index = new Mock<IKnowledgeIndexRepository>();
			_index.Setup(x => x.GetProtocolHeadersAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _protocols);
			_versions = new Mock<IProtocolVersionsRepository>();
			_versions.Setup(x => x.GetByIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), true))
				.ReturnsAsync((int _, IEnumerable<int> ids, bool __) => _protocolVersions.Where(v => ids.Contains(v.ProtocolVersionId)).ToList());
			_audiences = new Mock<IProtocolAudiencesRepository>();
			_audiences.Setup(x => x.GetForProtocolAsync(DepartmentId, It.IsAny<int>())).ReturnsAsync(new List<ProtocolAudience>());
			_pages = new Mock<IKnowledgePageTextSource>();
			_pages.Setup(x => x.GetPagesAsync(It.IsAny<int>(), It.IsAny<KnowledgeSourceKinds>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Array.Empty<KnowledgePageText>());
			_translations = new Mock<IProtocolVersionTranslationsRepository>();
			_translations.Setup(x => x.GetForVersionAsync(DepartmentId, It.IsAny<int>(), true)).ReturnsAsync((int _, int versionId, bool __) =>
				_protocolTranslations.Where(t => t.ProtocolVersionId == versionId).ToList());
			return new ProtocolKnowledgeSource(_index.Object, _versions.Object, _audiences.Object, access ?? Mock.Of<IProtocolAccessService>(), _pages.Object, _translations.Object);
		}

		private void Protocol(int id, ProtocolStatuses status, ProtocolVersionStatuses versionStatus, Action<DispatchProtocol> protocol = null, Action<ProtocolVersion> version = null,
			ProtocolKinds kind = ProtocolKinds.Sog)
		{
			var p = new DispatchProtocol
			{
				DispatchProtocolId = id, DepartmentId = DepartmentId, Name = "Protocol " + id, Code = "P" + id, Kind = (int)kind, Status = (int)status,
				CurrentVersionId = status == ProtocolStatuses.Draft ? (int?)null : id * 10
			};
			protocol?.Invoke(p);
			_protocols.Add(p);
			var v = new ProtocolVersion
			{
				ProtocolVersionId = id * 10, DepartmentId = DepartmentId, DispatchProtocolId = id, Status = (int)versionStatus, Major = 2, Minor = 1,
				EffectiveOn = DateTime.UtcNow.AddDays(-3), ContentJson = ProtocolContentDocument.FromHtml(Html).ToJson(), ContentHtml = Html
			};
			version?.Invoke(v);
			_protocolVersions.Add(v);
		}

		[SetUp]
		public void SetUp()
		{
			_protocols = new List<DispatchProtocol>();
			_protocolVersions = new List<ProtocolVersion>();
			_protocolTranslations = new List<ProtocolVersionTranslation>();
		}

		private static ProtocolVersionTranslation Translation(int versionId, string locale, string html, string sourceHash, ProtocolTranslationStatuses status = ProtocolTranslationStatuses.Approved) =>
			new ProtocolVersionTranslation
			{
				ProtocolVersionTranslationId = versionId * 100 + locale.Length, DepartmentId = DepartmentId, DispatchProtocolId = versionId / 10, ProtocolVersionId = versionId,
				Locale = locale, Status = (int)status, ContentJson = ProtocolContentDocument.FromHtml(html).ToJson(), ContentHtml = html,
				ContentHash = ProtocolContentHash.Compute(html), SourceContentHash = sourceHash, ApprovedOn = DateTime.UtcNow
			};

		[Test]
		public async Task Approved_translations_are_indexed_as_locale_variants_with_the_sources_audience()
		{
			// Round 5F: an approved translation of the version in force becomes locale-variant chunks of the same item, version and audience.
			var source = Protocols();
			Protocol(1, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, version: v => { v.ContentHash = ProtocolContentHash.Compute(Html); v.Locale = "en"; });
			_audiences.Setup(x => x.GetForProtocolAsync(DepartmentId, 1)).ReturnsAsync(new List<ProtocolAudience>
			{
				new ProtocolAudience { DepartmentId = DepartmentId, DispatchProtocolId = 1, Kind = (int)ContentAudienceKinds.Group, RefId = "3" }
			});
			var hash = ProtocolContentHash.Compute(Html);
			_protocolTranslations.Add(Translation(10, "fr", "<h2>Sécurité</h2><p>Stationner en amont.</p>", hash));
			_protocolTranslations.Add(Translation(10, "de", "<h2>Sicherheit</h2><p>Bergauf parken.</p>", hash, ProtocolTranslationStatuses.Draft));
			_protocolTranslations.Add(Translation(10, "es", "<h2>Seguridad</h2><p>Estacionar cuesta arriba.</p>", "stale-source-hash"));
			var tampered = Translation(10, "it", "<h2>Sicurezza</h2><p>Parcheggiare a monte.</p>", hash);
			tampered.ContentHtml = "<p>Changed after approval</p>";
			_protocolTranslations.Add(tampered);

			var content = await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");

			var variants = content.Chunks.Where(KnowledgeLocaleVariants.IsVariant).ToList();
			variants.Should().NotBeEmpty().And.OnlyContain(c => c.Locale == "fr", "only the approved, current, untampered translation");
			variants.Should().OnlyContain(c => c.ItemId == "1" && c.VersionId == "10" && c.VersionLabel == "2.1");
			variants.Select(c => c.Text).Should().Contain(t => t.Contains("Stationner en amont"));
			variants.Should().OnlyContain(c => c.AudienceJson == content.Chunks.First(x => !KnowledgeLocaleVariants.IsVariant(x)).AudienceJson, "a variant is read exactly like its source");
			content.Chunks.Where(c => !KnowledgeLocaleVariants.IsVariant(c)).Should().OnlyContain(c => c.Locale == "en");
		}

		[Test]
		public async Task A_published_version_in_force_is_indexed_by_section_with_its_version_dates_and_audience()
		{
			var source = Protocols();
			Protocol(1, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, kind: ProtocolKinds.Dispatch);
			_audiences.Setup(x => x.GetForProtocolAsync(DepartmentId, 1)).ReturnsAsync(new List<ProtocolAudience>
			{
				new ProtocolAudience { DepartmentId = DepartmentId, DispatchProtocolId = 1, Kind = (int)ContentAudienceKinds.Group, RefId = "3" }
			});

			var content = await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "1");

			content.Chunks.Select(c => c.Anchor).Should().Equal("p-scene-safety", "p-airway");
			content.Chunks.Should().OnlyContain(c => c.VersionId == "10" && c.VersionLabel == "2.1" && c.Title == "P1 · Protocol 1" && c.EffectiveOn.HasValue);
			content.Chunks.Should().OnlyContain(c => c.AccessFlags == (int)KnowledgeAccessFlags.DispatchersRead);
			KnowledgeAccessFilter.ParseAudience(content.Chunks[0].AudienceJson).Should().ContainSingle(r => r.Kind == (int)ContentAudienceKinds.Group && r.RefId == "3");
			(await source.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Protocol)).Should().Equal("1");
		}

		[Test]
		public async Task Drafts_reviews_retired_deleted_no_AI_licensed_and_out_of_effect_versions_are_never_indexed()
		{
			var source = Protocols();
			Protocol(1, ProtocolStatuses.Draft, ProtocolVersionStatuses.Draft);
			Protocol(2, ProtocolStatuses.Published, ProtocolVersionStatuses.InReview);
			Protocol(3, ProtocolStatuses.Retired, ProtocolVersionStatuses.Retired);
			Protocol(4, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, p => p.ExcludeFromAi = true);
			Protocol(5, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, p => p.IsLicensedContent = true);
			Protocol(6, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, p => p.IsDeleted = true);
			Protocol(7, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, version: v => v.EffectiveOn = DateTime.UtcNow.AddDays(2));
			Protocol(8, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, version: v => v.ExpiresOn = DateTime.UtcNow.AddMinutes(-1));
			Protocol(9, ProtocolStatuses.Published, ProtocolVersionStatuses.Published);

			foreach (var id in Enumerable.Range(1, 8))
				(await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Protocol, id.ToString())).Should().BeNull("protocol {0} must not be indexed", id);
			(await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Protocol, "9")).Should().NotBeNull();
			(await source.BuildAsync(DepartmentId + 1, KnowledgeSourceKinds.Protocol, "9")).Should().BeNull("another department's protocol");
		}

		[Test]
		public async Task Protocol_files_are_indexed_by_page_from_the_page_text_store()
		{
			var source = Protocols();
			Protocol(1, ProtocolStatuses.Published, ProtocolVersionStatuses.Published);
			_pages.Setup(x => x.GetPagesAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, "1", "10", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new[] { new KnowledgePageText { AttachmentId = "4", FileName = "SOG.pdf", PageNumber = 1, Text = "Page one text." } });

			var content = await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, "1");

			content.Chunks.Single().Anchor.Should().Be("page-4-1");
			content.Chunks.Single().SourceKind.Should().Be((int)KnowledgeSourceKinds.ProtocolFile);
			(await new NullKnowledgePageTextSource().GetPagesAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, "1", "10")).Should().BeEmpty();
		}

		[Test]
		public async Task Protocol_readers_get_what_the_library_lets_them_read_minus_deleted_protocols()
		{
			var access = new Mock<IProtocolAccessService>();
			access.Setup(x => x.GetReaderAsync(DepartmentId, Member)).ReturnsAsync(new ProtocolReader { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true, IsDispatcher = true });
			access.Setup(x => x.FilterReadableAsync(It.IsAny<ProtocolReader>(), It.IsAny<IEnumerable<DispatchProtocol>>()))
				.ReturnsAsync((ProtocolReader _, IEnumerable<DispatchProtocol> list) => list.Where(p => p.DispatchProtocolId != 2).ToList());
			var source = Protocols(access.Object);
			Protocol(1, ProtocolStatuses.Published, ProtocolVersionStatuses.Published);
			Protocol(2, ProtocolStatuses.Published, ProtocolVersionStatuses.Published);
			Protocol(3, ProtocolStatuses.Published, ProtocolVersionStatuses.Published, p => p.IsDeleted = true);
			var scope = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true };

			await source.AddReadableAsync(new KnowledgeReader { DepartmentId = DepartmentId, UserId = Member, CanViewProtocols = true }, scope);

			scope.ReadableItems[KnowledgeSourceKinds.Protocol].Should().BeEquivalentTo("1");
			scope.ReadableItems[KnowledgeSourceKinds.ProtocolFile].Should().BeEquivalentTo("1");
			scope.IsDispatcher.Should().BeTrue();

			var noClaim = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true };
			await source.AddReadableAsync(new KnowledgeReader { DepartmentId = DepartmentId, UserId = Member, CanViewProtocols = false }, noClaim);
			noClaim.ReadableItems.Should().BeEmpty();
		}

		#endregion Protocols

		#region Trainings

		private static Training Training(int id, Action<Training> configure = null)
		{
			var training = new Training
			{
				TrainingId = id, DepartmentId = DepartmentId, Name = "Training " + id, Description = "<p>Overview of the drill.</p>", Objectives = "Know the knots.",
				TrainingText = "<h2>Knots</h2><p>Tie a bowline.</p>", CreatedOn = DateTime.UtcNow.AddDays(-10), Users = new List<TrainingUser>(),
				Questions = new List<TrainingQuestion>
				{
					new TrainingQuestion { Question = "SECRET-QUESTION which knot?", Answers = new List<TrainingQuestionAnswer> { new TrainingQuestionAnswer { Answer = "SECRET-ANSWER bowline", Correct = true } } }
				}
			};
			configure?.Invoke(training);
			return training;
		}

		private static (TrainingKnowledgeSource Source, Mock<ITrainingService> Trainings) Trainings(List<Training> trainings, bool moduleOn = true, TrainingManagementScope scope = null)
		{
			// Strict: the source may read training rows and the department list, nothing else (no quiz, attempts or learner data).
			var service = new Mock<ITrainingService>(MockBehavior.Strict);
			service.Setup(x => x.GetTrainingRowByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => trainings.FirstOrDefault(t => t.TrainingId == id));
			service.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(trainings);
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, false)).ReturnsAsync(new DepartmentModuleSettings { TrainingDisabled = !moduleOn });
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.GetTrainingManagementScopeAsync(Member, DepartmentId)).ReturnsAsync(scope ?? TrainingManagementScope.None(Member, DepartmentId));
			return (new TrainingKnowledgeSource(service.Object, authorization.Object, settings.Object, new NullKnowledgePageTextSource()), service);
		}

		[Test]
		public async Task A_training_is_indexed_from_its_content_and_never_from_its_quiz_or_answer_key()
		{
			var (source, _) = Trainings(new List<Training> { Training(1) });

			var content = await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Training, "1");

			content.Chunks.Should().HaveCount(2);
			content.Chunks[0].Text.Should().Contain("Overview of the drill").And.Contain("Know the knots");
			content.Chunks[1].Anchor.Should().Be("p-knots");
			string.Join(" ", content.Chunks.Select(KnowledgeChunker.EmbeddingInput)).Should().NotContain("SECRET");
			content.Chunks.Should().OnlyContain(c => c.AccessFlags == (int)KnowledgeAccessFlags.AssigneesRead && c.AudienceJson == null);
		}

		[Test]
		public async Task Archived_not_started_no_AI_and_module_off_trainings_are_never_indexed()
		{
			var trainings = new List<Training>
			{
				Training(1, t => t.ArchivedOn = DateTime.UtcNow.AddDays(-1)),
				Training(2, t => t.StartsOn = DateTime.UtcNow.AddDays(3)),
				Training(3, t => t.ExcludeFromAi = true),
				Training(4)
			};
			var (source, _) = Trainings(trainings);

			foreach (var id in new[] { 1, 2, 3 })
				(await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Training, id.ToString())).Should().BeNull();
			(await source.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Training)).Should().Equal("4");

			var (off, _) = Trainings(trainings, moduleOn: false);
			(await off.BuildAsync(DepartmentId, KnowledgeSourceKinds.Training, "4")).Should().BeNull();
			(await off.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Training)).Should().BeEmpty();
		}

		[Test]
		public async Task A_member_reads_the_trainings_assigned_to_them_and_the_open_ones_only()
		{
			var trainings = new List<Training>
			{
				Training(1, t => t.Users = new List<TrainingUser> { new TrainingUser { UserId = Member } }),
				Training(2, t => t.OpenToAllMembers = true),
				Training(3),
				Training(4, t => { t.Users = new List<TrainingUser> { new TrainingUser { UserId = Member } }; t.ArchivedOn = DateTime.UtcNow; })
			};
			var (source, _) = Trainings(trainings);
			var scope = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true };

			await source.AddReadableAsync(new KnowledgeReader { DepartmentId = DepartmentId, UserId = Member, CanViewTrainings = true }, scope);

			scope.ReadableItems[KnowledgeSourceKinds.Training].Should().BeEquivalentTo("1", "2");
			var open = (await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Training, "2")).Chunks.First();
			KnowledgeAccessFilter.ParseAudience(open.AudienceJson).Single().Kind.Should().Be((int)ContentAudienceKinds.Everyone);
		}

		#endregion Trainings

		#region Documents and Academy

		private static DocumentKnowledgeSource Documents(bool optIn, DepartmentDataProtectionState protection, List<KnowledgeDocumentHeader> documents)
		{
			var index = new Mock<IKnowledgeIndexRepository>();
			index.Setup(x => x.GetDocumentHeadersAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(documents);
			var settingsRepository = new Mock<IDepartmentSettingsRepository>();
			settingsRepository.Setup(x => x.GetDepartmentSettingByIdTypeAsync(DepartmentId, DepartmentSettingTypes.KnowledgeSettings)).ReturnsAsync(new DepartmentSetting
			{
				DepartmentId = DepartmentId,
				Setting = Resgrid.Framework.ObjectSerialization.Serialize(new KnowledgeSettings { IncludeDocuments = optIn })
			});
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, false)).ReturnsAsync(new DepartmentModuleSettings());
			var adp = new Mock<IDepartmentDataProtectionService>();
			adp.Setup(x => x.GetStateAsync(DepartmentId, true)).ReturnsAsync(protection);
			return new DocumentKnowledgeSource(index.Object, settingsRepository.Object, settings.Object, adp.Object, new NullKnowledgePageTextSource());
		}

		private static List<KnowledgeDocumentHeader> DocumentRows() => new List<KnowledgeDocumentHeader>
		{
			new KnowledgeDocumentHeader { DocumentId = 1, DepartmentId = DepartmentId, Name = "Hose testing", Description = "Annual hose test.", AddedOn = DateTime.UtcNow.AddDays(-30) },
			new KnowledgeDocumentHeader { DocumentId = 2, DepartmentId = DepartmentId, Name = "Payroll", Description = "Admins only.", AdminsOnly = true, AddedOn = DateTime.UtcNow.AddDays(-30) },
			new KnowledgeDocumentHeader { DocumentId = 3, DepartmentId = DepartmentId, Name = "Sealed", IsProtected = true, AddedOn = DateTime.UtcNow.AddDays(-30) },
			new KnowledgeDocumentHeader { DocumentId = 4, DepartmentId = DepartmentId, Name = "Old", AddedOn = DateTime.UtcNow.AddDays(-30), RemoveOn = DateTime.UtcNow.AddDays(-1) }
		};

		[Test]
		public async Task Documents_are_used_only_by_opt_in_and_never_under_Advanced_Data_Protection()
		{
			var reader = new KnowledgeReader { DepartmentId = DepartmentId, UserId = Member, CanViewDocuments = true };

			var notOptedIn = Documents(false, DepartmentDataProtectionState.Disabled, DocumentRows());
			(await notOptedIn.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Document)).Should().BeEmpty();
			(await notOptedIn.BuildAsync(DepartmentId, KnowledgeSourceKinds.Document, "1")).Should().BeNull();

			var protectedDepartment = Documents(true, DepartmentDataProtectionState.Enabled, DocumentRows());
			(await protectedDepartment.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Document)).Should().BeEmpty();
			var scope = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true };
			await protectedDepartment.AddReadableAsync(reader, scope);
			scope.ReadableItems.Should().BeEmpty();

			var open = Documents(true, DepartmentDataProtectionState.Disabled, DocumentRows());
			(await open.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Document)).Should().Equal("1", "2");
			(await open.BuildAsync(DepartmentId, KnowledgeSourceKinds.Document, "2")).Chunks.Should().OnlyContain(c => c.AccessFlags == (int)KnowledgeAccessFlags.AdminOnly);

			var member = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true };
			await open.AddReadableAsync(reader, member);
			member.ReadableItems[KnowledgeSourceKinds.Document].Should().BeEquivalentTo("1");
			var admin = new KnowledgeReaderScope { DepartmentId = DepartmentId, UserId = Member, IsCurrentMember = true, IsDepartmentAdmin = true };
			await open.AddReadableAsync(reader, admin);
			admin.ReadableItems[KnowledgeSourceKinds.Document].Should().BeEquivalentTo("1", "2");
		}

		[Test]
		public async Task The_Academy_is_one_system_corpus_with_a_chunk_per_lesson()
		{
			var source = new AcademyKnowledgeSource();

			(await source.ListItemIdsAsync(0, KnowledgeSourceKinds.Academy)).Should().HaveCount(AcademyCatalog.Lessons.Count);
			(await source.ListItemIdsAsync(DepartmentId, KnowledgeSourceKinds.Academy)).Should().BeEmpty();
			var calls = (await source.BuildAsync(0, KnowledgeSourceKinds.Academy, "05-calls")).Chunks.Single();
			calls.DepartmentId.Should().Be(0);
			calls.Anchor.Should().Be("05-calls");
			calls.Text.Should().Contain("Run Cards").And.Contain("Dispatcher Desk");
			(await source.BuildAsync(DepartmentId, KnowledgeSourceKinds.Academy, "05-calls")).Should().BeNull();
		}

		#endregion Documents and Academy

		[Test]
		public void Only_published_content_sources_exist_no_transcripts_certifications_learning_records_or_wellness()
		{
			var sources = typeof(ProtocolKnowledgeSource).Assembly.GetTypes()
				.Where(t => typeof(IKnowledgeCorpusSource).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).Select(t => t.Name).ToList();
			// K3: LMS courses (published lessons only; never answer keys, attempts or learner data, see LmsCourseKnowledgeSourceTests).
			sources.Should().BeEquivalentTo("ProtocolKnowledgeSource", "TrainingKnowledgeSource", "AcademyKnowledgeSource", "DocumentKnowledgeSource", "LmsCourseKnowledgeSource");

			var kinds = Enum.GetNames(typeof(KnowledgeSourceKinds));
			kinds.Should().NotContain(k => k.Contains("Transcript") || k.Contains("Certification") || k.Contains("Learning") || k.Contains("Wellness") ||
				k.Contains("Exposure") || k.Contains("Attempt") || k.Contains("Quiz"));
		}
	}
}
