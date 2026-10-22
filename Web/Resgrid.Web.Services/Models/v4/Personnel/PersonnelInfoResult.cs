using System;
using System.Collections.Generic;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;

namespace Resgrid.Web.Services.Models.v4.Personnel
{
	/// <summary>
	/// Result containing all the data required to populate the New Call form
	/// </summary>
	public class PersonnelInfoResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public PersonnelInfoResultData Data { get; set; }
	}

	/// <summary>
	/// Information about a User
	/// </summary>
	public class PersonnelInfoResultData
	{
		/// <summary>
		/// The UserId GUID/UUID for the user
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// DepartmentId of the deparment the user belongs to
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Department specificed ID number for this user
		/// </summary>
		public string IdentificationNumber { get; set; }

		/// <summary>
		/// The Users First Name
		/// </summary>
		public string FirstName { get; set; }

		/// <summary>
		/// The Users Last Name
		/// </summary>
		public string LastName { get; set; }

		/// <summary>
		/// The Users Email Address
		/// </summary>
		public string EmailAddress { get; set; }

		/// <summary>
		/// The Users Mobile Telephone Number
		/// </summary>
		public string MobilePhone { get; set; }

		/// <summary>
		/// GroupId the user is assigned to (0 for no group)
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Name of the group the user is assigned to
		/// </summary>
		public string GroupName { get; set; }

		/// <summary>
		/// Enumeration/List of roles the user currently holds
		/// </summary>
		public List<string> Roles { get; set; }

		/// <summary>
		/// Absolute URL of the person's map icon: the uploaded icon of the first role they hold, in the department's role
		/// order, that has one (a 128 px square PNG, anonymous GET, immutable). Null when none of their roles has an icon;
		/// clients then use their default person marker.
		/// </summary>
		public string RoleIconUrl { get; set; }

		/// <summary>
		/// The current action/status type for the user
		/// </summary>
		public string StatusId { get; set; }

		/// <summary>
		/// The current action/status string for the user
		/// </summary>
		public string Status { get; set; }

		/// <summary>
		/// The current action/status color hex string for the user
		/// </summary>
		public string StatusColor { get; set; }

		/// <summary>
		/// What the current status means (an ActionBaseTypes value: 0 Available, 2 Responding, 3 On Scene, 23 In Quarters,
		/// ...), for built-in and department statuses alike; -1 when the status has no system meaning. Clients decide
		/// availability from this, not from the status text.
		/// </summary>
		public int StatusBaseType { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime StatusTimestamp { get; set; }

		/// <summary>
		/// The current action/status destination id for the user
		/// </summary>
		public string StatusDestinationId { get; set; }

		/// <summary>
		/// The current action/status destination name for the user
		/// </summary>
		public string StatusDestinationName { get; set; }

		/// <summary>
		/// What StatusDestinationId names (DestinationEntityTypes: 1 station, 2 call, 3 POI), when the status recorded it.
		/// </summary>
		public int? StatusDestinationType { get; set; }

		/// <summary>
		/// The current staffing level (state) type for the user
		/// </summary>
		public string StaffingId { get; set; }

		/// <summary>
		/// The current staffing level (state) string for the user
		/// </summary>
		public string Staffing { get; set; }

		/// <summary>
		/// The current staffing level (state) color hex string for the user
		/// </summary>
		public string StaffingColor { get; set; }

		/// <summary>
		/// What the current staffing level means (a StaffingBaseTypes value: 0 Available, 1 Delayed, 2 Unavailable,
		/// 3 Committed, 4 On Shift, 5 Off Duty, 6 On Call), for built-in and department staffing levels alike; -1 when the
		/// level has no system meaning.
		/// </summary>
		public int StaffingBaseType { get; set; }

		/// <summary>
		/// The timestamp of the last state/staffing level. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime StaffingTimestamp { get; set; }

		/// <summary>
		/// Users last known location
		/// </summary>
		public string Location { get; set; }

		/// <summary>
		/// Sorting weight for the user
		/// </summary>
		public int Weight { get; set; }

		/// <summary>
		/// User Defined Field values for this personnel record
		/// </summary>
		public List<UdfFieldValueResultData> UdfValues { get; set; }
	}
}
