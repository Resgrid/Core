using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// In-memory IntakeConversations with the repository's conditional semantics (one open thread per department, transport and
	/// participant; connect/release/claim/close only from the expected state), so service tests exercise the races as the database would.
	/// </summary>
	internal sealed class FakeIntakeConversationsRepository : IIntakeConversationsRepository
	{
		public readonly List<IntakeConversation> Rows = new List<IntakeConversation>();

		private static IntakeConversation Copy(IntakeConversation c) => c == null ? null : (IntakeConversation)typeof(IntakeConversation)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(c, null);

		private IntakeConversation Row(string id, int departmentId) =>
			Rows.FirstOrDefault(r => r.IntakeConversationId == id && r.DepartmentId == departmentId);

		private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		public Task<bool> TryInsertOpenAsync(IntakeConversation conversation, CancellationToken cancellationToken = default)
		{
			if (Rows.Any(r => r.DepartmentId == conversation.DepartmentId && r.ChannelType == conversation.ChannelType && Same(r.ParticipantAddress, conversation.ParticipantAddress) && r.IsOpen))
				return Task.FromResult(false);
			Rows.Add(Copy(conversation));
			return Task.FromResult(true);
		}

		public Task<IntakeConversation> GetOpenForParticipantAsync(int departmentId, int channelType, string participantAddress) =>
			Task.FromResult(Copy(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.ChannelType == channelType && Same(r.ParticipantAddress, participantAddress) && r.IsOpen)));

		public Task<IntakeConversation> GetOpenForReplyAsync(int channelType, string participantAddress, DateTime activeSince) =>
			Task.FromResult(Copy(Rows.Where(r => r.ChannelType == channelType && Same(r.ParticipantAddress, participantAddress) && r.IsOpen && r.LastActivityOn >= activeSince)
				.OrderByDescending(r => r.LastActivityOn).FirstOrDefault()));

		public Task<IntakeConversation> GetLatestForCallAsync(int departmentId, int callId) =>
			Task.FromResult(Copy(Rows.Where(r => r.DepartmentId == departmentId && r.CallId == callId).OrderBy(r => r.State).ThenByDescending(r => r.StartedOn).FirstOrDefault()));

		/// <summary>Runs right after the idle sweep's read, as a text arriving between the read and the close would.</summary>
		public Action AfterIdleRead { get; set; }

		/// <summary>The next closes throw, as a database that fails mid-STOP would.</summary>
		public int FailingCloses { get; set; }

		public Task<List<IntakeConversation>> GetOpenIdleAsync(DateTime idleBefore, int take)
		{
			var rows = Rows.Where(r => r.IsOpen && r.LastActivityOn < idleBefore).OrderBy(r => r.LastActivityOn).Take(take).Select(Copy).ToList();
			AfterIdleRead?.Invoke();
			return Task.FromResult(rows);
		}

		/// <summary>Call ids the test says ended (closed or deleted).</summary>
		public readonly HashSet<int> EndedCalls = new HashSet<int>();

		public Task<List<IntakeConversation>> GetOpenWithEndedCallAsync(int[] openCallStates, int take) =>
			Task.FromResult(Rows.Where(r => r.IsOpen && r.CallId.HasValue && EndedCalls.Contains(r.CallId.Value)).Take(take).Select(Copy).ToList());

		public Task<bool> TryConnectAsync(string conversationId, int departmentId, string userId, string expectedAssignedUserId, DateTime assignedOn,
			CancellationToken cancellationToken = default)
		{
			var row = Row(conversationId, departmentId);
			if (row == null || !row.IsOpen || row.OptedOut || (string.IsNullOrWhiteSpace(expectedAssignedUserId) ? row.AssignedUserId != null : !Same(row.AssignedUserId, expectedAssignedUserId)))
				return Task.FromResult(false);
			row.Handler = (int)IntakeConversationHandlers.Dispatcher;
			row.AssignedUserId = userId;
			row.AssignedOn = assignedOn;
			row.LastActivityOn = assignedOn;
			return Task.FromResult(true);
		}

		public Task<bool> TryReleaseAsync(string conversationId, int departmentId, string userId, int handler, CancellationToken cancellationToken = default)
		{
			var row = Row(conversationId, departmentId);
			if (row == null || !row.IsOpen || !Same(row.AssignedUserId, userId))
				return Task.FromResult(false);
			row.Handler = handler;
			row.AssignedUserId = null;
			row.AssignedOn = null;
			return Task.FromResult(true);
		}

		public Task<bool> TrySetHandlerAsync(string conversationId, int departmentId, int expectedHandler, int handler, bool exhaustAssistantTurns, int maxAssistantTurns,
			CancellationToken cancellationToken = default)
		{
			var row = Row(conversationId, departmentId);
			if (row == null || !row.IsOpen || row.Handler != expectedHandler)
				return Task.FromResult(false);
			row.Handler = handler;
			if (exhaustAssistantTurns)
				row.AssistantTurns = Math.Max(row.AssistantTurns, maxAssistantTurns);
			return Task.FromResult(true);
		}

		public Task<bool> TryClaimAssistantTurnAsync(string conversationId, int departmentId, int expectedTurns, CancellationToken cancellationToken = default)
		{
			var row = Row(conversationId, departmentId);
			if (row == null || !row.IsOpen || row.Handler != (int)IntakeConversationHandlers.Assistant || row.AssistantTurns != expectedTurns)
				return Task.FromResult(false);
			row.AssistantTurns++;
			return Task.FromResult(true);
		}

		public Task<bool> TryCloseAsync(string conversationId, int departmentId, string reason, bool optedOut, DateTime closedOn, CancellationToken cancellationToken = default)
		{
			if (FailingCloses > 0)
			{
				FailingCloses--;
				throw new TimeoutException("database");
			}

			var row = Row(conversationId, departmentId);
			if (row == null || !row.IsOpen)
				return Task.FromResult(false);
			row.State = (int)IntakeConversationStates.Closed;
			row.ClosedOn = closedOn;
			row.CloseReason = reason;
			row.OptedOut = row.OptedOut || optedOut;
			return Task.FromResult(true);
		}

		/// <summary>The message rows the idle close checks for a reply after the cutoff (as the repository's NOT EXISTS does).</summary>
		public FakeIntakeConversationMessagesRepository Messages { get; set; }

		public Task<bool> TryCloseIdleAsync(string conversationId, int departmentId, string reason, DateTime idleBefore, DateTime closedOn, CancellationToken cancellationToken = default)
		{
			var row = Row(conversationId, departmentId);
			if (row == null || !row.IsOpen || row.LastActivityOn >= idleBefore)
				return Task.FromResult(false);
			if (Messages != null && Messages.Rows.Any(m => m.IntakeConversationId == conversationId && m.Direction == (int)IntakeConversationMessageDirections.Inbound && m.CreatedOn >= idleBefore))
				return Task.FromResult(false);
			row.State = (int)IntakeConversationStates.Closed;
			row.ClosedOn = closedOn;
			row.CloseReason = reason;
			return Task.FromResult(true);
		}

		/// <summary>The next activity writes throw, as a database that fails after the texter's note was saved would.</summary>
		public int FailingTouches { get; set; }

		public Task TouchAsync(string conversationId, int departmentId, DateTime lastActivityOn, DateTime? lastInboundOn, CancellationToken cancellationToken = default)
		{
			if (FailingTouches > 0)
			{
				FailingTouches--;
				throw new TimeoutException("database");
			}

			var row = Row(conversationId, departmentId);
			if (row != null)
			{
				if (row.LastActivityOn < lastActivityOn) row.LastActivityOn = lastActivityOn;
				if (lastInboundOn.HasValue && (!row.LastInboundOn.HasValue || row.LastInboundOn < lastInboundOn)) row.LastInboundOn = lastInboundOn;
			}
			return Task.CompletedTask;
		}

		public Task<bool> SetDeskItemAsync(string conversationId, int departmentId, string deskItemId, CancellationToken cancellationToken = default)
		{
			var row = Row(conversationId, departmentId);
			if (row == null)
				return Task.FromResult(false);
			row.DeskItemId = deskItemId;
			return Task.FromResult(true);
		}

		/// <summary>The next read by id throws, as a database that fails just before a send would.</summary>
		public bool FailNextRead { get; set; }

		public Task<IntakeConversation> GetByIdAsync(object id)
		{
			if (FailNextRead)
			{
				FailNextRead = false;
				throw new TimeoutException("database");
			}

			return Task.FromResult(Copy(Rows.FirstOrDefault(r => r.IntakeConversationId == (string)id)));
		}

		public Task<IEnumerable<IntakeConversation>> GetAllAsync() => throw new NotSupportedException();
		public Task<IEnumerable<IntakeConversation>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException();
		public Task<IntakeConversation> InsertAsync(IntakeConversation entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException("The service never writes a whole row.");
		public Task<IntakeConversation> UpdateAsync(IntakeConversation entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException("The service never writes a whole row.");
		public Task<bool> DeleteAsync(IntakeConversation entity, CancellationToken cancellationToken) => throw new NotSupportedException();
		public Task<IntakeConversation> SaveOrUpdateAsync(IntakeConversation entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException("The service never writes a whole row.");
		public Task<IEnumerable<IntakeConversation>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();
		public Task<bool> DeleteMultipleAsync(IntakeConversation entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
	}

	/// <summary>In-memory IntakeConversationMessages: an inbound provider id is one row per conversation.</summary>
	internal sealed class FakeIntakeConversationMessagesRepository : IIntakeConversationMessagesRepository
	{
		public readonly List<IntakeConversationMessage> Rows = new List<IntakeConversationMessage>();

		public Task<bool> TryInsertInboundAsync(IntakeConversationMessage message, CancellationToken cancellationToken = default)
		{
			if (!string.IsNullOrWhiteSpace(message.ProviderMessageId) && Rows.Any(r => r.IntakeConversationId == message.IntakeConversationId &&
				r.Direction == (int)IntakeConversationMessageDirections.Inbound && string.Equals(r.ProviderMessageId, message.ProviderMessageId, StringComparison.OrdinalIgnoreCase)))
				return Task.FromResult(false);
			Rows.Add(message);
			return Task.FromResult(true);
		}

		public Task InsertMessageAsync(IntakeConversationMessage message, CancellationToken cancellationToken = default)
		{
			Rows.Add(message);
			return Task.CompletedTask;
		}

		public Task<List<IntakeConversationMessage>> GetForConversationAsync(int departmentId, string conversationId) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.IntakeConversationId == conversationId).OrderBy(r => r.CreatedOn).ToList());

		public Task<bool> UpdateDeliveryAsync(string messageId, int departmentId, int status, string providerMessageId, string error, DateTime? sentOn,
			CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.IntakeConversationMessageId == messageId && r.DepartmentId == departmentId);
			if (row == null)
				return Task.FromResult(false);
			row.Status = status;
			row.ProviderMessageId = providerMessageId;
			row.Error = error;
			row.SentOn = sentOn;
			return Task.FromResult(true);
		}

		public Task<bool> SetCallNoteAsync(string messageId, int departmentId, int callNoteId, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.IntakeConversationMessageId == messageId && r.DepartmentId == departmentId);
			if (row == null)
				return Task.FromResult(false);
			row.CallNoteId = callNoteId;
			return Task.FromResult(true);
		}

		public Task<IntakeConversationMessage> GetByIdAsync(object id) => Task.FromResult(Rows.FirstOrDefault(r => r.IntakeConversationMessageId == (string)id));
		public Task<IEnumerable<IntakeConversationMessage>> GetAllAsync() => throw new NotSupportedException();
		public Task<IEnumerable<IntakeConversationMessage>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException();
		public Task<IntakeConversationMessage> InsertAsync(IntakeConversationMessage entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
		public Task<IntakeConversationMessage> UpdateAsync(IntakeConversationMessage entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
		public Task<bool> DeleteAsync(IntakeConversationMessage entity, CancellationToken cancellationToken) => throw new NotSupportedException();
		public Task<IntakeConversationMessage> SaveOrUpdateAsync(IntakeConversationMessage entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
		public Task<IEnumerable<IntakeConversationMessage>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();
		public Task<bool> DeleteMultipleAsync(IntakeConversationMessage entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
	}

	/// <summary>Records every router callback; can throw to prove a router never fails an action.</summary>
	internal sealed class RecordingRouter : Resgrid.Model.Services.IIntakeConversationRouter
	{
		public readonly List<string> Calls = new List<string>();
		public bool Throw { get; set; }
		public IntakeConversationRouting Routing { get; set; } = IntakeConversationRouting.NotRouted;
		public Func<IntakeConversation, Task> OnRoute { get; set; }

		private void Record(string call)
		{
			Calls.Add(call);
			if (Throw)
				throw new InvalidOperationException("router down");
		}

		public async Task<IntakeConversationRouting> RouteNewConversationAsync(IntakeConversation conversation, Call call, CancellationToken cancellationToken)
		{
			Record("Route:" + conversation.IntakeConversationId);
			if (OnRoute != null)
				await OnRoute(conversation);
			return Routing;
		}

		public Task OnParticipantMessageAsync(IntakeConversation conversation, IntakeConversationMessage message, CancellationToken cancellationToken)
		{
			Record("Message:" + message.IntakeConversationMessageId);
			return Task.CompletedTask;
		}

		public Task OnConversationClosedAsync(IntakeConversation conversation, CancellationToken cancellationToken)
		{
			Record("Closed:" + conversation.CloseReason);
			return Task.CompletedTask;
		}

		public Task OnDispatcherConnectedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken)
		{
			Record("Connected:" + userId);
			return Task.CompletedTask;
		}

		public Task OnDispatcherReleasedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken)
		{
			Record("Released:" + userId);
			return Task.CompletedTask;
		}
	}

	/// <summary>A clock that moves one second per read, so rows written in one operation keep their order.</summary>
	internal sealed class SteppingClock : TimeProvider
	{
		public DateTime Now = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

		public override DateTimeOffset GetUtcNow()
		{
			Now = Now.AddSeconds(1);
			return new DateTimeOffset(Now);
		}
	}
}
