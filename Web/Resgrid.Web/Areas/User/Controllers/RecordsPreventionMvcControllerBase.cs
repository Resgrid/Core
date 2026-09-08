using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Shared plumbing for the RMS-5 prevention, investigation and RMS-4 quality/health pages: the module flag check
	/// (404 when off, like the v4 controllers), TempData messages and one exception-to-page mapping. Authorization is
	/// enforced by the services; the controllers only pre-hide buttons.
	/// </summary>
	public abstract class RecordsPreventionMvcControllerBase : SecureBaseController
	{
		protected readonly IRecordsCutoverService Cutover;
		protected readonly IFeatureToggleService FeatureToggles;
		protected readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> Localizer;

		protected RecordsPreventionMvcControllerBase(IRecordsCutoverService cutover, IFeatureToggleService featureToggles, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			Cutover = cutover;
			FeatureToggles = featureToggles;
			Localizer = localizer;
		}

		/// <summary>Records flag on and the module flag on.</summary>
		protected async Task<bool> ModuleOnAsync(string flagKey)
		{
			if (!(await Cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return false;
			return string.IsNullOrWhiteSpace(flagKey) || await FeatureToggles.IsEnabledAsync(flagKey, DepartmentId);
		}

		protected async Task<bool> FlagAsync(string flagKey) => await FeatureToggles.IsEnabledAsync(flagKey, DepartmentId);

		protected T Prepare<T>(T model) where T : RecordsPreventionBaseView
		{
			model.CanAdminister = ClaimsAuthorizationHelper.CanAdministerRecordsPrevention();
			model.IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			Response.Headers["Cache-Control"] = "no-store";
			return model;
		}

		protected void Notify(string key, params object[] args) => TempData["RecordsMessage"] = args.Length == 0 ? Localizer[key].Value : string.Format(Localizer[key].Value, args);
		protected void NotifyError(string message) => TempData["RecordsError"] = message;

		/// <summary>Maps service failures for a redirecting POST. Returns null when the caller should redirect normally.</summary>
		protected IActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordsModuleDisabledException _: return NotFound();
				case RecordProtectedContentException p: NotifyError(Localizer["ProtectedContentBlocked"].Value + " (" + p.Reason + ")"); return null;
				case RecordAttachmentRejectedException a: NotifyError(a.Message); return null;
				case ArgumentException _:
				case InvalidOperationException _: NotifyError(ex.Message); return null;
				default: throw ex;
			}
		}

		protected static Dictionary<string, string> NameMap<T>(IEnumerable<T> items, Func<T, string> id, Func<T, string> name)
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var item in items ?? Enumerable.Empty<T>())
			{
				var key = id(item);
				if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key)) map[key] = name(item);
			}
			return map;
		}

		protected static DateTime? ParseUtc(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return null;
			return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : (DateTime?)null;
		}
	}
}
