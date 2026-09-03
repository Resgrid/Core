using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Reusable transactional outbox transport (RMS plan section 5.6). Enqueue writes inside the caller's
	/// unit of work; dispatch publishes <see cref="DomainEventDispatchedEvent"/> on the in-process event
	/// aggregator and marks the row, retrying with exponential backoff and parking it as Failed after the
	/// attempt cap. The row is the durable source: a crash between publish and mark re-delivers rather
	/// than drops.
	/// </summary>
	public class DomainEventOutboxService : IDomainEventOutboxService
	{
		public const int MaximumAttempts = 8;
		private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

		private readonly IDomainEventOutboxRepository _outboxRepository;
		private readonly IEventAggregator _eventAggregator;

		public DomainEventOutboxService(IDomainEventOutboxRepository outboxRepository, IEventAggregator eventAggregator)
		{
			_outboxRepository = outboxRepository;
			_eventAggregator = eventAggregator;
		}

		public async Task<DomainEventOutboxEntry> EnqueueAsync(int departmentId, string producerSubsystem, DomainEventEnvelope envelope, CancellationToken cancellationToken = default)
		{
			if (envelope == null) throw new ArgumentNullException(nameof(envelope));
			if (string.IsNullOrWhiteSpace(envelope.EventName)) throw new ArgumentException("EventName is required.", nameof(envelope));
			if (string.IsNullOrWhiteSpace(envelope.AggregateId)) throw new ArgumentException("AggregateId is required.", nameof(envelope));

			var now = DateTime.UtcNow;
			var entry = new DomainEventOutboxEntry
			{
				DepartmentId = departmentId,
				EventId = Guid.NewGuid().ToString(),
				ProducerSubsystem = producerSubsystem,
				EventName = envelope.EventName,
				SchemaVersion = envelope.SchemaVersion <= 0 ? 1 : envelope.SchemaVersion,
				AggregateType = envelope.AggregateType ?? string.Empty,
				AggregateId = envelope.AggregateId,
				AggregateVersion = envelope.AggregateVersion,
				Sequence = await _outboxRepository.GetNextSequenceAsync(departmentId, envelope.AggregateId),
				TriggerEventType = envelope.Trigger.HasValue ? (int?)envelope.Trigger.Value : null,
				PayloadJson = JsonConvert.SerializeObject(envelope.Payload ?? new object()),
				CorrelationId = envelope.CorrelationId,
				CausationId = envelope.CausationId,
				OriginClient = (int)envelope.OriginClient,
				HopCount = 0,
				State = (int)DomainEventOutboxState.Pending,
				Attempts = 0,
				OccurredOn = envelope.OccurredOn ?? now,
				CreatedOn = now
			};

			return await _outboxRepository.InsertAsync(entry, cancellationToken, true);
		}

		public async Task<int> DispatchAfterCommitAsync(IEnumerable<long> domainEventOutboxIds, CancellationToken cancellationToken = default)
		{
			var dispatched = 0;
			foreach (var id in (domainEventOutboxIds ?? Enumerable.Empty<long>()).Distinct())
			{
				try
				{
					var entry = await _outboxRepository.ClaimByIdAsync(id, "inproc:" + Environment.MachineName, LeaseDuration, DateTime.UtcNow, cancellationToken);
					if (entry == null)
						continue;

					if (await DispatchOneAsync(entry, cancellationToken))
						dispatched++;
				}
				catch (Exception ex)
				{
					// The post-commit path is best effort; worker command 40 sweeps whatever it missed.
					Logging.LogException(ex, $"Post-commit outbox dispatch failed for row {id}.");
				}
			}

			return dispatched;
		}

		public async Task<int> DispatchPendingAsync(string leaseOwner, int batchSize, CancellationToken cancellationToken = default)
		{
			var entries = (await _outboxRepository.ClaimPendingBatchAsync(leaseOwner, LeaseDuration, batchSize <= 0 ? 100 : batchSize, DateTime.UtcNow, cancellationToken))?.ToList()
						  ?? new List<DomainEventOutboxEntry>();

			var dispatched = 0;
			foreach (var entry in entries.OrderBy(e => e.DomainEventOutboxId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await DispatchOneAsync(entry, cancellationToken))
					dispatched++;
			}

			return dispatched;
		}

		public async Task<DomainEventOutboxHealth> GetHealthAsync()
		{
			var oldest = await _outboxRepository.GetOldestPendingCreatedOnAsync();
			return new DomainEventOutboxHealth
			{
				Pending = await _outboxRepository.CountByStateAsync((int)DomainEventOutboxState.Pending),
				Failed = await _outboxRepository.CountByStateAsync((int)DomainEventOutboxState.Failed),
				OldestPendingCreatedOn = oldest,
				Backlog = oldest.HasValue ? DateTime.UtcNow - oldest.Value : (TimeSpan?)null
			};
		}

		public Task<int> PurgeDispatchedAsync(int olderThanDays, CancellationToken cancellationToken = default)
		{
			return _outboxRepository.PurgeDispatchedOlderThanAsync(DateTime.UtcNow.AddDays(-Math.Max(1, olderThanDays)), cancellationToken);
		}

		/// <summary>Backoff for attempt n (1-based): 1m, 4m, 16m, ... capped at 6h.</summary>
		public static TimeSpan BackoffFor(int attempts)
		{
			var minutes = Math.Pow(4, Math.Max(0, attempts - 1));
			return TimeSpan.FromMinutes(Math.Min(360, minutes));
		}

		private async Task<bool> DispatchOneAsync(DomainEventOutboxEntry entry, CancellationToken cancellationToken)
		{
			try
			{
				_eventAggregator.SendMessage(new DomainEventDispatchedEvent
				{
					DepartmentId = entry.DepartmentId,
					EventId = entry.EventId,
					ProducerSubsystem = entry.ProducerSubsystem,
					EventName = entry.EventName,
					SchemaVersion = entry.SchemaVersion,
					AggregateType = entry.AggregateType,
					AggregateId = entry.AggregateId,
					Sequence = entry.Sequence,
					TriggerEventType = entry.TriggerEventType,
					PayloadJson = entry.PayloadJson,
					CorrelationId = entry.CorrelationId,
					CausationId = entry.CausationId,
					OriginClient = entry.OriginClient,
					IsReplay = entry.Attempts > 1,
					OccurredOn = entry.OccurredOn
				});

				await _outboxRepository.MarkDispatchedAsync(entry.DomainEventOutboxId, DateTime.UtcNow, cancellationToken);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Outbox dispatch failed for event {entry.EventId} (attempt {entry.Attempts}).");
				var terminal = entry.Attempts >= MaximumAttempts;
				await _outboxRepository.MarkFailedAsync(entry.DomainEventOutboxId, ex.Message, terminal ? (DateTime?)null : DateTime.UtcNow.Add(BackoffFor(entry.Attempts)), terminal, cancellationToken);
				return false;
			}
		}
	}
}
