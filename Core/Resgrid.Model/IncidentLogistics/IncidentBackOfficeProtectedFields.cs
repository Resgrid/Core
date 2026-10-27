using System;
using System.Collections.Generic;

namespace Resgrid.Model.IncidentLogistics
{
	/// <summary>
	/// The Incident Back Office ADP catalog (plan §15.3a): exactly the fields that identify, locate, price or describe the health of a
	/// person, or carry a financial identifier — and nothing else. LI-A participants are catalog 34, LI-B lodging, travel, transport,
	/// service, communications, vendor and self-service fields 35, LI-C rate, cost, expense, time remarks, claim and intake fields 36.
	/// Values are sealed in place (text columns); the AAD is the string key + DepartmentId. Accessor maps drive the shared seams.
	/// </summary>
	public static class IncidentBackOfficeProtectedFields
	{
		public const int FoundationCatalogVersion = 34;
		public const int LogisticsCatalogVersion = 35;
		public const int BusinessCatalogVersion = 36;
		public const string LogisticsWorkloadPurpose = "incident-logistics";
		public const string CostingWorkloadPurpose = "incident-costing";
		public const string SelfServiceWorkloadPurpose = "incident-self-service";

		private static IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> Map<T>(params (string Field, Func<T, string> Get, Action<T, string> Set)[] entries)
		{
			var map = new Dictionary<string, (Func<T, string>, Action<T, string>)>(StringComparer.OrdinalIgnoreCase);
			foreach (var (field, get, set) in entries) map[field] = (get, set);
			return map;
		}

