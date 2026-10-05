namespace Resgrid.Model
{
	/// <summary>
	/// Where a unit state or personnel status was submitted from (UnitStates/ActionLogs.SetByOrigin, M0260), so a report
	/// author can tell a crew's own button press from a dispatcher's or an incident commander's entry. The app values come
	/// from the session's self-asserted client header: they say which app was used, not proof of it. Null on rows written
	/// before M0260.
	/// </summary>
	public enum StatusSetOrigins
	{
		Unknown = 0,
		/// <summary>The Resgrid website.</summary>
		Web = 1,
		ResponderApp = 2,
		UnitApp = 3,
		DispatchApp = 4,
		BigBoard = 5,
		/// <summary>The Incident Command app.</summary>
		CommandApp = 6,
		/// <summary>The MCP endpoint (an AI assistant acting for the member).</summary>
		Mcp = 7,
		/// <summary>Another API client.</summary>
		Api = 8,
		Sms = 9,
		Voice = 10,
		/// <summary>A chat platform through the chatbot.</summary>
		Chat = 11,
		/// <summary>Applied by Resgrid when a call was dispatched or closed (the department's dispatch statuses).</summary>
		DispatchAutomation = 12,
		/// <summary>A scheduled department status reset.</summary>
		Schedule = 13,
		/// <summary>A maintenance safety hold placed or released the unit.</summary>
		Maintenance = 14
	}

	public static class StatusSetOriginsExtensions
	{
		/// <summary>The origin for a request made with a session's client application claim.</summary>
		public static StatusSetOrigins FromClientApplication(UserSessionClientApplication? application)
		{
			switch (application)
			{
				case UserSessionClientApplication.Web: return StatusSetOrigins.Web;
				case UserSessionClientApplication.Responder: return StatusSetOrigins.ResponderApp;
				case UserSessionClientApplication.Unit: return StatusSetOrigins.UnitApp;
				case UserSessionClientApplication.Dispatch: return StatusSetOrigins.DispatchApp;
				case UserSessionClientApplication.BigBoard: return StatusSetOrigins.BigBoard;
				case UserSessionClientApplication.Command: return StatusSetOrigins.CommandApp;
				case UserSessionClientApplication.Mcp: return StatusSetOrigins.Mcp;
				default: return StatusSetOrigins.Api;
			}
		}

		/// <summary>True for the origins where Resgrid, not a person, applied the status.</summary>
		public static bool IsAutomated(this StatusSetOrigins origin)
		{
			return origin == StatusSetOrigins.DispatchAutomation || origin == StatusSetOrigins.Schedule || origin == StatusSetOrigins.Maintenance;
		}
	}
}
