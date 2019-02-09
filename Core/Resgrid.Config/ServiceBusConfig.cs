namespace Resgrid.Config
{
	/// <summary>
	/// Service Bus specific values for both Azure and NATS
	/// </summary>
	public static class ServiceBusConfig
	{
		public static string CallBroadcastQueueName = "resgridcalls";
		public static string MessageBroadcastQueueName = "resgridmessages";
		public static string NotificaitonBroadcastQueueName = "resgridnotifications";
		public static string ShiftNotificationsQueueName = "resgridshiftsnots";
		public static string EmailBroadcastQueueName = "resgridemails";
		public static string SystemQueueName = "resgridsys";

		#region Azure Service Bus Values
		public static string SignalRServiceBusConnectionString = "";
		public static string SignalRTopicName = "";

		public static string AzureQueueSystemConnectionString = "";

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

		public static string AzureQueueMessageConnectionString = "";

		public static string AzureQueueNotificationConnectionString = "";

		public static string AzureQueueShiftsConnectionString = "";

		public static string AzureQueueEmailConnectionString = "";

		public static string AzureUnitNotificationHub_FullConnectionString = "";
		public static string AzureUnitNotificationHub_PushUrl = "";
		#endregion Azure Service Bus Values

		#region RabbitMQ Bus Values
		public static string RabbitHostname = "";
		public static string RabbitUsername = "";
		public static string RabbbitPassword = "";
		public static string RabbbitExchange = "";
		#endregion RabbitMQ Bus Values
	}

	public enum ServiceBusTypes
	{
		Azure,
		Rabbit
	}
}
