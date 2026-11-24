﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Data needed to create a new call
	/// </summary>
	public class EditCallInput
	{
		/// <summary>
		/// Id of the call to update
		/// </summary>
		[Required]
		public string Id { get; set; }

		/// <summary>
		/// Priority of the call
		/// </summary>
		[Required]
		public int Priority { get; set; }

		/// <summary>
		/// Name of the call
		/// </summary>
		[Required]
		public string Name { get; set; }

		/// <summary>
		/// Nature of the call
		/// </summary>
		[Required]
		public string Nature { get; set; }

		/// <summary>
		/// Dispatch note
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Address
		/// </summary>
		public string Address { get; set; }

		/// <summary>
		/// Optional destination POI id for transfers, transports, and relocations.
		/// </summary>
		public int? DestinationPoiId { get; set; }

		/// <summary>
		/// The POI (any type) the call location was picked from (M0322). Null keeps the stored one; 0 clears it.
		/// </summary>
		public int? LocationPoiId { get; set; }

		/// <summary>
		/// Free destination address (M0322). Null keeps the stored one; an empty string clears it. A destination POI clears it.
		/// </summary>
		public string DestinationAddress { get; set; }

		/// <summary>
		/// Free destination point "lat,lon" (M0322). Null keeps the stored one; an empty string clears it.
		/// </summary>
		public string DestinationGeolocation { get; set; }

		/// <summary>
		/// Free destination what3words address (M0322). Null keeps the stored one; an empty string clears it.
		/// </summary>
		public string DestinationWhat3Words { get; set; }

		/// <summary>
		/// The pre-defined route the call follows (M0322). Null keeps the stored one; an empty string clears it.
		/// </summary>
		public string RoutePlanId { get; set; }

		/// <summary>
		/// Geolocation data "lat,lon"
		/// </summary>
		public string Geolocation { get; set; }

		/// <summary>
		/// Type of the call
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// What 3 Words location
		/// </summary>
		public string What3Words { get; set; }

		/// <summary>
		/// Pipe separated list of users, units, roles and groups to dispatch ("P:", "U:", "R:" and "G:" prefixes), replacing
		/// the call's dispatches. "0" adds everyone, and is the only way to. Left blank, the call keeps its dispatches. A list
		/// with no entries ("|") removes every dispatch, except those of a recipient list the department hides.
		/// </summary>
		public string DispatchList { get; set; }

		/// <summary>
		/// Optional (conditional run cards). Pipe separated per-unit response codes, "unitId:codeId", from the department's
		/// response code catalog; "unitId:" clears a unit's code. Units not listed keep the code the run card proposed.
		/// </summary>
		public string UnitResponseCodes { get; set; }

		/// <summary>
		/// Contact Name
		/// </summary>
		public string ContactName { get; set; }

		/// <summary>
		/// Contact Info
		/// </summary>
		public string ContactInfo { get; set; }

		/// <summary>
		/// Id of the primary Contact (the premises/customer record) to link to the call. Optional;
		/// the contact must belong to the department. Contacts plan Phase A (A5).
		/// </summary>
		public string ContactId { get; set; }

		/// <summary>
		/// Ids of additional Contacts to link to the call. Optional. On EditCall, supplying ContactId or
		/// AdditionalContactIds (even empty) replaces the call's existing contact links; omitting both leaves them unchanged.
		/// </summary>
		public List<string> AdditionalContactIds { get; set; }

		/// <summary>
		/// External Call Id
		/// </summary>
		public string ExternalId { get; set; }

		/// <summary>
		/// External subject and record identifiers, for example { "ehr_client_id": "123456" }. Keys match ^[a-z0-9_]{1,64}$,
		/// values are at most 256 characters, at most 20 keys. Protected: encrypted at rest in an Advanced Data Protection
		/// department. On an edit, omit it (null) to leave the stored identifiers unchanged; an empty object clears them.
		/// </summary>
		public Dictionary<string, string> SubjectIdentifiers { get; set; }

		/// <summary>
		/// The department has 42 CFR Part 2 consent (or another Part 2 basis) on file for this call. On an edit, omit it to leave
		/// the stored value unchanged.
		/// </summary>
		public bool? Part2ConsentOnFile { get; set; }

		/// <summary>
		/// Incident Id
		/// </summary>
		public string IncidentId { get; set; }

		/// <summary>
		/// Reference Id
		/// </summary>
		public string ReferenceId { get; set; }

		/// <summary>
		/// Optional. The map book page for the call's location. Null or blank keeps the stored page; leave it out (null) when
		/// the client has no input for it, so the department's map page requirement is not enforced against it.
		/// </summary>
		public string MapPage { get; set; }

		/// <summary>
		/// Optional. Indoor map zone the incident is in. Null (left out) keeps the stored zone and floor — leave it out when the
		/// client has no indoor location picker, which also leaves the department's indoor location requirement unenforced; an
		/// empty string clears both, the same as <see cref="RoutePlanId"/>.
		/// </summary>
		public string IndoorMapZoneId { get; set; }

		/// <summary>
		/// Optional. Floor of <see cref="IndoorMapZoneId"/>, read only when that zone is sent. With a non-empty zone the floor is
		/// replaced by this value: left out or blank stores no floor (it does not keep the stored one), so send the floor again
		/// with the zone to keep it. With the zone left out (null) both stay; with an empty zone both are cleared.
		/// </summary>
		public string IndoorMapFloorId { get; set; }

		/// <summary>
		/// Optional. Ids of the department's dispatch protocols to add to the call; protocols already on it are kept. Leave
		/// it out (null) when the client has no protocol picker, which also leaves the protocols requirement unenforced.
		/// </summary>
		public List<int> ProtocolIds { get; set; }

		/// <summary>
		/// Optional. Id of another call in the department to link this call to; existing links are kept. Leave it out
		/// (null) when the client has no way to pick one, which also leaves the linked-call requirement unenforced.
		/// </summary>
		public string LinkedCallId { get; set; }

		/// <summary>
		/// Time in the future, in the departments local time, to dispatch the call
		/// </summary>
		public DateTime? DispatchOn { get; set; }

		/// <summary>
		/// Optional. The same as DispatchOn but in UTC (ISO 8601, e.g. "2026-10-07T14:30:00Z"), for clients that do not know
		/// the department's time zone. When both are sent this one is used.
		/// </summary>
		public DateTime? DispatchOnUtc { get; set; }

		/// <summary>
		/// Call Intake form JSON
		/// </summary>
		public string CallFormData { get; set; }

		/// <summary>
		/// Should all the entities attached to the call be re-notified
		/// </summary>
		public bool RebroadcastCall { get; set; }

		/// <summary>
		/// If true, entities removed from the dispatch list will receive a cancellation notification
		/// </summary>
		public bool NotifyCancelledEntities { get; set; }

		/// <summary>
		/// User Defined Field values for this call
		/// </summary>
		public List<UdfFieldValueInput> UdfValues { get; set; }
	}
}
