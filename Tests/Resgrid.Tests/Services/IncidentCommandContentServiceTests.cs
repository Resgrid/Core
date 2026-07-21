using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class IncidentCommandContentServiceTests
	{
		private const int DepartmentId = 10;
		private const int CallId = 22;
		private Mock<IIncidentCommandRepository> _commandRepository;
		private Mock<IIncidentNoteRepository> _noteRepository;
		private Mock<IIncidentAttachmentRepository> _attachmentRepository;
		private Mock<ICommandLogEntryRepository> _logRepository;
		private Mock<ICallsService> _callsService;
		private Mock<IIncidentWeatherProvider> _weatherProvider;
		private Mock<IEventAggregator> _events;
		private IncidentCommandService _service;

		[SetUp]
		public void SetUp()
		{
			_commandRepository = new Mock<IIncidentCommandRepository>();
			_noteRepository = new Mock<IIncidentNoteRepository>();
			_attachmentRepository = new Mock<IIncidentAttachmentRepository>();
			_logRepository = new Mock<ICommandLogEntryRepository>();
			_callsService = new Mock<ICallsService>();
			_weatherProvider = new Mock<IIncidentWeatherProvider>();
			_events = new Mock<IEventAggregator>();

			_logRepository.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandLogEntry value, CancellationToken _, bool _) => value);
			_noteRepository.Setup(x => x.InsertAsync(It.IsAny<IncidentNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentNote value, CancellationToken _, bool _) => value);
			_attachmentRepository.Setup(x => x.InsertAsync(It.IsAny<IncidentAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentAttachment value, CancellationToken _, bool _) => value);

			_service = new IncidentCommandService(
				_commandRepository.Object,
				new Mock<ICommandStructureNodeRepository>().Object,
				new Mock<IResourceAssignmentRepository>().Object,
				new Mock<ITacticalObjectiveRepository>().Object,
				new Mock<IIncidentTimerRepository>().Object,
				new Mock<IIncidentMapAnnotationRepository>().Object,
				_logRepository.Object,
				new Mock<ICommandTransferRepository>().Object,
				new Mock<ICommandsService>().Object,
				_callsService.Object,
				new Mock<ICheckInTimerService>().Object,
				new Mock<IIncidentVoiceService>().Object,
				new Mock<IIncidentRoleAssignmentRepository>().Object,
				_events.Object,
				new Mock<ICoreEventService>().Object,
				new Mock<IUnitsService>().Object,
				new Mock<IPersonnelRolesService>().Object,
				_noteRepository.Object,
				_attachmentRepository.Object,
				_weatherProvider.Object,
				new Mock<IIncidentNeedRepository>().Object,
				new Mock<IUserProfileService>().Object,
				new Mock<IIncidentCommandNotificationService>().Object);
		}

		[Test]
		public async Task AddNote_WithPublicContainmentUpdate_StampsOwnershipAndRaisesEvent()
		{
			// Arrange
			ArrangeOwnedCommand();
			var note = new IncidentNote
			{
				IncidentCommandId = "ic-1",
				DepartmentId = DepartmentId,
				CallId = 999,
				NoteType = (int)IncidentNoteType.Containment,
				Visibility = (int)IncidentContentVisibility.Public,
				Title = " Containment update ",
				Body = "  Fire is 40% contained.  ",
				ContainmentPercent = 40
			};

			// Act
			var saved = await _service.AddNoteAsync(note, "pio-1");

			// Assert
			saved.CallId.Should().Be(CallId);
			saved.Title.Should().Be("Containment update");
			saved.Body.Should().Be("Fire is 40% contained.");
			saved.CreatedByUserId.Should().Be("pio-1");
			saved.ModifiedOn.Should().NotBeNull();
			_events.Verify(x => x.SendMessage(It.Is<IncidentNoteAddedEvent>(e =>
				e.CallId == CallId && e.Visibility == (int)IncidentContentVisibility.Public && e.ContainmentPercent == 40)), Times.Once);
		}

		[TestCase("..\\plans\\iap.pdf")]
		[TestCase("../plans/iap.pdf")]
		public async Task AddAttachment_WithAllowedDocument_ComputesIntegrityMetadata(string fileName)
		{
			// Arrange
			ArrangeOwnedCommand();
			var data = Encoding.UTF8.GetBytes("incident action plan");
			var attachment = new IncidentAttachment
			{
				IncidentCommandId = "ic-1",
				DepartmentId = DepartmentId,
				Visibility = (int)IncidentContentVisibility.Internal,
				FileName = fileName,
				ContentType = "application/pdf",
				Data = data
			};

			// Act
			var saved = await _service.AddAttachmentAsync(attachment, "docs-1");

			// Assert
			saved.FileName.Should().Be("iap.pdf");
			saved.ContentLength.Should().Be(data.LongLength);
			saved.Sha256Hash.Should().HaveLength(64);
			saved.Sha256Hash.Should().Be("3620f1049dbe154a423fd33fdb92ceb6d591a77d4af0e6341451c6f4d05659f6");
			_events.Verify(x => x.SendMessage(It.Is<IncidentAttachmentAddedEvent>(e =>
				e.FileName == "iap.pdf" && e.ContentLength == data.LongLength)), Times.Once);
		}

		[Test]
		public async Task AddAttachment_WithExecutableExtension_IsRejected()
		{
			// Arrange
			var attachment = new IncidentAttachment
			{
				IncidentCommandId = "ic-1",
				DepartmentId = DepartmentId,
				Visibility = (int)IncidentContentVisibility.Internal,
				FileName = "payload.exe",
				ContentType = "application/octet-stream",
				Data = new byte[] { 1, 2, 3 }
			};

			// Act
			Func<Task> act = () => _service.AddAttachmentAsync(attachment, "docs-1");

			// Assert
			await act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task GetPublicInformation_WithMixedContent_ReturnsOnlyExplicitlyPublicRecords()
		{
			// Arrange
			var shareToken = new string('a', 64);
			_commandRepository.Setup(x => x.GetByPublicShareTokenAsync(shareToken)).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic-1",
				DepartmentId = DepartmentId,
				CallId = CallId,
				EstablishedOn = DateTime.UtcNow.AddHours(-2),
				Status = (int)IncidentCommandStatus.Active,
				PublicShareEnabled = true
			});
			_noteRepository.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<IncidentNote>
			{
				new IncidentNote { IncidentNoteId = "public-note", CallId = CallId, Visibility = (int)IncidentContentVisibility.Public, CreatedOn = DateTime.UtcNow },
				new IncidentNote { IncidentNoteId = "internal-note", CallId = CallId, Visibility = (int)IncidentContentVisibility.Internal, CreatedOn = DateTime.UtcNow }
			});
			_attachmentRepository.Setup(x => x.GetAllMetadataByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<IncidentAttachment>
			{
				new IncidentAttachment { IncidentAttachmentId = "public-file", CallId = CallId, Visibility = (int)IncidentContentVisibility.Public, UploadedOn = DateTime.UtcNow },
				new IncidentAttachment { IncidentAttachmentId = "internal-file", CallId = CallId, Visibility = (int)IncidentContentVisibility.Internal, UploadedOn = DateTime.UtcNow }
			});

			// Act
			var result = await _service.GetPublicInformationAsync(shareToken);

			// Assert
			result.Notes.Select(x => x.IncidentNoteId).Should().Equal("public-note");
			result.Attachments.Select(x => x.IncidentAttachmentId).Should().Equal("public-file");
		}

		[Test]
		public async Task GetWeatherForIncident_WithCommandPost_UsesCommandPostInsteadOfCallLocation()
		{
			// Arrange
			_commandRepository.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<IncidentCommand>
			{
				new IncidentCommand
				{
					IncidentCommandId = "ic-1",
					DepartmentId = DepartmentId,
					CallId = CallId,
					CommandPostLatitude = "39.7817",
					CommandPostLongitude = "-89.6501",
					EstablishedOn = DateTime.UtcNow
				}
			});
			_weatherProvider.Setup(x => x.GetWeatherAsync(39.7817m, -89.6501m, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IncidentWeather { Latitude = 39.7817m, Longitude = -89.6501m });

			// Act
			var result = await _service.GetWeatherForIncidentAsync(DepartmentId, CallId);

			// Assert
			result.Latitude.Should().Be(39.7817m);
			_weatherProvider.Verify(x => x.GetWeatherAsync(39.7817m, -89.6501m, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
			_callsService.Verify(x => x.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		private void ArrangeOwnedCommand()
		{
			_commandRepository.Setup(x => x.GetByIdAsync("ic-1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic-1",
				DepartmentId = DepartmentId,
				CallId = CallId,
				Status = (int)IncidentCommandStatus.Active
			});
		}
	}
}
