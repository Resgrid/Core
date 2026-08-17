﻿namespace Resgrid.Config
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
		public static string UnitLocationQueueV2Name = "unitlocation-v2test";
		public static string UnitLocationRetryQueueV2Name = "unitlocation-v2test.retry";
		public static string UnitLocationDeadQueueV2Name = "unitlocation-v2test.dead";
		public static string PersonnelLoactionQueueName = "personnellocationtest";
		public static string SecurityRefreshQueueName = "securityrefreshtest";
		public static string WorkflowQueueName = "workflowqueuetest";
		public static string ChatbotProcessingQueueName = "chatbotprocessingtest";
		public static string CommunicationTestQueueName = "communicationtesttest";
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
		public static string UnitLocationQueueV2Name = "unitlocation-v2";
		public static string UnitLocationRetryQueueV2Name = "unitlocation-v2.retry";
		public static string UnitLocationDeadQueueV2Name = "unitlocation-v2.dead";
		public static string PersonnelLoactionQueueName = "personnellocation";
		public static string SecurityRefreshQueueName = "securityrefresh";
		public static string WorkflowQueueName = "workflowqueue";
		public static string ChatbotProcessingQueueName = "chatbotprocessing";
		public static string CommunicationTestQueueName = "communicationtest";
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
