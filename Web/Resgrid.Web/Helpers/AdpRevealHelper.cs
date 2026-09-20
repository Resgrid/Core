using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Shared answer shape for the per-module ADP reveal endpoints (plan 7.2). The reveal module writes every field
	/// it is handed that is not null, empty, REDACTED or ciphertext; when the grant did not open anything the caller
	/// gets the access-denied reason instead of a silent no-op.
	/// </summary>
	public static class AdpRevealHelper
	{
		public static IActionResult Answer(Controller controller, IDictionary<string, string> fields)
		{
			var present = fields.Where(f => !string.IsNullOrEmpty(f.Value)).ToList();
			if (present.Count > 0 && present.All(f => f.Value == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(f.Value)))
				return controller.Json(new { success = false, error = "protected_access_denied" });
			return controller.Json(new { success = true, fields });
		}
	}
}
