using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.CallEmailTemplates;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory CAD email update ledger with the repository's call lookups (open/pending or recently closed calls by external
	/// identifier, incident number or a ledger row) over a caller-supplied call list, and a real per-ID lock.
	/// </summary>
	public class FakeCallEmailUpdatesRepository : ICallEmailUpdatesRepository
	{
		private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

		public List<CallEmailUpdate> Rows { get; } = new List<CallEmailUpdate>();

		/// <summary>The department's calls as the database would hold them.</summary>
		public Func<IEnumerable<Call>> Calls { get; set; } = () => Enumerable.Empty<Call>();

		/// <summary>Another holder keeps every lock: AcquireCadCallLockAsync answers null.</summary>
		public bool LockBusy { get; set; }

		public Task<IEnumerable<CallEmailUpdate>> GetByCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default)
		{
			lock (Rows)
				return Task.FromResult<IEnumerable<CallEmailUpdate>>(Rows.Where(r => r.DepartmentId == departmentId && r.CallId == callId).OrderBy(r => r.ReceivedOn).ToList());
		}

		public Task<IEnumerable<CallEmailUpdate>> GetByMessageAsync(int departmentId, string cadCallId, string messageId, CancellationToken cancellationToken = default)
		{
			lock (Rows)
				return Task.FromResult<IEnumerable<CallEmailUpdate>>(Rows.Where(r => r.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(messageId) &&
					string.Equals(r.CadCallId, cadCallId?.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(r.MessageId, messageId, StringComparison.OrdinalIgnoreCase))
					.OrderBy(r => r.ReceivedOn).ToList());
		}

		public Task<bool> SetOutcomeAsync(string callEmailUpdateId, int fromOutcome, int toOutcome, CancellationToken cancellationToken = default)
		{
			if (FailOutcomeMoves)
				throw new InvalidOperationException("The ledger could not be written.");

			lock (Rows)
			{
				var row = Rows.FirstOrDefault(r => r.CallEmailUpdateId == callEmailUpdateId && r.Outcome == fromOutcome);
				if (row == null)
					return Task.FromResult(false);
				row.Outcome = toOutcome;
				return Task.FromResult(true);
			}
		}

		/// <summary>Every ledger insert throws (the database refused it).</summary>
		public bool FailInserts { get; set; }

		/// <summary>Every <see cref="SetOutcomeAsync"/> throws: the row an update wrote first stays Applying.</summary>
		public bool FailOutcomeMoves { get; set; }

		public Task<IEnumerable<Call>> GetOpenCallsForCadCallIdAsync(int departmentId, string cadCallId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IEnumerable<Call>>(Find(departmentId, cadCallId)
				.Where(c => c.State == (int)CallStates.Active || c.State == (int)CallStates.Pending).ToList());
		}

		public Task<IEnumerable<Call>> GetClosedCallsForCadCallIdAsync(int departmentId, string cadCallId, DateTime closedSinceUtc, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IEnumerable<Call>>(Find(departmentId, cadCallId)
				.Where(c => c.State != (int)CallStates.Active && c.State != (int)CallStates.Pending && c.ClosedOn.HasValue && c.ClosedOn.Value >= closedSinceUtc).ToList());
		}

		public async Task<IAsyncDisposable> AcquireCadCallLockAsync(int departmentId, string cadCallId, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			if (LockBusy)
				return null;

			var gate = _locks.GetOrAdd(departmentId + "|" + cadCallId, _ => new SemaphoreSlim(1, 1));
			if (!await gate.WaitAsync(timeout, cancellationToken))
				return null;

			return new Releaser(gate);
		}

		public Task<CallEmailUpdate> InsertAsync(CallEmailUpdate entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			if (FailInserts)
				throw new InvalidOperationException("The ledger could not be written.");

			lock (Rows)
				Rows.Add(entity);
			return Task.FromResult(entity);
		}

		private IEnumerable<Call> Find(int departmentId, string cadCallId)
		{
			List<int> ledgerCalls;
			lock (Rows)
				ledgerCalls = Rows.Where(r => r.DepartmentId == departmentId && r.CallId.HasValue && string.Equals(r.CadCallId, cadCallId, StringComparison.OrdinalIgnoreCase))
					.Select(r => r.CallId.Value).ToList();

			return (Calls() ?? Enumerable.Empty<Call>()).Where(c => c != null && c.DepartmentId == departmentId && !c.IsDeleted &&
				(string.Equals(c.ExternalIdentifier?.Trim(), cadCallId, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(c.IncidentNumber?.Trim(), cadCallId, StringComparison.OrdinalIgnoreCase) ||
				 ledgerCalls.Contains(c.CallId)));
		}

		public Task<IEnumerable<CallEmailUpdate>> GetAllAsync() => Task.FromResult<IEnumerable<CallEmailUpdate>>(Rows.ToList());
		public Task<CallEmailUpdate> GetByIdAsync(object id) => Task.FromResult(Rows.FirstOrDefault(r => r.CallEmailUpdateId == (string)id));
		public Task<IEnumerable<CallEmailUpdate>> GetAllByDepartmentIdAsync(int departmentId) => Task.FromResult<IEnumerable<CallEmailUpdate>>(Rows.Where(r => r.DepartmentId == departmentId).ToList());
		public Task<CallEmailUpdate> UpdateAsync(CallEmailUpdate entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => Task.FromResult(entity);
		public Task<bool> DeleteAsync(CallEmailUpdate entity, CancellationToken cancellationToken) => Task.FromResult(Rows.Remove(entity));
		public Task<CallEmailUpdate> SaveOrUpdateAsync(CallEmailUpdate entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => InsertAsync(entity, cancellationToken, firstLevelOnly);
		public Task<IEnumerable<CallEmailUpdate>> GetAllByUserIdAsync(string userId) => Task.FromResult(Enumerable.Empty<CallEmailUpdate>());
		public Task<bool> DeleteMultipleAsync(CallEmailUpdate entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => Task.FromResult(true);

		private sealed class Releaser : IAsyncDisposable
		{
			private SemaphoreSlim _gate;

			public Releaser(SemaphoreSlim gate) => _gate = gate;

			public ValueTask DisposeAsync()
			{
				Interlocked.Exchange(ref _gate, null)?.Release();
				return ValueTask.CompletedTask;
			}
		}
	}

	/// <summary>Builds the real CAD update engine over mocks, for controller, importer and engine tests.</summary>
	public static class EmailCallUpdateServices
	{
		/// <summary>
		/// The engine with the given calls service, ledger and settings (stored config, else the format's preset). Everything else is
		/// a loose mock; pass <paramref name="outbox"/>, <paramref name="closure"/> or <paramref name="events"/> to verify them.
		/// </summary>
		public static EmailCallUpdateService Create(ICallsService calls, FakeCallEmailUpdatesRepository ledger, Func<EmailCallUpdateConfig> stored = null,
			Mock<ICallBroadcastOutboxService> outbox = null, Mock<ICallClosureService> closure = null, Mock<IEventAggregator> events = null,
			IDepartmentsService departments = null, IGeoLocationProvider geo = null, EmailLabeledFieldsConfig labels = null,
			Mock<ICallDispatchUnitRepository> unitDispatches = null, IUnitsService units = null, Mock<ICallDispatchStatusService> dispatchStatus = null)
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetEmailCallUpdateConfigAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>()))
				.ReturnsAsync((int d, int? format, bool b) => stored?.Invoke() ?? (format.HasValue ? EmailCallUpdatePresets.ForFormat(format.Value) : null) ?? new EmailCallUpdateConfig());
			settings.Setup(s => s.GetEmailLabeledFieldsConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => labels ?? new EmailLabeledFieldsConfig());

			var departmentsService = departments ?? Mock.Of<IDepartmentsService>(d =>
				d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()) == Task.FromResult(new Department { DepartmentId = 1, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" }));

			return new EmailCallUpdateService(settings.Object, new CallEmailFactory(), calls, ledger, departmentsService, geo ?? Mock.Of<IGeoLocationProvider>(),
				(outbox ?? new Mock<ICallBroadcastOutboxService>()).Object, (closure ?? new Mock<ICallClosureService>()).Object, (dispatchStatus ?? new Mock<ICallDispatchStatusService>()).Object,
				units ?? Mock.Of<IUnitsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<ICallDispatchesRepository>(), Mock.Of<ICallDispatchGroupRepository>(),
				(unitDispatches ?? new Mock<ICallDispatchUnitRepository>()).Object, Mock.Of<ICallDispatchRoleRepository>(), (events ?? new Mock<IEventAggregator>()).Object);
		}
	}
}
