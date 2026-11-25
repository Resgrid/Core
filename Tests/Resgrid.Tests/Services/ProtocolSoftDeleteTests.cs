using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Deleting a protocol keeps its row: calls that used it cite it, and removing the row used to remove it from those
	/// calls (the CallProtocols foreign key cascaded). A deleted protocol leaves the department's current set but stays
	/// readable by id for the calls that carry it.
	/// </summary>
	[TestFixture]
	public class ProtocolSoftDeleteTests
	{
		private const int DepartmentId = 10;

		private Mock<IDispatchProtocolRepository> _protocols;
		private ProtocolsService _service;

		[SetUp]
		public void SetUp()
		{
			_protocols = new Mock<IDispatchProtocolRepository>();

			var attachments = new Mock<IDispatchProtocolAttachmentRepository>();
			attachments.Setup(x => x.GetDispatchProtocolAttachmentByProtocolIdAsync(It.IsAny<int>())).ReturnsAsync(new List<DispatchProtocolAttachment>());
			var questions = new Mock<IDispatchProtocolQuestionsRepository>();
			questions.Setup(x => x.GetDispatchProtocolQuestionsByProtocolIdAsync(It.IsAny<int>())).ReturnsAsync(new List<DispatchProtocolQuestion>());
			var triggers = new Mock<IDispatchProtocolTriggersRepository>();
			triggers.Setup(x => x.GetDispatchProtocolTriggersByProtocolIdAsync(It.IsAny<int>())).ReturnsAsync(new List<DispatchProtocolTrigger>());

			_service = new ProtocolsService(_protocols.Object, attachments.Object, questions.Object, triggers.Object,
				Mock.Of<IDispatchProtocolQuestionAnswersRepository>());
		}

		[Test]
		public async Task Deleting_marks_the_protocol_deleted_and_never_removes_the_row()
		{
			var protocol = new DispatchProtocol { DispatchProtocolId = 4, DepartmentId = DepartmentId, Name = "Mayday" };
			_protocols.Setup(x => x.GetDispatchProtocolByIdAsync(4)).ReturnsAsync(protocol);
			_protocols.Setup(x => x.SaveOrUpdateAsync(protocol, It.IsAny<CancellationToken>(), true)).ReturnsAsync(protocol);

			var deleted = await _service.DeleteProtocol(4);

			deleted.Should().BeTrue();
			protocol.IsDeleted.Should().BeTrue();
			protocol.DeletedOn.Should().NotBeNull();
			_protocols.Verify(x => x.SaveOrUpdateAsync(protocol, It.IsAny<CancellationToken>(), true), Times.Once);
			_protocols.Verify(x => x.DeleteAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Deleting_a_protocol_that_is_gone_reports_nothing_deleted()
		{
			_protocols.Setup(x => x.GetDispatchProtocolByIdAsync(404)).ReturnsAsync((DispatchProtocol)null);

			(await _service.DeleteProtocol(404)).Should().BeFalse();
		}

		[Test]
		public async Task A_deleted_protocol_is_not_in_the_departments_current_set()
		{
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = DepartmentId, IsDeleted = true }
			});

			var current = await _service.GetAllProtocolsForDepartmentAsync(DepartmentId);

			current.Select(x => x.DispatchProtocolId).Should().Equal(1);
		}

		[Test]
		public async Task A_deleted_protocol_cannot_be_attached_to_a_new_call()
		{
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = DepartmentId, IsDeleted = true }
			});

			(await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 1, 2 })).Should().Equal(1);
		}

		[Test]
		public async Task A_deleted_protocol_is_still_read_by_id_for_the_calls_that_cite_it()
		{
			_protocols.Setup(x => x.GetDispatchProtocolByIdAsync(2))
				.ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = DepartmentId, IsDeleted = true, Name = "Retired" });

			var protocol = await _service.GetProtocolByIdAsync(2);

			protocol.Should().NotBeNull();
			protocol.Name.Should().Be("Retired");
		}
	}
}
