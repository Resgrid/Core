﻿﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for message management in the Resgrid system
	/// </summary>
	public sealed class MessagesToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<MessagesToolProvider> _logger;
		private readonly List<string> _toolNames;

		public MessagesToolProvider(IApiClient apiClient, ILogger<MessagesToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetInboxTool(server);
			RegisterGetOutboxTool(server);
			RegisterSendMessageTool(server);
			RegisterGetMessageTool(server);
			RegisterDeleteMessageTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetInboxTool(McpServer server)
		{
			const string toolName = "get_inbox";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves all inbox messages for the user",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<TokenArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving inbox messages");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Inbox/GetInbox",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving inbox");
						return CreateErrorResponse("Failed to retrieve inbox. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetOutboxTool(McpServer server)
		{
			const string toolName = "get_outbox";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves all sent messages (outbox) for the user",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<TokenArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving outbox messages");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Messages/GetSentMessages",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving outbox");
						return CreateErrorResponse("Failed to retrieve outbox. Please try again later.");
					}
				}
			);
		}

		private void RegisterSendMessageTool(McpServer server)
		{
			const string toolName = "send_message";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["subject"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Message subject" },
					["body"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Message body/content" },
					["recipients"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "string", Description = "Array of recipient user IDs" }
				},
				new[] { "accessToken", "subject", "body", "recipients" }
			);

			server.AddTool(
				toolName,
				"Sends a message to specified recipients",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<SendMessageArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (string.IsNullOrWhiteSpace(args.Subject))
						{
							return CreateErrorResponse("Subject is required");
						}

						if (string.IsNullOrWhiteSpace(args.Body))
						{
							return CreateErrorResponse("Message body is required");
						}

						if (args.Recipients == null || args.Recipients.Length == 0)
						{
							return CreateErrorResponse("At least one recipient is required");
						}

						_logger.LogInformation("Sending message: {Subject}", args.Subject);

						var messageData = new
						{
							subject = args.Subject,
							body = args.Body,
							recipients = args.Recipients
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Messages/SendMessage",
							messageData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Message sent successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error sending message");
						return CreateErrorResponse("Failed to send message. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetMessageTool(McpServer server)
		{
			const string toolName = "get_message";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["messageId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "The unique identifier of the message" }
				},
				new[] { "accessToken", "messageId" }
			);

			server.AddTool(
				toolName,
				"Retrieves details of a specific message by ID",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<MessageIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving message {MessageId}", args.MessageId);

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Messages/GetMessage?messageId={args.MessageId}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving message");
						return CreateErrorResponse("Failed to retrieve message. Please try again later.");
					}
				}
			);
		}

		private void RegisterDeleteMessageTool(McpServer server)
		{
			const string toolName = "delete_message";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["messageId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "The unique identifier of the message to delete" }
				},
				new[] { "accessToken", "messageId" }
			);

			server.AddTool(
				toolName,
				"Deletes a message",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<MessageIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Deleting message {MessageId}", args.MessageId);

						var success = await _apiClient.DeleteAsync(
							$"/api/v4/Messages/DeleteMessage?messageId={args.MessageId}",
							args.AccessToken
						);

						return new { success, message = success ? "Message deleted successfully" : "Failed to delete message" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error deleting message");
						return CreateErrorResponse("Failed to delete message. Please try again later.");
					}
				}
			);
		}

		private static object CreateErrorResponse(string errorMessage) =>
			new { success = false, error = errorMessage };

		private sealed class TokenArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }
		}

		private sealed class MessageIdArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("messageId")]
			public int MessageId { get; set; }
		}

		private sealed class SendMessageArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("subject")]
			public string Subject { get; set; }

			[JsonProperty("body")]
			public string Body { get; set; }

			[JsonProperty("recipients")]
			public string[] Recipients { get; set; }
		}
	}
}

