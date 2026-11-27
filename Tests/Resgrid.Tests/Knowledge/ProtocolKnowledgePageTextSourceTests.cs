using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>
	/// The protocol file text the knowledge index reads (K1 over the P1 wave-2 extraction store): only finished extractions of the cited
	/// version's primary file and of the protocol's attachments, never another version's file, never a queued or "no text" file, never
	/// another department's rows; other source kinds still yield nothing.
	/// </summary>
	[TestFixture]
	public class ProtocolKnowledgePageTextSourceTests
	{
		private const int DepartmentId = 4;
		private const int ProtocolId = 12;
		private const int VersionId = 30;

		private Mock<IProtocolFileTextsRepository> _texts;
		private Mock<IProtocolVersionFilesRepository> _files;
		private ProtocolKnowledgePageTextSource _source;

		[SetUp]
		public void SetUp()
		{
			_texts = new Mock<IProtocolFileTextsRepository>();
			_files = new Mock<IProtocolVersionFilesRepository>();
			_files.Setup(f => f.GetMetadataForVersionAsync(DepartmentId, VersionId))
				.ReturnsAsync(new ProtocolVersionFile { DepartmentId = DepartmentId, DispatchProtocolId = ProtocolId, ProtocolVersionId = VersionId, FileName = "SOG-204 Mayday.pdf" });

			_texts.Setup(t => t.GetForProtocolAsync(DepartmentId, ProtocolId)).ReturnsAsync(new List<ProtocolFileText>
			{
				Text(1, ProtocolFileSourceKinds.VersionFile, 5, VersionId, ProtocolFileTextStatuses.Extracted),
				Text(2, ProtocolFileSourceKinds.VersionFile, 6, VersionId - 1, ProtocolFileTextStatuses.Extracted), // an older version's file
				Text(3, ProtocolFileSourceKinds.Attachment, 5, null, ProtocolFileTextStatuses.Extracted),
				Text(4, ProtocolFileSourceKinds.Attachment, 8, null, ProtocolFileTextStatuses.NoText),
				Text(5, ProtocolFileSourceKinds.Attachment, 9, null, ProtocolFileTextStatuses.Pending),
				new ProtocolFileText { ProtocolFileTextId = 6, DepartmentId = DepartmentId + 1, DispatchProtocolId = ProtocolId, SourceKind = (int)ProtocolFileSourceKinds.Attachment, SourceId = 10, Status = (int)ProtocolFileTextStatuses.Extracted }
			});
			foreach (var id in new[] { 1, 2, 3, 4, 5, 6 })
			{
				var textId = id;
				_texts.Setup(t => t.GetPagesAsync(DepartmentId, textId)).ReturnsAsync(new List<ProtocolFileTextPage>
				{
					new ProtocolFileTextPage { ProtocolFileTextId = textId, DepartmentId = DepartmentId, PageNumber = 1, Text = "text " + textId + " page 1" },
					new ProtocolFileTextPage { ProtocolFileTextId = textId, DepartmentId = DepartmentId, PageNumber = 2, Text = "text " + textId + " page 2" }
				});
			}

			_source = new ProtocolKnowledgePageTextSource(_texts.Object, _files.Object);
		}

		private static ProtocolFileText Text(int id, ProtocolFileSourceKinds kind, int sourceId, int? versionId, ProtocolFileTextStatuses status) => new ProtocolFileText
		{
			ProtocolFileTextId = id, DepartmentId = DepartmentId, DispatchProtocolId = ProtocolId, SourceKind = (int)kind, SourceId = sourceId,
			ProtocolVersionId = versionId, Status = (int)status
		};

		[Test]
		public async Task Only_finished_extractions_of_the_cited_versions_file_and_the_attachments_are_read()
		{
			var pages = await _source.GetPagesAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, ProtocolId.ToString(), VersionId.ToString());

			pages.Select(p => p.Text).Should().BeEquivalentTo("text 1 page 1", "text 1 page 2", "text 3 page 1", "text 3 page 2");
			pages.Where(p => p.Text.StartsWith("text 1")).Should().OnlyContain(p => p.FileName == "SOG-204 Mayday.pdf" && p.AttachmentId == "file-5");
			pages.Where(p => p.Text.StartsWith("text 3")).Should().OnlyContain(p => p.FileName == null && p.AttachmentId == "attachment-5",
				"a version file and an attachment with the same id keep separate page anchors");
		}

		[Test]
		public async Task Other_source_kinds_and_unparsable_ids_yield_no_pages()
		{
			(await _source.GetPagesAsync(DepartmentId, KnowledgeSourceKinds.TrainingFile, ProtocolId.ToString(), VersionId.ToString())).Should().BeEmpty();
			(await _source.GetPagesAsync(DepartmentId, KnowledgeSourceKinds.Document, ProtocolId.ToString(), VersionId.ToString())).Should().BeEmpty();
			(await _source.GetPagesAsync(DepartmentId, KnowledgeSourceKinds.ProtocolFile, "x", VersionId.ToString())).Should().BeEmpty();
			(await _source.GetPagesAsync(0, KnowledgeSourceKinds.ProtocolFile, ProtocolId.ToString(), VersionId.ToString())).Should().BeEmpty();
			_texts.Verify(t => t.GetForProtocolAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
		}
	}
}
