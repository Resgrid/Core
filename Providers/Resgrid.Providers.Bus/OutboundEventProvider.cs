using Microsoft.Azure.NotificationHubs.Messaging;
using Microsoft.Azure.ServiceBus;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Providers.Bus
{
	public class OutboundEventProvider : IOutboundEventProvider
	{
		private readonly IEventAggregator _eventAggregator;
		private static ISignalrProvider _signalrProvider;
		private static IOutboundQueueProvider _outboundQueueProvider;
		private static RabbitTopicProvider _rabbitTopicProvider;

		public OutboundEventProvider(IEventAggregator eventAggregator, IOutboundQueueProvider outboundQueueProvider, ISignalrProvider signalrProvider)
		{
			_eventAggregator = eventAggregator;
			_outboundQueueProvider = outboundQueueProvider;
			_signalrProvider = signalrProvider;

			_rabbitTopicProvider = new RabbitTopicProvider();

			_eventAggregator.AddListener(unitStatusHandler);
			_eventAggregator.AddListener(unitTypeGroupAvailabilityHandler);
			_eventAggregator.AddListener(unitTypeDepartmentAvailabilityHandler);
			_eventAggregator.AddListener(userStaffingHandler);
			_eventAggregator.AddListener(userRoleGroupAvailabilityHandler);
			_eventAggregator.AddListener(userRoleDepartmentAvailabilityHandler);
			_eventAggregator.AddListener(personnelStatusChangedHandler);
			_eventAggregator.AddListener(userCreatedHandler);
			_eventAggregator.AddListener(userAssignedToGroupHandler);
			_eventAggregator.AddListener(calendarEventUpcomingHandler);
			_eventAggregator.AddListener(calendarEventAddedHandler);
			_eventAggregator.AddListener(calendarEventUpdatedHandler);
			_eventAggregator.AddListener(documentAddedHandler);
			_eventAggregator.AddListener(noteAddedHandler);
			_eventAggregator.AddListener(unitAddedHandler);
			_eventAggregator.AddListener(logAddedHandler);
			_eventAggregator.AddListener(resourceOrderAddedHandler);
			_eventAggregator.AddListener(workerHeartbeatHandler);
			_eventAggregator.AddListener(dListCheckHandler);
			_eventAggregator.AddListener(shiftTradeRequestedHandler);
			_eventAggregator.AddListener(shiftTradeRejectedEventHandler);
			_eventAggregator.AddListener(shiftTradeFilledEventHandler);
			_eventAggregator.AddListener(shiftCreatedEventHandler);
			_eventAggregator.AddListener(shiftUpdatedEventHandler);
			_eventAggregator.AddListener(shiftDaysAddedEventHandler);

			// Topics (SignalR Integration)
			_eventAggregator.AddListener(personnelStatusChangedTopicHandler);
			_eventAggregator.AddListener(personnelStaffingChangedTopicHandler);
			_eventAggregator.AddListener(unitStatusTopicHandler);
			_eventAggregator.AddListener(callAddedTopicHandler);
		}

		#region Private Helpers
		private static Microsoft.Azure.ServiceBus.Message CreateMessage(Guid messageId, string messageBody)
		{
			return new Microsoft.Azure.ServiceBus.Message(Encoding.UTF8.GetBytes(messageBody)) { MessageId = messageId.ToString() };
		}

		private static void HandleTransientErrors(MessagingException e)
		{
			//If transient error/exception, let's back-off for 2 seconds and retry
			Thread.Sleep(2000);
		}
		#endregion Private Helpers

		public Action<UnitStatusEvent> unitStatusHandler = async delegate(UnitStatusEvent message)
		{
			var nqi = new NotificationItem();

			int previousState = 0;
			if (message.PreviousStatus != null)
				previousState = message.PreviousStatus.UnitStateId;

			nqi.Type = (int)EventTypes.UnitStatusChanged;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Status.UnitStateId;
			nqi.PreviousStateId = previousState;
			nqi.Value = message.Status.State.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
			await _signalrProvider.UnitStatusUpdated(message.DepartmentId, message.Status);
		};


		//public class UnitStatusHandler : IListener<UnitStatusEvent>
		//{
		//	public async Task<bool> Handle(UnitStatusEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousState = 0;
		//		if (message.PreviousStatus != null)
		//			previousState = message.PreviousStatus.UnitStateId;

		//		nqi.Type = (int)EventTypes.UnitStatusChanged;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Status.UnitStateId;
		//		nqi.PreviousStateId = previousState;
		//		nqi.Value = message.Status.State.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);
		//		await _signalrProvider.UnitStatusUpdated(message.DepartmentId, message.Status);

		//		return true;
		//	}
		//}

		public Action<UnitStatusEvent> unitTypeGroupAvailabilityHandler = async delegate(UnitStatusEvent message)
		{
			var nqi = new NotificationItem();

			int previousState = 0;
			if (message.PreviousStatus != null)
				previousState = message.PreviousStatus.UnitStateId;

			nqi.Type = (int)EventTypes.UnitTypesInGroupAvailabilityAlert;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Status.UnitStateId;
			nqi.PreviousStateId = previousState;
			nqi.Value = message.Status.State.ToString();
			nqi.UnitId = message.Status.UnitId;

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UnitTypeGroupAvailabilityHandler : IListener<UnitStatusEvent>
		//{
		//	public async Task<bool> Handle(UnitStatusEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousState = 0;
		//		if (message.PreviousStatus != null)
		//			previousState = message.PreviousStatus.UnitStateId;

		//		nqi.Type = (int)EventTypes.UnitTypesInGroupAvailabilityAlert;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Status.UnitStateId;
		//		nqi.PreviousStateId = previousState;
		//		nqi.Value = message.Status.State.ToString();
		//		nqi.UnitId = message.Status.UnitId;

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<UnitStatusEvent> unitTypeDepartmentAvailabilityHandler = async delegate(UnitStatusEvent message)
		{
			var nqi = new NotificationItem();

			int previousState = 0;
			if (message.PreviousStatus != null)
				previousState = message.PreviousStatus.UnitStateId;

			nqi.Type = (int)EventTypes.UnitTypesInDepartmentAvailabilityAlert;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Status.UnitStateId;
			nqi.PreviousStateId = previousState;
			nqi.Value = message.Status.State.ToString();
			nqi.UnitId = message.Status.UnitId;

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UnitTypeDepartmentAvailabilityHandler : IListener<UnitStatusEvent>
		//{
		//	public async Task<bool> Handle(UnitStatusEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousState = 0;
		//		if (message.PreviousStatus != null)
		//			previousState = message.PreviousStatus.UnitStateId;

		//		nqi.Type = (int)EventTypes.UnitTypesInDepartmentAvailabilityAlert;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Status.UnitStateId;
		//		nqi.PreviousStateId = previousState;
		//		nqi.Value = message.Status.State.ToString();
		//		nqi.UnitId = message.Status.UnitId;

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<UserStaffingEvent> userStaffingHandler = async delegate(UserStaffingEvent message)
		{
			var nqi = new NotificationItem();

			int previousStaffing = 0;
			if (message.PreviousStaffing != null)
				previousStaffing = message.PreviousStaffing.UserStateId;

			nqi.Type = (int)EventTypes.PersonnelStaffingChanged;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Staffing.UserStateId;
			nqi.PreviousStateId = previousStaffing;
			nqi.Value = message.Staffing.State.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UserStaffingHandler : IListener<UserStaffingEvent>
		//{
		//	public async Task<bool> Handle(UserStaffingEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousStaffing = 0;
		//		if (message.PreviousStaffing != null)
		//			previousStaffing = message.PreviousStaffing.UserStateId;

		//		nqi.Type = (int)EventTypes.PersonnelStaffingChanged;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Staffing.UserStateId;
		//		nqi.PreviousStateId = previousStaffing;
		//		nqi.Value = message.Staffing.State.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<UserStaffingEvent> userRoleGroupAvailabilityHandler = async delegate(UserStaffingEvent message)
		{
			var nqi = new NotificationItem();

			int previousStaffing = 0;
			if (message.PreviousStaffing != null)
				previousStaffing = message.PreviousStaffing.UserStateId;

			nqi.Type = (int)EventTypes.RolesInGroupAvailabilityAlert;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Staffing.UserStateId;
			nqi.PreviousStateId = previousStaffing;
			//nqi.Value = message.Staffing.State.ToString();
			nqi.Value = message.Staffing.UserStateId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UserRoleGroupAvailabilityHandler : IListener<UserStaffingEvent>
		//{
		//	public async Task<bool> Handle(UserStaffingEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousStaffing = 0;
		//		if (message.PreviousStaffing != null)
		//			previousStaffing = message.PreviousStaffing.UserStateId;

		//		nqi.Type = (int)EventTypes.RolesInGroupAvailabilityAlert;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Staffing.UserStateId;
		//		nqi.PreviousStateId = previousStaffing;
		//		//nqi.Value = message.Staffing.State.ToString();
		//		nqi.Value = message.Staffing.UserStateId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<UserStaffingEvent> userRoleDepartmentAvailabilityHandler = async delegate(UserStaffingEvent message)
		{
			var nqi = new NotificationItem();

			int previousStaffing = 0;
			if (message.PreviousStaffing != null)
				previousStaffing = message.PreviousStaffing.UserStateId;

			nqi.Type = (int)EventTypes.RolesInDepartmentAvailabilityAlert;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Staffing.UserStateId;
			nqi.PreviousStateId = previousStaffing;
			//nqi.Value = message.Staffing.State.ToString();
			nqi.Value = message.Staffing.UserStateId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
			await _signalrProvider.PersonnelStaffingUpdated(message.Staffing.DepartmentId, message.Staffing);
		};

		//public class UserRoleDepartmentAvailabilityHandler : IListener<UserStaffingEvent>
		//{
		//	public async Task<bool> Handle(UserStaffingEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousStaffing = 0;
		//		if (message.PreviousStaffing != null)
		//			previousStaffing = message.PreviousStaffing.UserStateId;

		//		nqi.Type = (int)EventTypes.RolesInDepartmentAvailabilityAlert;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Staffing.UserStateId;
		//		nqi.PreviousStateId = previousStaffing;
		//		//nqi.Value = message.Staffing.State.ToString();
		//		nqi.Value = message.Staffing.UserStateId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);
		//		await _signalrProvider.PersonnelStaffingUpdated(message.Staffing.DepartmentId, message.Staffing);

		//		return true;
		//	}
		//}

		public Action<UserStatusEvent> personnelStatusChangedHandler = async delegate(UserStatusEvent message)
		{
			var nqi = new NotificationItem();

			int previousStatus = 0;
			if (message.PreviousStatus != null)
				previousStatus = message.PreviousStatus.ActionLogId;

			nqi.Type = (int)EventTypes.PersonnelStatusChanged;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Status.ActionLogId;
			nqi.PreviousStateId = previousStatus;
			nqi.Value = message.Status.ActionTypeId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
			await _signalrProvider.PersonnelStatusUpdated(message.Status.DepartmentId, message.Status);
		};

		//public class PersonnelStatusChangedHandler : IListener<UserStatusEvent>
		//{
		//	public async Task<bool> Handle(UserStatusEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousStatus = 0;
		//		if (message.PreviousStatus != null)
		//			previousStatus = message.PreviousStatus.ActionLogId;

		//		nqi.Type = (int)EventTypes.PersonnelStatusChanged;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.StateId = message.Status.ActionLogId;
		//		nqi.PreviousStateId = previousStatus;
		//		nqi.Value = message.Status.ActionTypeId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);
		//		await _signalrProvider.PersonnelStatusUpdated(message.Status.DepartmentId, message.Status);

		//		return true;
		//	}
		//}

		public Action<UserCreatedEvent> userCreatedHandler = async delegate(UserCreatedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.UserCreated;
			nqi.DepartmentId = message.DepartmentId;
			nqi.UserId = message.User.UserId;
			nqi.Value = message.User.UserId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UserCreatedHandler : IListener<UserCreatedEvent>
		//{
		//	public async Task<bool> Handle(UserCreatedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.UserCreated;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.UserId = message.User.UserId;
		//		nqi.Value = message.User.UserId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<UserAssignedToGroupEvent> userAssignedToGroupHandler = async delegate(UserAssignedToGroupEvent message)
		{
			var nqi = new NotificationItem();

			int previousGroup = 0;
			if (message.PreviousGroup != null)
				previousGroup = message.PreviousGroup.DepartmentGroupId;

			nqi.Type = (int)EventTypes.UserAssignedToGroup;
			nqi.DepartmentId = message.DepartmentId;
			nqi.UserId = message.UserId;
			nqi.GroupId = message.Group.DepartmentGroupId;
			nqi.PreviousGroupId = previousGroup;
			nqi.Value = message.UserId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UserAssignedToGroupHandler : IListener<UserAssignedToGroupEvent>
		//{
		//	public async Task<bool> Handle(UserAssignedToGroupEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		int previousGroup = 0;
		//		if (message.PreviousGroup != null)
		//			previousGroup = message.PreviousGroup.DepartmentGroupId;

		//		nqi.Type = (int)EventTypes.UserAssignedToGroup;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.UserId = message.UserId;
		//		nqi.GroupId = message.Group.DepartmentGroupId;
		//		nqi.PreviousGroupId = previousGroup;
		//		nqi.Value = message.UserId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<CalendarEventUpcomingEvent> calendarEventUpcomingHandler = async delegate(CalendarEventUpcomingEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.CalendarEventUpcoming;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Item.CalendarItemId;
			nqi.Value = message.Item.CalendarItemId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class CalendarEventUpcomingHandler : IListener<CalendarEventUpcomingEvent>
		//{
		//	public async Task<bool> Handle(CalendarEventUpcomingEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.CalendarEventUpcoming;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.ItemId = message.Item.CalendarItemId;
		//		nqi.Value = message.Item.CalendarItemId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<CalendarEventAddedEvent> calendarEventAddedHandler = async delegate(CalendarEventAddedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.CalendarEventAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Item.CalendarItemId;
			nqi.Value = message.Item.CalendarItemId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class CalendarEventAddedHandler : IListener<CalendarEventAddedEvent>
		//{
		//	public async Task<bool> Handle(CalendarEventAddedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.CalendarEventAdded;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.ItemId = message.Item.CalendarItemId;
		//		nqi.Value = message.Item.CalendarItemId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<CalendarEventUpdatedEvent> calendarEventUpdatedHandler = async delegate(CalendarEventUpdatedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.CalendarEventUpdated;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Item.CalendarItemId;
			nqi.Value = message.Item.CalendarItemId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class CalendarEventUpdatedHandler : IListener<CalendarEventUpdatedEvent>
		//{
		//	public async Task<bool> Handle(CalendarEventUpdatedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.CalendarEventUpdated;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.ItemId = message.Item.CalendarItemId;
		//		nqi.Value = message.Item.CalendarItemId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<DocumentAddedEvent> documentAddedHandler = async delegate(DocumentAddedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.DocumentAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Document.DocumentId;
			nqi.Value = message.Document.DocumentId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class DocumentAddedHandler : IListener<DocumentAddedEvent>
		//{
		//	public async Task<bool> Handle(DocumentAddedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.DocumentAdded;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.ItemId = message.Document.DocumentId;
		//		nqi.Value = message.Document.DocumentId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<NoteAddedEvent> noteAddedHandler = async delegate(NoteAddedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.NoteAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Note.NoteId;
			nqi.Value = message.Note.NoteId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class NoteAddedHandler : IListener<NoteAddedEvent>
		//{
		//	public async Task<bool> Handle(NoteAddedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.NoteAdded;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.ItemId = message.Note.NoteId;
		//		nqi.Value = message.Note.NoteId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<UnitAddedEvent> unitAddedHandler = async delegate(UnitAddedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.UnitAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.UnitId = message.Unit.UnitId;
			nqi.Value = message.Unit.UnitId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class UnitAddedHandler : IListener<UnitAddedEvent>
		//{
		//	public async Task<bool> Handle(UnitAddedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.UnitAdded;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.UnitId = message.Unit.UnitId;
		//		nqi.Value = message.Unit.UnitId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<LogAddedEvent> logAddedHandler = async delegate(LogAddedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.LogAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Log.LogId;
			nqi.Value = message.Log.LogId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class LogAddedHandler : IListener<LogAddedEvent>
		//{
		//	public async Task<bool> Handle(LogAddedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.LogAdded;
		//		nqi.DepartmentId = message.DepartmentId;
		//		nqi.ItemId = message.Log.LogId;
		//		nqi.Value = message.Log.LogId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<ResourceOrderAddedEvent> resourceOrderAddedHandler = async delegate(ResourceOrderAddedEvent message)
		{
			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.ResourceOrderAdded;
			nqi.DepartmentId = message.Order.DepartmentId;
			nqi.ItemId = message.Order.ResourceOrderId;
			nqi.Value = message.Order.ResourceOrderId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		//public class ResourceOrderAddedHandler : IListener<ResourceOrderAddedEvent>
		//{
		//	public async Task<bool> Handle(ResourceOrderAddedEvent message)
		//	{
		//		var nqi = new NotificationItem();

		//		nqi.Type = (int)EventTypes.ResourceOrderAdded;
		//		nqi.DepartmentId = message.Order.DepartmentId;
		//		nqi.ItemId = message.Order.ResourceOrderId;
		//		nqi.Value = message.Order.ResourceOrderId.ToString();

		//		await _outboundQueueProvider.EnqueueNotification(nqi);

		//		return true;
		//	}
		//}

		public Action<ShiftTradeRequestedEvent> shiftTradeRequestedHandler = async delegate(ShiftTradeRequestedEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.ShiftSignupTradeId = message.ShiftSignupTradeId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.Type = (int)ShiftQueueTypes.TradeRequested;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftTradeRequestedHandler : IListener<ShiftTradeRequestedEvent>
		//{
		//	public async Task<bool> Handle(ShiftTradeRequestedEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.ShiftSignupTradeId = message.ShiftSignupTradeId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.Type = (int)ShiftQueueTypes.TradeRequested;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}

		public Action<ShiftTradeRejectedEvent> shiftTradeRejectedEventHandler = async delegate(ShiftTradeRejectedEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.ShiftSignupTradeId = message.ShiftSignupTradeId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.Type = (int)ShiftQueueTypes.TradeRejected;
			item.SourceUserId = message.UserId;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftTradeRejectedEventHandler : IListener<ShiftTradeRejectedEvent>
		//{
		//	public async Task<bool> Handle(ShiftTradeRejectedEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.ShiftSignupTradeId = message.ShiftSignupTradeId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.Type = (int)ShiftQueueTypes.TradeRejected;
		//		item.SourceUserId = message.UserId;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}

		public Action<ShiftTradeProposedEvent> shiftTradeProposedEventHandler = async delegate(ShiftTradeProposedEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.ShiftSignupTradeId = message.ShiftSignupTradeId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.Type = (int)ShiftQueueTypes.TradeProposed;
			item.SourceUserId = message.UserId;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftTradeProposedEventHandler : IListener<ShiftTradeProposedEvent>
		//{
		//	public async Task<bool> Handle(ShiftTradeProposedEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.ShiftSignupTradeId = message.ShiftSignupTradeId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.Type = (int)ShiftQueueTypes.TradeProposed;
		//		item.SourceUserId = message.UserId;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}

		public Action<ShiftTradeFilledEvent> shiftTradeFilledEventHandler = async delegate(ShiftTradeFilledEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.ShiftSignupTradeId = message.ShiftSignupTradeId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.Type = (int)ShiftQueueTypes.TradeFilled;
			item.SourceUserId = message.UserId;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftTradeFilledEventHandler : IListener<ShiftTradeFilledEvent>
		//{
		//	public async Task<bool> Handle(ShiftTradeFilledEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.ShiftSignupTradeId = message.ShiftSignupTradeId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.Type = (int)ShiftQueueTypes.TradeFilled;
		//		item.SourceUserId = message.UserId;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}

		public Action<ShiftCreatedEvent> shiftCreatedEventHandler = async delegate(ShiftCreatedEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.ShiftId = message.Item.ShiftId;
			item.Type = (int)ShiftQueueTypes.ShiftCreated;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftCreatedEventHandler : IListener<ShiftCreatedEvent>
		//{
		//	public async Task<bool> Handle(ShiftCreatedEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.ShiftId = message.Item.ShiftId;
		//		item.Type = (int)ShiftQueueTypes.ShiftCreated;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}

		public Action<ShiftUpdatedEvent> shiftUpdatedEventHandler = async delegate(ShiftUpdatedEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.ShiftId = message.Item.ShiftId;
			item.Type = (int)ShiftQueueTypes.ShiftUpdated;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftUpdatedEventHandler : IListener<ShiftUpdatedEvent>
		//{
		//	public async Task<bool> Handle(ShiftUpdatedEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.ShiftId = message.Item.ShiftId;
		//		item.Type = (int)ShiftQueueTypes.ShiftUpdated;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}

		public Action<ShiftDaysAddedEvent> shiftDaysAddedEventHandler = async delegate(ShiftDaysAddedEvent message)
		{
			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.ShiftId = message.Item.ShiftId;
			item.Type = (int)ShiftQueueTypes.ShiftDaysAdded;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		//public class ShiftDaysAddedEventHandler : IListener<ShiftDaysAddedEvent>
		//{
		//	public async Task<bool> Handle(ShiftDaysAddedEvent message)
		//	{
		//		if (_outboundQueueProvider == null)
		//			_outboundQueueProvider = new OutboundQueueProvider();

		//		var item = new ShiftQueueItem();
		//		item.DepartmentId = message.DepartmentId;
		//		item.DepartmentNumber = message.DepartmentNumber;
		//		item.ShiftId = message.Item.ShiftId;
		//		item.Type = (int)ShiftQueueTypes.ShiftDaysAdded;

		//		await _outboundQueueProvider.EnqueueShiftNotification(item);

		//		return true;
		//	}
		//}


		#region Topic Based Events
		public Action<DepartmentSettingsChangedEvent> departmentSettingsChangedHandler = async delegate(DepartmentSettingsChangedEvent message)
		{
			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureServiceBusConnectionString, Topics.GenericTopic);

			var messageBus = CreateMessage(Guid.NewGuid(), new { DepartmentId = message.DepartmentId }.SerializeJson());
			messageBus.CorrelationId = message.DepartmentId.ToString();
			messageBus.UserProperties.Add("Type", (int)EventTypes.DepartmentSettingsChanged);
			messageBus.UserProperties.Add("Value", message.DepartmentId);
			messageBus.UserProperties.Add("DepartmentId", message.DepartmentId);

			while (true)
			{
				try
				{
					await topicClient.SendAsync(messageBus);
					break;
				}
				catch (MessagingCommunicationException e)
				{
					if (!e.IsTransient)
						throw;
					else
						HandleTransientErrors(e);
				}
			}
		};

		//public class DepartmentSettingsChangedHandler : IListener<DepartmentSettingsChangedEvent>
		//{
		//	public async Task<bool> Handle(DepartmentSettingsChangedEvent message)
		//	{
		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureServiceBusConnectionString, Topics.GenericTopic);

		//		var messageBus = CreateMessage(Guid.NewGuid(), new { DepartmentId = message.DepartmentId }.SerializeJson());
		//		messageBus.CorrelationId = message.DepartmentId.ToString();
		//		messageBus.UserProperties.Add("Type", (int)EventTypes.DepartmentSettingsChanged);
		//		messageBus.UserProperties.Add("Value", message.DepartmentId);
		//		messageBus.UserProperties.Add("DepartmentId", message.DepartmentId);

		//		while (true)
		//		{
		//			try
		//			{
		//				await topicClient.SendAsync(messageBus);
		//				break;
		//			}
		//			catch (MessagingCommunicationException e)
		//			{
		//				if (!e.IsTransient)
		//					throw;
		//				else
		//					HandleTransientErrors(e);
		//			}
		//		}

		//		return true;
		//	}
		//}

		public Action<WorkerHeartbeatEvent> workerHeartbeatHandler = async delegate(WorkerHeartbeatEvent message)
		{
			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureServiceBusWorkerConnectionString, Topics.WorkerHeartbeatTopic);

			var messageBus = CreateMessage(Guid.NewGuid(), new { WorkerType = message.WorkerType, TimeStamp = message.Timestamp }.SerializeJson());
			messageBus.UserProperties.Add("Type", (int)HeartbeatTypes.Worker);

			while (true)
			{
				int retryCount = 0;

				try
				{
					await topicClient.SendAsync(messageBus);
					break;
				}
				catch (MessagingException e)
				{
					if (!e.IsTransient)
						throw;
					else
						HandleTransientErrors(e);
				}
				catch (TimeoutException)
				{
					if (retryCount < 3)
					{
						retryCount++;
						Thread.Sleep(2000);
					}
					else
						throw;
				}
			}
		};

		//public class WorkerHeartbeatHandler : IListener<WorkerHeartbeatEvent>
		//{
		//	public async Task<bool> Handle(WorkerHeartbeatEvent message)
		//	{
		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureServiceBusWorkerConnectionString, Topics.WorkerHeartbeatTopic);

		//		var messageBus = CreateMessage(Guid.NewGuid(), new { WorkerType = message.WorkerType, TimeStamp = message.Timestamp }.SerializeJson());
		//		messageBus.UserProperties.Add("Type", (int)HeartbeatTypes.Worker);

		//		while (true)
		//		{
		//			int retryCount = 0;

		//			try
		//			{
		//				await topicClient.SendAsync(messageBus);
		//				break;
		//			}
		//			catch (MessagingException e)
		//			{
		//				if (!e.IsTransient)
		//					throw;
		//				else
		//					HandleTransientErrors(e);
		//			}
		//			catch (TimeoutException)
		//			{
		//				if (retryCount < 3)
		//				{
		//					retryCount++;
		//					Thread.Sleep(2000);
		//				}
		//				else
		//					throw;
		//			}
		//		}

		//		return true;
		//	}
		//}

		public Action<DistributionListCheckEvent> dListCheckHandler = async delegate(DistributionListCheckEvent message)
		{
			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureServiceBusWorkerConnectionString, Topics.WorkerHeartbeatTopic);

			var messageBus = CreateMessage(Guid.NewGuid(), new { ListId = message.DistributionListId, TimeStamp = message.Timestamp, IsFailure = message.IsFailure, ErrorMessage = message.ErrorMessage }.SerializeJson());
			messageBus.UserProperties.Add("Type", (int)HeartbeatTypes.DListCheck);

			while (true)
			{
				int retryCount = 0;

				try
				{
					await topicClient.SendAsync(messageBus);
					break;
				}
				catch (MessagingException e)
				{
					if (!e.IsTransient)
						throw;
					else
						HandleTransientErrors(e);
				}
				catch (TimeoutException)
				{
					if (retryCount < 3)
					{
						retryCount++;
						Thread.Sleep(2000);
					}
					else
						throw;
				}
			}
		};

		//public class DListCheckHandler : IListener<DistributionListCheckEvent>
		//{
		//	public async Task<bool> Handle(DistributionListCheckEvent message)
		//	{
		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureServiceBusWorkerConnectionString, Topics.WorkerHeartbeatTopic);

		//		var messageBus = CreateMessage(Guid.NewGuid(), new { ListId = message.DistributionListId, TimeStamp = message.Timestamp, IsFailure = message.IsFailure, ErrorMessage = message.ErrorMessage }.SerializeJson());
		//		messageBus.UserProperties.Add("Type", (int)HeartbeatTypes.DListCheck);

		//		while (true)
		//		{
		//			int retryCount = 0;

		//			try
		//			{
		//				await topicClient.SendAsync(messageBus);
		//				break;
		//			}
		//			catch (MessagingException e)
		//			{
		//				if (!e.IsTransient)
		//					throw;
		//				else
		//					HandleTransientErrors(e);
		//			}
		//			catch (TimeoutException)
		//			{
		//				if (retryCount < 3)
		//				{
		//					retryCount++;
		//					Thread.Sleep(2000);
		//				}
		//				else
		//					throw;
		//			}
		//		}

		//		return true;
		//	}
		//}

		public Action<UserStatusEvent> personnelStatusChangedTopicHandler = async delegate(UserStatusEvent message)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				_rabbitTopicProvider.PersonnelStatusChanged(message);


			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
			var topicMessage = CreateMessage(Guid.NewGuid(),
				new
				{
					Type = EventingTypes.PersonnelStatusUpdated,
					TimeStamp = DateTime.UtcNow,
					DepartmentId = message.Status.DepartmentId,
					ItemId = message.Status.ActionLogId
				}.SerializeJson());
			topicMessage.UserProperties.Add("Type", (int)EventingTypes.PersonnelStatusUpdated);
			topicMessage.UserProperties.Add("DepartmentId", message.Status.DepartmentId);
			topicMessage.UserProperties.Add("ItemId", message.Status.ActionLogId);


			int retry = 0;
			bool sent = false;

			while (!sent)
			{
				try
				{
					await topicClient.SendAsync(topicMessage);
					sent = true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, message.ToString());

					if (retry >= 5)
						break;

					Thread.Sleep(1000);
					retry++;
				}
			}
		};


		//public class PersonnelStatusChangedTopicHandler : IListener<UserStatusEvent>
		//{
		//	public async Task<bool> Handle(UserStatusEvent message)
		//	{
		//		if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
		//			return _rabbitTopicProvider.PersonnelStatusChanged(message);


		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
		//		var topicMessage = CreateMessage(Guid.NewGuid(),
		//			new
		//			{
		//				Type = EventingTypes.PersonnelStatusUpdated,
		//				TimeStamp = DateTime.UtcNow,
		//				DepartmentId = message.Status.DepartmentId,
		//				ItemId = message.Status.ActionLogId
		//			}.SerializeJson());
		//		topicMessage.UserProperties.Add("Type", (int)EventingTypes.PersonnelStatusUpdated);
		//		topicMessage.UserProperties.Add("DepartmentId", message.Status.DepartmentId);
		//		topicMessage.UserProperties.Add("ItemId", message.Status.ActionLogId);


		//		int retry = 0;
		//		bool sent = false;

		//		while (!sent)
		//		{
		//			try
		//			{
		//				await topicClient.SendAsync(topicMessage);
		//				sent = true;
		//			}
		//			catch (Exception ex)
		//			{
		//				Logging.LogException(ex, message.ToString());

		//				if (retry >= 5)
		//					return false;

		//				Thread.Sleep(1000);
		//				retry++;
		//			}
		//		}

		//		return sent;
		//	}
		//}

		public Action<UserStaffingEvent> personnelStaffingChangedTopicHandler = async delegate(UserStaffingEvent message)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				_rabbitTopicProvider.PersonnelStaffingChanged(message);

			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
			var topicMessage = CreateMessage(Guid.NewGuid(),
				new
				{
					Type = EventingTypes.PersonnelStaffingUpdated,
					TimeStamp = DateTime.UtcNow,
					DepartmentId = message.DepartmentId,
					ItemId = message.Staffing.UserStateId
				}.SerializeJson());
			topicMessage.UserProperties.Add("Type", (int)EventingTypes.PersonnelStaffingUpdated);
			topicMessage.UserProperties.Add("DepartmentId", message.DepartmentId);
			topicMessage.UserProperties.Add("ItemId", message.Staffing.UserStateId);


			int retry = 0;
			bool sent = false;

			while (!sent)
			{
				try
				{
					await topicClient.SendAsync(topicMessage);
					sent = true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, message.ToString());

					if (retry >= 5)
						break;

					Thread.Sleep(1000);
					retry++;
				}
			}
		};

		//public class PersonnelStaffingChangedTopicHandler : IListener<UserStaffingEvent>
		//{
		//	public async Task<bool> Handle(UserStaffingEvent message)
		//	{
		//		if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
		//			return _rabbitTopicProvider.PersonnelStaffingChanged(message);

		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
		//		var topicMessage = CreateMessage(Guid.NewGuid(),
		//			new
		//			{
		//				Type = EventingTypes.PersonnelStaffingUpdated,
		//				TimeStamp = DateTime.UtcNow,
		//				DepartmentId = message.DepartmentId,
		//				ItemId = message.Staffing.UserStateId
		//			}.SerializeJson());
		//		topicMessage.UserProperties.Add("Type", (int)EventingTypes.PersonnelStaffingUpdated);
		//		topicMessage.UserProperties.Add("DepartmentId", message.DepartmentId);
		//		topicMessage.UserProperties.Add("ItemId", message.Staffing.UserStateId);


		//		int retry = 0;
		//		bool sent = false;

		//		while (!sent)
		//		{
		//			try
		//			{
		//				await topicClient.SendAsync(topicMessage);
		//				sent = true;
		//			}
		//			catch (Exception ex)
		//			{
		//				Logging.LogException(ex, message.ToString());

		//				if (retry >= 5)
		//					return false;

		//				Thread.Sleep(1000);
		//				retry++;
		//			}
		//		}

		//		return sent;
		//	}
		//}

		public Action<UnitStatusEvent> unitStatusTopicHandler = async delegate(UnitStatusEvent message)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				_rabbitTopicProvider.UnitStatusChanged(message);

			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
			var topicMessage = CreateMessage(Guid.NewGuid(),
				new
				{
					Type = EventingTypes.UnitStatusUpdated,
					TimeStamp = DateTime.UtcNow,
					DepartmentId = message.DepartmentId,
					ItemId = message.Status.UnitStateId
				}.SerializeJson());
			topicMessage.UserProperties.Add("Type", (int)EventingTypes.UnitStatusUpdated);
			topicMessage.UserProperties.Add("DepartmentId", message.DepartmentId);
			topicMessage.UserProperties.Add("ItemId", message.Status.UnitStateId);


			int retry = 0;
			bool sent = false;

			while (!sent)
			{
				try
				{
					await topicClient.SendAsync(topicMessage);
					sent = true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, message.ToString());

					if (retry >= 5)
						break;

					Thread.Sleep(1000);
					retry++;
				}
			}
		};

		//public class UnitStatusTopicHandler : IListener<UnitStatusEvent>
		//{
		//	public async Task<bool> Handle(UnitStatusEvent message)
		//	{
		//		if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
		//			return _rabbitTopicProvider.UnitStatusChanged(message);

		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
		//		var topicMessage = CreateMessage(Guid.NewGuid(),
		//			new
		//			{
		//				Type = EventingTypes.UnitStatusUpdated,
		//				TimeStamp = DateTime.UtcNow,
		//				DepartmentId = message.DepartmentId,
		//				ItemId = message.Status.UnitStateId
		//			}.SerializeJson());
		//		topicMessage.UserProperties.Add("Type", (int)EventingTypes.UnitStatusUpdated);
		//		topicMessage.UserProperties.Add("DepartmentId", message.DepartmentId);
		//		topicMessage.UserProperties.Add("ItemId", message.Status.UnitStateId);


		//		int retry = 0;
		//		bool sent = false;

		//		while (!sent)
		//		{
		//			try
		//			{
		//				await topicClient.SendAsync(topicMessage);
		//				sent = true;
		//			}
		//			catch (Exception ex)
		//			{
		//				Logging.LogException(ex, message.ToString());

		//				if (retry >= 5)
		//					return false;

		//				Thread.Sleep(1000);
		//				retry++;
		//			}
		//		}

		//		return sent;
		//	}
		//}

		public Action<CallAddedEvent> callAddedTopicHandler = async delegate(CallAddedEvent message)
		{
			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				_rabbitTopicProvider.CallAdded(message);


			var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
			var topicMessage = CreateMessage(Guid.NewGuid(),
				new
				{
					Type = EventingTypes.CallsUpdated,
					TimeStamp = DateTime.UtcNow,
					DepartmentId = message.DepartmentId,
					ItemId = message.Call.CallId
				}.SerializeJson());
			topicMessage.UserProperties.Add("Type", (int)EventingTypes.CallsUpdated);
			topicMessage.UserProperties.Add("DepartmentId", message.DepartmentId);
			topicMessage.UserProperties.Add("ItemId", message.Call.CallId);

			int retry = 0;
			bool sent = false;

			while (!sent)
			{
				try
				{
					await topicClient.SendAsync(topicMessage);
					sent = true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, message.ToString());

					if (retry >= 5)
						break;

					Thread.Sleep(1000);
					retry++;
				}
			}
		};

		//public class CallAddedTopicHandler : IListener<CallAddedEvent>
		//{
		//	public async Task<bool> Handle(CallAddedEvent message)
		//	{
		//		if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
		//			return _rabbitTopicProvider.CallAdded(message);


		//		var topicClient = new TopicClient(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
		//		var topicMessage = CreateMessage(Guid.NewGuid(),
		//			new
		//			{
		//				Type = EventingTypes.CallsUpdated,
		//				TimeStamp = DateTime.UtcNow,
		//				DepartmentId = message.DepartmentId,
		//				ItemId = message.Call.CallId
		//			}.SerializeJson());
		//		topicMessage.UserProperties.Add("Type", (int)EventingTypes.CallsUpdated);
		//		topicMessage.UserProperties.Add("DepartmentId", message.DepartmentId);
		//		topicMessage.UserProperties.Add("ItemId", message.Call.CallId);

		//		int retry = 0;
		//		bool sent = false;

		//		while (!sent)
		//		{
		//			try
		//			{
		//				await topicClient.SendAsync(topicMessage);
		//				sent = true;
		//			}
		//			catch (Exception ex)
		//			{
		//				Logging.LogException(ex, message.ToString());

		//				if (retry >= 5)
		//					return false;

		//				Thread.Sleep(1000);
		//				retry++;
		//			}
		//		}

		//		return sent;
		//	}
		//}
		#endregion Topic Based Events
	}
}
