using System;
using Autofac;
using System.Security.Claims;
using CommonServiceLocator;
using Microsoft.AspNetCore.Http;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Helpers
{
	public static class ClaimsAuthorizationHelper
	{
		public static IHttpContextAccessor _httpContextAccessor;

		public static ResgridIdentity GetIdentity()
		{
			if (GetClaimsPrincipal().Identity.IsAuthenticated)
			{
				return GetClaimsPrincipal().Identity as ResgridIdentity;
			}

			return null;
		}

		public static ClaimsPrincipal GetClaimsPrincipal()
		{
			if (_httpContextAccessor == null)
				_httpContextAccessor = ServiceLocator.Current.GetInstance<IHttpContextAccessor>();// WebBootstrapper.GetKernel().Resolve<IHttpContextAccessor>();

			return _httpContextAccessor.HttpContext.User;
		}

		public static string GetUsername()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.Name);

			if (claim != null)
				return claim.Value;

			return String.Empty;
		}

		public static string GetFullName()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.GivenName);

			if (claim != null)
				return claim.Value;

			return String.Empty;
		}

		public static string GetDepartmentName()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.Actor);

			if (claim != null)
				return claim.Value;

			return String.Empty;
		}

		public static string GetUserId()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.PrimarySid);

			if (claim != null)
				return claim.Value;

			return String.Empty;
		}

		public static int GetDepartmentId()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.PrimaryGroupSid);

			if (claim != null)
				return int.Parse(claim.Value);

			return 0;
		}

		public static string GetEmailAddress()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.Email);

			if (claim != null)
				return claim.Value;

			return String.Empty;
		}

		public static string GetDepartmentSignupDate()
		{
			var claim = GetClaimsPrincipal().FindFirst(ClaimTypes.OtherPhone);

			if (claim != null)
			{
				string timestamp = (DateTime.Parse(claim.Value) - new DateTime(1970, 1, 1).ToLocalTime()).TotalSeconds.ToString();
				return timestamp;
			}

			return String.Empty;
		}

		public static bool IsUserResgridAdmin()
		{
			//ClaimsAuthorizationManager authorizationManager = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthorizationManager;
			//return authorizationManager.CheckAccess(ResgridAuthorizationContext.BuildAdminRoleContext());

			return false;
		}

		public static bool IsUserDepartmentAdmin()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);
		}

		public static bool IsUserGroupAdmin(int groupId)
		{
			//return  GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Actions.Update, ResgridClaimTypes.Resources.Group, groupId.ToString());
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.CreateGroupClaimTypeString(groupId), ResgridClaimTypes.Actions.Update);
		}

		public static bool IsUserDepartmentOrGroupAdmin(int? groupId)
		{
			if (GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update))
				return true;

			if (groupId.HasValue)
				//return ClaimsAuthorization.CheckAccess(ResgridClaimTypes.Actions.Update, ResgridClaimTypes.Resources.Group, groupId.ToString());
				return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Update);


			return false;
		}

		public static bool CanCreateCall()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanViewCalls() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View);

		public static bool CanCreateTraining()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanCreateDocument()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanUpdateDocument()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanDeleteDocument()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete);
		}

		public static bool CanCreateNote()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanCreateLog()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanDeleteLog()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete);
		}

		// Records (RMS) helpers. The Web and Web.Services copies of this file must expose identical members
		// (ClaimsAuthorizationHelperParityTests).
		public static bool CanViewRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View);
		}

		public static bool CanCreateRecord()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanReviewRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review);
		}

		public static bool CanApproveRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Approve);
		}

		public static bool CanFinalizeRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Finalize);
		}

		public static bool CanSubmitRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Submit);
		}

		public static bool CanAmendRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Amend);
		}

		public static bool CanVoidRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Void);
		}

		public static bool CanExportRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Export);
		}

		public static bool CanShareRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Share);
		}

		public static bool CanReassignRecordDrafts()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Reassign);
		}

		public static bool CanViewLegacyRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordLegacy, ResgridClaimTypes.Actions.View);
		}

		public static bool CanViewRestrictedRecords()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View);
		}

		public static bool CanManageRecordDefinitions()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordDefinition, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanPublishRecordDefinitions()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordDefinition, ResgridClaimTypes.Actions.Publish);
		}

		public static bool CanManageRecordReports()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordReport, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanManageRecordDisclosures()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordDisclosure, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanManageRecordLegalHold()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordLegalHold, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanAdministerRecordsPrevention()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.PreventionAdmin);
		}

		public static bool CanCreateShift()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanCreateCalendarEntry()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanViewPII()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanAdjustInventory()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanViewContacts()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View);
		}

		public static bool CanCreateContacts()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanEditContacts()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanDeleteContacts()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete);
		}

		public static bool CanManageWorkOrders() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.WorkOrder, ResgridClaimTypes.Actions.Update);
		public static bool CanViewAllWorkOrders() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.WorkOrder, ResgridClaimTypes.Actions.View);
		public static bool CanManageInvoicing() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.Update);
		public static bool CanViewInvoicing() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Invoicing, ResgridClaimTypes.Actions.View);
		public static bool CanManageCertifications() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Update);
		public static bool CanViewCertifications() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View);
		public static bool CanManageCertificationSetup() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Setup);
		public static bool CanManageDeployments() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Deployments, ResgridClaimTypes.Actions.Update);
		public static bool CanViewDeployments() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Deployments, ResgridClaimTypes.Actions.View);
		public static bool CanApproveTimeReports() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.TimeReports, ResgridClaimTypes.Actions.Approve);
		public static bool CanManageBids() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Bids, ResgridClaimTypes.Actions.Update);
		public static bool CanViewBids() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Bids, ResgridClaimTypes.Actions.View);
		public static bool CanManageContracts() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.ServiceContracts, ResgridClaimTypes.Actions.Update);
		public static bool CanViewContracts() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.ServiceContracts, ResgridClaimTypes.Actions.View);
		public static bool CanViewMutualAidReimbursement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.MutualAidReimbursement, ResgridClaimTypes.Actions.View);
		public static bool CanManageMutualAidReimbursement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.MutualAidReimbursement, ResgridClaimTypes.Actions.Update);
		public static bool CanSubmitMutualAidReimbursement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.MutualAidReimbursement, ResgridClaimTypes.Actions.Submit);
		public static bool CanReconcileMutualAidReimbursement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.MutualAidReimbursement, ResgridClaimTypes.Actions.Reconcile);
		public static bool CanManageDepartmentImport() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.DepartmentImport, ResgridClaimTypes.Actions.Update);
		public static bool CanManageRecordImports() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.RecordImports, ResgridClaimTypes.Actions.Update);
		// Incident Back Office (registry §4C).
		public static bool CanViewIncidentBackOffice() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBackOffice, ResgridClaimTypes.Actions.View);
		public static bool CanActivateIncident() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBackOffice, ResgridClaimTypes.Actions.Create);
		public static bool CanCloseIncidentActivation() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBackOffice, ResgridClaimTypes.Actions.Approve);
		public static bool CanManageIncidentPeriods() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBackOffice, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentPosition() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentPosition, ResgridClaimTypes.Actions.View);
		public static bool CanCreateIncidentPosition() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentPosition, ResgridClaimTypes.Actions.Create);
		public static bool CanManageIncidentPosition() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentPosition, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentParticipant() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentParticipant, ResgridClaimTypes.Actions.View);
		public static bool CanCreateIncidentParticipant() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentParticipant, ResgridClaimTypes.Actions.Create);
		public static bool CanManageIncidentParticipant() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentParticipant, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentParticipantSensitive() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentParticipantSensitive, ResgridClaimTypes.Actions.View);
		public static bool CanViewIncidentRequest() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentRequest, ResgridClaimTypes.Actions.View);
		public static bool CanCreateIncidentRequest() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentRequest, ResgridClaimTypes.Actions.Create);
		public static bool CanApproveIncidentRequest() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentRequest, ResgridClaimTypes.Actions.Approve);
		public static bool CanOrderIncidentRequest() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentRequest, ResgridClaimTypes.Actions.Order);
		public static bool CanManageIncidentRequest() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentRequest, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentFacility() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentFacility, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentFacility() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentFacility, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentLodging() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentLodging, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentLodging() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentLodging, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentLodgingDetail() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentLodgingDetail, ResgridClaimTypes.Actions.View);
		public static bool CanViewIncidentFeeding() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentFeeding, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentFeeding() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentFeeding, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentService() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentService, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentService() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentService, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentGroundSupport() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentGroundSupport, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentGroundSupport() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentGroundSupport, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentComms() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentComms, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentComms() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentComms, ResgridClaimTypes.Actions.Update);
		public static bool CanViewVendor() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Vendor, ResgridClaimTypes.Actions.View);
		public static bool CanCreateVendor() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Vendor, ResgridClaimTypes.Actions.Create);
		public static bool CanManageVendor() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Vendor, ResgridClaimTypes.Actions.Update);
		public static bool CanViewVendorProtected() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.VendorProtected, ResgridClaimTypes.Actions.View);
		public static bool CanOrderVendor() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Vendor, ResgridClaimTypes.Actions.Order);
		public static bool CanViewIncidentProcurement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentProcurement, ResgridClaimTypes.Actions.View);
		public static bool CanCreateIncidentProcurement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentProcurement, ResgridClaimTypes.Actions.Create);
		public static bool CanManageIncidentProcurement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentProcurement, ResgridClaimTypes.Actions.Update);
		public static bool CanCommitIncidentProcurement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentProcurement, ResgridClaimTypes.Actions.Commit);
		public static bool CanApproveIncidentProcurement() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentProcurement, ResgridClaimTypes.Actions.Approve);
		public static bool CanViewIncidentBudget() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBudget, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentBudget() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBudget, ResgridClaimTypes.Actions.Update);
		public static bool CanApproveIncidentBudget() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentBudget, ResgridClaimTypes.Actions.Approve);
		public static bool CanViewIncidentTime() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentTime, ResgridClaimTypes.Actions.View);
		public static bool CanCreateIncidentTime() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentTime, ResgridClaimTypes.Actions.Create);
		public static bool CanCertifyIncidentTime() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentTime, ResgridClaimTypes.Actions.Certify);
		public static bool CanApproveIncidentTime() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentTime, ResgridClaimTypes.Actions.Approve);
		public static bool CanViewIncidentCost() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentCost, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentCost() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentCost, ResgridClaimTypes.Actions.Update);
		public static bool CanExportIncidentCost() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentCost, ResgridClaimTypes.Actions.Export);
		public static bool CanViewIncidentClaim() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentClaim, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentClaim() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentClaim, ResgridClaimTypes.Actions.Update);
		public static bool CanViewIncidentClaimProtected() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentClaimProtected, ResgridClaimTypes.Actions.View);
		public static bool CanViewIncidentIap() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentIap, ResgridClaimTypes.Actions.View);
		public static bool CanManageIncidentIap() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.IncidentIap, ResgridClaimTypes.Actions.Update);
		public static bool CanViewCoordinationBoard() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.CoordinationBoard, ResgridClaimTypes.Actions.View);
		public static bool CanCreateCoordinationBoard() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.CoordinationBoard, ResgridClaimTypes.Actions.Create);
		public static bool CanManageCoordinationBoard() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.CoordinationBoard, ResgridClaimTypes.Actions.Update);
		public static bool CanShareCoordinationBoard() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.CoordinationBoard, ResgridClaimTypes.Actions.Share);
		public static bool CanViewWorkforce() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Workforce, ResgridClaimTypes.Actions.View);
		public static bool CanManageWorkforce() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Workforce, ResgridClaimTypes.Actions.Update);
		public static bool CanViewWorkforceCompensation() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.WorkforceCompensation, ResgridClaimTypes.Actions.View);
		public static bool CanManageWorkforceCompensation() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.WorkforceCompensation, ResgridClaimTypes.Actions.Update);
		public static bool CanViewInternalCosts() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.InternalCosts, ResgridClaimTypes.Actions.View);
		public static bool CanViewPayDataReporting() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.View);
		public static bool CanManagePayDataReporting() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.Update);
		public static bool CanExportPayDataReporting() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.PayDataReporting, ResgridClaimTypes.Actions.Export);
		public static bool CanManageChecklists() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Checklist, ResgridClaimTypes.Actions.Update);
		public static bool CanViewChecklistResults() => GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.ChecklistResults, ResgridClaimTypes.Actions.View);

		public static bool CanViewRoutes()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Route, ResgridClaimTypes.Actions.View);
		}

		public static bool CanCreateRoutes()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Route, ResgridClaimTypes.Actions.Create);
		}

		public static bool CanEditRoutes()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Route, ResgridClaimTypes.Actions.Update);
		}

		public static bool CanDeleteRoutes()
		{
			return GetClaimsPrincipal().HasClaim(ResgridClaimTypes.Resources.Route, ResgridClaimTypes.Actions.Delete);
		}
	}
}
