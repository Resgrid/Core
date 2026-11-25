using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Import.ProtocolFiles;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Document-backed protocols (P1 R-P5, R-P6): a file becomes a draft version whose hash is the file's SHA-256 and whose text is
	/// extracted (inline when small, queued for worker 86 otherwise); a new upload goes only into a draft, never a version that left
	/// draft; the per-version read rule guards the file; bulk upload names drafts from file names and turns zip folders into categories;
	/// "Move to Protocols" leaves the document as a redirect and keeps protected documents in Documents.
	/// </summary>
	[TestFixture]
	public class ProtocolDocumentServiceTests
	{
		private const int DepartmentId = 1;
		private const string Author = "author";

		private readonly Dictionary<int, DispatchProtocol> _protocols = new Dictionary<int, DispatchProtocol>();
		private readonly Dictionary<int, ProtocolVersion> _versions = new Dictionary<int, ProtocolVersion>();
		private readonly Dictionary<int, ProtocolVersionFile> _files = new Dictionary<int, ProtocolVersionFile>();
		private readonly List<ProtocolFileText> _texts = new List<ProtocolFileText>();
		private readonly Dictionary<int, List<string>> _pages = new Dictionary<int, List<string>>();
		private readonly List<ProtocolCategory> _categories = new List<ProtocolCategory>();
		private readonly Dictionary<int, Document> _documents = new Dictionary<int, Document>();
		private Mock<IProtocolLibraryService> _library;
		private Mock<IProtocolTextExtractor> _extractor;
		private Mock<IKnowledgeIndexQueue> KnowledgeQueue;
		private ProtocolDocumentService _service;
		private int _nextProtocol, _nextVersion, _nextFile, _nextText, _nextCategory;

		[SetUp]
		public void SetUp()
		{
			KnowledgeQueue = new Mock<IKnowledgeIndexQueue>();
			_protocols.Clear(); _versions.Clear(); _files.Clear(); _texts.Clear(); _pages.Clear(); _categories.Clear(); _documents.Clear();
			_nextProtocol = 1; _nextVersion = 100; _nextFile = 500; _nextText = 900; _nextCategory = 10;

			_library = new Mock<IProtocolLibraryService>();
			_library.Setup(x => x.CreateAsync(It.IsAny<ProtocolCreateRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolCreateRequest r, CancellationToken _) =>
				{
					var p = r.Protocol;
					p.DispatchProtocolId = _nextProtocol++;
					p.DepartmentId = r.DepartmentId;
					_protocols[p.DispatchProtocolId] = p;
					var v = new ProtocolVersion
					{
						ProtocolVersionId = _nextVersion++, DepartmentId = r.DepartmentId, DispatchProtocolId = p.DispatchProtocolId, Status = (int)ProtocolVersionStatuses.Draft,
						ContentHtml = r.ContentHtml, ContentHash = ProtocolContentHash.Compute(r.ContentHtml), RequiresAcknowledgment = r.RequiresAcknowledgment ?? false
					};
					_versions[v.ProtocolVersionId] = v;
					return new ProtocolLifecycleResult { Protocol = p, Version = v };
				});
			_library.Setup(x => x.GetVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int id) => _versions.TryGetValue(id, out var v) && v.DepartmentId == d ? ProtocolAcknowledgmentHarness.Copy(v) : null);
			_library.Setup(x => x.GetOpenDraftAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int p) => _versions.Values.Where(v => v.DispatchProtocolId == p && v.Status <= (int)ProtocolVersionStatuses.Approved)
					.OrderByDescending(v => v.ProtocolVersionId).Select(ProtocolAcknowledgmentHarness.Copy).FirstOrDefault());
			_library.Setup(x => x.GetAudienceAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<ProtocolAudience>());
			_library.Setup(x => x.GetCategoriesAsync(It.IsAny<int>())).ReturnsAsync(() => _categories.ToList());
			_library.Setup(x => x.SaveCategoryAsync(It.IsAny<int>(), It.IsAny<ProtocolCategory>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, ProtocolCategory c, string u, CancellationToken _) => { c.ProtocolCategoryId = _nextCategory++; c.DepartmentId = d; _categories.Add(c); return c; });

			var protocolsService = new Mock<IProtocolsService>();
			protocolsService.Setup(x => x.GetProtocolHeaderByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _protocols.TryGetValue(id, out var p) ? p : null);

			var versions = new Mock<IProtocolVersionsRepository>();
			versions.Setup(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolVersion v, long expected, CancellationToken _) =>
				{
					if (_versions[v.ProtocolVersionId].RowVersion != expected) return false;
					v.RowVersion = expected + 1;
					_versions[v.ProtocolVersionId] = ProtocolAcknowledgmentHarness.Copy(v);
					return true;
				});

			var files = new Mock<IProtocolVersionFilesRepository>();
			files.Setup(x => x.InsertAsync(It.IsAny<ProtocolVersionFile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ProtocolVersionFile f, CancellationToken _, bool __) => { f.ProtocolVersionFileId = _nextFile++; _files[f.ProtocolVersionId] = f; return f; });
			files.Setup(x => x.GetMetadataForVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int v) => _files.TryGetValue(v, out var f) ? new ProtocolVersionFile { ProtocolVersionFileId = f.ProtocolVersionFileId, ProtocolVersionId = v, FileName = f.FileName, Sha256 = f.Sha256, ContentType = f.ContentType } : null);
			files.Setup(x => x.GetWithDataForVersionAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int v) => _files.TryGetValue(v, out var f) ? f : null);
			files.Setup(x => x.GetWithDataByIdAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int id) => _files.Values.FirstOrDefault(f => f.ProtocolVersionFileId == id));
			files.Setup(x => x.DeleteForVersionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int v, CancellationToken _) => _versions[v].Status == (int)ProtocolVersionStatuses.Draft && _files.Remove(v) ? 1 : 0);

			var texts = new Mock<IProtocolFileTextsRepository>();
			texts.Setup(x => x.InsertAsync(It.IsAny<ProtocolFileText>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ProtocolFileText t, CancellationToken _, bool __) => { t.ProtocolFileTextId = _nextText++; _texts.Add(t); return t; });
			texts.Setup(x => x.GetForSourceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int k, int s) => _texts.FirstOrDefault(t => t.SourceKind == k && t.SourceId == s));
			texts.Setup(x => x.SaveResultAsync(It.IsAny<ProtocolFileText>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
				.Callback((ProtocolFileText t, IReadOnlyList<string> pages, CancellationToken _) => _pages[t.ProtocolFileTextId] = pages.ToList())
				.Returns(Task.CompletedTask);
			texts.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(() => _texts.Where(t => t.Status == (int)ProtocolFileTextStatuses.Pending).ToList());
			texts.Setup(x => x.TryClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, int expected, CancellationToken _) =>
				{
					// The conditional UPDATE: the loaded row is not touched, the service counts the attempt it claimed.
					return _texts.First(x => x.ProtocolFileTextId == id).Attempts == expected;
				});
			texts.Setup(x => x.DeleteForSourceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, int k, int s, CancellationToken _) => _texts.RemoveAll(t => t.SourceKind == k && t.SourceId == s));

			var documents = new Mock<IDocumentsService>();
			documents.Setup(x => x.GetDocumentByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _documents.TryGetValue(id, out var d) ? d : null);
			documents.Setup(x => x.SaveDocumentAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Document d, CancellationToken _) => { _documents[d.DocumentId] = d; return d; });
			documents.Setup(x => x.GetFilteredDocumentsByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string type, string category) => _documents.Values.Where(x => x.DepartmentId == d && x.Category == category).ToList());

			var acks = new Mock<IProtocolAcknowledgmentsRepository>();
			_extractor = new Mock<IProtocolTextExtractor>();
			var real = new ProtocolTextExtractor();
			_extractor.Setup(x => x.Extract(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.Returns((string n, string c, byte[] b, CancellationToken t) => real.Extract(n, c, b, t));

			_service = new ProtocolDocumentService(_library.Object, Mock.Of<IProtocolAccessService>(), protocolsService.Object, versions.Object, files.Object, texts.Object,
				documents.Object, _extractor.Object, Mock.Of<IEventAggregator>(), new Lazy<ISearchProjectionService>(() => Mock.Of<ISearchProjectionService>()), acks.Object,
				KnowledgeQueue.Object);
		}

		private static ProtocolReader Reader(bool manager = true) => new ProtocolReader
		{
			DepartmentId = DepartmentId, UserId = Author, IsCurrentMember = true, CanAuthor = manager, IsDepartmentAdmin = false
		};

		[Test]
		public async Task A_file_becomes_a_draft_version_hashed_by_its_bytes_with_its_text_extracted()
		{
			var pdf = ProtocolTextExtractorTests.Pdf("Mayday procedure for crews");

			var result = await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { Kind = (int)ProtocolKinds.Sog, FileName = "SOG-204 Mayday.pdf", Data = pdf });

			result.Succeeded.Should().BeTrue();
			result.Protocol.Name.Should().Be("Mayday");
			result.Protocol.Number.Should().Be("SOG-204");
			result.Protocol.Code.Should().Be("SOG-204");
			var version = _versions[result.Version.ProtocolVersionId];
			version.IsDocument.Should().BeTrue();
			version.ContentHash.Should().Be(ProtocolDocumentRules.HashFile(pdf));
			version.ContentHtml.Should().Contain("SOG-204 Mayday.pdf", "the stub names the file for the readers that predate documents");
			_files[version.ProtocolVersionId].Sha256.Should().Be(version.ContentHash);
			var text = _texts.Should().ContainSingle().Subject;
			text.Status.Should().Be((int)ProtocolFileTextStatuses.Extracted);
			_pages[text.ProtocolFileTextId].Single().Should().Contain("Mayday procedure for crews");
			KnowledgeQueue.Verify(q => q.EnqueueAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, result.Protocol.DispatchProtocolId.ToString(),
				It.IsAny<CancellationToken>()), Times.Once(), "the knowledge index re-chunks the protocol once its file has text");
		}

		[Test]
		public async Task Only_pdf_and_docx_within_the_cap_are_accepted()
		{
			(await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { FileName = "evil.exe", Data = Encoding.ASCII.GetBytes("MZ....") }))
				.Error.Should().Be(ProtocolLifecycleErrors.FileTypeNotAllowed);
			(await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { FileName = "a.pdf", Data = null }))
				.Error.Should().Be(ProtocolLifecycleErrors.FileRequired);
			var big = new byte[ProtocolDocumentRules.MaxFileBytes + 1];
			Encoding.ASCII.GetBytes("%PDF").CopyTo(big, 0);
			(await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { FileName = "big.pdf", Data = big }))
				.Error.Should().Be(ProtocolLifecycleErrors.FileTooLarge);
			(await _service.CreateFromFileAsync(Reader(false), new ProtocolDocumentCreateRequest { FileName = "a.pdf", Data = ProtocolTextExtractorTests.Pdf("x") }))
				.Error.Should().Be(ProtocolLifecycleErrors.NotAuthorized);
			_protocols.Should().BeEmpty();
		}

		[Test]
		public async Task A_new_upload_replaces_the_drafts_file_but_never_the_file_of_a_version_in_review()
		{
			var created = await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { FileName = "Policy.pdf", Data = ProtocolTextExtractorTests.Pdf("First text") });
			var id = created.Protocol.DispatchProtocolId;
			var second = ProtocolTextExtractorTests.Docx("Second text body");

			var replaced = await _service.UploadToDraftAsync(Reader(), id, "Policy.docx", second);
			replaced.Succeeded.Should().BeTrue();
			_versions[created.Version.ProtocolVersionId].ContentHash.Should().Be(ProtocolDocumentRules.HashFile(second));
			_files.Should().ContainSingle();
			_texts.Should().ContainSingle("the replaced file's text goes with it");

			// Submitted: the content is locked.
			_versions[created.Version.ProtocolVersionId].Status = (int)ProtocolVersionStatuses.InReview;
			var refused = await _service.UploadToDraftAsync(Reader(), id, "Policy.pdf", ProtocolTextExtractorTests.Pdf("Third"));
			refused.Error.Should().Be(ProtocolLifecycleErrors.NotDraft);
			_versions[created.Version.ProtocolVersionId].ContentHash.Should().Be(ProtocolDocumentRules.HashFile(second));
		}

		[Test]
		public async Task A_large_file_is_queued_for_worker_86_instead_of_extracted_in_the_request()
		{
			var large = ProtocolTextExtractorTests.Pdf("Large binder page");
			var padded = large.Concat(new byte[ProtocolDocumentRules.InlineExtractionMaxBytes]).ToArray();

			var created = await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { FileName = "binder.pdf", Data = padded });

			created.Succeeded.Should().BeTrue();
			_texts.Single().Status.Should().Be((int)ProtocolFileTextStatuses.Pending);
			_extractor.Verify(x => x.Extract(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);

			(await _service.ProcessPendingExtractionsAsync(DateTime.UtcNow)).Should().Be(1);
			_texts.Single().Attempts.Should().Be(1);
			_extractor.Verify(x => x.Extract("binder.pdf", ProtocolDocumentRules.PdfContentType, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Bulk_upload_names_drafts_from_files_skips_duplicates_and_turns_folders_into_categories()
		{
			var mayday = ProtocolTextExtractorTests.Pdf("Mayday");
			var files = new List<ProtocolUploadFile>
			{
				new ProtocolUploadFile { FileName = "SOG-204 Mayday.pdf", Folder = "Fireground", Data = mayday },
				new ProtocolUploadFile { FileName = "copy of mayday.pdf", Folder = "Fireground", Data = mayday },
				new ProtocolUploadFile { FileName = "Uniforms.docx", Folder = "Admin/Policies", Data = ProtocolTextExtractorTests.Docx("Uniform policy") },
				new ProtocolUploadFile { FileName = "notes.txt", Data = Encoding.UTF8.GetBytes("not a protocol") }
			};
			var manager = Reader();
			manager.CanApprove = true;

			var results = await _service.BulkUploadAsync(manager, (int)ProtocolKinds.Sog, null, true, files);

			results.Should().HaveCount(4);
			results[0].Succeeded.Should().BeTrue();
			results[0].Number.Should().Be("SOG-204");
			results[1].Error.Should().Be("duplicate");
			results[2].Succeeded.Should().BeTrue();
			results[3].Error.Should().Be(ProtocolLifecycleErrors.FileTypeNotAllowed.ToString());
			_categories.Select(c => c.Name).Should().BeEquivalentTo("Fireground", "Policies");
			_protocols[results[0].ProtocolId.Value].CategoryId.Should().Be(_categories.Single(c => c.Name == "Fireground").ProtocolCategoryId);
			_protocols.Values.Should().OnlyContain(p => p.Kind == (int)ProtocolKinds.Sog);
		}

		[Test]
		public void A_zip_gives_its_pdf_and_docx_entries_with_their_folders()
		{
			using var stream = new MemoryStream();
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
			{
				void Add(string name, byte[] bytes)
				{
					using var entry = archive.CreateEntry(name).Open();
					entry.Write(bytes, 0, bytes.Length);
				}
				Add("Binder/Fireground/SOG-1 Size up.pdf", ProtocolTextExtractorTests.Pdf("Size up"));
				Add("Binder/readme.txt", Encoding.UTF8.GetBytes("ignore me"));
				Add("__MACOSX/Binder/._SOG-1 Size up.pdf", new byte[] { 0 });
				Add("Policy.docx", ProtocolTextExtractorTests.Docx("Policy"));
			}

			var files = _service.ReadZip(stream.ToArray());

			files.Select(f => (f.FileName, f.Folder)).Should().BeEquivalentTo(new[] { ("SOG-1 Size up.pdf", "Binder/Fireground"), ("Policy.docx", (string)null) });
			_service.ReadZip(Encoding.ASCII.GetBytes("not a zip")).Should().BeEmpty();
		}

		[Test]
		public async Task Moving_a_document_creates_a_draft_protocol_and_leaves_the_document_as_the_redirect()
		{
			_documents[7] = new Document { DocumentId = 7, DepartmentId = DepartmentId, Name = "Uniform policy", Filename = "POL-3 Uniforms.pdf", Category = "Policies", Data = ProtocolTextExtractorTests.Pdf("Uniforms"), UserId = "u" };

			var moved = await _service.MoveFromDocumentAsync(Reader(), 7, (int)ProtocolKinds.Reference, canSeeAdminsOnly: false);

			moved.ProtocolId.Should().NotBeNull();
			var protocol = _protocols[moved.ProtocolId.Value];
			protocol.Kind.Should().Be((int)ProtocolKinds.Reference);
			protocol.Name.Should().Be("Uniform policy");
			protocol.Number.Should().Be("POL-3");
			_documents[7].MovedToProtocolId.Should().Be(protocol.DispatchProtocolId);
			_documents[7].MovedOn.Should().NotBeNull();
			_documents[7].Data.Should().BeEmpty("the bytes live with the protocol version now");

			// Moving it again lands on the same protocol; any kind other than Reference is a Policy.
			(await _service.MoveFromDocumentAsync(Reader(), 7, (int)ProtocolKinds.Dispatch, false)).ProtocolId.Should().Be(protocol.DispatchProtocolId);
			_protocols.Should().HaveCount(1);
		}

		[Test]
		public async Task A_protected_or_admins_only_or_foreign_document_is_not_moved()
		{
			_documents[1] = new Document { DocumentId = 1, DepartmentId = DepartmentId, Name = "Secret", Filename = "s.pdf", IsProtected = true, Data = new byte[] { 1 }, UserId = "u" };
			_documents[2] = new Document { DocumentId = 2, DepartmentId = DepartmentId, Name = "Admins", Filename = "a.pdf", AdminsOnly = true, Data = ProtocolTextExtractorTests.Pdf("a"), UserId = "u" };
			_documents[3] = new Document { DocumentId = 3, DepartmentId = 99, Name = "Theirs", Filename = "t.pdf", Data = ProtocolTextExtractorTests.Pdf("t"), UserId = "u" };

			(await _service.MoveFromDocumentAsync(Reader(), 1, (int)ProtocolKinds.Policy, true)).Error.Should().Be("protected");
			(await _service.MoveFromDocumentAsync(Reader(), 2, (int)ProtocolKinds.Policy, false)).Error.Should().Be(ProtocolLifecycleErrors.NotFound.ToString());
			(await _service.MoveFromDocumentAsync(Reader(), 3, (int)ProtocolKinds.Policy, true)).Error.Should().Be(ProtocolLifecycleErrors.NotFound.ToString());
			(await _service.MoveFromDocumentAsync(Reader(false), 2, (int)ProtocolKinds.Policy, true)).Error.Should().Be(ProtocolLifecycleErrors.NotAuthorized.ToString());
			_protocols.Should().BeEmpty();
			_documents[1].MovedToProtocolId.Should().BeNull();
		}

		[Test]
		public async Task A_whole_category_moves_and_reports_each_document()
		{
			_documents[1] = new Document { DocumentId = 1, DepartmentId = DepartmentId, Name = "One", Filename = "one.pdf", Category = "SOGs", Data = ProtocolTextExtractorTests.Pdf("one"), UserId = "u" };
			_documents[2] = new Document { DocumentId = 2, DepartmentId = DepartmentId, Name = "Two", Filename = "two.xlsx", Category = "SOGs", Data = new byte[] { 0x50, 0x4B, 3, 4, 0 }, UserId = "u" };

			var results = await _service.MoveCategoryFromDocumentsAsync(Reader(), "SOGs", (int)ProtocolKinds.Policy, false);

			results.Should().HaveCount(2);
			results.Count(r => r.ProtocolId.HasValue).Should().Be(1);
			results.Single(r => !r.ProtocolId.HasValue).Error.Should().Be(ProtocolLifecycleErrors.FileTypeNotAllowed.ToString(), "a spreadsheet stays in Documents");
		}

		[Test]
		public async Task The_file_endpoint_applies_the_per_version_read_rule()
		{
			var created = await _service.CreateFromFileAsync(Reader(), new ProtocolDocumentCreateRequest { FileName = "Policy.pdf", Data = ProtocolTextExtractorTests.Pdf("Policy") });
			var versionId = created.Version.ProtocolVersionId;

			var member = new ProtocolReader { DepartmentId = DepartmentId, UserId = "member", IsCurrentMember = true, Facts = new ContentAudienceMemberFacts { UserId = "member", IsActive = true } };
			(await _service.GetReadableFileAsync(member, versionId)).Should().BeNull("a member never reads a draft's file");
			(await _service.GetReadableFileAsync(Reader(), versionId)).Should().NotBeNull("the author manages the library");

			_versions[versionId].Status = (int)ProtocolVersionStatuses.Published;
			_protocols[created.Protocol.DispatchProtocolId].Status = (int)ProtocolStatuses.Published;
			_protocols[created.Protocol.DispatchProtocolId].CurrentVersionId = versionId;
			(await _service.GetReadableFileAsync(member, versionId)).Data.Should().NotBeEmpty();

			var stranger = new ProtocolReader { DepartmentId = 99, UserId = "x", IsCurrentMember = true };
			(await _service.GetReadableFileAsync(stranger, versionId)).Should().BeNull();
		}
	}
}
