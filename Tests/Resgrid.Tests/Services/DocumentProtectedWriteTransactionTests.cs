using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A new document cannot be enveloped before it is inserted — the AAD row key IS the identity pk,
	/// and only the database can assign it. That leaves a window where the row holds the document's
	/// plaintext, file bytes included, and a broker failure used to commit that window permanently:
	/// the method threw, but the row stayed.
	///
	/// So the insert, the encryption and the re-save share one transaction that commits only once the
	/// values are enveloped. An UPDATE has none of this problem — it already has its identity, so it
	/// is enveloped before the save and never writes plaintext at all.
	/// </summary>
	[TestFixture]
	public class DocumentProtectedWriteTransactionTests
	{
		private const int DeptId = 4;

		private Mock<IDocumentRepository> _repo;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private Mock<IUnitOfWork> _unitOfWork;
		private DocumentsService _service;

		[SetUp]
		public void SetUp()
		{
			_repo = new Mock<IDocumentRepository>();
			_repo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Document d, CancellationToken _, bool __) =>
				{
					if (d.DocumentId == 0)
						d.DocumentId = 99;      // the database assigning the identity

					return d;
				});

			_protectedWriteService = new Mock<IProtectedWriteService>();
			_protectedWriteService.Setup(x => x.PrepareDocumentWriteAsync(It.IsAny<int>(), It.IsAny<Document>(),
					It.IsAny<Document>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));

			_unitOfWork = new Mock<IUnitOfWork>();
			_unitOfWork.SetupGet(x => x.Connection).Returns((System.Data.Common.DbConnection)null);

			_service = new DocumentsService(_repo.Object, new Mock<IDocumentCategoriesRepository>().Object,
				new Mock<IEventAggregator>().Object,
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object),
				_unitOfWork.Object);
		}

		private static Document New() => new Document { DepartmentId = DeptId, Name = "SOP", Data = new byte[] { 1, 2, 3 } };

		[Test]
		public async Task A_new_document_commits_only_after_the_values_are_enveloped()
		{
			var order = new System.Collections.Generic.List<string>();

			_unitOfWork.Setup(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync((System.Data.Common.DbConnection)null)
				.Callback(() => order.Add("begin"));
			_repo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Document d, CancellationToken _, bool __) => d)
				.Callback(() => order.Add("save"));
			_protectedWriteService.Setup(x => x.PrepareDocumentWriteAsync(It.IsAny<int>(), It.IsAny<Document>(),
					It.IsAny<Document>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true))
				.Callback(() => order.Add("protect"));
			_unitOfWork.Setup(x => x.CommitChanges()).Callback(() => order.Add("commit"));

			await _service.SaveDocumentAsync(New());

			order.Should().Equal(new[] { "begin", "save", "protect", "save", "commit" });
		}

		[Test]
		public async Task A_blocked_broker_rolls_the_insert_back()
		{
			_protectedWriteService.Setup(x => x.PrepareDocumentWriteAsync(It.IsAny<int>(), It.IsAny<Document>(),
					It.IsAny<Document>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> save = () => _service.SaveDocumentAsync(New());

			await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*broker_unavailable*");

			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Once,
				"the inserted row holds the document's plaintext until the envelopes land");
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task A_broker_that_throws_rolls_the_insert_back_too()
		{
			_protectedWriteService.Setup(x => x.PrepareDocumentWriteAsync(It.IsAny<int>(), It.IsAny<Document>(),
					It.IsAny<Document>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
					It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TimeoutException("broker"));

			Func<Task> save = () => _service.SaveDocumentAsync(New());

			await save.Should().ThrowAsync<TimeoutException>();

			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task An_existing_document_is_enveloped_before_the_save_and_needs_no_transaction()
		{
			var existing = new Document { DocumentId = 7, DepartmentId = DeptId, Name = "SOP" };
			_repo.Setup(x => x.GetByIdAsync(7)).ReturnsAsync(existing);

			await _service.SaveDocumentAsync(new Document { DocumentId = 7, DepartmentId = DeptId, Name = "SOP v2" });

			// Nothing plaintext ever reaches the table on this path, so there is nothing to roll back.
			_unitOfWork.Verify(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			_repo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
				Times.Once, "an update is saved exactly once — already enveloped");
		}

		[Test]
		public async Task A_caller_that_already_owns_a_transaction_keeps_it()
		{
			// Committing someone else's in-flight unit of work here — or discarding it — would be
			// worse than the problem this transaction solves.
			_unitOfWork.SetupGet(x => x.Connection).Returns(Mock.Of<System.Data.Common.DbConnection>());

			await _service.SaveDocumentAsync(New());

			_unitOfWork.Verify(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Never);
		}
	}
}
