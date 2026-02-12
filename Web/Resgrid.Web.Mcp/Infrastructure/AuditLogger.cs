﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

		// Sensitive field names that should be redacted from audit logs
		private static readonly HashSet<string> SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"password",
			"accessToken",
			"token",
			"secret",
			"apiKey",
			"authorization",
			"bearer",
			"credential",
			"privateKey"
		};

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
					arguments = SanitizeSensitiveData(arguments),
					error
				}
			};

			await LogOperationAsync(entry);
		}

		/// <summary>
		/// Sanitizes sensitive data from objects before logging
		/// </summary>
		/// <param name="data">The data to sanitize</param>
		/// <returns>A sanitized copy of the data with sensitive fields redacted</returns>
		internal static object SanitizeSensitiveData(object data)
		{
			if (data == null)
				return null;

			try
			{
				// Convert to JToken for easier manipulation
				var json = JsonConvert.SerializeObject(data);
				var jToken = JToken.Parse(json);

				AuditLogger.SanitizeJToken(jToken);

				// Convert back to object
				return JsonConvert.DeserializeObject(jToken.ToString());
			}
			catch (JsonException)
			{
				// If JSON serialization/deserialization fails, return a safe placeholder
				// Note: Logger is not available in static context, but we return a safe error object
				return new { sanitized = true, error = "Unable to sanitize data" };
			}
		}

		/// <summary>
		/// Recursively sanitizes sensitive fields in a JToken
		/// </summary>
		private static void SanitizeJToken(JToken token)
		{
			if (token == null)
				return;

			switch (token.Type)
			{
				case JTokenType.Object:
					var obj = (JObject)token;
					var properties = obj.Properties().ToList();

					foreach (var prop in properties)
					{
						if (SensitiveFields.Contains(prop.Name))
						{
							// Redact sensitive fields
							prop.Value = JValue.CreateString("***REDACTED***");
						}
						else
						{
							// Recursively sanitize nested objects
							SanitizeJToken(prop.Value);
						}
					}
					break;

				case JTokenType.Array:
					foreach (var item in (JArray)token)
					{
						SanitizeJToken(item);
					}
					break;
			}
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

		/// <summary>
		/// Access token for API calls - excluded from serialization for security
		/// </summary>
		[JsonIgnore]
		public string AccessToken { get; set; }

		public override string ToString()
		{
			// Explicitly create object without AccessToken to prevent token leakage in logs
			// Sanitize Details to redact sensitive fields like passwords and tokens
			var safeObject = new
			{
				UserId,
				Operation,
				Success,
				Timestamp,
				IpAddress,
				Details = AuditLogger.SanitizeSensitiveData(Details),
				DurationMs
			};

			return JsonConvert.SerializeObject(safeObject, Formatting.None);
		}
	}
}

