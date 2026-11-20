using Resgrid.Web.Services.Models.v4.CallPriorities;
using Resgrid.Web.Services.Models.v4.CallProtocols;
using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Depicts a call in the Resgrid system.
	/// </summary>
	public class CallExtraDataResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CallExtraDataResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallExtraDataResult()
		{
			Data = new CallExtraDataResultData();
		}
	}

	/// <summary>
	/// Depicts a call in the Resgrid system.
	/// </summary>
	public class CallExtraDataResultData
	{
		/// <summary>
		/// Call form data that was filled out during call creation.
		/// </summary>
		public string CallFormData { get; set; }

		/// <summary>
		/// Unit and Personnel activities attached to this call
		/// </summary>
		public List<DispatchedEventResultData> Activity { get; set; }

		/// <summary>
		/// Who was dispatched on this call, units, personnel, roles and groups
		/// </summary>
		public List<DispatchedEventResultData> Dispatches { get; set; }

		/// <summary>
		/// Units released (stood down) from this call while it stayed open: kept on the call's record but no longer on it, so
		/// they are left out of <see cref="Dispatches"/> (an older client pre-selecting Dispatches on an edit would otherwise
		/// send a stood-down unit back to the call). Entries carry <see cref="DispatchedEventResultData.ReleasedOn"/>.
		/// </summary>
		public List<DispatchedEventResultData> ReleasedUnits { get; set; }

		/// <summary>
		/// Members released (stood down) from this call while it stayed open, however they were reached (by name, a dispatched group
		/// or role, a unit's crew, or a status). Left out of <see cref="Dispatches"/> when dispatched by name; a group or role entry
		/// stays there, so clients subtract these members from those they show on the call. Entries carry
		/// <see cref="DispatchedEventResultData.ReleasedOn"/>.
		/// </summary>
		public List<DispatchedEventResultData> ReleasedPersonnel { get; set; }

		/// <summary>
		/// Call priority inforamtion
		/// </summary>
		public CallPriorityResultData Priority { get; set; }

		/// <summary>
		/// Protocols active fro this call
		/// </summary>
		public List<CallProtocolResultData> Protocols { get; set; }

		/// <summary>
		/// Runs of the call's route started by the units working it (M0322), newest first.
		/// </summary>
		public List<CallRouteInstanceResultData> RouteInstances { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallExtraDataResultData()
		{
			Activity = new List<DispatchedEventResultData>();
			Dispatches = new List<DispatchedEventResultData>();
			ReleasedUnits = new List<DispatchedEventResultData>();
			ReleasedPersonnel = new List<DispatchedEventResultData>();
			Protocols = new List<CallProtocolResultData>();
			RouteInstances = new List<CallRouteInstanceResultData>();
		}
	}

	/// <summary>A run of the call's route by one unit (M0322).</summary>
	public class CallRouteInstanceResultData
	{
		public string RouteInstanceId { get; set; }

		public string RoutePlanId { get; set; }

		public string RoutePlanName { get; set; }

		public int UnitId { get; set; }

		public string UnitName { get; set; }

		/// <summary>RouteInstanceStatus: 0 Scheduled, 1 InProgress, 2 Completed, 3 Cancelled, 4 Abandoned, 5 Paused.</summary>
		public int Status { get; set; }

		public int StopsCompleted { get; set; }

		public int StopsTotal { get; set; }

		/// <summary>ISO 8601 UTC ("Z"); null when not started.</summary>
		public string ActualStartOn { get; set; }

		/// <summary>ISO 8601 UTC ("Z"); null while running.</summary>
		public string ActualEndOn { get; set; }

		/// <summary>When the unit reached the plan start, ISO 8601 UTC ("Z"); null until then.</summary>
		public string StartReachedOn { get; set; }
	}

	public class DispatchedEventResultData
	{
		public string Id { get; set; }
		public DateTime Timestamp { get; set; }
		public string Type { get; set; }
		public string Name { get; set; }
		public string GroupId { get; set; }
		public string Group { get; set; }
		public string Note { get; set; }
		public int StatusId { get; set; }
		public string Location { get; set; }
		public string StatusText { get; set; }
		public string StatusColor { get; set; }

		/// <summary>
		/// For status activity: how the status came to be on this call (StatusDestinationSources: 1 chosen by the sender,
		/// 2 carried forward from the previous status's call, 3 from the one open dispatch, 4 from the unit the person rode,
		/// 5 inferred from a dispatched unit's/person's status that named no destination). Null for older rows and for
		/// non-status entries.
		/// </summary>
		public int? DestinationSource { get; set; }

		/// <summary>
		/// Unit entries only: absolute URL of the department's uploaded icon for the unit's type (a 128 px square PNG,
		/// anonymous GET, immutable); null for other entries and when the type has no uploaded icon.
		/// </summary>
		public string TypeIconUrl { get; set; }

		/// <summary>
		/// Unit entries only: built-in icon name the unit's type picked (e.g. "ambulance"); null when none.
		/// </summary>
		public string TypeMapIcon { get; set; }

		/// <summary>
		/// Unit dispatch entries only (conditional run cards): the response code the unit was sent with ("C3"), its name and
		/// colour. Null unless the department shows response codes to crews.
		/// </summary>
		public string ResponseCode { get; set; }

		public string ResponseCodeName { get; set; }

		public string ResponseCodeColor { get; set; }

		/// <summary>
		/// <see cref="CallExtraDataResultData.ReleasedUnits"/> and <see cref="CallExtraDataResultData.ReleasedPersonnel"/>
		/// entries only: when the unit or member was released from the call, ISO 8601 UTC ("Z"); null elsewhere.
		/// </summary>
		public string ReleasedOn { get; set; }

		/// <summary>
		/// <see cref="CallExtraDataResultData.ReleasedUnits"/> and <see cref="CallExtraDataResultData.ReleasedPersonnel"/>
		/// entries only: the name of who released the unit or member; null elsewhere, and null when that member cannot be found.
		/// </summary>
		public string ReleasedBy { get; set; }
	}
}
