using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The Workforce Pro gate asked at most once per request (the LmsRequestGate pattern): the flag (navigation, no billing read) and the full
	/// evaluation (one billing read) are kept in HttpContext.Items, so the personnel page, the call page's buttons and the wellness pages share
	/// one answer. The gate itself keeps no entitlement cache across requests. Fails closed: an error reads as not offered.
	/// </summary>
	public static class WorkforceProRequestGate
	{
		private const string FlagKey = "__workforcepro_flag_";
		private const string AccessKey = "__workforcepro_access_";

		/// <summary>The Workforce.Pro flag is on for the department (hide navigation when it is not).</summary>
		public static async Task<bool> IsOfferedAsync(HttpContext context, IWorkforceProAccessService access, int departmentId)
		{
			var key = FlagKey + departmentId;
			if (context?.Items != null && context.Items.TryGetValue(key, out var cached) && cached is bool known)
				return known;
			if (context?.Items != null && context.Items.TryGetValue(AccessKey + departmentId, out var evaluated) && evaluated is WorkforceProAccess full)
				return full.Offered;

			var offered = false;
			try
			{
				offered = access != null && await access.IsOfferedAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			if (context?.Items != null)
				context.Items[key] = offered;
			return offered;
		}

		/// <summary>The full evaluation (flag, add-on window, seat holders); not offered when it cannot be read.</summary>
		public static async Task<WorkforceProAccess> GetAccessAsync(HttpContext context, IWorkforceProAccessService access, int departmentId)
		{
			var key = AccessKey + departmentId;
			if (context?.Items != null && context.Items.TryGetValue(key, out var cached) && cached is WorkforceProAccess known)
				return known;

			WorkforceProAccess result;
			try
			{
				result = access == null ? WorkforceProAccess.NotOffered(departmentId) : await access.GetAccessAsync(departmentId) ?? WorkforceProAccess.NotOffered(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				result = WorkforceProAccess.NotOffered(departmentId);
			}

			if (context?.Items != null)
			{
				context.Items[key] = result;
				context.Items[FlagKey + departmentId] = result.Offered;
			}
			return result;
		}
	}
}
