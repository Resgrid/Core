using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Resgrid.Web.Mcp.Infrastructure;

namespace Resgrid.Web.Mcp.Controllers
{
	/// <summary>
	/// HTTP controller that exposes the MCP JSON-RPC interface over HTTP
	/// </summary>
	[ApiController]
	[Route("mcp")]
	public sealed class McpController : ControllerBase
	{
		private readonly IMcpRequestHandler _mcpHandler;
		private readonly ILogger<McpController> _logger;

		// Maximum request body size: 1MB
		private const long MaxRequestBodySize = 1_048_576;

		public McpController(IMcpRequestHandler mcpHandler, ILogger<McpController> logger)
		{
			_mcpHandler = mcpHandler;
			_logger = logger;
		}

		/// <summary>
		/// Handles MCP JSON-RPC requests over HTTP POST
		/// </summary>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>JSON-RPC response</returns>
		[HttpPost]
		[Consumes("application/json")]
		[Produces("application/json")]
		[RequestSizeLimit(MaxRequestBodySize)]
		public async Task<IActionResult> HandleRequest(CancellationToken cancellationToken)
		{
			try
			{
				// Validate request body size to prevent unbounded memory allocation
				var contentLength = Request.ContentLength;
				if (contentLength.HasValue && contentLength.Value > MaxRequestBodySize)
				{
					_logger.LogWarning("Request body size {Size} bytes exceeds maximum allowed size {MaxSize} bytes",
						contentLength.Value, MaxRequestBodySize);
					return StatusCode(StatusCodes.Status413PayloadTooLarge, new
					{
						jsonrpc = "2.0",
						id = (object)null,
						error = new
						{
							code = -32600,
							message = $"Request body too large. Maximum size is {MaxRequestBodySize} bytes."
						}
					});
				}

			// Read the raw request body
			using var reader = new StreamReader(Request.Body);
			var requestBody = await reader.ReadToEndAsync(cancellationToken);

				if (string.IsNullOrWhiteSpace(requestBody))
				{
					_logger.LogWarning("Received empty request body");
					return BadRequest(new
					{
						jsonrpc = "2.0",
						id = (object)null,
						error = new
						{
							code = -32600,
							message = "Invalid Request: Empty request body"
						}
					});
			}

			var redactedRequest = SensitiveDataRedactor.RedactSensitiveFields(requestBody);
			_logger.LogDebug("Received MCP request: {Request}", redactedRequest);

			// Process the request through the MCP handler
			var response = await _mcpHandler.HandleRequestAsync(requestBody, cancellationToken);

			var redactedResponse = SensitiveDataRedactor.RedactSensitiveFields(response);
			_logger.LogDebug("Sending MCP response: {Response}", redactedResponse);

			// Return the JSON-RPC response
				return Content(response, "application/json");
			}
			catch (JsonException ex)
			{
				_logger.LogError(ex, "Failed to parse JSON-RPC request");
				return BadRequest(new
				{
					jsonrpc = "2.0",
					id = (object)null,
					error = new
					{
						code = -32700,
						message = "Parse error",
						data = ex.Message
					}
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unexpected error handling MCP request");
				return StatusCode(StatusCodes.Status500InternalServerError, new
				{
					jsonrpc = "2.0",
					id = (object)null,
					error = new
					{
						code = -32603,
						message = "Internal error"
					}
				});
			}
		}

		/// <summary>
		/// GET endpoint for basic connectivity check
		/// </summary>
		[HttpGet]
		public IActionResult Get()
		{
			return Ok(new
			{
				service = "Resgrid MCP Server",
				version = "1.0.0",
				protocol = "Model Context Protocol",
				transport = "HTTP",
				endpoint = "/mcp",
				method = "POST"
			});
		}
	}
}

