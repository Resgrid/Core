namespace Resgrid.Config
{
	/// <summary>
	/// Service Bus specific values for both Azure and NATS
	/// </summary>
	public static class ServiceBusConfig
	{
		#region Azure Service Bus Values
		public static string SignalRServiceBusConnectionString = "";
		public static string SignalRTopicName = "";

		public static string AzureQueueSystemConnectionString = "";
		public static string SystemQueueName = "";

		public static string AzureEventingTopicConnectionString = "";
		public static string EventingTopicQueueName = "";

		public static string AzureNotificationHub_FullConnectionString = "";
		public static string AzureNotificationHub_PushUrl = "";

		public static string AzureServiceBusConnectionString = "";
		public static string AzureServiceBusOwnerName = "";
		public static string AzureServiceBusOwnerSecret = "";
		public static string AzureServiceBusRootName = "";
		public static string AzureQueueOwnerSecret = "";

		public static string AzureServiceBusWorkerConnectionString = "";

		public static string AzureQueueConnectionString = "";
		public static string CallBroadcastQueueName = "";

		public static string AzureQueueMessageConnectionString = "";
		public static string MessageBroadcastQueueName = "";

		public static string AzureQueueNotificationConnectionString = "";
		public static string NotificaitonBroadcastQueueName = "";

		public static string AzureQueueShiftsConnectionString = "";
		public static string ShiftNotificationsQueueName = "";

		public static string AzureQueueEmailConnectionString = "";
		public static string EmailBroadcastQueueName = "";

		public static string AzureUnitNotificationHub_FullConnectionString = "";
		public static string AzureUnitNotificationHub_PushUrl = "";
		#endregion Azure Service Bus Values
	}
	
	public enum ServiceBusTypes
	{
		Azure,
		Nats
	}
}
