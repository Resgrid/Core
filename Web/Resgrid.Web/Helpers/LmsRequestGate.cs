using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The LMS gate asked at most once per request (protocols-training-knowledge-plan §17.3, R-L9): the flag and the seat summary (one
	/// billing read) are kept in HttpContext.Items, so the personnel page, its seat counter and the LMS settings section share one
	/// answer. The gate itself keeps no entitlement cache; a new request always asks again. Fails closed: an error reads as off.
	/// </summary>
	public static class LmsRequestGate
	{
		private const string FlagKey = "__lms_gate_flag_";
		private const string SummaryKey = "__lms_gate_summary_";

		public static async Task<bool> IsFlagEnabledAsync(HttpContext context, ILmsAccessService access, int departmentId)
		{
			var key = FlagKey + departmentId;
			if (context?.Items != null && context.Items.TryGetValue(key, out var cached) && cached is bool known)
				return known;

			var enabled = false;
			try
			{
				enabled = await access.IsFlagEnabledAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			if (context?.Items != null)
				context.Items[key] = enabled;
			return enabled;
		}

		/// <summary>The department's seat summary, or null when the LMS is off for it or the summary could not be read.</summary>
		public static async Task<LmsSeatSummary> GetSummaryAsync(HttpContext context, ILmsAccessService access, ILmsSeatService seats, int departmentId)
		{
			if (!await IsFlagEnabledAsync(context, access, departmentId))
				return null;

			var key = SummaryKey + departmentId;
			if (context?.Items != null && context.Items.TryGetValue(key, out var cached))
				return cached as LmsSeatSummary;

			LmsSeatSummary summary = null;
			try
			{
				summary = await seats.GetSummaryAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			if (context?.Items != null)
				context.Items[key] = summary;
			return summary;
		}
	}
}
