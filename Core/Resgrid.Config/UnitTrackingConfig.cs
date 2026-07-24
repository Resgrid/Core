namespace Resgrid.Config
{
	public static class UnitTrackingConfig
	{
		public static bool Enabled = false;
		public static bool HttpsIngressEnabled = false;
		public static bool NativeGatewayEnabled = false;
		public static string CredentialPepper = "";
		public static string PublicHttpsBaseUrl = "";
		public static int MaxRequestBytes = 262144;
		public static int MaxBatchPositions = 100;
		public static int MaxJsonDepth = 16;
		public static int MaxFutureSkewSeconds = 300;
		public static int DefaultLocationRetentionDays = 90;
		public static int MinimumLocationRetentionDays = 1;
		public static int MaximumLocationRetentionDays = 3650;
		public static int PerDeviceRequestsPerMinute = 120;
		public static int PerDeviceRecordsPerMinute = 1200;
		public static int UnknownEndpointRequestsPerMinute = 30;
		public static int CredentialCacheSeconds = 60;
		public static int CredentialRotationOverlapHours = 24;
		public static int DeviceMappingCacheSeconds = 300;
		public static int UnknownDeviceCacheSeconds = 30;
		public static int QueueMessageTtlSeconds = 86400;
		public static int QueuePublishTimeoutSeconds = 5;
		public static int UnitLocationQueuePrefetchCount = 25;
		public static int UnitLocationRetryDelaySeconds = 30;
		public static int UnitLocationMaxRetryAttempts = 3;
		public static int TcpIdleTimeoutSeconds = 300;
		public static int MaxFrameBytes = 65536;
		public static int MaxConnections = 5000;
		public static int MaxConnectionsPerIp = 100;
		public static int GracefulShutdownSeconds = 30;
		public static int InternalHealthPort = 8080;
		public static int QueclinkTcpPort = 5004;
		public static int QueclinkUdpPort = 5004;
		public static int Gt06TcpPort = 5023;
		public static int Gt06UdpPort = 5023;
		public static int TeltonikaTcpPort = 5027;
		public static int TeltonikaUdpPort = 5027;
		public static bool EnableQueclink = false;
		public static bool EnableGt06 = false;
		public static bool EnableTeltonika = false;
		public static bool RawDiagnosticCaptureEnabled = false;
	}
}
