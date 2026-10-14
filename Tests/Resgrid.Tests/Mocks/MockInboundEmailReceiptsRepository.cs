using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.Mocks
{
	/// <summary>
	/// In-memory <see cref="IInboundEmailReceiptsRepository"/> with the same conditional-write rules as the SQL
	/// (InboundEmailReceiptsDatabaseTests proves those on both engines), so controller tests run redeliveries end to end.
	/// </summary>
	public sealed class MockInboundEmailReceiptsRepository : IInboundEmailReceiptsRepository
	{
		private readonly List<InboundEmailReceipt> _receipts = new List<InboundEmailReceipt>();

		public IReadOnlyList<InboundEmailReceipt> Receipts => _receipts;

		public InboundEmailReceipt Find(string providerMessageId) =>
			_receipts.SingleOrDefault(r => string.Equals(r.ProviderMessageId, providerMessageId, StringComparison.OrdinalIgnoreCase));

		public void Seed(InboundEmailReceipt receipt) => _receipts.Add(receipt);

		public Task<bool> TryInsertAsync(InboundEmailReceipt receipt, CancellationToken cancellationToken = default)
		{
			if (Find(receipt.ProviderMessageId) != null)
				return Task.FromResult(false);

			_receipts.Add(Copy(receipt));
			return Task.FromResult(true);
		}

		public Task<InboundEmailReceipt> GetByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default)
		{
			var receipt = Find(providerMessageId);
			return Task.FromResult(receipt == null ? null : Copy(receipt));
		}

		public Task<bool> TryReclaimAsync(string receiptId, int state, DateTime now, DateTime leaseCutoff, CancellationToken cancellationToken = default)
		{
			var receipt = Get(receiptId);
			if (receipt == null || receipt.State != state || (receipt.ClaimedOn.HasValue && receipt.ClaimedOn.Value >= leaseCutoff))
				return Task.FromResult(false);

			receipt.ClaimedOn = now;
			receipt.Deliveries++;
			return Task.FromResult(true);
		}

		public Task<bool> RecordDeliveryAsync(string receiptId, CancellationToken cancellationToken = default)
		{
			var receipt = Get(receiptId);
			if (receipt == null)
				return Task.FromResult(false);

			receipt.Deliveries++;
			return Task.FromResult(true);
		}

		public Task<bool> MarkSavedAsync(string receiptId, int entityId, int pendingSteps, DateTime now, CancellationToken cancellationToken = default)
		{
			var receipt = Get(receiptId);
			if (receipt == null || receipt.State != (int)InboundEmailReceiptStates.Processing)
				return Task.FromResult(false);

			receipt.State = (int)InboundEmailReceiptStates.Saved;
			receipt.EntityId = entityId;
			receipt.PendingSteps = pendingSteps;
			receipt.SavedOn = now;
			receipt.ClaimedOn = now;
			return Task.FromResult(true);
		}

		public Task<bool> CompleteStepsAsync(string receiptId, int steps, CancellationToken cancellationToken = default)
		{
			var receipt = Get(receiptId);
			if (receipt == null || receipt.State != (int)InboundEmailReceiptStates.Saved)
				return Task.FromResult(false);

			receipt.PendingSteps &= ~steps;
			return Task.FromResult(true);
		}

		public Task<bool> MarkCompletedAsync(string receiptId, DateTime now, CancellationToken cancellationToken = default)
		{
			var receipt = Get(receiptId);
			if (receipt == null || receipt.State == (int)InboundEmailReceiptStates.Completed)
				return Task.FromResult(false);

			receipt.State = (int)InboundEmailReceiptStates.Completed;
			receipt.PendingSteps = 0;
			receipt.CompletedOn = now;
			receipt.ClaimedOn = null;
			return Task.FromResult(true);
		}

		public Task<bool> ReleaseAsync(string receiptId, CancellationToken cancellationToken = default)
		{
			var receipt = Get(receiptId);
			if (receipt == null || receipt.State == (int)InboundEmailReceiptStates.Completed)
				return Task.FromResult(false);

			receipt.ClaimedOn = null;
			return Task.FromResult(true);
		}

		public Task<int> PurgeReceivedBeforeAsync(DateTime before, CancellationToken cancellationToken = default)
			=> Task.FromResult(_receipts.RemoveAll(r => r.ReceivedOn < before));

		public Task<IEnumerable<InboundEmailReceipt>> GetAllAsync() => Task.FromResult<IEnumerable<InboundEmailReceipt>>(_receipts.Select(Copy).ToList());

		public Task<InboundEmailReceipt> GetByIdAsync(object id)
		{
			var receipt = Get(id as string);
			return Task.FromResult(receipt == null ? null : Copy(receipt));
		}

		public Task<IEnumerable<InboundEmailReceipt>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException("Receipts have no department.");

		public Task<InboundEmailReceipt> InsertAsync(InboundEmailReceipt entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();

		public Task<InboundEmailReceipt> UpdateAsync(InboundEmailReceipt entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();

		public Task<bool> DeleteAsync(InboundEmailReceipt entity, CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task<InboundEmailReceipt> SaveOrUpdateAsync(InboundEmailReceipt entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();

		public Task<IEnumerable<InboundEmailReceipt>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();

		public Task<bool> DeleteMultipleAsync(InboundEmailReceipt entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

		private InboundEmailReceipt Get(string receiptId) => _receipts.SingleOrDefault(r => r.InboundEmailReceiptId == receiptId);

		private static InboundEmailReceipt Copy(InboundEmailReceipt receipt) => new InboundEmailReceipt
		{
			InboundEmailReceiptId = receipt.InboundEmailReceiptId,
			ProviderMessageId = receipt.ProviderMessageId,
			Kind = receipt.Kind,
			State = receipt.State,
			EntityId = receipt.EntityId,
			PendingSteps = receipt.PendingSteps,
			Deliveries = receipt.Deliveries,
			ReceivedOn = receipt.ReceivedOn,
			ClaimedOn = receipt.ClaimedOn,
			SavedOn = receipt.SavedOn,
			CompletedOn = receipt.CompletedOn
		};
	}
}
