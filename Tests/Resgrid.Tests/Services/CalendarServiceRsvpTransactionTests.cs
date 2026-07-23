using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CalendarServiceRsvpTransactionTests
	{
		private Mock<ICalendarItemAttendeeRepository> _attendeeRepository;
		private Mock<IMessageRecipientRepository> _messageRecipientRepository;
		private Mock<IUnitOfWork> _unitOfWork;
		private CalendarService _service;

		[SetUp]
		public void SetUp()
		{
			_attendeeRepository = new Mock<ICalendarItemAttendeeRepository>();
			_messageRecipientRepository = new Mock<IMessageRecipientRepository>();
			_unitOfWork = new Mock<IUnitOfWork>();
			_service = new CalendarService(
				null,
				null,
				_attendeeRepository.Object,
				null,
				null,
				null,
				null,
				null,
				null,
				null,
				_messageRecipientRepository.Object,
				_unitOfWork.Object);
		}

		[Test]
		public async Task SignupForEventAndUpdateMessageRecipientAsync_CommitsBothWrites()
		{
			var recipient = new MessageRecipient { MessageRecipientId = 10, MessageId = 20, UserId = "user-1" };
			_attendeeRepository.Setup(x => x.GetCalendarItemAttendeeByUserAsync(30, "user-1"))
				.ReturnsAsync((CalendarItemAttendee)null);
			_attendeeRepository.Setup(x => x.SaveOrUpdateAsync(
					It.IsAny<CalendarItemAttendee>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemAttendee attendee, CancellationToken cancellationToken, bool firstLevelOnly) => attendee);
			_messageRecipientRepository.Setup(x => x.SaveOrUpdateAsync(
					recipient, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync(recipient);

			var result = await _service.SignupForEventAndUpdateMessageRecipientAsync(
				30, "user-1", "Yes", (int)CalendarItemAttendeeTypes.RSVP, recipient);

			result.CalendarItemId.Should().Be(30);
			result.UserId.Should().Be("user-1");
			recipient.Response.Should().Be("Yes");
			recipient.ReadOn.Should().NotBeNull();
			_unitOfWork.Verify(x => x.CreateOrGetConnection(), Times.Once);
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Once);
			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Never);
		}

		[Test]
		public async Task SignupForEventAndUpdateMessageRecipientAsync_RollsBackWhenRecipientWriteFails()
		{
			var recipient = new MessageRecipient { MessageRecipientId = 10, MessageId = 20, UserId = "user-1" };
			_attendeeRepository.Setup(x => x.GetCalendarItemAttendeeByUserAsync(30, "user-1"))
				.ReturnsAsync((CalendarItemAttendee)null);
			_attendeeRepository.Setup(x => x.SaveOrUpdateAsync(
					It.IsAny<CalendarItemAttendee>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CalendarItemAttendee attendee, CancellationToken cancellationToken, bool firstLevelOnly) => attendee);
			_messageRecipientRepository.Setup(x => x.SaveOrUpdateAsync(
					recipient, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("Recipient write failed."));

			Func<Task> act = () => _service.SignupForEventAndUpdateMessageRecipientAsync(
				30, "user-1", "Yes", (int)CalendarItemAttendeeTypes.RSVP, recipient);

			await act.Should().ThrowAsync<InvalidOperationException>();
			_unitOfWork.Verify(x => x.CommitChanges(), Times.Never);
			_unitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
		}
	}
}
