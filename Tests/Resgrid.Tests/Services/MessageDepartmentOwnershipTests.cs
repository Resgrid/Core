using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Messages and their recipients gained their own DepartmentId in M0137, because the ADP
	/// envelope AAD binds the department and these rows previously had no department at all — only
	/// user ids, and a user can move between departments.
	///
	/// M0137 backfills the history. These pin the other half: every row written from now on must
	/// carry the owner, or the column silently refills with unattributable rows and the tables can
	/// never be bound to the catalog.
	/// </summary>
	[TestFixture]
	public class MessageDepartmentOwnershipTests
	{
		private const int DeptId = 42;

		private Mock<IMessageRepository> _messageRepository;
		private Mock<IMessageRecipientRepository> _recipientRepository;
		private Mock<IQueueService> _queueService;
		private Mock<IUserProfileService> _userProfileService;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private MessageService _service;

		[SetUp]
		public void SetUp()
		{
			_messageRepository = new Mock<IMessageRepository>();
			_messageRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Message m, CancellationToken _, bool __) => m);

			_recipientRepository = new Mock<IMessageRecipientRepository>();
			_recipientRepository.Setup(x => x.SaveOrUpdateAsync(It.IsAny<MessageRecipient>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((MessageRecipient r, CancellationToken _, bool __) => r);

			_queueService = new Mock<IQueueService>();
			_queueService.Setup(x => x.EnqueueMessageBroadcastAsync(It.IsAny<MessageQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			_userProfileService = new Mock<IUserProfileService>();
			_userProfileService.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>()))
				.ReturnsAsync(new List<UserProfile>());

			_protectedWriteService = new Mock<IProtectedWriteService>();
			_protectedWriteService.Setup(x => x.PrepareMessageWriteAsync(It.IsAny<int>(), It.IsAny<Message>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_protectedWriteService.Setup(x => x.PrepareMessageRecipientWriteAsync(It.IsAny<int>(), It.IsAny<MessageRecipient>(),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_service = new MessageService(_messageRepository.Object, null, null, _queueService.Object,
				_userProfileService.Object, _recipientRepository.Object,
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object));
		}

		[Test]
		public async Task Sending_stamps_the_department_on_a_message_that_lacks_one()
		{
			var message = new Message { MessageId = 7, Subject = "s", Body = "b", SentOn = DateTime.UtcNow };

			await _service.SendMessageAsync(message, "sender", DeptId, broadcastSingle: false);

			message.DepartmentId.Should().Be(DeptId);

			// The row was already saved by the caller, so the owner has to be persisted, not just
			// set on the instance being queued.
			_messageRepository.Verify(x => x.SaveOrUpdateAsync(It.Is<Message>(m => m.DepartmentId == DeptId),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task Sending_never_relocates_a_message_that_already_has_an_owner()
		{
			var message = new Message { MessageId = 7, DepartmentId = 9, Subject = "s", Body = "b", SentOn = DateTime.UtcNow };

			await _service.SendMessageAsync(message, "sender", DeptId, broadcastSingle: false);

			message.DepartmentId.Should().Be(9, "the owner is resolved once and frozen - envelopes are bound to it");
			_messageRepository.Verify(x => x.SaveOrUpdateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>(),
				It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Broadcast_copies_each_carry_the_department()
		{
			var message = new Message
			{
				Subject = "s",
				Body = "b",
				SentOn = DateTime.UtcNow,
				SendingUserId = "sender-1",
				Recipients = "user-1|user-2"
			};

			await _service.SendMessageAsync(message, "sender", DeptId);

			_messageRepository.Verify(x => x.SaveOrUpdateAsync(It.Is<Message>(m => m.DepartmentId == DeptId),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
		}

		[Test]
		public async Task Saving_a_message_pushes_the_owner_onto_its_cascade_saved_recipients()
		{
			// RepositoryBase cascade-saves the collection, so the children are written by the same
			// call that writes the parent - they have to be stamped before it, not after.
			var message = new Message { DepartmentId = DeptId, Subject = "s", Body = "b", SentOn = DateTime.UtcNow };
			message.AddRecipient("user-1");
			message.AddRecipient("user-2");

			await _service.SaveMessageAsync(message);

			message.MessageRecipients.Should().OnlyContain(r => r.DepartmentId == DeptId);
		}

		[Test]
		public async Task A_recipient_saved_on_its_own_inherits_the_owner_from_its_message()
		{
			_messageRepository.Setup(x => x.GetMessagesByMessageIdAsync(7))
				.ReturnsAsync(new Message { MessageId = 7, DepartmentId = DeptId });

			var recipient = new MessageRecipient { MessageRecipientId = 3, MessageId = 7, UserId = "user-1" };

			await _service.SaveMessageRecipientAsync(recipient);

			recipient.DepartmentId.Should().Be(DeptId);
		}

		[Test]
		public async Task A_recipient_that_already_has_an_owner_does_not_read_its_parent()
		{
			var recipient = new MessageRecipient
			{
				MessageRecipientId = 3,
				MessageId = 7,
				UserId = "user-1",
				DepartmentId = DeptId
			};

			await _service.SaveMessageRecipientAsync(recipient);

			_messageRepository.Verify(x => x.GetMessagesByMessageIdAsync(It.IsAny<int>()), Times.Never,
				"rows written after M0137 already carry the owner; the lookup is for the historic tail only");
		}
	}
}
