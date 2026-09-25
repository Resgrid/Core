using System;

namespace Resgrid.Web.Mcp.ModelContextProtocol
{
	/// <summary>
	/// A tool failure the MCP client can act on, such as refreshing an expired access token. McpServer returns it as the
	/// tool's result ({ success = false, errorCode, error }) instead of the generic failure message the tool's own
	/// catch-all would give, so tool handlers must let it pass: catch (Exception ex) when (ex is not McpToolErrorException).
	/// </summary>
	public sealed class McpToolErrorException : Exception
	{
		/// <summary>The access token was rejected (expired, or its session revoked): refresh it, then retry.</summary>
		public const string AccessTokenExpired = "access_token_expired";

		/// <summary>The user is signed in but not permitted to do this; a new token will not help.</summary>
		public const string Forbidden = "forbidden";

		/// <summary>The client made too many tool calls in the last minute: wait, then retry.</summary>
		public const string RateLimited = "rate_limited";

		public McpToolErrorException(string errorCode, string message)
			: base(message)
		{
			ErrorCode = errorCode;
		}

		public string ErrorCode { get; }
	}
}
