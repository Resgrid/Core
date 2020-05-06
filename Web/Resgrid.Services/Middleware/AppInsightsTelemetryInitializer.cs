using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Resgrid.Web.Services.Middleware
{
	public class AppInsightsTelemetryInitializer : ITelemetryInitializer
	{
		public void Initialize(ITelemetry telemetry)
		{
			switch (telemetry)
			{
				case RequestTelemetry request when request.ResponseCode == "404": // i.e. REST api calls (GetAvatar) return 404
					request.Success = true;
					break;
				case RequestTelemetry request when request.ResponseCode == "426": // 426 is used when the auth token is expired
					request.Success = true;
					break;
			}
		}
	}
}