		public static readonly IReadOnlyDictionary<string, (Func<IncidentParticipant, string> Get, Action<IncidentParticipant, string> Set)> IncidentParticipant = Map<IncidentParticipant>(
			("incidentparticipants.personalphone", e => e.PersonalPhone, (e, v) => e.PersonalPhone = v),
			("incidentparticipants.personalemail", e => e.PersonalEmail, (e, v) => e.PersonalEmail = v),
			("incidentparticipants.homeaddress", e => e.HomeAddress, (e, v) => e.HomeAddress = v),
			("incidentparticipants.dateofbirth", e => e.DateOfBirth, (e, v) => e.DateOfBirth = v),
			("incidentparticipants.externalidentifier", e => e.ExternalIdentifier, (e, v) => e.ExternalIdentifier = v),
			("incidentparticipants.notes", e => e.Notes, (e, v) => e.Notes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentParticipantSensitive, string> Get, Action<IncidentParticipantSensitive, string> Set)> IncidentParticipantSensitive = Map<IncidentParticipantSensitive>(
			("incidentparticipantsensitive.emergencycontactname", e => e.EmergencyContactName, (e, v) => e.EmergencyContactName = v),
			("incidentparticipantsensitive.emergencycontactphone", e => e.EmergencyContactPhone, (e, v) => e.EmergencyContactPhone = v),
			("incidentparticipantsensitive.emergencycontactrelationship", e => e.EmergencyContactRelationship, (e, v) => e.EmergencyContactRelationship = v),
			("incidentparticipantsensitive.medicalaccommodation", e => e.MedicalAccommodation, (e, v) => e.MedicalAccommodation = v),
			("incidentparticipantsensitive.accessibilityneed", e => e.AccessibilityNeed, (e, v) => e.AccessibilityNeed = v),
			("incidentparticipantsensitive.allergendetail", e => e.AllergenDetail, (e, v) => e.AllergenDetail = v),
			("incidentparticipantsensitive.dietaryrestriction", e => e.DietaryRestriction, (e, v) => e.DietaryRestriction = v),
			("incidentparticipantsensitive.dietarycategorycode", e => e.DietaryCategoryCode, (e, v) => e.DietaryCategoryCode = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentParticipantQualification, string> Get, Action<IncidentParticipantQualification, string> Set)> IncidentParticipantQualification = Map<IncidentParticipantQualification>(
			("incidentparticipantqualifications.credentialnumber", e => e.CredentialNumber, (e, v) => e.CredentialNumber = v),
			("incidentparticipantqualifications.verificationnotes", e => e.VerificationNotes, (e, v) => e.VerificationNotes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentLodgingUnit, string> Get, Action<IncidentLodgingUnit, string> Set)> IncidentLodgingUnit = Map<IncidentLodgingUnit>(
			("incidentlodgingunits.unitlabel", e => e.UnitLabel, (e, v) => e.UnitLabel = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentLodgingAssignment, string> Get, Action<IncidentLodgingAssignment, string> Set)> IncidentLodgingAssignment = Map<IncidentLodgingAssignment>(
			("incidentlodgingassignments.confirmationnumber", e => e.ConfirmationNumber, (e, v) => e.ConfirmationNumber = v),
			("incidentlodgingassignments.notes", e => e.Notes, (e, v) => e.Notes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentRoomingListVersion, string> Get, Action<IncidentRoomingListVersion, string> Set)> IncidentRoomingListVersion = Map<IncidentRoomingListVersion>(
			("incidentroominglistversions.contentjson", e => e.ContentJson, (e, v) => e.ContentJson = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentServiceRequest, string> Get, Action<IncidentServiceRequest, string> Set)> IncidentServiceRequest = Map<IncidentServiceRequest>(
			("incidentservicerequests.description", e => e.Description, (e, v) => e.Description = v),
			("incidentservicerequests.completionnotes", e => e.CompletionNotes, (e, v) => e.CompletionNotes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentServiceActivity, string> Get, Action<IncidentServiceActivity, string> Set)> IncidentServiceActivity = Map<IncidentServiceActivity>(
			("incidentserviceactivities.description", e => e.Description, (e, v) => e.Description = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentTransportMovement, string> Get, Action<IncidentTransportMovement, string> Set)> IncidentTransportMovement = Map<IncidentTransportMovement>(
			("incidenttransportmovements.pickupaddresses", e => e.PickupAddresses, (e, v) => e.PickupAddresses = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentTravelItinerary, string> Get, Action<IncidentTravelItinerary, string> Set)> IncidentTravelItinerary = Map<IncidentTravelItinerary>(
			("incidenttravelitineraries.bookinglocator", e => e.BookingLocator, (e, v) => e.BookingLocator = v),
			("incidenttravelitineraries.carrierdetail", e => e.CarrierDetail, (e, v) => e.CarrierDetail = v),
			("incidenttravelitineraries.pickupaddress", e => e.PickupAddress, (e, v) => e.PickupAddress = v),
			("incidenttravelitineraries.notes", e => e.Notes, (e, v) => e.Notes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentCommunicationsResourceAssignment, string> Get, Action<IncidentCommunicationsResourceAssignment, string> Set)> IncidentCommunicationsResourceAssignment = Map<IncidentCommunicationsResourceAssignment>(
			("incidentcommunicationsresourceassignments.restrictedchannelids", e => e.RestrictedChannelIds, (e, v) => e.RestrictedChannelIds = v));

		public static readonly IReadOnlyDictionary<string, (Func<VendorProtectedDetail, string> Get, Action<VendorProtectedDetail, string> Set)> VendorProtectedDetail = Map<VendorProtectedDetail>(
			("vendorprotecteddetails.taxidentifier", e => e.TaxIdentifier, (e, v) => e.TaxIdentifier = v),
			("vendorprotecteddetails.remittancedetails", e => e.RemittanceDetails, (e, v) => e.RemittanceDetails = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentSelfServiceLink, string> Get, Action<IncidentSelfServiceLink, string> Set)> IncidentSelfServiceLink = Map<IncidentSelfServiceLink>(
			("incidentselfservicelinks.deliverytarget", e => e.DeliveryTarget, (e, v) => e.DeliveryTarget = v));

		public static readonly IReadOnlyDictionary<string, (Func<VendorPaymentPlan, string> Get, Action<VendorPaymentPlan, string> Set)> VendorPaymentPlan = Map<VendorPaymentPlan>(
			("vendorpaymentplans.remittancereference", e => e.RemittanceReference, (e, v) => e.RemittanceReference = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentTimeRecord, string> Get, Action<IncidentTimeRecord, string> Set)> IncidentTimeRecord = Map<IncidentTimeRecord>(
			("incidenttimerecords.remarks", e => e.Remarks, (e, v) => e.Remarks = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentExpense, string> Get, Action<IncidentExpense, string> Set)> IncidentExpense = Map<IncidentExpense>(
			("incidentexpenses.notes", e => e.Notes, (e, v) => e.Notes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentRateSnapshot, string> Get, Action<IncidentRateSnapshot, string> Set)> IncidentRateSnapshot = Map<IncidentRateSnapshot>(
			("incidentratesnapshots.personalamount", e => e.PersonalAmount, (e, v) => e.PersonalAmount = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentCostTransaction, string> Get, Action<IncidentCostTransaction, string> Set)> IncidentCostTransaction = Map<IncidentCostTransaction>(
			("incidentcosttransactions.protectedamount", e => e.ProtectedAmount, (e, v) => e.ProtectedAmount = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentClaim, string> Get, Action<IncidentClaim, string> Set)> IncidentClaim = Map<IncidentClaim>(
			("incidentclaims.financialsjson", e => e.FinancialsJson, (e, v) => e.FinancialsJson = v),
			("incidentclaims.casenotes", e => e.CaseNotes, (e, v) => e.CaseNotes = v));

		public static readonly IReadOnlyDictionary<string, (Func<IncidentIntakeItem, string> Get, Action<IncidentIntakeItem, string> Set)> IncidentIntakeItem = Map<IncidentIntakeItem>(
			("incidentintakeitems.senderaddress", e => e.SenderAddress, (e, v) => e.SenderAddress = v),
			("incidentintakeitems.subject", e => e.Subject, (e, v) => e.Subject = v));

		/// <summary>Catalog rows: table, column, classification, family and the catalog version that added them.</summary>
		public static IEnumerable<(string Table, string Column, string Classification, string Family, int CatalogVersion)> All()
		{
			yield return ("IncidentParticipants", "PersonalPhone", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipants", "PersonalEmail", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipants", "HomeAddress", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipants", "DateOfBirth", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipants", "ExternalIdentifier", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipants", "Notes", "Sensitive", "Operational", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "EmergencyContactName", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "EmergencyContactPhone", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "EmergencyContactRelationship", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "MedicalAccommodation", "Phi", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "AccessibilityNeed", "Phi", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "AllergenDetail", "Phi", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "DietaryRestriction", "Phi", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantSensitive", "DietaryCategoryCode", "Phi", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantQualifications", "CredentialNumber", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentParticipantQualifications", "VerificationNotes", "Pii", "Personnel", FoundationCatalogVersion);
			yield return ("IncidentLodgingUnits", "UnitLabel", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentLodgingAssignments", "ConfirmationNumber", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentLodgingAssignments", "Notes", "Sensitive", "Operational", LogisticsCatalogVersion);
			yield return ("IncidentRoomingListVersions", "ContentJson", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentServiceRequests", "Description", "Sensitive", "Operational", LogisticsCatalogVersion);
			yield return ("IncidentServiceRequests", "CompletionNotes", "Sensitive", "Operational", LogisticsCatalogVersion);
			yield return ("IncidentServiceActivities", "Description", "Sensitive", "Operational", LogisticsCatalogVersion);
			yield return ("IncidentTransportMovements", "PickupAddresses", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentTravelItineraries", "BookingLocator", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentTravelItineraries", "CarrierDetail", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentTravelItineraries", "PickupAddress", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentTravelItineraries", "Notes", "Pii", "Personnel", LogisticsCatalogVersion);
			yield return ("IncidentCommunicationsResourceAssignments", "RestrictedChannelIds", "Sensitive", "Operational", LogisticsCatalogVersion);
			yield return ("VendorProtectedDetails", "TaxIdentifier", "Pii", "Contacts", LogisticsCatalogVersion);
			yield return ("VendorProtectedDetails", "RemittanceDetails", "Pii", "Contacts", LogisticsCatalogVersion);
			yield return ("IncidentSelfServiceLinks", "DeliveryTarget", "Pii", "Contacts", LogisticsCatalogVersion);
			yield return ("VendorPaymentPlans", "RemittanceReference", "Pii", "Contacts", BusinessCatalogVersion);
			yield return ("IncidentTimeRecords", "Remarks", "Sensitive", "Operational", BusinessCatalogVersion);
			yield return ("IncidentExpenses", "Notes", "Sensitive", "Operational", BusinessCatalogVersion);
			yield return ("IncidentRateSnapshots", "PersonalAmount", "Pii", "Personnel", BusinessCatalogVersion);
			yield return ("IncidentCostTransactions", "ProtectedAmount", "Pii", "Personnel", BusinessCatalogVersion);
			yield return ("IncidentClaims", "FinancialsJson", "Pii", "Personnel", BusinessCatalogVersion);
			yield return ("IncidentClaims", "CaseNotes", "Pii", "Personnel", BusinessCatalogVersion);
			yield return ("IncidentIntakeItems", "SenderAddress", "Pii", "Contacts", BusinessCatalogVersion);
			yield return ("IncidentIntakeItems", "Subject", "Pii", "Contacts", BusinessCatalogVersion);
		}
	}
}
