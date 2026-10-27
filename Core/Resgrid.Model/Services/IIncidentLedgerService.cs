using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The shared write path of every Incident Back Office table (plan §11.2, §13.9): tenant and activation guards (a sealed incident
	/// is read-only), the table's rules and fixed lifecycle, compare-and-increment concurrency, ADP sealing of catalogued columns,
	/// and the ledger audit — in one transaction. Registered as an open generic; domain services and controllers take the closed
	/// type they need by constructor injection.
	/// </summary>
	public interface IIncidentLedgerService<T> where T : class, IIncidentLedgerEntity, new()
	{
		/// <summary>The row if it belongs to the actor's department. Protected values stay sealed unless <paramref name="reveal"/> and the caller holds a grant.</summary>
		Task<T> GetAsync(IncidentActor actor, string id, bool reveal = false, CancellationToken cancellationToken = default);

		Task<IReadOnlyList<T>> ListAsync(IncidentActor actor, IncidentLedgerQuery query, CancellationToken cancellationToken = default);

		Task<int> CountAsync(IncidentActor actor, IncidentLedgerQuery query, CancellationToken cancellationToken = default);

		Task<decimal> SumAsync(IncidentActor actor, string column, IncidentLedgerQuery query, CancellationToken cancellationToken = default);

		/// <summary>Insert (empty key) or update (the stored row must exist; <paramref name="expectedVersion"/> enforced when the table is versioned).</summary>
		Task<T> SaveAsync(IncidentActor actor, T entity, int? expectedVersion = null, CancellationToken cancellationToken = default);

		Task DeleteAsync(IncidentActor actor, string id, CancellationToken cancellationToken = default);

		/// <summary>A fixed-lifecycle transition (plan §10 R1): refused unless the table's lifecycle allows it from the stored status.</summary>
		Task<T> TransitionAsync(IncidentActor actor, string id, int toStatus, string reason = null, int? expectedVersion = null, CancellationToken cancellationToken = default);

		/// <summary>The table's lifecycle, when it has one.</summary>
		IIncidentLifecycle<T> Lifecycle { get; }
	}

	/// <summary>A table rule: validation and derived values before the write, events and follow-on writes after it (same transaction).</summary>
	public interface IIncidentLedgerRule<T> where T : class, IIncidentLedgerEntity
	{
		Task BeforeSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken);
		Task AfterSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken);
	}

	/// <summary>The fixed lifecycle of a table (plan §10 R1): departments label sub-states through CustomState, they never add or reorder these.</summary>
	public interface IIncidentLifecycle<T> where T : class, IIncidentLedgerEntity
	{
		Type StatusEnum { get; }
		int GetStatus(T entity);
		void SetStatus(T entity, int status);
		bool CanTransition(int from, int to);
		IReadOnlyList<int> NextStatuses(int from);
		bool IsTerminal(int status);
		/// <summary>Statuses that require a reason (rejections, cancellations, waivers).</summary>
		bool RequiresReason(int status);
	}

	public sealed class IncidentLedgerChange<T> where T : class, IIncidentLedgerEntity
	{
		public IncidentActor Actor { get; set; }
		public T Entity { get; set; }
		/// <summary>The stored row before this write; null on insert.</summary>
		public T Existing { get; set; }
		/// <summary>The row's activation (null for department-scoped rows).</summary>
		public IncidentActivation Activation { get; set; }
		public bool IsNew => Existing == null;
		public int? OldStatus { get; set; }
		public int? NewStatus { get; set; }
		public string Reason { get; set; }
		public bool IsTransition { get; set; }
		public bool IsDelete { get; set; }
	}
}
