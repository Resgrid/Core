using Microsoft.ServiceBus.Messaging;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus.Rabbit;
using System;
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

			_eventAggregator.AddListener(new UnitStatusHandler(), true);
			_eventAggregator.AddListener(new UnitTypeGroupAvailabilityHandler(), true);
			_eventAggregator.AddListener(new UnitTypeDepartmentAvailabilityHandler(), true);
			_eventAggregator.AddListener(new UserStaffingHandler(), true);
			_eventAggregator.AddListener(new UserRoleGroupAvailabilityHandler(), true);
			_eventAggregator.AddListener(new UserRoleDepartmentAvailabilityHandler(), true);
			_eventAggregator.AddListener(new PersonnelStatusChangedHandler(), true);
			_eventAggregator.AddListener(new UserCreatedHandler(), true);
			_eventAggregator.AddListener(new UserAssignedToGroupHandler(), true);
			_eventAggregator.AddListener(new CalendarEventUpcomingHandler(), true);
			_eventAggregator.AddListener(new CalendarEventAddedHandler(), true);
			_eventAggregator.AddListener(new CalendarEventUpdatedHandler(), true);
			_eventAggregator.AddListener(new DocumentAddedHandler(), true);
			_eventAggregator.AddListener(new NoteAddedHandler(), true);
			_eventAggregator.AddListener(new UnitAddedHandler(), true);
			_eventAggregator.AddListener(new LogAddedHandler(), true);
			_eventAggregator.AddListener(new DepartmentSettingsChangedHandler(), true);
			_eventAggregator.AddListener(new WorkerHeartbeatHandler(), true);
			_eventAggregator.AddListener(new DListCheckHandler(), true);
			_eventAggregator.AddListener(new ShiftTradeRequestedHandler(), true);
			_eventAggregator.AddListener(new ShiftTradeRejectedEventHandler(), true);
			_eventAggregator.AddListener(new ShiftTradeFilledEventHandler(), true);
			_eventAggregator.AddListener(new ShiftCreatedEventHandler(), true);
			_eventAggregator.AddListener(new ShiftUpdatedEventHandler(), true);
			_eventAggregator.AddListener(new ShiftDaysAddedEventHandler(), true);

			// Topics (SignalR Integration)
			_eventAggregator.AddListener(new PersonnelStatusChangedTopicHandler(), true);
			_eventAggregator.AddListener(new PersonnelStaffingChangedTopicHandler(), true);
			_eventAggregator.AddListener(new UnitStatusTopicHandler(), true);
			_eventAggregator.AddListener(new CallAddedTopicHandler(), true);
		}

		#region Private Helpers
		private static BrokeredMessage CreateMessage(Guid messageId, string messageBody)
		{
			return new BrokeredMessage(messageBody) { MessageId = messageId.ToString() };
		}

		private static void HandleTransientErrors(MessagingException e)
		{
			//If transient error/exception, let's back-off for 2 seconds and retry
			Thread.Sleep(2000);
		}
		#endregion Private Helpers


		public class UnitStatusHandler : IListener<UnitStatusEvent>
		{
			public void Handle(UnitStatusEvent message)
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

				_outboundQueueProvider.EnqueueNotification(nqi);
				_signalrProvider.UnitStatusUpdated(message.DepartmentId, message.Status);
			}
		}

		public class UnitTypeGroupAvailabilityHandler : IListener<UnitStatusEvent>
		{
			public void Handle(UnitStatusEvent message)
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

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class UnitTypeDepartmentAvailabilityHandler : IListener<UnitStatusEvent>
		{
			public void Handle(UnitStatusEvent message)
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

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class UserStaffingHandler : IListener<UserStaffingEvent>
		{
			public void Handle(UserStaffingEvent message)
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

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class UserRoleGroupAvailabilityHandler : IListener<UserStaffingEvent>
		{
			public void Handle(UserStaffingEvent message)
			{
				var nqi = new NotificationItem();

				int previousStaffing = 0;
				if (message.PreviousStaffing != null)
					previousStaffing = message.PreviousStaffing.UserStateId;

				nqi.Type = (int)EventTypes.RolesInGroupAvailabilityAlert;
				nqi.DepartmentId = message.DepartmentId;
				nqi.StateId = message.Staffing.UserStateId;
				nqi.PreviousStateId = previousStaffing;
				nqi.Value = message.Staffing.State.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class UserRoleDepartmentAvailabilityHandler : IListener<UserStaffingEvent>
		{
			public void Handle(UserStaffingEvent message)
			{
				var nqi = new NotificationItem();

				int previousStaffing = 0;
				if (message.PreviousStaffing != null)
					previousStaffing = message.PreviousStaffing.UserStateId;

				nqi.Type = (int)EventTypes.RolesInDepartmentAvailabilityAlert;
				nqi.DepartmentId = message.DepartmentId;
				nqi.StateId = message.Staffing.UserStateId;
				nqi.PreviousStateId = previousStaffing;
				nqi.Value = message.Staffing.State.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
				_signalrProvider.PersonnelStaffingUpdated(message.Staffing.DepartmentId, message.Staffing);
			}
		}

		public class PersonnelStatusChangedHandler : IListener<UserStatusEvent>
		{
			public void Handle(UserStatusEvent message)
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

				_outboundQueueProvider.EnqueueNotification(nqi);
				_signalrProvider.PersonnelStatusUpdated(message.Status.DepartmentId, message.Status);
			}
		}

		public class UserCreatedHandler : IListener<UserCreatedEvent>
		{
			public void Handle(UserCreatedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.UserCreated;
				nqi.DepartmentId = message.DepartmentId;
				nqi.UserId = message.User.UserId;
				nqi.Value = message.User.UserId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class UserAssignedToGroupHandler : IListener<UserAssignedToGroupEvent>
		{
			public void Handle(UserAssignedToGroupEvent message)
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

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class CalendarEventUpcomingHandler : IListener<CalendarEventUpcomingEvent>
		{
			public void Handle(CalendarEventUpcomingEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.CalendarEventUpcoming;
				nqi.DepartmentId = message.DepartmentId;
				nqi.ItemId = message.Item.CalendarItemId;
				nqi.Value = message.Item.CalendarItemId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class CalendarEventAddedHandler : IListener<CalendarEventAddedEvent>
		{
			public void Handle(CalendarEventAddedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.CalendarEventAdded;
				nqi.DepartmentId = message.DepartmentId;
				nqi.ItemId = message.Item.CalendarItemId;
				nqi.Value = message.Item.CalendarItemId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class CalendarEventUpdatedHandler : IListener<CalendarEventUpdatedEvent>
		{
			public void Handle(CalendarEventUpdatedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.CalendarEventUpdated;
				nqi.DepartmentId = message.DepartmentId;
				nqi.ItemId = message.Item.CalendarItemId;
				nqi.Value = message.Item.CalendarItemId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class DocumentAddedHandler : IListener<DocumentAddedEvent>
		{
			public void Handle(DocumentAddedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.DocumentAdded;
				nqi.DepartmentId = message.DepartmentId;
				nqi.ItemId = message.Document.DocumentId;
				nqi.Value = message.Document.DocumentId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class NoteAddedHandler : IListener<NoteAddedEvent>
		{
			public void Handle(NoteAddedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.NoteAdded;
				nqi.DepartmentId = message.DepartmentId;
				nqi.ItemId = message.Note.NoteId;
				nqi.Value = message.Note.NoteId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class UnitAddedHandler : IListener<UnitAddedEvent>
		{
			public void Handle(UnitAddedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.UnitAdded;
				nqi.DepartmentId = message.DepartmentId;
				nqi.UnitId = message.Unit.UnitId;
				nqi.Value = message.Unit.UnitId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class LogAddedHandler : IListener<LogAddedEvent>
		{
			public void Handle(LogAddedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.LogAdded;
				nqi.DepartmentId = message.DepartmentId;
				nqi.ItemId = message.Log.LogId;
				nqi.Value = message.Log.LogId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class ResourceOrderAddedHandler : IListener<ResourceOrderAddedEvent>
		{
			public void Handle(ResourceOrderAddedEvent message)
			{
				var nqi = new NotificationItem();

				nqi.Type = (int)EventTypes.ResourceOrderAdded;
				nqi.DepartmentId = message.Order.DepartmentId;
				nqi.ItemId = message.Order.ResourceOrderId;
				nqi.Value = message.Order.ResourceOrderId.ToString();

				_outboundQueueProvider.EnqueueNotification(nqi);
			}
		}

		public class ShiftTradeRequestedHandler : IListener<ShiftTradeRequestedEvent>
		{
			public void Handle(ShiftTradeRequestedEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.ShiftSignupTradeId = message.ShiftSignupTradeId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.Type = (int)ShiftQueueTypes.TradeRequested;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}

		public class ShiftTradeRejectedEventHandler : IListener<ShiftTradeRejectedEvent>
		{
			public void Handle(ShiftTradeRejectedEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.ShiftSignupTradeId = message.ShiftSignupTradeId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.Type = (int)ShiftQueueTypes.TradeRejected;
				item.SourceUserId = message.UserId;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}

		public class ShiftTradeProposedEventHandler : IListener<ShiftTradeProposedEvent>
		{
			public void Handle(ShiftTradeProposedEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.ShiftSignupTradeId = message.ShiftSignupTradeId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.Type = (int)ShiftQueueTypes.TradeProposed;
				item.SourceUserId = message.UserId;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}

		public class ShiftTradeFilledEventHandler : IListener<ShiftTradeFilledEvent>
		{
			public void Handle(ShiftTradeFilledEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.ShiftSignupTradeId = message.ShiftSignupTradeId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.Type = (int)ShiftQueueTypes.TradeFilled;
				item.SourceUserId = message.UserId;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}

		public class ShiftCreatedEventHandler : IListener<ShiftCreatedEvent>
		{
			public void Handle(ShiftCreatedEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.ShiftId = message.Item.ShiftId;
				item.Type = (int)ShiftQueueTypes.ShiftCreated;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}

		public class ShiftUpdatedEventHandler : IListener<ShiftUpdatedEvent>
		{
			public void Handle(ShiftUpdatedEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.ShiftId = message.Item.ShiftId;
				item.Type = (int)ShiftQueueTypes.ShiftUpdated;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}

		public class ShiftDaysAddedEventHandler : IListener<ShiftDaysAddedEvent>
		{
			public void Handle(ShiftDaysAddedEvent message)
			{
				if (_outboundQueueProvider == null)
					_outboundQueueProvider = new OutboundQueueProvider();

				var item = new ShiftQueueItem();
				item.DepartmentId = message.DepartmentId;
				item.DepartmentNumber = message.DepartmentNumber;
				item.ShiftId = message.Item.ShiftId;
				item.Type = (int)ShiftQueueTypes.ShiftDaysAdded;

				_outboundQueueProvider.EnqueueShiftNotification(item);
			}
		}


		#region Topic Based Events
		public class DepartmentSettingsChangedHandler : IListener<DepartmentSettingsChangedEvent>
		{
			public void Handle(DepartmentSettingsChangedEvent message)
			{
				var topicClient = TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureServiceBusConnectionString, Topics.GenericTopic);

				var messageBus = CreateMessage(Guid.NewGuid(), new { DepartmentId = message.DepartmentId }.SerializeJson());
				messageBus.CorrelationId = message.DepartmentId.ToString();
				messageBus.Properties.Add("Type", (int)EventTypes.DepartmentSettingsChanged);
				messageBus.Properties.Add("Value", message.DepartmentId);
				messageBus.Properties.Add("DepartmentId", message.DepartmentId);

				while (true)
				{
					try
					{
						topicClient.Send(messageBus);
					}
					catch (MessagingException e)
					{
						if (!e.IsTransient)
							throw;
						else
							HandleTransientErrors(e);
					}

					break;
				}
			}
		}

		public class WorkerHeartbeatHandler : IListener<WorkerHeartbeatEvent>
		{
			public void Handle(WorkerHeartbeatEvent message)
			{
				var topicClient = TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureServiceBusWorkerConnectionString, Topics.WorkerHeartbeatTopic);

				var messageBus = CreateMessage(Guid.NewGuid(), new { WorkerType = message.WorkerType, TimeStamp = message.Timestamp }.SerializeJson());
				messageBus.Properties.Add("Type", (int)HeartbeatTypes.Worker);
				//messageBus.Properties.Add("Timestamp", DateTime.UtcNow);

				while (true)
				{
					int retryCount = 0;

					try
					{
						topicClient.Send(messageBus);
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

					break;
				}
			}
		}

		public class DListCheckHandler : IListener<DistributionListCheckEvent>
		{
			public void Handle(DistributionListCheckEvent message)
			{
				var topicClient = TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureServiceBusWorkerConnectionString, Topics.WorkerHeartbeatTopic);

				var messageBus = CreateMessage(Guid.NewGuid(), new { ListId = message.DistributionListId, TimeStamp = message.Timestamp, IsFailure = message.IsFailure, ErrorMessage = message.ErrorMessage }.SerializeJson());
				messageBus.Properties.Add("Type", (int)HeartbeatTypes.DListCheck);
				//messageBus.Properties.Add("Timestamp", DateTime.UtcNow);

				while (true)
				{
					int retryCount = 0;

					try
					{
						topicClient.Send(messageBus);
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

					break;
				}
			}
		}


		public class PersonnelStatusChangedTopicHandler : IListener<UserStatusEvent>
		{
			public void Handle(UserStatusEvent message)
			{
				try
				{
					if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
					{
						_rabbitTopicProvider.PersonnelStatusChanged(message);
						return;
					}

					var topicClient =
						TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
					var topicMessage = CreateMessage(Guid.NewGuid(),
						new
						{
							Type = EventingTypes.PersonnelStatusUpdated,
							TimeStamp = DateTime.UtcNow,
							DepartmentId = message.Status.DepartmentId,
							ItemId = message.Status.ActionLogId
						}.SerializeJson());
					topicMessage.Properties.Add("Type", (int)EventingTypes.PersonnelStatusUpdated);
					topicMessage.Properties.Add("DepartmentId", message.Status.DepartmentId);
					topicMessage.Properties.Add("ItemId", message.Status.ActionLogId);

#pragma warning disable 4014
					Task.Run(() =>
					{
						int retry = 0;
						bool sent = false;

						while (!sent)
						{
							try
							{
								topicClient.Send(topicMessage);
								sent = true;
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, message.ToString());

								if (retry >= 5)
									return false;

								Thread.Sleep(1000);
								retry++;
							}
						}

						return sent;
					}).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{

				}
			}
		}

		public class PersonnelStaffingChangedTopicHandler : IListener<UserStaffingEvent>
		{
			public void Handle(UserStaffingEvent message)
			{
				try
				{
					if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
					{
						_rabbitTopicProvider.PersonnelStaffingChanged(message);
						return;
					}

					var topicClient =
						TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
					var topicMessage = CreateMessage(Guid.NewGuid(),
						new
						{
							Type = EventingTypes.PersonnelStaffingUpdated,
							TimeStamp = DateTime.UtcNow,
							DepartmentId = message.DepartmentId,
							ItemId = message.Staffing.UserStateId
						}.SerializeJson());
					topicMessage.Properties.Add("Type", (int)EventingTypes.PersonnelStaffingUpdated);
					topicMessage.Properties.Add("DepartmentId", message.DepartmentId);
					topicMessage.Properties.Add("ItemId", message.Staffing.UserStateId);

#pragma warning disable 4014
					Task.Run(() =>
					{
						int retry = 0;
						bool sent = false;

						while (!sent)
						{
							try
							{
								topicClient.Send(topicMessage);
								sent = true;
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, message.ToString());

								if (retry >= 5)
									return false;

								Thread.Sleep(1000);
								retry++;
							}
						}

						return sent;
					}).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{

				}
			}
		}

		public class UnitStatusTopicHandler : IListener<UnitStatusEvent>
		{
			public void Handle(UnitStatusEvent message)
			{
				try
				{
					if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
					{
						_rabbitTopicProvider.UnitStatusChanged(message);
						return;
					}

					var topicClient =
						TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
					var topicMessage = CreateMessage(Guid.NewGuid(),
						new
						{
							Type = EventingTypes.UnitStatusUpdated,
							TimeStamp = DateTime.UtcNow,
							DepartmentId = message.DepartmentId,
							ItemId = message.Status.UnitStateId
						}.SerializeJson());
					topicMessage.Properties.Add("Type", (int)EventingTypes.UnitStatusUpdated);
					topicMessage.Properties.Add("DepartmentId", message.DepartmentId);
					topicMessage.Properties.Add("ItemId", message.Status.UnitStateId);

#pragma warning disable 4014
					Task.Run(() =>
					{
						int retry = 0;
						bool sent = false;

						while (!sent)
						{
							try
							{
								topicClient.Send(topicMessage);
								sent = true;
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, message.ToString());

								if (retry >= 5)
									return false;

								Thread.Sleep(1000);
								retry++;
							}
						}

						return sent;
					}).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{
				}
			}
		}

		public class CallAddedTopicHandler : IListener<CallAddedEvent>
		{
			public void Handle(CallAddedEvent message)
			{
				if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				{
					_rabbitTopicProvider.CallAdded(message);
					return;
				}

				try
				{
					var topicClient =
						TopicClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureEventingTopicConnectionString, Topics.EventingTopic);
					var topicMessage = CreateMessage(Guid.NewGuid(),
						new
						{
							Type = EventingTypes.CallsUpdated,
							TimeStamp = DateTime.UtcNow,
							DepartmentId = message.DepartmentId,
							ItemId = message.Call.CallId
						}.SerializeJson());
					topicMessage.Properties.Add("Type", (int)EventingTypes.CallsUpdated);
					topicMessage.Properties.Add("DepartmentId", message.DepartmentId);
					topicMessage.Properties.Add("ItemId", message.Call.CallId);

#pragma warning disable 4014
					Task.Run(() =>
					{
						int retry = 0;
						bool sent = false;

						while (!sent)
						{
							try
							{
								topicClient.Send(topicMessage);
								sent = true;
							}
							catch (Exception ex)
							{
								Logging.LogException(ex, message.ToString());

								if (retry >= 5)
									return false;

								Thread.Sleep(1000);
								retry++;
							}
						}

						return sent;
					}).ConfigureAwait(false);
#pragma warning restore 4014
				}
				catch (Exception ex)
				{

				}
			}
		}
		#endregion Topic Based Events
	}
}
