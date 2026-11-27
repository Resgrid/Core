using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Round 10 (Omni): a document-backed version's file name, type and size for the v4 reader (ProtocolContent/GetVersionContent), read from the
	/// metadata select: never the bytes, never another department's file.
	/// </summary>
	[TestFixture]
	public class ProtocolDocumentFileInfoTests
	{
		private const int DepartmentId = 7;

		private static ProtocolDocumentService Service(Mock<IProtocolVersionFilesRepository> files) =>
			new ProtocolDocumentService(Mock.Of<IProtocolLibraryService>(), Mock.Of<IProtocolAccessService>(), Mock.Of<IProtocolsService>(), Mock.Of<IProtocolVersionsRepository>(),
				files.Object, Mock.Of<IProtocolFileTextsRepository>(), Mock.Of<IDocumentsService>(), Mock.Of<IProtocolTextExtractor>(), Mock.Of<IEventAggregator>(),
				new Lazy<ISearchProjectionService>(() => Mock.Of<ISearchProjectionService>()), Mock.Of<IProtocolAcknowledgmentsRepository>(), Mock.Of<IKnowledgeIndexQueue>(),
				Mock.Of<IDepartmentDataProtectionService>());

		[Test]
		public async Task The_file_info_is_the_metadata_of_the_departments_version_file()
		{
			var files = new Mock<IProtocolVersionFilesRepository>();
			files.Setup(f => f.GetMetadataForVersionAsync(DepartmentId, 40)).ReturnsAsync(new ProtocolVersionFile
			{
				ProtocolVersionFileId = 3, DepartmentId = DepartmentId, ProtocolVersionId = 40, FileName = "SOG-204 Mayday.pdf", ContentType = "application/pdf", Size = 4096, Data = new byte[] { 1 }
			});

			var file = await Service(files).GetFileInfoAsync(DepartmentId, 40);

			file.FileName.Should().Be("SOG-204 Mayday.pdf");
			file.Size.Should().Be(4096);
			file.Data.Should().BeNull("the bytes never leave through the metadata read");
			files.Verify(f => f.GetWithDataForVersionAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_file_a_missing_file_or_bad_ids_give_nothing()
		{
			var files = new Mock<IProtocolVersionFilesRepository>();
			files.Setup(f => f.GetMetadataForVersionAsync(DepartmentId, 41)).ReturnsAsync(new ProtocolVersionFile { DepartmentId = DepartmentId + 1, ProtocolVersionId = 41 });

			(await Service(files).GetFileInfoAsync(DepartmentId, 41)).Should().BeNull();
			(await Service(files).GetFileInfoAsync(DepartmentId, 42)).Should().BeNull();
			(await Service(files).GetFileInfoAsync(0, 41)).Should().BeNull();
			(await Service(files).GetFileInfoAsync(DepartmentId, 0)).Should().BeNull();
		}
	}
}
