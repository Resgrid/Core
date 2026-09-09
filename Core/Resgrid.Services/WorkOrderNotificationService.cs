using System;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
	public sealed class WorkOrderNotificationService : IWorkOrderNotificationService
	{
		private readonly IWorkOrderRepository _orders;
		private readonly IWorkOrderAuthorizationService _authorization;
		private readonly IReadinessAccessService _access;
		private readonly IUnitOfWork _uow;
		private readonly ICommunicationService _communication;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserProfileService _profiles;
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.WorkOrders.WorkOrders", typeof(SupportedLocales).Assembly);
		public WorkOrderNotificationService(IWorkOrderRepository orders, IWorkOrderAuthorizationService authorization, IReadinessAccessService access, IUnitOfWork uow,
			ICommunicationService communication, IDepartmentsService departments, IDepartmentSettingsService settings, IUserProfileService profiles)
		{ _orders = orders; _authorization = authorization; _access = access; _uow = uow; _communication = communication; _departments = departments; _settings = settings; _profiles = profiles; }
		private async Task<T> TransactionAsync<T>(int departmentId, Func<Task<T>> action)
		{
			if (_uow.Transaction != null) throw new InvalidOperationException("Work-order notification claims own their transaction.");
			try { await _uow.CreateOrGetConnectionAsync(CancellationToken.None); await _orders.LockDepartmentAsync(departmentId); var result = await action(); _uow.CommitChanges(); return result; }
			catch { _uow.DiscardChanges(); throw; }
		}
		public async Task DispatchAsync(DomainEventOutboxEntry entry)
		{
			if (entry?.ProducerSubsystem != "WorkOrders" || entry.TriggerEventType is not (70 or 71 or 72) || !Guid.TryParseExact(entry.EventId, "D", out _) || !int.TryParse(entry.AggregateId, out var id)) return;
			if (!await _access.CanUseMaintenanceAsync(entry.DepartmentId)) return;
			var row = await _orders.GetAsync<WorkOrder>(entry.DepartmentId, id);
			if (row == null) return;
			foreach (var user in await RecipientsAsync(entry.DepartmentId, row))
			{
				var notice = new WorkOrderNotification { DepartmentId = entry.DepartmentId, WorkOrderId = id, EventId = entry.EventId, UserId = user, LeaseOwner = Guid.NewGuid().ToString("D") };
				var state = await TransactionAsync(entry.DepartmentId, () => _orders.ClaimNotificationAsync(notice, DateTime.UtcNow));
				if (state == 2 || state == 3) continue;
				if (state != 1) throw new InvalidOperationException("Work-order notification is leased by another delivery attempt.");
				try
				{
					var profile = await _profiles.GetProfileByUserIdAsync(user);
					var department = await _departments.GetDepartmentByIdAsync(entry.DepartmentId, true);
					var number = await _settings.GetTextToCallNumberForDepartmentAsync(entry.DepartmentId);
					var current = await _orders.GetAsync<WorkOrder>(entry.DepartmentId, id);
					if (department == null || profile == null || current == null || !await _access.CanUseMaintenanceAsync(entry.DepartmentId) || !(await RecipientsAsync(entry.DepartmentId, current)).Contains(user))
					{ await FinishAsync(notice, 3); continue; }
					CultureInfo culture;
					try { culture = CultureInfo.GetCultureInfo(profile.Language ?? "en"); if (!SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName)) culture = CultureInfo.GetCultureInfo("en"); }
					catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo("en"); }
					// Never decrypt, copy a title, embed an actor/grant or treat handoff as a delivery receipt.
					var handedOff = await _communication.SendNotificationAsync(user, entry.DepartmentId,
						Strings.GetString("NotificationMessage", culture) + " " + (Config.SystemBehaviorConfig.ResgridBaseUrl ?? "").TrimEnd('/') + "/User/WorkOrders/Detail/" + id,
						number, department, Strings.GetString("NotificationTitle", culture), profile);
					await FinishAsync(notice, handedOff ? 2 : 3);
				}
				catch { await FinishAsync(notice, 0); throw new InvalidOperationException("Work-order notification handoff failed."); }
			}
		}
		private async Task<System.Collections.Generic.List<string>> RecipientsAsync(int departmentId, WorkOrder row)
        {
            var result = new System.Collections.Generic.HashSet<string>(await _authorization.RecipientsAsync(departmentId, row));
            var members = await _departments.GetAllMembersForDepartmentUnlimitedAsync(departmentId, true);
            foreach (var member in members.Where(m => m.DepartmentId == departmentId && !m.IsDeleted && m.IsDisabled != true))
            {
                var actor = new Resgrid.Model.Checklists.ChecklistActor { DepartmentId = departmentId, UserId = member.UserId };
                if (member.UserId == row.CreatedBy || row.Status is 0 or 1 && await _authorization.CanManageAsync(actor, row.TargetGroupId))
                    result.Add(member.UserId);
            }
            return result.OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
        private async Task FinishAsync(WorkOrderNotification row, int state)
		{
			if (!await TransactionAsync(row.DepartmentId, () => _orders.FinishNotificationAsync(row, state, DateTime.UtcNow)))
				throw new InvalidOperationException("Work-order notification lease was lost.");
		}
	}
}
