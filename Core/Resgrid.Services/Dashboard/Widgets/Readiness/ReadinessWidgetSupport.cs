using System;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// Shared plumbing for the readiness widget providers (home-dashboard-implementation-contract.md §2): the module actors built
	/// exactly as the module MVC controllers build them, and the mapping of module failures to widget states. Checklists, work
	/// orders and inventory are mixed-content modules (plan §9A.4): their protected-data sentinels become the "Verify to view"
	/// state and the sentinel text never reaches the client.
	/// </summary>
	internal static class ReadinessWidgetSupport
	{
		/// <summary>The checklist reveal paths' sentinel; the module pages match the same prefix (ChecklistsController).</summary>
		internal const string ChecklistProtectedPrefix = "Unlock protected";

		/// <summary>The work order and inventory reveal paths' sentinel code.</summary>
		internal const string ProtectedDataRequiredCode = "ProtectedDataRequired";

		/// <summary>As ChecklistsController / WorkOrdersController.Actor: department, user and the caller's protected grant.</summary>
		internal static ChecklistActor ChecklistActor(DashboardWidgetContext context) =>
			new ChecklistActor { DepartmentId = context.DepartmentId, UserId = context.UserId, GrantToken = context.ProtectedGrantToken };

		/// <summary>As InventoryController.Actor.</summary>
		internal static InventoryActor InventoryActor(DashboardWidgetContext context) =>
			new InventoryActor { DepartmentId = context.DepartmentId, UserId = context.UserId, GrantToken = context.ProtectedGrantToken };

		internal static DashboardWidgetStateException Protected() => new DashboardWidgetStateException(DashboardWidgetErrorCodes.Protected, "WidgetErrorProtected");

		internal static DashboardWidgetStateException Forbidden() => new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

		internal static DashboardWidgetStateException NotFound() => new DashboardWidgetStateException(DashboardWidgetErrorCodes.NotFound, "WidgetErrorNotFound");

		internal static bool IsProtectedSentinel(ChecklistException ex) =>
			ex != null && ex.StatusCode == 403 && ex.Message != null && ex.Message.StartsWith(ChecklistProtectedPrefix, StringComparison.Ordinal);

		/// <summary>Runs a checklist service read; the sentinel becomes the protected state, membership/permission refusals forbidden.</summary>
		internal static async Task<T> ChecklistsAsync<T>(Func<Task<T>> read)
		{
			try
			{
				return await read();
			}
			catch (ChecklistException ex) when (IsProtectedSentinel(ex))
			{
				throw Protected();
			}
			catch (ChecklistException ex) when (ex.StatusCode == 403)
			{
				throw Forbidden();
			}
			catch (ChecklistException ex) when (ex.StatusCode == 404)
			{
				throw NotFound();
			}
		}

		internal static async Task<T> WorkOrdersAsync<T>(Func<Task<T>> read)
		{
			try
			{
				return await read();
			}
			catch (WorkOrderException ex) when (ex.Code == ProtectedDataRequiredCode)
			{
				throw Protected();
			}
			catch (WorkOrderException ex) when (ex.StatusCode == 403)
			{
				throw Forbidden();
			}
			catch (WorkOrderException ex) when (ex.StatusCode is 402 or 404 or 503)
			{
				throw NotFound();
			}
			catch (ChecklistException ex) when (IsProtectedSentinel(ex))
			{
				throw Protected();
			}
		}

		internal static async Task<T> InventoryAsync<T>(Func<Task<T>> read)
		{
			try
			{
				return await read();
			}
			catch (InventoryException ex) when (ex.Code == ProtectedDataRequiredCode)
			{
				throw Protected();
			}
			catch (InventoryException ex) when (ex.StatusCode == 403)
			{
				throw Forbidden();
			}
			catch (InventoryException ex) when (ex.StatusCode is 404 or 409)
			{
				throw NotFound();
			}
		}

		/// <summary>Display text that is never a redaction placeholder or a sealed envelope.</summary>
		internal static string SafeText(string value) =>
			string.IsNullOrWhiteSpace(value) || value == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(value) ? null : value;

		/// <summary>The department's time zone (the viewer carries it), falling back to UTC for an unknown id.</summary>
		internal static TimeZoneInfo DepartmentZone(DashboardWidgetContext context)
		{
			var id = context?.Viewer?.TimeZoneId;
			if (string.IsNullOrWhiteSpace(id))
				return TimeZoneInfo.Utc;

			try
			{
				return DateTimeHelpers.CreateTimeZoneInfo(id) ?? TimeZoneInfo.Utc;
			}
			catch (Exception)
			{
				return TimeZoneInfo.Utc;
			}
		}

		/// <summary>Today's date in the department's zone.</summary>
		internal static DateTime DepartmentToday(DashboardWidgetContext context, TimeZoneInfo zone) =>
			TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(context.NowUtc, DateTimeKind.Utc), zone).Date;

		/// <summary>A department-local wall time as UTC.</summary>
		internal static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
		{
			try
			{
				return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
			}
			catch (ArgumentException)
			{
				// An invalid local time (inside a spring-forward gap) moves to the hour after.
				return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.AddHours(1), DateTimeKind.Unspecified), zone);
			}
		}
	}
}
