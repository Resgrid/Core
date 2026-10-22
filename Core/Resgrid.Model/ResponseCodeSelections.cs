using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>
	/// Per-unit response code choices a dispatcher posts with a dispatch (conditional run cards): the v4
	/// <c>UnitResponseCodes</c> field (<c>unitId:codeId|unitId:|…</c>, an empty code clears it) and the web form's
	/// <c>unitResponseCode_{unitId}</c> fields. Kept apart from the dispatch list so old clients are unaffected.
	/// </summary>
	public static class ResponseCodeSelections
	{
		public const string FormFieldPrefix = "unitResponseCode_";

		/// <summary>Unit id to chosen code id (null = cleared). Malformed entries are ignored.</summary>
		public static Dictionary<int, int?> Parse(string value)
		{
			var selections = new Dictionary<int, int?>();

			if (string.IsNullOrWhiteSpace(value))
				return selections;

			foreach (var part in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var pieces = part.Split(':');

				if (pieces.Length != 2 || !int.TryParse(pieces[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unitId))
					continue;

				selections[unitId] = int.TryParse(pieces[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var codeId) ? codeId : (int?)null;
			}

			return selections;
		}

		/// <summary>
		/// Writes the chosen code onto the dispatch as a snapshot (code and name), or clears it. A code id the catalog does
		/// not have clears the code rather than storing an id nothing can name.
		/// </summary>
		public static void Apply(CallDispatchUnit dispatch, int? codeId, DispatchResponseCodesConfig codes)
		{
			if (dispatch == null)
				return;

			var code = codeId.HasValue ? codes?.Find(codeId) : null;

			dispatch.ResponseCodeId = code?.Id;
			dispatch.ResponseCode = code?.Code;
			dispatch.ResponseCodeName = code?.Name;
		}

		/// <summary>Applies every selection to the matching dispatches; units with no selection keep what they have.</summary>
		public static void ApplyAll(IEnumerable<CallDispatchUnit> dispatches, IDictionary<int, int?> selections, DispatchResponseCodesConfig codes)
		{
			if (dispatches == null || selections == null || selections.Count == 0)
				return;

			foreach (var dispatch in dispatches)
			{
				if (dispatch != null && selections.TryGetValue(dispatch.UnitId, out var codeId))
					Apply(dispatch, codeId, codes);
			}
		}
	}
}
