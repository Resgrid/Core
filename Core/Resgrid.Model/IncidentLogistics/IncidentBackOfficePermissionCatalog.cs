using System.Collections.Generic;

namespace Resgrid.Model.IncidentLogistics
{
	/// <summary>
	/// Department-configurable Incident Back Office permissions (large-incident-logistics-finance-plan.md §15.1; registry §4C,
	/// values 80-110 and 120-124), with the no-row defaults the plan fixes. Ledger, catalog, dashboard and board operations
	/// only: incident record authorization is RMS 50-67 plus the definition's role policies. Every value sits behind the
	/// Incident Back Office add-on, its flags and module switches, so a permissive default is unreachable without the add-on.
	/// LockToGroup is evaluated per subject at the service layer where marked meaningful.
	/// </summary>
	public static class IncidentBackOfficePermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new[]
		{
			new RecordPermissionDescriptor(PermissionTypes.ViewIncidentBackOffice, PermissionActions.Everyone, true, "ViewIncidentBackOfficeNote", true),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentActivation, PermissionActions.DepartmentAdminsOnly, false, "ManageIncidentActivationNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentPeriods, PermissionActions.DepartmentAndGroupAdmins, false, "ManageIncidentPeriodsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentPositions, PermissionActions.DepartmentAndGroupAdmins, false, "ManageIncidentPositionsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentParticipants, PermissionActions.DepartmentAndGroupAdmins, true, "ManageIncidentParticipantsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewParticipantSensitive, PermissionActions.DepartmentAdminsOnly, true, "ViewParticipantSensitiveNote", false),
			new RecordPermissionDescriptor(PermissionTypes.CreateIncidentRequest, PermissionActions.DepartmentAndGroupAdmins, true, "CreateIncidentRequestNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ApproveIncidentRequest, PermissionActions.DepartmentAdminsOnly, true, "ApproveIncidentRequestNote", false),
			new RecordPermissionDescriptor(PermissionTypes.OrderIncidentRequest, PermissionActions.DepartmentAdminsOnly, false, "OrderIncidentRequestNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ReceiveIncidentRequest, PermissionActions.DepartmentAndGroupAdmins, true, "ReceiveIncidentRequestNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentFacilities, PermissionActions.DepartmentAndGroupAdmins, false, "ManageIncidentFacilitiesNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentLodging, PermissionActions.DepartmentAdminsOnly, false, "ManageIncidentLodgingNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewLodgingAssignmentDetail, PermissionActions.DepartmentAdminsOnly, false, "ViewLodgingAssignmentDetailNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentFeeding, PermissionActions.DepartmentAndGroupAdmins, false, "ManageIncidentFeedingNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentServices, PermissionActions.Everyone, true, "ManageIncidentServicesNote", true),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentGroundSupport, PermissionActions.DepartmentAndGroupAdmins, false, "ManageIncidentGroundSupportNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentComms, PermissionActions.DepartmentAndGroupAdmins, false, "ManageIncidentCommsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageVendorCatalog, PermissionActions.DepartmentAndGroupAdmins, false, "ManageVendorCatalogNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewVendorProtected, PermissionActions.DepartmentAdminsOnly, false, "ViewVendorProtectedNote", false),
			new RecordPermissionDescriptor(PermissionTypes.EngageVendor, PermissionActions.DepartmentAdminsOnly, false, "EngageVendorNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentProcurement, PermissionActions.DepartmentAdminsOnly, false, "ManageIncidentProcurementNote", false),
			new RecordPermissionDescriptor(PermissionTypes.CommitIncidentPurchase, PermissionActions.DepartmentAdminsOnly, false, "CommitIncidentPurchaseNote", false),
			new RecordPermissionDescriptor(PermissionTypes.AcceptVendorInvoice, PermissionActions.DepartmentAdminsOnly, false, "AcceptVendorInvoiceNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentBudget, PermissionActions.DepartmentAdminsOnly, false, "ManageIncidentBudgetNote", false),
			new RecordPermissionDescriptor(PermissionTypes.AuthorizeIncidentFunding, PermissionActions.DepartmentAdminsOnly, false, "AuthorizeIncidentFundingNote", false),
			new RecordPermissionDescriptor(PermissionTypes.EnterIncidentTime, PermissionActions.DepartmentAndGroupAdmins, true, "EnterIncidentTimeNote", false),
			new RecordPermissionDescriptor(PermissionTypes.CertifyIncidentTime, PermissionActions.DepartmentAndGroupAdmins, true, "CertifyIncidentTimeNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ApproveIncidentTime, PermissionActions.DepartmentAdminsOnly, true, "ApproveIncidentTimeNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewIncidentCost, PermissionActions.DepartmentAdminsOnly, false, "ViewIncidentCostNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentCost, PermissionActions.DepartmentAdminsOnly, false, "ManageIncidentCostNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ManageIncidentClaims, PermissionActions.DepartmentAdminsOnly, false, "ManageIncidentClaimsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewClaimProtected, PermissionActions.DepartmentAdminsOnly, false, "ViewClaimProtectedNote", false),
			new RecordPermissionDescriptor(PermissionTypes.AssembleIncidentIap, PermissionActions.DepartmentAdminsOnly, false, "AssembleIncidentIapNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ViewCoordinationBoards, PermissionActions.Everyone, true, "ViewCoordinationBoardsNote", true),
			new RecordPermissionDescriptor(PermissionTypes.ManageCoordinationBoards, PermissionActions.DepartmentAndGroupAdmins, true, "ManageCoordinationBoardsNote", false),
			new RecordPermissionDescriptor(PermissionTypes.ShareCoordinationBoards, PermissionActions.DepartmentAdminsOnly, false, "ShareCoordinationBoardsNote", false)
		};
	}
}
