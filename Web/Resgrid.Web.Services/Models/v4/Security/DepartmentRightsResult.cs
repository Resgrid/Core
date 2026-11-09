using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Security
{
	public class DepartmentRightsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public DepartmentRightsResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public DepartmentRightsResult()
		{
			Data = new DepartmentRightsResultData();
		}
	}

	/// <summary>
	/// Object that denotes the right assignments for a user in a department
	/// </summary>
	public class DepartmentRightsResultData
	{
		/// <summary>
		/// Department name
		/// </summary>
		public string DepartmentName { get; set; }

		/// <summary>
		/// Department code
		/// </summary>
		public string DepartmentCode { get; set; }

		/// <summary>
		/// Novu subscriberHash for the user's own inbox subscriber ({DepartmentCode}_User_{userId}, Responder and
		/// Dispatch apps). Pass it with the subscriber id when opening the inbox; required once the Novu
		/// environment enforces HMAC. Null when Novu is not configured.
		/// </summary>
		public string NovuSubscriberHash { get; set; }

		/// <summary>
		/// Novu subscriberHash for the user's IC app inbox subscriber ({DepartmentCode}_IC_User_{userId}).
		/// Null when Novu is not configured.
		/// </summary>
		public string NovuICSubscriberHash { get; set; }

		/// <summary>
		/// Users full name
		/// </summary>
		public string FullName { get; set; }

		/// <summary>
		/// Email address
		/// </summary>
		public string EmailAddress { get; set; }

		/// <summary>
		/// Department id
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Is the user a department admin
		/// </summary>
		public bool IsAdmin { get; set; }

		/// <summary>
		/// Can the user view PII
		/// </summary>
		public bool CanViewPII { get; set; }

		/// <summary>
		/// Can the user create calls
		/// </summary>
		public bool CanCreateCalls { get; set; }

		/// <summary>
		/// Can the user close calls: the Call_Update right (from the Create Call permission) and the Close Call
		/// permission. Department-wide: a Close Call permission locked to the user's group can still refuse a call
		/// that group was not dispatched to (CloseCall checks each call).
		/// </summary>
		public bool CanCloseCalls { get; set; }

		/// <summary>
		/// Can the user delete calls: the Call_Delete right (from the Create Call permission) and the Delete Call
		/// permission. Department-wide: a Delete Call permission locked to the user's group can still refuse a call
		/// that group was not dispatched to (DeleteCall checks each call).
		/// </summary>
		public bool CanDeleteCalls { get; set; }

		/// <summary>
		/// Can the user add a note
		/// </summary>
		public bool CanAddNote { get; set; }

		/// <summary>
		/// Can the user create messages
		/// </summary>
		public bool CanCreateMessage { get; set; }

		/// <summary>
		/// Can the user create/edit workflows
		/// </summary>
		public bool CanCreateWorkflow { get; set; }

		/// <summary>
		/// Can the user sign in to the Dispatch app. Dispatch exposes private command, unit and responder
		/// traffic, so departments can restrict it to admins or selected roles; defaults to everyone.
		/// </summary>
		public bool CanLoginToDispatchApp { get; set; }

		/// <summary>
		/// Can the user run incident command: sign in to the IC app, establish command, and work the boards
		/// where they hold incident capabilities (per board: IncidentRoles/GetMyCapabilities). Restrictable to
		/// admins or selected roles; defaults to everyone.
		/// </summary>
		public bool CanLoginToCommandApp { get; set; }

		/// <summary>
		/// Can the user read every command board in the department (Security &gt; View Command Boards). Without it
		/// a member still reads the boards of incidents they are on. Restrictable to admins or selected roles; with
		/// no setting of its own it follows <see cref="CanLoginToCommandApp"/>'s setting, and with neither, everyone.
		/// </summary>
		public bool CanViewCommandBoards { get; set; }

		/// <summary>
		/// Can the user manage workflow credentials
		/// </summary>
		public bool CanManageWorkflowCredentials { get; set; }

		/// <summary>
		/// Can the user view workflow runs
		/// </summary>
		public bool CanViewWorkflowRuns { get; set; }

		/// <summary>
		/// Groups in the department the user is a member of
		/// </summary>
		public List<GroupRightData> Groups { get; set; }
	}

	/// <summary>
	/// Object containting a group right assignemnt
	/// </summary>
	public class GroupRightData
	{
		/// <summary>
		/// Id of the group this right assignement is for
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Is the user a group admin
		/// </summary>
		public bool IsGroupAdmin { get; set; }
	}
}
