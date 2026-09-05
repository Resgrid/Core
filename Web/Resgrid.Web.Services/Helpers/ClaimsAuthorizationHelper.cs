using System;
using Autofac;
using System.Security.Claims;
using CommonServiceLocator;
using Microsoft.AspNetCore.Http;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.ServicesCore.Helpers
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
				_httpContextAccessor = ServiceLocator.Current.GetInstance<IHttpContextAccessor>();

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

		public static string GetTimeZone()
		{
			var claim = GetClaimsPrincipal().FindFirst(ResgridClaimTypes.Data.TimeZone);

			if (claim != null)
				return claim.Value;

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
	}
}
