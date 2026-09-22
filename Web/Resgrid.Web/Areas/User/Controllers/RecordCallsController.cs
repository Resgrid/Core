using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User"), Authorize, DepartmentLocalTime]
	public class RecordCallsController : SecureBaseController
	{
		private readonly ICallsService _calls;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsProtectionService _protection;

		public RecordCallsController(ICallsService calls, IRecordsAuthorizationService authorization,
			IRecordsCutoverService cutover, IRecordsProtectionService protection)
		{
			_calls = calls;
			_authorization = authorization;
			_cutover = cutover;
			_protection = protection;
		}

		[HttpGet]
		public async Task<IActionResult> Search(string term = null, string status = null, string from = null, string to = null, int offset = 0, int? selectedId = null)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			if (!ClaimsAuthorizationHelper.CanViewCalls() || !(ClaimsAuthorizationHelper.CanViewRecords()
				|| ClaimsAuthorizationHelper.CanCreateRecord() || ClaimsAuthorizationHelper.CanViewRestrictedRecords())
				|| !await _authorization.IsActiveMemberAsync(UserId, DepartmentId)) return Forbid();
			if (offset < 0 || offset > int.MaxValue - 26 || term?.Length > 200 ||
				(!string.IsNullOrEmpty(status) && status != "active" && status != "closed")) return BadRequest();
			var time = DepartmentTime.From(ViewData);
			DateTime? ParseDate(string value) => string.IsNullOrWhiteSpace(value) ? null
				: DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new ArgumentException();
			DateTime? first, last;
			try
			{
				first = ParseDate(from); last = ParseDate(to);
				if (first.HasValue && last.HasValue && first > last) return BadRequest();
				last = last?.AddDays(1);
			}
			catch (ArgumentException) { return BadRequest(); }
			// Picker metadata remains usable under protection, without searching or exposing protected text.
			var includeText = !await _protection.IsEnforcedAsync(DepartmentId);
			object Item(Call call) => new {
				id = call.CallId,
				text = string.Join(" · ", new[] { ProtectedDataEnvelope.SafeDisplay(call.Number) ?? call.CallId.ToString(),
					includeText ? ProtectedDataEnvelope.SafeDisplay(call.Name) : null, time.Format(call.LoggedOn),
					includeText ? ProtectedDataEnvelope.SafeDisplay(call.Address) : null }.Where(s => !string.IsNullOrWhiteSpace(s)))
			};
			async Task<bool> Allowed(Call call) => call != null && !call.IsDeleted && call.DepartmentId == DepartmentId
				&& await _authorization.CanReadSourceCallAsync(UserId, DepartmentId, call);
			if (selectedId.HasValue)
			{
				var call = await _calls.GetCallByIdAsync(selectedId.Value);
				return await Allowed(call) ? Json(new { selected = Item(call) }) : NotFound();
			}
			const int pageSize = 25;
			var candidates = await _calls.SearchCallCandidatesAsync(DepartmentId, new CallSearchQuery {
				Term = term?.Trim(), Closed = status == "closed" ? true : status == "active" ? false : (bool?)null,
				FromUtc = time.ToUtc(first), UntilUtc = time.ToUtc(last), Offset = offset, Take = pageSize + 1, IncludeText = includeText
			});
			var items = new List<object>();
			foreach (var call in candidates.Take(pageSize))
				if (await Allowed(call)) items.Add(Item(call));
			return Json(new { items, nextOffset = candidates.Count > pageSize ? (int?)(offset + pageSize) : null, metadataOnly = !includeText });
		}
	}
}
