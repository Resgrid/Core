using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.IncidentBackOffice
{
	/// <summary>
	/// What every Incident Back Office provider does first, the way IncidentBackOfficeControllerBase does it (plan §7.10): one access
	/// evaluation (a lapsed add-on still reads, level None does not), the actor, and the activation the widget's setting names.
	/// </summary>
	internal static class IncidentWidgetSupport
	{
		/// <summary>The "activation" setting value that means the most recently opened open activation.</summary>
		public const string MostRecent = "recent";

		public const string ActivationSetting = "activation";

		// The permission types that issue each finance-desk view claim (ClaimsLogic.RecordClaimGrants); the desk shows each block only
		// to its own claim.
		public static readonly PermissionTypes[] BudgetView = { PermissionTypes.ManageIncidentBudget, PermissionTypes.AuthorizeIncidentFunding };
		public static readonly PermissionTypes[] ProcurementView = { PermissionTypes.ManageIncidentProcurement, PermissionTypes.CommitIncidentPurchase, PermissionTypes.AcceptVendorInvoice };
		public static readonly PermissionTypes[] CostView = { PermissionTypes.ViewIncidentCost, PermissionTypes.ManageIncidentCost };
		public static readonly PermissionTypes[] TimeView = { PermissionTypes.EnterIncidentTime, PermissionTypes.CertifyIncidentTime, PermissionTypes.ApproveIncidentTime };

		/// <summary>
		/// The controller base's actor. The dashboard data request carries no client address or agent into the provider context; the
		/// ledger reads used here record neither.
		/// </summary>
		public static IncidentActor Actor(DashboardWidgetContext context) => new IncidentActor
		{
			DepartmentId = context.DepartmentId,
			UserId = context.UserId,
			IsDepartmentAdmin = context.Viewer?.IsDepartmentAdmin == true
		};

		/// <summary>
		/// The module gate (flag, module switch, add-on level): None never reads, ReadOnly (lapsed) still renders. The area switch
		/// (Business / Coordination) is re-checked here as the desk base does.
		/// </summary>
		public static async Task<IncidentBackOfficeAccess> RequireReadAsync(IIncidentBackOfficeAccessService access, int departmentId,
			bool business = false, bool coordination = false)
		{
			var result = await access.GetAccessAsync(departmentId) ?? IncidentBackOfficeAccess.Denied;
			if (!result.CanRead || (business && !result.BusinessEnabled) || (coordination && !result.CoordinationEnabled))
				throw new DashboardWidgetStateException(DashboardWidgetErrorCodes.Forbidden, "WidgetErrorForbidden");

			return result;
		}

		/// <summary>
		/// The activation a widget instance shows: a stored id re-loaded through the department-checked ledger read (never trusted as
		/// posted), or — for "recent" or no value — the open activation opened last. Null when there is none (the widget's empty state).
		/// </summary>
		public static async Task<IncidentActivation> ResolveActivationAsync(IIncidentActivationService activations, IIncidentLedgerService<IncidentActivation> ledger,
			IncidentActor actor, string setting, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(setting) && !string.Equals(setting, MostRecent, StringComparison.OrdinalIgnoreCase))
			{
				var chosen = await ledger.GetAsync(actor, setting, false, cancellationToken);
				return chosen != null && chosen.DepartmentId == actor.DepartmentId && !chosen.IsDeleted ? chosen : null;
			}

			var open = await activations.GetActivationsAsync(actor, false, cancellationToken) ?? Array.Empty<IncidentActivation>();
			return open.OrderByDescending(a => a.OpenedOn ?? a.AddedOn).FirstOrDefault();
		}

		/// <summary>The activation and, for a complex, its member incidents (the desk base's Scope).</summary>
		public static async Task<IReadOnlyList<string>> ScopeAsync(IIncidentActivationService activations, IncidentActor actor, IncidentActivation activation,
			CancellationToken cancellationToken)
		{
			if (activation.Kind != (int)IncidentActivationKinds.Complex)
				return new[] { activation.IncidentActivationId };

			var scope = await activations.GetScopeAsync(actor, activation.IncidentActivationId, cancellationToken);
			return scope != null && scope.Count > 0 ? scope : new[] { activation.IncidentActivationId };
		}

		/// <summary>Fills the header every activation-scoped DTO carries.</summary>
		public static T Header<T>(T data, IncidentBackOfficeAccess access, IncidentActivation activation) where T : IncidentWidgetDataBase
		{
			data.ReadOnly = access.IsLapsed;
			data.HasActivation = activation != null;
			if (activation != null)
			{
				data.ActivationId = activation.IncidentActivationId;
				data.ActivationName = activation.Name;
				data.ShortCode = activation.ShortCode;
				data.ActivationStatus = Enum.GetName(typeof(IncidentActivationStatuses), activation.Status) ?? activation.Status.ToString();
				data.IsExercise = activation.IsExercise;
				data.Currency = activation.Currency;
			}

			return data;
		}
	}
}
