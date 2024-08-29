namespace Resgrid.Config
{
	/// <summary>
	/// Service Bus specific values for both Azure and NATS
	/// </summary>
	public static class ServiceBusConfig
	{
#if DEBUG
		public static string CallBroadcastQueueName = "callbroadcasttest";
		public static string MessageBroadcastQueueName = "messagebroadcasttest";
		public static string NotificaitonBroadcastQueueName = "notificationbroadcasttest";
		public static string ShiftNotificationsQueueName = "shiftsnotificationstest";
		public static string EmailBroadcastQueueName = "emailbroadcasttest";
		public static string SystemQueueName = "systemtest";
		public static string PaymentQueueName = "paymenttest";
		public static string AuditQueueName = "audittest";
		public static string UnitLoactionQueueName = "unitlocationtest";
		public static string PersonnelLoactionQueueName = "personnellocationtest";
		public static string SecurityRefreshQueueName = "securityrefreshtest";
#else
		public static string CallBroadcastQueueName = "callbroadcast";
		public static string MessageBroadcastQueueName = "messagebroadcast";
		public static string NotificaitonBroadcastQueueName = "notificationbroadcast";
		public static string ShiftNotificationsQueueName = "shiftsnotifications";
		public static string EmailBroadcastQueueName = "emailbroadcast";
		public static string SystemQueueName = "system";
		public static string PaymentQueueName = "payment";
		public static string AuditQueueName = "audit";
		public static string UnitLoactionQueueName = "unitlocation";
		public static string PersonnelLoactionQueueName = "personnellocation";
		public static string SecurityRefreshQueueName = "securityrefresh";
#endif

		#region Azure Service Bus Values
		public static string AzureNotificationHub_FullConnectionString = "";
		public static string AzureNotificationHub_PushUrl = "rgpush";

		public static string AzureUnitNotificationHub_FullConnectionString = "";
		public static string AzureUnitNotificationHub_PushUrl = "unit";
		#endregion Azure Service Bus Values

		#region RabbitMQ Bus Values
		public static string RabbitHostname = "rgdevinfaserver";
		public static string RabbitHostname2 = ""; // For 3 host cluster, node 2
		public static string RabbitHostname3 = ""; // For 3 host cluster, node 3
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
