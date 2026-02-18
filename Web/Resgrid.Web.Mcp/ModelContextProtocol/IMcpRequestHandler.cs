﻿using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Mcp.ModelContextProtocol
{
	/// <summary>
	/// Interface for handling MCP JSON-RPC requests
	/// </summary>
	public interface IMcpRequestHandler
	{
		/// <summary>
		/// Handles a JSON-RPC request and returns a JSON-RPC response
		/// </summary>
		/// <param name="requestJson">The JSON-RPC request as a string</param>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>The JSON-RPC response as a string</returns>
		Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken);
	}
}

