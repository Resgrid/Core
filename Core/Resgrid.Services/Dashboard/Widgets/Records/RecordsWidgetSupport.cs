using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// Shared plumbing for the Records widget providers (home-dashboard-implementation-contract.md §2). Records widgets return
	/// counts, numbers, states and dates only — never narratives or a protected column (plan §9A.2) — so they carry no ADP field.
	/// The module services enforce membership and permissions themselves; their refusals become widget states, not errors.
	/// </summary>
	internal static class RecordsWidgetSupport
	{
		internal static DashboardWidgetStateException Forbidden() => new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

		internal static DashboardWidgetStateException NotFound() => new DashboardWidgetStateException(DashboardWidgetErrorCodes.NotFound, "WidgetErrorNotFound");

		/// <summary>The Records pages admit active members only (RecordsController: IsActiveMemberAsync, else Forbid).</summary>
		internal static async Task RequireActiveMemberAsync(IRecordsAuthorizationService authorization, DashboardWidgetContext context)
		{
			if (!await authorization.IsActiveMemberAsync(context.UserId, context.DepartmentId))
				throw Forbidden();
		}

		/// <summary>Runs a Records service read: an authorization refusal is the forbidden state, a disabled module not-found.</summary>
		internal static async Task<T> RunAsync<T>(Func<Task<T>> read)
		{
			try
			{
				return await read();
			}
			catch (UnauthorizedAccessException)
			{
				throw Forbidden();
			}
			catch (RecordsModuleDisabledException)
			{
				throw NotFound();
			}
		}

		/// <summary>Identifiers and codes only; a redaction placeholder or a sealed envelope is never passed on.</summary>
		internal static string SafeText(string value) =>
			string.IsNullOrWhiteSpace(value) || value == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(value) ? null : value;
	}
}
