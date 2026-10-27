using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// The shared Incident Back Office write path (plan §11.2, §13.9 C1–C6), registered as an open generic. One transaction per call
	/// (joined when the caller already opened one, never nested): guards, rules, lifecycle, ADP sealing, the write and the audit.
	/// </summary>
	public class IncidentLedgerService<T> : IIncidentLedgerService<T> where T : class, IIncidentLedgerEntity, new()
	{
		private static readonly T Shape = new T();
		private static readonly bool IsActivationTable = typeof(T) == typeof(IncidentActivation);

		private readonly IIncidentLedgerRepository<T> _repository;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly IReadOnlyList<IIncidentLedgerRule<T>> _rules;
		private readonly IIncidentAuditService _audit;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IncidentProtectionSeam _seam;

		public IncidentLedgerService(IIncidentLedgerRepository<T> repository, IIncidentLedgerRepository<IncidentActivation> activations,
			IEnumerable<IIncidentLedgerRule<T>> rules, IEnumerable<IIncidentLifecycle<T>> lifecycles, IIncidentAuditService audit, IUnitOfWork unitOfWork,
			Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null, IProtectedGrantContext grant = null)
		{
			_repository = repository;
			_activations = activations;
			_rules = rules?.ToList() ?? new List<IIncidentLedgerRule<T>>();
			Lifecycle = lifecycles?.FirstOrDefault();
			_audit = audit;
			_unitOfWork = unitOfWork;
			_seam = new IncidentProtectionSeam(protectedWrite, protectedRead, grant);
		}

		public IIncidentLifecycle<T> Lifecycle { get; }

		public async Task<T> GetAsync(IncidentActor actor, string id, bool reveal = false, CancellationToken cancellationToken = default)
		{
			var row = await _repository.GetForDepartmentAsync(actor.DepartmentId, id, false, cancellationToken);
			if (row != null && reveal)
				await _seam.ResolveForReadAsync(new[] { row }, actor.DepartmentId);
			return row;
		}

		public async Task<IReadOnlyList<T>> ListAsync(IncidentActor actor, IncidentLedgerQuery query, CancellationToken cancellationToken = default)
			=> (await _repository.ListAsync(actor.DepartmentId, query, cancellationToken)).ToList();

		public Task<int> CountAsync(IncidentActor actor, IncidentLedgerQuery query, CancellationToken cancellationToken = default)
			=> _repository.CountAsync(actor.DepartmentId, query, cancellationToken);

		public Task<decimal> SumAsync(IncidentActor actor, string column, IncidentLedgerQuery query, CancellationToken cancellationToken = default)
			=> _repository.SumAsync(actor.DepartmentId, column, query, cancellationToken);

		public Task<T> SaveAsync(IncidentActor actor, T entity, int? expectedVersion = null, CancellationToken cancellationToken = default)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));
			return TransactionAsync(async () =>
			{
				entity.DepartmentId = actor.DepartmentId;
				var id = entity.IdValue as string;
				var existing = string.IsNullOrWhiteSpace(id) ? null : await _repository.GetForDepartmentAsync(actor.DepartmentId, id, false, cancellationToken);
				if (!string.IsNullOrWhiteSpace(id) && existing == null)
				{
					// A caller-allocated key on a new row is allowed only when no row (in any department) holds it.
					if (await _repository.GetByIdAsync(id) != null)
						throw new IncidentRuleException("RecordNotFound");
				}

				var change = new IncidentLedgerChange<T> { Actor = actor, Entity = entity, Existing = existing };
				change.Activation = await GuardActivationAsync(actor, entity, cancellationToken);
				if (existing != null && Lifecycle != null)
				{
					// Status moves only through TransitionAsync: a plain save keeps the stored status.
					Lifecycle.SetStatus(entity, Lifecycle.GetStatus(existing));
				}
				if (existing != null)
				{
					entity.AddedOn = existing.AddedOn;
					entity.AddedByUserId = existing.AddedByUserId;
					if (entity is IIncidentSoftDeletable deletable && existing is IIncidentSoftDeletable stored)
						deletable.IsDeleted = stored.IsDeleted;
				}

				foreach (var rule in _rules)
					await rule.BeforeSaveAsync(change, cancellationToken);

				var saved = await WriteAsync(actor, entity, existing, expectedVersion, cancellationToken);
				change.Entity = saved;

				foreach (var rule in _rules)
					await rule.AfterSaveAsync(change, cancellationToken);

				_audit.Record(actor, existing == null ? AuditLogTypes.IncidentRecordCreated : AuditLogTypes.IncidentRecordUpdated, Shape.TableName, saved.IdValue as string, existing, saved);
				return saved;
			}, cancellationToken);
		}

		public Task DeleteAsync(IncidentActor actor, string id, CancellationToken cancellationToken = default)
		{
			return TransactionAsync(async () =>
			{
				var existing = await _repository.GetForDepartmentAsync(actor.DepartmentId, id, false, cancellationToken);
				if (existing == null)
					throw new IncidentRuleException("RecordNotFound");
				if (existing is not IIncidentSoftDeletable)
					throw new IncidentRuleException("RecordCannotBeDeleted");
				var change = new IncidentLedgerChange<T> { Actor = actor, Entity = existing, Existing = existing, IsDelete = true };
				change.Activation = await GuardActivationAsync(actor, existing, cancellationToken);
				foreach (var rule in _rules)
					await rule.BeforeSaveAsync(change, cancellationToken);
				await _repository.SoftDeleteAsync(actor.DepartmentId, id, actor.UserId, cancellationToken);
				foreach (var rule in _rules)
					await rule.AfterSaveAsync(change, cancellationToken);
				_audit.Record(actor, AuditLogTypes.IncidentRecordDeleted, Shape.TableName, id, existing, null);
				return true;
			}, cancellationToken);
		}

		public Task<T> TransitionAsync(IncidentActor actor, string id, int toStatus, string reason = null, int? expectedVersion = null, CancellationToken cancellationToken = default)
		{
			if (Lifecycle == null)
				throw new IncidentRuleException("NoLifecycle");
			return TransactionAsync(async () =>
			{
				var existing = await _repository.LockForUpdateAsync(actor.DepartmentId, id, cancellationToken);
				if (existing == null || existing is IIncidentSoftDeletable { IsDeleted: true })
					throw new IncidentRuleException("RecordNotFound");
				var from = Lifecycle.GetStatus(existing);
				if (from == toStatus)
					return existing;
				if (!Lifecycle.CanTransition(from, toStatus))
					throw new IncidentRuleException("TransitionNotAllowed", Enum.GetName(Lifecycle.StatusEnum, from) ?? from.ToString(), Enum.GetName(Lifecycle.StatusEnum, toStatus) ?? toStatus.ToString());
				if (Lifecycle.RequiresReason(toStatus) && string.IsNullOrWhiteSpace(reason))
					throw new IncidentRuleException("ReasonRequired");

				var entity = existing.CloneJson();
				Lifecycle.SetStatus(entity, toStatus);
				var change = new IncidentLedgerChange<T> { Actor = actor, Entity = entity, Existing = existing, OldStatus = from, NewStatus = toStatus, Reason = reason, IsTransition = true };
				change.Activation = await GuardActivationAsync(actor, entity, cancellationToken);
				foreach (var rule in _rules)
					await rule.BeforeSaveAsync(change, cancellationToken);

				var saved = await WriteAsync(actor, entity, existing, expectedVersion ?? (existing as IIncidentVersioned)?.RowVersion, cancellationToken);
				change.Entity = saved;
				foreach (var rule in _rules)
					await rule.AfterSaveAsync(change, cancellationToken);

				_audit.Record(actor, AuditLogTypes.IncidentStatusTransition, Shape.TableName, id,
					new { Status = Enum.GetName(Lifecycle.StatusEnum, from) }, new { Status = Enum.GetName(Lifecycle.StatusEnum, toStatus), Reason = reason });
				return saved;
			}, cancellationToken);
		}

		private async Task<T> WriteAsync(IncidentActor actor, T entity, T existing, int? expectedVersion, CancellationToken cancellationToken)
		{
			if (existing == null)
			{
				if (_seam.IsActive && IncidentProtectionSeam.HasProtectedFields<T>())
					return await _seam.InsertAsync(_repository, entity, actor, cancellationToken);
				return await _repository.InsertNewAsync(entity, actor.UserId, cancellationToken);
			}

			var version = expectedVersion ?? (existing as IIncidentVersioned)?.RowVersion ?? 0;
			if (_seam.IsActive && IncidentProtectionSeam.HasProtectedFields<T>())
				await _seam.PrepareUpdateAsync(entity, existing, actor, cancellationToken);
			return await _repository.UpdateVersionedAsync(entity, version, actor.UserId, cancellationToken);
		}

		/// <summary>The row's activation must be the department's, and a sealed incident takes no write (plan §18).</summary>
		private async Task<IncidentActivation> GuardActivationAsync(IncidentActor actor, T entity, CancellationToken cancellationToken)
		{
			if (IsActivationTable || entity is not IIncidentScopedEntity scoped || string.IsNullOrWhiteSpace(scoped.IncidentActivationId))
				return IsActivationTable ? entity as IncidentActivation : null;
			var activation = await _activations.GetForDepartmentAsync(actor.DepartmentId, scoped.IncidentActivationId, false, cancellationToken);
			if (activation == null)
				throw new IncidentRuleException("ActivationNotFound");
			if (activation.Status == (int)IncidentActivationStatuses.Sealed)
				throw new IncidentRuleException("ActivationSealed");
			return activation;
		}

		/// <summary>Runs in the scope's transaction, joining one the caller already opened (tests pass no unit of work and run unwrapped).</summary>
		private async Task<TResult> TransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null)
				return await action();
			try
			{
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				var result = await action();
				_unitOfWork.CommitChanges();
				return result;
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}
	}

	/// <summary>A fixed lifecycle as a transition table over an int status property (plan §10 R1).</summary>
	public class IncidentLifecycle<T, TStatus> : IIncidentLifecycle<T> where T : class, IIncidentLedgerEntity where TStatus : struct, Enum
	{
		private readonly Func<T, int> _get;
		private readonly Action<T, int> _set;
		private readonly Dictionary<int, HashSet<int>> _transitions = new Dictionary<int, HashSet<int>>();
		private readonly HashSet<int> _terminal = new HashSet<int>();
		private readonly HashSet<int> _reasons = new HashSet<int>();

		public IncidentLifecycle(Func<T, int> get, Action<T, int> set)
		{
			_get = get;
			_set = set;
		}

		public Type StatusEnum => typeof(TStatus);

		public IncidentLifecycle<T, TStatus> Allow(TStatus from, params TStatus[] to)
		{
			var key = Convert.ToInt32(from);
			if (!_transitions.TryGetValue(key, out var set))
				_transitions[key] = set = new HashSet<int>();
			foreach (var target in to)
				set.Add(Convert.ToInt32(target));
			return this;
		}

		/// <summary>Allows a move to <paramref name="to"/> from every non-terminal status.</summary>
		public IncidentLifecycle<T, TStatus> AllowFromAnyOpen(TStatus to)
		{
			foreach (var value in Enum.GetValues(typeof(TStatus)).Cast<TStatus>())
				if (!_terminal.Contains(Convert.ToInt32(value)) && !value.Equals(to))
					Allow(value, to);
			return this;
		}

		public IncidentLifecycle<T, TStatus> Terminal(params TStatus[] statuses)
		{
			foreach (var status in statuses) _terminal.Add(Convert.ToInt32(status));
			return this;
		}

		public IncidentLifecycle<T, TStatus> ReasonFor(params TStatus[] statuses)
		{
			foreach (var status in statuses) _reasons.Add(Convert.ToInt32(status));
			return this;
		}

		public int GetStatus(T entity) => _get(entity);
		public void SetStatus(T entity, int status) => _set(entity, status);
		public bool CanTransition(int from, int to) => _transitions.TryGetValue(from, out var set) && set.Contains(to);
		public IReadOnlyList<int> NextStatuses(int from) => _transitions.TryGetValue(from, out var set) ? set.OrderBy(x => x).ToList() : new List<int>();
		public bool IsTerminal(int status) => _terminal.Contains(status);
		public bool RequiresReason(int status) => _reasons.Contains(status);
	}

	/// <summary>
	/// Advanced Data Protection seam for the Incident Back Office catalog (plan §15.3a; catalogs 34-36), the WorkforceProtectionSeam
	/// pattern made generic: the accessor map of a table is the IncidentBackOfficeProtectedFields field named after its entity.
	/// Without the protection services (a department that has not enrolled, or a test) rows are written and read as-is.
	/// </summary>
	public sealed class IncidentProtectionSeam
	{
		private readonly Lazy<IProtectedWriteService> _write;
		private readonly Lazy<IProtectedReadService> _read;
		private readonly IProtectedGrantContext _grant;

		public IncidentProtectionSeam(Lazy<IProtectedWriteService> write, Lazy<IProtectedReadService> read, IProtectedGrantContext grant)
		{
			_write = write;
			_read = read;
			_grant = grant;
		}

		public bool IsActive => _write?.Value != null;

		public static bool HasProtectedFields<TRow>() => Accessors<TRow>() != null;

		/// <summary>The table's accessor map, or null when the table holds no catalogued field.</summary>
		public static IReadOnlyDictionary<string, (Func<TRow, string> Get, Action<TRow, string> Set)> Accessors<TRow>() => AccessorCache<TRow>.Map;

		private static class AccessorCache<TRow>
		{
			public static readonly IReadOnlyDictionary<string, (Func<TRow, string> Get, Action<TRow, string> Set)> Map =
				typeof(IncidentBackOfficeProtectedFields).GetField(typeof(TRow).Name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
					as IReadOnlyDictionary<string, (Func<TRow, string> Get, Action<TRow, string> Set)>;
		}

		public static int CatalogVersionFor(string table)
		{
			var row = IncidentBackOfficeProtectedFields.All().FirstOrDefault(f => string.Equals(f.Table, table, StringComparison.OrdinalIgnoreCase));
			return row.CatalogVersion == 0 ? IncidentBackOfficeProtectedFields.FoundationCatalogVersion : row.CatalogVersion;
		}

		private static void Mark<TRow>(TRow row) where TRow : class, IIncidentLedgerEntity
		{
			if (row is IIncidentProtectable protectable)
			{
				protectable.IsProtected = true;
				protectable.ProtectedCatalogVersion = Math.Max(protectable.ProtectedCatalogVersion ?? 0, CatalogVersionFor(row.TableName));
			}
		}

		/// <summary>Insert: the key is allocated first (it binds the envelope), then the values are sealed and written.</summary>
		public async Task<TRow> InsertAsync<TRow>(IIncidentLedgerRepository<TRow> repository, TRow entity, IncidentActor actor, CancellationToken cancellationToken)
			where TRow : class, IIncidentLedgerEntity, new()
		{
			var accessors = Accessors<TRow>();
			var held = accessors.ToDictionary(a => a.Key, a => a.Value.Get(entity), StringComparer.OrdinalIgnoreCase);
			if (held.Values.All(string.IsNullOrEmpty))
				return await repository.InsertNewAsync(entity, actor.UserId, cancellationToken);
			foreach (var accessor in accessors) accessor.Value.Set(entity, null);
			var allocated = await repository.InsertNewAsync(entity, actor.UserId, cancellationToken);
			foreach (var accessor in accessors) accessor.Value.Set(allocated, held[accessor.Key]);
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, allocated, null, (string)allocated.IdValue, accessors, () => Mark(allocated), null, null, true, cancellationToken);
			if (result != null && !result.Success) throw new IncidentRuleException("ProtectedWriteRefused");
			var version = (allocated as IIncidentVersioned)?.RowVersion ?? 0;
			return await repository.UpdateVersionedAsync(allocated, version, actor.UserId, cancellationToken);
		}

		/// <summary>Update: seals changed values in place; a REDACTED value posted back from an unrevealed page keeps the stored envelope.</summary>
		public async Task PrepareUpdateAsync<TRow>(TRow entity, TRow existing, IncidentActor actor, CancellationToken cancellationToken) where TRow : class, IIncidentLedgerEntity
		{
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, entity, existing, (string)entity.IdValue, Accessors<TRow>(), () => Mark(entity), null, null, true, cancellationToken);
			if (result != null && !result.Success) throw new IncidentRuleException("ProtectedWriteRefused");
		}

		/// <summary>User read: a grant holder sees the values, everyone else REDACTED. Never throws.</summary>
		public async Task ResolveForReadAsync<TRow>(IReadOnlyList<TRow> rows, int departmentId) where TRow : class, IIncidentLedgerEntity
		{
			var accessors = Accessors<TRow>();
			if (accessors == null || _read?.Value == null || rows == null || rows.Count == 0) return;
			try { await _read.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, (string)r.IdValue)).ToList(), accessors, _grant?.GrantToken, _grant?.UserId); }
			catch (Exception ex) { Logging.LogException(ex, "Protected Incident Back Office rows could not be resolved for read."); }
		}

		/// <summary>Workload read under a broker purpose (incident-logistics, incident-costing, incident-self-service). Never throws.</summary>
		public async Task<bool> ResolveForWorkloadAsync<TRow>(IReadOnlyList<TRow> rows, int departmentId, string purpose) where TRow : class, IIncidentLedgerEntity
		{
			var accessors = Accessors<TRow>();
			if (accessors == null || _read?.Value == null || rows == null || rows.Count == 0) return true;
			try
			{
				var result = await _read.Value.ResolveRecordsEntitiesForWorkloadAsync(departmentId, purpose, rows.Select(r => (r, (string)r.IdValue)).ToList(), accessors);
				return result == null || !result.IsProtected || result.RedactedFields == null || result.RedactedFields.Count == 0;
			}
			catch (Exception ex) { Logging.LogException(ex, $"Protected Incident Back Office rows could not be resolved for the {purpose} workload."); return false; }
		}
	}
}
