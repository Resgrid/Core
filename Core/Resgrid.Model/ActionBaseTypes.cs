using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace Resgrid.Model
{
	/// <summary>
	/// Canonical, system-level "base type" that a Custom State (Custom Status) option maps to for both
	/// Personnel and Units. A department can name and color their custom statuses however they like
	/// (e.g. "Rolling", "Wheels Up", "Bus 12 to Mercy"), but the base type is how Resgrid understands
	/// what that status actually *means* operationally so it can drive system-level logic such as
	/// availability/utilization reporting, automations and downstream integrations.
	///
	/// The chosen value is stored on <c>CustomStateDetail.BaseType</c> and is resolved to an
	/// <see cref="Resgrid.Model.Reporting.AvailabilityClass"/> through
	/// <see cref="Resgrid.Model.Reporting.AvailabilityMatrix.ForCustomBaseType"/>. When you add a value
	/// here you MUST also classify it in <see cref="Resgrid.Model.Reporting.AvailabilityMatrix"/>,
	/// otherwise it will be treated as <c>Unknown</c> for availability purposes.
	///
	/// The <see cref="DescriptionAttribute"/> on each member is the user-facing explanation surfaced in
	/// the add/edit Custom Status screens; keep it in sync with the help text shown in the UI. The
	/// <see cref="DisplayAttribute"/> is the short label used in drop-downs.
	/// </summary>
	public enum ActionBaseTypes
	{
		/// <summary>
		/// No canonical meaning. The custom status is treated as informational/labelling only and does
		/// not drive availability, utilization or any automated system-level logic.
		/// </summary>
		[Description("No system meaning. The status is informational only and does not affect availability, reporting or automations.")]
		[Display(Name = "None")]
		None = -1,

		/// <summary>
		/// The resource is in service and available to be assigned or dispatched. Classified as Available.
		/// </summary>
		[Description("The unit or responder is in service and available to be assigned or dispatched to a call or task.")]
		[Display(Name = "Available")]
		Available = 0,

		/// <summary>
		/// The responder has acknowledged the assignment but is declining or unable to respond.
		/// Classified as Unavailable.
		/// </summary>
		[Description("The responder has acknowledged but is declining or is unable to respond to the current assignment.")]
		[Display(Name = "Not Responding")]
		NotResponding = 1,

		/// <summary>
		/// The resource is actively responding (en route) to an assigned call or incident, typically as
		/// an emergency response. Classified as Committed.
		/// </summary>
		[Description("The unit or responder is actively responding (en route) to an assigned call or incident.")]
		[Display(Name = "Responding")]
		Responding = 2,

		/// <summary>
		/// The resource has arrived and is operating at the scene of a call or incident. Classified as Committed.
		/// </summary>
		[Description("The unit or responder has arrived and is operating at the scene of a call or incident.")]
		[Display(Name = "On Scene")]
		OnScene = 3,

		/// <summary>
		/// The resource has made contact with the subject, patient, party or point of interest.
		/// Classified as Committed.
		/// </summary>
		[Description("The unit or responder has made contact with the subject, patient, party or point of interest.")]
		[Display(Name = "Made Contact")]
		MadeContact = 4,

		/// <summary>
		/// The resource is investigating, assessing or performing size-up of the situation. Classified as Committed.
		/// </summary>
		[Description("The unit or responder is investigating, assessing or sizing up the situation.")]
		[Display(Name = "Investigating")]
		Investigating = 5,

		/// <summary>
		/// The resource has been dispatched/assigned to a call but has not yet begun responding.
		/// Classified as Committed.
		/// </summary>
		[Description("The unit or responder has been dispatched or assigned but has not yet begun responding.")]
		[Display(Name = "Dispatched")]
		Dispatched = 6,

		/// <summary>
		/// The resource has cleared the call or incident and is returning to an available state.
		/// Classified as Available.
		/// </summary>
		[Description("The unit or responder has cleared the call or incident and is returning to an available state.")]
		[Display(Name = "Cleared")]
		Cleared = 7,

		/// <summary>
		/// The resource is returning to quarters, station or base. Classified as Committed.
		/// </summary>
		[Description("The unit or responder is returning to quarters, station or base.")]
		[Display(Name = "Returning")]
		Returning = 8,

		/// <summary>
		/// The resource is staged at a safe location awaiting assignment or orders. Classified as Committed.
		/// </summary>
		[Description("The unit or responder is staged at a safe location awaiting assignment or orders.")]
		[Display(Name = "Staging")]
		Staging = 9,

		/// <summary>
		/// The resource is out of service and unavailable for assignment. Classified as Unavailable.
		/// </summary>
		[Description("The unit or responder is out of service and unavailable for assignment.")]
		[Display(Name = "Unavailable")]
		Unavailable = 10,

		/// <summary>
		/// The resource is traveling to an assignment, destination or location on a routine
		/// (non-emergency) basis, as opposed to an emergency <see cref="Responding"/>. Classified as Committed.
		/// Useful for logistics, security, industrial and commodity-delivery operations.
		/// </summary>
		[Description("The unit or responder is traveling to an assignment, destination or location on a routine (non-emergency) basis.")]
		[Display(Name = "En Route")]
		Enroute = 11,

		/// <summary>
		/// The resource is actively transporting a patient, subject, prisoner or cargo to a destination.
		/// Classified as Committed. Covers EMS transport to a hospital, law-enforcement prisoner
		/// transport, search-and-rescue subject transport and in-transit freight.
		/// </summary>
		[Description("The unit or responder is actively transporting a patient, subject, prisoner or cargo to a destination.")]
		[Display(Name = "Transporting")]
		Transporting = 12,

		/// <summary>
		/// The resource is actively delivering goods, supplies, equipment or commodities to a recipient
		/// or drop-off point. Classified as Committed. Primarily for commodity delivery and logistics.
		/// </summary>
		[Description("The unit or responder is actively delivering goods, supplies, equipment or commodities to a recipient or drop-off point.")]
		[Display(Name = "Delivering")]
		Delivering = 13,

		/// <summary>
		/// The resource has reached and is providing care to, or is in contact with, a patient or the
		/// subject of a search. Classified as Committed. Common in EMS and search-and-rescue operations.
		/// </summary>
		[Description("The unit or responder has reached and is providing care to, or is in contact with, a patient or search subject.")]
		[Display(Name = "At Patient")]
		AtPatient = 14,

		/// <summary>
		/// The resource has arrived at a hospital or receiving facility to transfer patient care.
		/// Classified as Committed. Common in EMS operations.
		/// </summary>
		[Description("The unit or responder has arrived at a hospital or receiving facility to transfer patient care.")]
		[Display(Name = "At Hospital")]
		AtHospital = 15,

		/// <summary>
		/// The resource is conducting an active search of an assigned area, sector or grid. Classified as
		/// Committed. Common in search-and-rescue and law-enforcement operations.
		/// </summary>
		[Description("The unit or responder is conducting an active search of an assigned area, sector or grid.")]
		[Display(Name = "Searching")]
		Searching = 16,

		/// <summary>
		/// The resource is loading or picking up cargo, supplies, equipment or a subject prior to
		/// transport. Classified as Committed. Useful for logistics, industrial and commodity-delivery
		/// operations.
		/// </summary>
		[Description("The unit or responder is loading or picking up cargo, supplies, equipment or a subject prior to transport.")]
		[Display(Name = "Loading")]
		Loading = 17,

		/// <summary>
		/// The resource is held in reserve, on standby or on-call and ready to be assigned. Classified as
		/// Committed. Common in disaster response and planned-event operations.
		/// </summary>
		[Description("The unit or responder is held in reserve, on standby or on-call and ready to be assigned.")]
		[Display(Name = "Standby")]
		Standby = 18,

		/// <summary>
		/// The resource is actively patrolling an assigned area or beat while remaining available for
		/// dispatch. Classified as Available. Common in security and law-enforcement operations.
		/// </summary>
		[Description("The unit or responder is actively patrolling an assigned area or beat while remaining available for dispatch.")]
		[Display(Name = "On Patrol")]
		OnPatrol = 19,

		/// <summary>
		/// The resource is out of service for maintenance, refueling, restocking, cleaning or servicing.
		/// Classified as Unavailable. Common in industrial management and fleet operations.
		/// </summary>
		[Description("The unit or responder is out of service for maintenance, refueling, restocking, cleaning or servicing.")]
		[Display(Name = "Maintenance")]
		Maintenance = 20,

		/// <summary>
		/// The resource is on a rest, meal or crew-rest break and is temporarily unavailable. Classified
		/// as Delayed (expected to return to service). Common in industrial management and extended operations.
		/// </summary>
		[Description("The unit or responder is on a rest, meal or crew-rest break and is temporarily unavailable.")]
		[Display(Name = "On Break")]
		OnBreak = 21,

		/// <summary>
		/// The assignment, task or delivery is complete; the resource is wrapping up and becoming
		/// available again. Classified as Available. Useful for industrial, logistics and
		/// commodity-delivery operations.
		/// </summary>
		[Description("The assignment, task or delivery is complete; the unit or responder is wrapping up and becoming available.")]
		[Display(Name = "Completed")]
		Completed = 22
	}
}
