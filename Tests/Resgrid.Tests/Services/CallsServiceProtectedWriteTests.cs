using System;
using System.Collections.Generic;
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
	/// Pins the ADP write safety net in CallsService.SaveCallAsync (plan 4.2/19.2): every path
	/// that persists a call through the service — including cascade-saved Attachments/CallNotes
	/// collections, which is how email import lands children — leaves no cataloged plaintext in a
	/// protected department's rows, and a blocked write throws instead of degrading.
	/// </summary>
	[TestFixture]
	public class CallsServiceProtectedWriteTests
	{
		private Mock<ICallsRepository> _callsRepo;
		private Mock<ICallNotesRepository> _callNotesRepo;
		private Mock<ICallAttachmentRepository> _callAttachmentRepo;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private CallsService _service;

		[SetUp]
		public void SetUp()
		{
			_callsRepo = new Mock<ICallsRepository>();
			_callNotesRepo = new Mock<ICallNotesRepository>();
			_callAttachmentRepo = new Mock<ICallAttachmentRepository>();
			_protectedWriteService = new Mock<IProtectedWriteService>();

			_callsRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, CancellationToken _, bool __) => c);
			_callNotesRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CallNote n, CancellationToken _, bool __) => n);
			_callAttachmentRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _, bool __) => a);

			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(It.IsAny<int>(), It.IsAny<Call>(), It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_protectedWriteService.Setup(x => x.PrepareCallNoteWriteAsync(It.IsAny<int>(), It.IsAny<CallNote>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_protectedWriteService.Setup(x => x.PrepareCallAttachmentWriteAsync(It.IsAny<int>(), It.IsAny<CallAttachment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_service = new CallsService(
				_callsRepo.Object, Mock.Of<ICommunicationService>(), Mock.Of<ICallDispatchesRepository>(),
				Mock.Of<ICallTypesRepository>(), Mock.Of<ICallEmailFactory>(), Mock.Of<ICacheProvider>(),
				_callNotesRepo.Object, _callAttachmentRepo.Object, Mock.Of<ICallDispatchGroupRepository>(),
				Mock.Of<ICallDispatchUnitRepository>(), Mock.Of<ICallDispatchRoleRepository>(), Mock.Of<IDepartmentCallPriorityRepository>(),
				Mock.Of<IShortenUrlProvider>(), Mock.Of<ICallProtocolsRepository>(), Mock.Of<IGeoLocationProvider>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<ICallReferencesRepository>(), Mock.Of<ICallContactsRepository>(),
				Mock.Of<IIndoorMapService>(), Mock.Of<ICallVideoFeedRepository>(),
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object));
		}

		private static Call BuildCall() => new Call
		{
			CallId = 42,
			DepartmentId = 10,
			Number = "26-100",
			Name = "Structure Fire",
			LoggedOn = DateTime.UtcNow
		};

		[Test]
		public async Task SaveCallAsync_UnprotectedDepartment_DoesNotTouchCascadeChildren()
		{
			var call = BuildCall();
			call.Attachments = new List<CallAttachment> { new CallAttachment { CallAttachmentId = 7, FileName = "scene.jpg" } };
			call.CallNotes = new List<CallNote> { new CallNote { CallNoteId = 8, Note = "note" } };

			var result = await _service.SaveCallAsync(call);

			result.Should().NotBeNull();
			_protectedWriteService.Verify(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), null, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
			_protectedWriteService.Verify(x => x.PrepareCallAttachmentWriteAsync(It.IsAny<int>(), It.IsAny<CallAttachment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_protectedWriteService.Verify(x => x.PrepareCallNoteWriteAsync(It.IsAny<int>(), It.IsAny<CallNote>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_callsRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task SaveCallAsync_ProtectedDepartment_EncryptsAndResavesCascadeChildren()
		{
			var call = BuildCall();
			var attachment = new CallAttachment { CallAttachmentId = 7, FileName = "scene.jpg" };
			var note = new CallNote { CallNoteId = 8, Note = "note" };
			call.Attachments = new List<CallAttachment> { attachment };
			call.CallNotes = new List<CallNote> { note };

			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), null, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));
			_protectedWriteService.Setup(x => x.PrepareCallAttachmentWriteAsync(10, attachment, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));
			_protectedWriteService.Setup(x => x.PrepareCallNoteWriteAsync(10, note, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));

			await _service.SaveCallAsync(call);

			// Call re-saved once for the encrypted fields (2 total), each child re-saved once.
			_callsRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
			_callAttachmentRepo.Verify(x => x.SaveOrUpdateAsync(attachment, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_callNotesRepo.Verify(x => x.SaveOrUpdateAsync(note, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task SaveCallAsync_ProtectedDepartment_UnchangedChildrenAreNotResaved()
		{
			var call = BuildCall();
			var attachment = new CallAttachment { CallAttachmentId = 7, FileName = "rgdp:1:1:AAAA" };
			call.Attachments = new List<CallAttachment> { attachment };

			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), null, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: false));
			_protectedWriteService.Setup(x => x.PrepareCallAttachmentWriteAsync(10, attachment, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: false));

			await _service.SaveCallAsync(call);

			_protectedWriteService.Verify(x => x.PrepareCallAttachmentWriteAsync(10, attachment, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
			_callsRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_callAttachmentRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SaveCallAsync_RedactedSentinel_FetchesStoredRowAndPersistsTheRestore()
		{
			var call = BuildCall();
			call.Name = ProtectedDataEnvelope.RedactionValue;
			var stored = new Call { CallId = 42, DepartmentId = 10, Name = "rgdp:1:1:storedname==" };
			_callsRepo.Setup(x => x.GetByIdAsync(42)).ReturnsAsync(stored);

			// The service passes the stored row through to Prepare; emulate its sentinel restore.
			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), stored, null, null, true, It.IsAny<CancellationToken>()))
				.Callback<int, Call, Call, string, string, bool, CancellationToken>((d, c, e, g, u, w, ct) => c.Name = e.Name)
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: false));

			var result = await _service.SaveCallAsync(call);

			result.Name.Should().Be("rgdp:1:1:storedname==");
			_protectedWriteService.Verify(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), stored, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
			// Initial save + the restore re-persist (Changed=false but the placeholder row must be fixed).
			_callsRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
		}

		[Test]
		public async Task SaveCallAsync_NoSentinel_DoesNotFetchTheStoredRow()
		{
			var call = BuildCall();

			await _service.SaveCallAsync(call);

			_callsRepo.Verify(x => x.GetByIdAsync(It.IsAny<int>()), Times.Never);
			_protectedWriteService.Verify(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), null, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SaveCallAsync_BlockedCallWrite_Throws()
		{
			var call = BuildCall();

			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), null, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> act = async () => await _service.SaveCallAsync(call);

			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*broker_unavailable*");
		}

		[Test]
		public async Task SaveCallAsync_BlockedCascadeAttachmentWrite_Throws()
		{
			var call = BuildCall();
			var attachment = new CallAttachment { CallAttachmentId = 7, FileName = "scene.jpg" };
			call.Attachments = new List<CallAttachment> { attachment };

			_protectedWriteService.Setup(x => x.PrepareCallWriteAsync(10, It.IsAny<Call>(), null, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: false));
			_protectedWriteService.Setup(x => x.PrepareCallAttachmentWriteAsync(10, attachment, null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> act = async () => await _service.SaveCallAsync(call);

			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*call attachment 7*");
		}
	}
}
