using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.IncidentLogistics;

namespace Resgrid.Model.Services
{
	/// <summary>Typed reads and writes of the Incident Back Office department policies (registry §4C settings 85-91, 96-97, 120).</summary>
	public interface IIncidentBackOfficeSettingsService
	{
		Task<T> GetAsync<T>(int departmentId, bool bypassCache = false) where T : class, new();
		Task<T> SaveAsync<T>(int departmentId, T policy, IncidentActor actor, CancellationToken cancellationToken = default) where T : class, new();
		DepartmentSettingTypes SettingTypeFor<T>();
	}

	/// <summary>
	/// Emits the Incident Back Office workflow triggers (plan §13.7) through the DomainEventOutbox in the caller's unit of work.
	/// The payload is the common incident variables plus the trigger's own, as dotted keys; a variable outside the trigger's catalog
	/// entry is dropped so nothing uncatalogued (and so nothing protected) can ride along. Exercise activations emit with
	/// <c>incident.is_exercise = true</c>. A failed publish never undoes a committed change.
	/// </summary>
	public interface IIncidentEventPublisher
	{
		Task PublishAsync(int departmentId, IncidentActivation activation, WorkflowTriggerEventType trigger, IDictionary<string, object> variables,
			string aggregateType, string aggregateId, CancellationToken cancellationToken = default);
	}

	/// <summary>Ledger-mutation audit (plan §15.4): AuditLog with the table, row, transition and acting incident position.</summary>
	public interface IIncidentAuditService
	{
		void Record(IncidentActor actor, AuditLogTypes type, string table, string id, object before = null, object after = null, string note = null);
	}

	/// <summary>Who is acting, from where, and in which incident position (the module's one extension to the audit context).</summary>
	public sealed class IncidentActor
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
		/// <summary>The position code the user currently holds on the activation, when known.</summary>
		public string ActingPositionCode { get; set; }
		public bool IsDepartmentAdmin { get; set; }

		public static IncidentActor System(int departmentId) => new IncidentActor { DepartmentId = departmentId, UserId = null, IpAddress = "worker", UserAgent = "Resgrid.Workers" };
	}
}
