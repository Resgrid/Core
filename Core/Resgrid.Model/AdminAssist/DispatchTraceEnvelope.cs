using System;
using System.Text.Json;

namespace Resgrid.Model.AdminAssist
{
	/// <summary>Versioned, bounded queue contract. No arbitrary provider payload or exception text is accepted.</summary>
	public static class DispatchTraceEnvelope
	{
		public const int MaximumBytes = 32768;
		public const string ContentType = "application/vnd.resgrid.admin-assist-trace.v1+json";
		public static void Validate(AdminAssistDispatchTraceRow row)
		{
			if (row == null || !Guid.TryParseExact(row.AdminAssistDispatchTraceId, "D", out _) ||
				!Guid.TryParseExact(row.AttemptId, "D", out _) || row.DepartmentId <= 0 || row.CallId <= 0 ||
				!Enum.TryParse<DispatchTraceStage>(row.Stage, out var stage) || !Enum.IsDefined(stage) ||
				string.IsNullOrWhiteSpace(row.ResolverVersion) || row.ResolverVersion.Length > 64 ||
				row.OccurredOn == default || row.OccurredOn > DateTime.UtcNow.AddMinutes(5) ||
				string.IsNullOrWhiteSpace(row.Content) || row.Content.Length > MaximumBytes)
				throw new ArgumentException("Invalid dispatch trace envelope.");
			if (row.IsProtected)
			{
				if (row.ProtectedCatalogVersion < 30 || !ProtectedDataEnvelope.IsEnveloped(row.Content))
					throw new ArgumentException("Invalid protected dispatch trace envelope.");
				return;
			}
			var observation = JsonSerializer.Deserialize<DispatchTraceObservation>(row.Content) ?? throw new ArgumentException("Missing dispatch trace observation.");
			if (row.ProtectedCatalogVersion != 0 || observation.Id != row.AdminAssistDispatchTraceId ||
				observation.DepartmentId != row.DepartmentId || observation.CallId != row.CallId ||
				observation.AttemptId != row.AttemptId || observation.Stage != stage || observation.OccurredOnUtc != row.OccurredOn ||
				!Enum.IsDefined(observation.Channel) || !Enum.IsDefined(observation.Reason) ||
				!Enum.IsDefined(observation.Provider) || observation.ProviderMessageId != null && !DispatchTraceTelemetry.IsProviderMessageId(observation.Provider, observation.ProviderMessageId) ||
				observation.LogicalMessageId != null && !Guid.TryParseExact(observation.LogicalMessageId, "D", out _) ||
				observation.RouteKind.HasValue && !Enum.IsDefined(observation.RouteKind.Value) ||
				observation.RecipientId?.Length > 128 || observation.SourceId?.Length > 128 ||
				observation.Sequence < 0 || observation.PriorDropped < 0 || observation.ResolverVersion != null && observation.ResolverVersion != row.ResolverVersion)
				throw new ArgumentException("Mismatched dispatch trace observation.");
		}
	}
}
