using Resgrid.Config;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using System;
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
			_eventAggregator.AddListener(auditEventHandler);
			_eventAggregator.AddListener(securityRefreshEventHandler);

			// Topics (SignalR Integration)
			_eventAggregator.AddListener(personnelStatusChangedTopicHandler);
			_eventAggregator.AddListener(personnelStaffingChangedTopicHandler);
			_eventAggregator.AddListener(unitStatusTopicHandler);
			_eventAggregator.AddListener(callAddedTopicHandler);
			_eventAggregator.AddListener(callUpdatedTopicHandler);
			_eventAggregator.AddListener(callClosedTopicHandler);
			_eventAggregator.AddListener(personnelLocationUpdatedTopicHandler);
			_eventAggregator.AddListener(unitLocationUpdatedTopicHandler);
		}

		public Action<UnitStatusEvent> unitStatusHandler = async delegate (UnitStatusEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

			try
			{
				await _signalrProvider.UnitStatusUpdated(message.DepartmentId, message.Status);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
		};

		public Action<UnitStatusEvent> unitTypeGroupAvailabilityHandler = async delegate (UnitStatusEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<UnitStatusEvent> unitTypeDepartmentAvailabilityHandler = async delegate (UnitStatusEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<UserStaffingEvent> userStaffingHandler = async delegate (UserStaffingEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			int previousStaffing = 0;
			if (message.PreviousStaffing != null)
				previousStaffing = message.PreviousStaffing.UserStateId;

			nqi.Type = (int)EventTypes.PersonnelStaffingChanged;
			nqi.DepartmentId = message.DepartmentId;
			nqi.StateId = message.Staffing.UserStateId;
			nqi.PreviousStateId = previousStaffing;
			nqi.Value = message.Staffing.State.ToString();
			nqi.UserId = message.Staffing.UserId;

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<UserStaffingEvent> userRoleGroupAvailabilityHandler = async delegate (UserStaffingEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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
			nqi.UserId = message.Staffing.UserId;

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<UserStaffingEvent> userRoleDepartmentAvailabilityHandler = async delegate (UserStaffingEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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
			nqi.UserId = message.Staffing.UserId;

			await _outboundQueueProvider.EnqueueNotification(nqi);
			await _signalrProvider.PersonnelStaffingUpdated(message.Staffing.DepartmentId, message.Staffing);
		};

		public Action<UserStatusEvent> personnelStatusChangedHandler = async delegate (UserStatusEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<UserCreatedEvent> userCreatedHandler = async delegate (UserCreatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.UserCreated;
			nqi.DepartmentId = message.DepartmentId;
			nqi.UserId = message.User.UserId;
			nqi.Value = message.User.UserId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<UserAssignedToGroupEvent> userAssignedToGroupHandler = async delegate (UserAssignedToGroupEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<CalendarEventUpcomingEvent> calendarEventUpcomingHandler = async delegate (CalendarEventUpcomingEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.CalendarEventUpcoming;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Item.CalendarItemId;
			nqi.Value = message.Item.CalendarItemId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<CalendarEventAddedEvent> calendarEventAddedHandler = async delegate (CalendarEventAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.CalendarEventAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Item.CalendarItemId;
			nqi.Value = message.Item.CalendarItemId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<CalendarEventUpdatedEvent> calendarEventUpdatedHandler = async delegate (CalendarEventUpdatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.CalendarEventUpdated;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Item.CalendarItemId;
			nqi.Value = message.Item.CalendarItemId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<DocumentAddedEvent> documentAddedHandler = async delegate (DocumentAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.DocumentAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Document.DocumentId;
			nqi.Value = message.Document.DocumentId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<NoteAddedEvent> noteAddedHandler = async delegate (NoteAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.NoteAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Note.NoteId;
			nqi.Value = message.Note.NoteId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<UnitAddedEvent> unitAddedHandler = async delegate (UnitAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.UnitAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.UnitId = message.Unit.UnitId;
			nqi.Value = message.Unit.UnitId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<LogAddedEvent> logAddedHandler = async delegate (LogAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.LogAdded;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.Log.LogId;
			nqi.Value = message.Log.LogId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<ResourceOrderAddedEvent> resourceOrderAddedHandler = async delegate (ResourceOrderAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.ResourceOrderAdded;
			nqi.DepartmentId = message.Order.DepartmentId;
			nqi.ItemId = message.Order.ResourceOrderId;
			nqi.Value = message.Order.ResourceOrderId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<ShiftTradeRequestedEvent> shiftTradeRequestedHandler = async delegate (ShiftTradeRequestedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.ShiftSignupTradeId = message.ShiftSignupTradeId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.Type = (int)ShiftQueueTypes.TradeRequested;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		public Action<ShiftTradeRejectedEvent> shiftTradeRejectedEventHandler = async delegate (ShiftTradeRejectedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<ShiftTradeProposedEvent> shiftTradeProposedEventHandler = async delegate (ShiftTradeProposedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<ShiftTradeFilledEvent> shiftTradeFilledEventHandler = async delegate (ShiftTradeFilledEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

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

		public Action<ShiftCreatedEvent> shiftCreatedEventHandler = async delegate (ShiftCreatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.ShiftId = message.Item.ShiftId;
			item.Type = (int)ShiftQueueTypes.ShiftCreated;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		public Action<ShiftUpdatedEvent> shiftUpdatedEventHandler = async delegate (ShiftUpdatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.ShiftId = message.Item.ShiftId;
			item.Type = (int)ShiftQueueTypes.ShiftUpdated;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		public Action<ShiftDaysAddedEvent> shiftDaysAddedEventHandler = async delegate (ShiftDaysAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			var item = new ShiftQueueItem();
			item.DepartmentId = message.DepartmentId;
			item.DepartmentNumber = message.DepartmentNumber;
			item.ShiftId = message.Item.ShiftId;
			item.Type = (int)ShiftQueueTypes.ShiftDaysAdded;

			await _outboundQueueProvider.EnqueueShiftNotification(item);
		};

		private Action<AuditEvent> auditEventHandler = async delegate (AuditEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			await _outboundQueueProvider.EnqueueAuditEvent(message);
		};

		private Action<SecurityRefreshEvent> securityRefreshEventHandler = async delegate (SecurityRefreshEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			if (_outboundQueueProvider == null)
				_outboundQueueProvider = new OutboundQueueProvider();

			await _outboundQueueProvider.EnqueueSecurityRefreshEvent(message);
		};

		#region Topic Based Events
		public Action<DepartmentSettingsChangedEvent> departmentSettingsChangedHandler = async delegate (DepartmentSettingsChangedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			var nqi = new NotificationItem();

			nqi.Type = (int)EventTypes.DepartmentSettingsChanged;
			nqi.DepartmentId = message.DepartmentId;
			nqi.ItemId = message.DepartmentId;
			nqi.Value = message.DepartmentId.ToString();

			await _outboundQueueProvider.EnqueueNotification(nqi);
		};

		public Action<WorkerHeartbeatEvent> workerHeartbeatHandler = async delegate (WorkerHeartbeatEvent message)
		{

		};

		public Action<DistributionListCheckEvent> dListCheckHandler = async delegate (DistributionListCheckEvent message)
		{

		};

		public Action<UserStatusEvent> personnelStatusChangedTopicHandler = async delegate (UserStatusEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.PersonnelStatusChanged(message);
		};


		public Action<UserStaffingEvent> personnelStaffingChangedTopicHandler = async delegate (UserStaffingEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.PersonnelStaffingChanged(message);
		};

		public Action<UnitStatusEvent> unitStatusTopicHandler = async delegate (UnitStatusEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.UnitStatusChanged(message);
		};

		public Action<CallAddedEvent> callAddedTopicHandler = async delegate (CallAddedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.CallAdded(message);
		};

		public Action<CallUpdatedEvent> callUpdatedTopicHandler = async delegate (CallUpdatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.CallUpdated(message);
		};

		public Action<CallClosedEvent> callClosedTopicHandler = async delegate (CallClosedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.CallClosed(message);
		};

		public Action<PersonnelLocationUpdatedEvent> personnelLocationUpdatedTopicHandler = async delegate (PersonnelLocationUpdatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.PersonnelLocationUnidatedChanged(message);
		};

		public Action<UnitLocationUpdatedEvent> unitLocationUpdatedTopicHandler = async delegate (UnitLocationUpdatedEvent message)
		{
			if (_rabbitTopicProvider == null)
				_rabbitTopicProvider = new RabbitTopicProvider();

			_rabbitTopicProvider.UnitLocationUpdatedChanged(message);
		};
		#endregion Topic Based Events
	}
}
