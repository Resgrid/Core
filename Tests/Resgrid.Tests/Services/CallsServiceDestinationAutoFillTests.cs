using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Plan D8 in CallsService.SaveCallAsync: a new call is offered to the destination auto-fill before it is saved, the chosen
	/// destination is saved with the call, and a system note says where it came from. Edits are never offered.
	/// </summary>
	[TestFixture]
	public class CallsServiceDestinationAutoFillTests
	{
		private const int DepartmentId = 12;

		private Mock<ICallsRepository> _callsRepo;
		private Mock<ICallNotesRepository> _callNotesRepo;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private Mock<IDestinationAutoFillService> _autoFill;
		private Call _savedCall;
		private CallsService _service;

		[SetUp]
		public void SetUp()
		{
			_callsRepo = new Mock<ICallsRepository>();
			_callNotesRepo = new Mock<ICallNotesRepository>();
			_protectedWriteService = new Mock<IProtectedWriteService>();
			_autoFill = new Mock<IDestinationAutoFillService>();

			_callsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, CancellationToken _, bool __) =>
				{
					if (c.CallId == 0)
						c.CallId = 501;
					_savedCall = c;
					return c;
				});
			_callsRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>())).ReturnsAsync(() => _savedCall);
			_callNotesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CallNote n, CancellationToken _, bool __) => n);

			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(It.IsAny<int>(), It.IsAny<Call>(), It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_protectedWriteService.Setup(x => x.PrepareCallNoteWriteAsync(It.IsAny<int>(), It.IsAny<CallNote>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_service = new CallsService(
				_callsRepo.Object, Mock.Of<ICommunicationService>(), Mock.Of<ICallDispatchesRepository>(),
				Mock.Of<ICallTypesRepository>(), Mock.Of<ICallEmailFactory>(), Mock.Of<ICacheProvider>(),
				_callNotesRepo.Object, Mock.Of<ICallAttachmentRepository>(), Mock.Of<ICallDispatchGroupRepository>(),
				Mock.Of<ICallDispatchUnitRepository>(), Mock.Of<ICallDispatchRoleRepository>(), Mock.Of<IDepartmentCallPriorityRepository>(),
				Mock.Of<IShortenUrlProvider>(), Mock.Of<ICallProtocolsRepository>(), Mock.Of<IGeoLocationProvider>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<ICallReferencesRepository>(), Mock.Of<ICallContactsRepository>(),
				Mock.Of<IIndoorMapService>(), Mock.Of<ICallVideoFeedRepository>(), Mock.Of<ICallNumberingService>(),
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object),
				destinationAutoFill: new Lazy<IDestinationAutoFillService>(() => _autoFill.Object));
		}

		private static Call NewEmailCall() => new Call
		{
			DepartmentId = DepartmentId,
			Number = "26-100",
			Name = "Burns",
			ReportingUserId = "importer",
			LoggedOn = DateTime.UtcNow,
			CallSource = (int)CallSources.EmailImport,
			GeoLocationData = "50.85,4.35"
		};

		[Test]
		public async Task A_new_call_saves_the_chosen_destination_and_a_system_note_naming_it()
		{
			int? offeredCallId = null;
			_autoFill.Setup(x => x.ChooseDestinationAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Callback((Call c, CancellationToken _) => offeredCallId = c.CallId)
				.ReturnsAsync(new DestinationAutoFillChoice { PoiId = 7, PoiName = "St. Mary Burn Center" });

			var saved = await _service.SaveCallAsync(NewEmailCall());

			offeredCallId.Should().Be(0, "the call is offered before it is saved");
			saved.DestinationPoiId.Should().Be(7);
			_callsRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<Call>(c => c.DestinationPoiId == 7), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.AtLeastOnce);
			_callNotesRepo.Verify(x => x.SaveOrUpdateAsync(It.Is<CallNote>(n =>
				n.CallId == 501 && n.Source == (int)CallNoteSources.System && n.UserId == "importer" &&
				n.Note == "Destination set from the department's destination rules: St. Mary Burn Center"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Nothing_chosen_leaves_the_call_and_adds_no_note()
		{
			_autoFill.Setup(x => x.ChooseDestinationAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((DestinationAutoFillChoice)null);

			var saved = await _service.SaveCallAsync(NewEmailCall());

			saved.DestinationPoiId.Should().BeNull();
			_callNotesRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task An_edited_call_is_never_offered()
		{
			var call = NewEmailCall();
			call.CallId = 77;

			await _service.SaveCallAsync(call);

			_autoFill.Verify(x => x.ChooseDestinationAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_failed_note_never_fails_the_saved_call()
		{
			_autoFill.Setup(x => x.ChooseDestinationAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DestinationAutoFillChoice { PoiId = 7, PoiName = "St. Mary Burn Center" });
			_callNotesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new TimeoutException());

			var saved = await _service.SaveCallAsync(NewEmailCall());

			saved.DestinationPoiId.Should().Be(7);
		}
	}
}
