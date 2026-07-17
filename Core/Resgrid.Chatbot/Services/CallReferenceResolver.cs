using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Resolves a user-supplied call reference to a call in the department. First responders reference
	/// calls three ways: the raw call id ("1445", "C1445"), the call number ("26-1" — two-digit year
	/// plus sequence), or a shorthand description ("fire") matched against the ACTIVE calls' name, type
	/// and nature with the most recent call winning. Department scoping is enforced here (anti-IDOR):
	/// a call from another department resolves to null, indistinguishable from not-found.
	/// </summary>
	public static class CallReferenceResolver
	{
		private static readonly Regex CallNumberRegex = new Regex(@"^\d{2,4}-\d+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

		public static async Task<Call> ResolveAsync(ICallsService callsService, int departmentId, string reference)
		{
			if (string.IsNullOrWhiteSpace(reference))
				return null;

			// Trailing punctuation is never part of a call reference ("omw to 26-1.", "respond to fire?").
			var text = reference.Trim().TrimEnd('?', '!', '.', ',');
			if (text.Length == 0)
				return null;

			// "C1445" style prefix ahead of a digit.
			if (text.Length > 1 && (text[0] == 'c' || text[0] == 'C') && char.IsDigit(text[1]))
				text = text.Substring(1);

			// 1. Raw call id — direct lookup (works for closed calls too, e.g. detail requests).
			if (int.TryParse(text, out var callId))
			{
				var call = await callsService.GetCallByIdAsync(callId);
				return (call != null && call.DepartmentId == departmentId) ? call : null;
			}

			var activeCalls = await callsService.GetActiveCallsByDepartmentAsync(departmentId);
			if (activeCalls == null || activeCalls.Count == 0)
				return null;

			// 2. Call number ("26-1").
			if (CallNumberRegex.IsMatch(text))
			{
				return activeCalls
					.Where(c => string.Equals(c.Number?.Trim(), text, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(c => c.LoggedOn)
					.FirstOrDefault();
			}

			// 3. Shorthand ("fire"): term appears in the name, type, or nature of an active call;
			// most recent match wins.
			return activeCalls
				.Where(c => ContainsTerm(c.Name, text)
					|| ContainsTerm(c.Type, text)
					|| ContainsTerm(c.NatureOfCall == null ? null : StringHelpers.StripHtmlTagsCharArray(c.NatureOfCall), text))
				.OrderByDescending(c => c.LoggedOn)
				.FirstOrDefault();
		}

		private static bool ContainsTerm(string haystack, string term)
			=> !string.IsNullOrWhiteSpace(haystack) && haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
	}
}
