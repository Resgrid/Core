using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Audit logging service for tracking MCP operations
	/// </summary>
	public interface IAuditLogger
	{
		Task LogOperationAsync(AuditEntry entry);
		Task LogAuthenticationAsync(string userId, bool success, string ipAddress);
		Task LogToolCallAsync(string userId, string toolName, object arguments, bool success, string error = null);
	}

	public sealed class AuditLogger : IAuditLogger
	{
		private readonly ILogger<AuditLogger> _logger;
		private readonly IApiClient _apiClient;

		public AuditLogger(ILogger<AuditLogger> logger, IApiClient apiClient)
		{
			_logger = logger;
			_apiClient = apiClient;
		}

		public async Task LogOperationAsync(AuditEntry entry)
		{
			try
			{
				// Log locally
				_logger.LogInformation(
					"AUDIT: User={UserId}, Operation={Operation}, Success={Success}, Duration={Duration}ms",
					entry.UserId,
					entry.Operation,
					entry.Success,
					entry.DurationMs
				);

				// Could also send to external audit service or database
				// await _apiClient.PostAsync<AuditEntry, object>("/api/v4/Audit/LogEntry", entry, entry.AccessToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error writing audit log");
			}
		}

		public async Task LogAuthenticationAsync(string userId, bool success, string ipAddress)
		{
			var entry = new AuditEntry
			{
				UserId = userId,
				Operation = "Authentication",
				Success = success,
				Timestamp = DateTime.UtcNow,
				IpAddress = ipAddress,
				Details = new { userId, success }
			};

			await LogOperationAsync(entry);
		}

		public async Task LogToolCallAsync(string userId, string toolName, object arguments, bool success, string error = null)
		{
			var entry = new AuditEntry
			{
				UserId = userId,
				Operation = $"ToolCall:{toolName}",
				Success = success,
				Timestamp = DateTime.UtcNow,
				Details = new
				{
					toolName,
					arguments,
					error
				}
			};

			await LogOperationAsync(entry);
		}
	}

	public sealed class AuditEntry
	{
		public string UserId { get; set; }
		public string Operation { get; set; }
		public bool Success { get; set; }
		public DateTime Timestamp { get; set; }
		public string IpAddress { get; set; }
		public object Details { get; set; }
		public long DurationMs { get; set; }
		public string AccessToken { get; set; }

		public override string ToString()
		{
			return JsonConvert.SerializeObject(this, Formatting.None);
		}
	}
}

