﻿using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Resgrid.Web.Mcp.Tools;

namespace Resgrid.Web.Mcp
{
	/// <summary>
	/// Registry that manages and registers all MCP tools with the server
	/// </summary>
	public sealed class McpToolRegistry
	{
		private readonly ILogger<McpToolRegistry> _logger;
		private readonly AuthenticationToolProvider _authTools;
		private readonly CallsToolProvider _callsTools;
		private readonly DispatchToolProvider _dispatchTools;
		private readonly PersonnelToolProvider _personnelTools;
		private readonly UnitsToolProvider _unitsTools;
		private readonly MessagesToolProvider _messagesTools;
		private readonly CalendarToolProvider _calendarTools;
		private readonly ShiftsToolProvider _shiftsTools;
		private readonly InventoryToolProvider _inventoryTools;
		private readonly ReportsToolProvider _reportsTools;
		private readonly List<string> _registeredTools;

		public McpToolRegistry(
			ILogger<McpToolRegistry> logger,
			AuthenticationToolProvider authTools,
			CallsToolProvider callsTools,
			DispatchToolProvider dispatchTools,
			PersonnelToolProvider personnelTools,
			UnitsToolProvider unitsTools,
			MessagesToolProvider messagesTools,
			CalendarToolProvider calendarTools,
			ShiftsToolProvider shiftsTools,
			InventoryToolProvider inventoryTools,
			ReportsToolProvider reportsTools)
		{
			_logger = logger;
			_authTools = authTools;
			_callsTools = callsTools;
			_dispatchTools = dispatchTools;
			_personnelTools = personnelTools;
			_unitsTools = unitsTools;
			_messagesTools = messagesTools;
			_calendarTools = calendarTools;
			_shiftsTools = shiftsTools;
			_inventoryTools = inventoryTools;
			_reportsTools = reportsTools;
			_registeredTools = new List<string>();
		}

		/// <summary>
		/// Registers all tools with the MCP server
		/// </summary>
		public void RegisterTools(McpServer server)
		{
			_logger.LogInformation("Registering MCP tools...");

			// Register Authentication tools
			_authTools.RegisterTools(server);
			_registeredTools.AddRange(_authTools.GetToolNames());

			// Register Calls tools
			_callsTools.RegisterTools(server);
			_registeredTools.AddRange(_callsTools.GetToolNames());

			// Register Dispatch tools
			_dispatchTools.RegisterTools(server);
			_registeredTools.AddRange(_dispatchTools.GetToolNames());

			// Register Personnel tools
			_personnelTools.RegisterTools(server);
			_registeredTools.AddRange(_personnelTools.GetToolNames());

			// Register Units tools
			_unitsTools.RegisterTools(server);
			_registeredTools.AddRange(_unitsTools.GetToolNames());

			// Register Messages tools
			_messagesTools.RegisterTools(server);
			_registeredTools.AddRange(_messagesTools.GetToolNames());

			// Register Calendar tools
			_calendarTools.RegisterTools(server);
			_registeredTools.AddRange(_calendarTools.GetToolNames());

			// Register Shifts tools
			_shiftsTools.RegisterTools(server);
			_registeredTools.AddRange(_shiftsTools.GetToolNames());

			// Register Inventory tools
			_inventoryTools.RegisterTools(server);
			_registeredTools.AddRange(_inventoryTools.GetToolNames());

			// Register Reports tools
			_reportsTools.RegisterTools(server);
			_registeredTools.AddRange(_reportsTools.GetToolNames());

			_logger.LogInformation("Registered {Count} MCP tools across {ProviderCount} providers",
				_registeredTools.Count, 10);
		}

		/// <summary>
		/// Gets the count of registered tools
		/// </summary>
		public int GetToolCount() => _registeredTools.Count;

		/// <summary>
		/// Gets the names of all registered tools
		/// </summary>
		public IReadOnlyList<string> GetRegisteredTools() => _registeredTools.AsReadOnly();
	}
}







