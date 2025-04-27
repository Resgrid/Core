using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class QueueService : IQueueService
	{
		private readonly IQueueItemsRepository _queueItemsRepository;
		private readonly IOutboundQueueProvider _outboundQueueProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IGeoLocationProvider _geoLocationProvider;

		public QueueService(IQueueItemsRepository queueItemsRepository, IOutboundQueueProvider outboundQueueProvider, IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService, IGeoLocationProvider geoLocationProvider)
		{
			_queueItemsRepository = queueItemsRepository;
			_outboundQueueProvider = outboundQueueProvider;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_geoLocationProvider = geoLocationProvider;
		}

		public async Task<QueueItem> GetQueueItemByIdAsync(int queueItemId)
		{
			return await _queueItemsRepository.GetByIdAsync(queueItemId);
		}

		public async Task<QueueItem> GetPendingDeleteDepartmentQueueItemAsync(int departmentId)
		{
			var allItems = await _queueItemsRepository.GetAllAsync();
			var depItem = allItems.FirstOrDefault(x =>
				x.SourceId == departmentId.ToString() && x.ToBeCompletedOn > DateTime.UtcNow && x.QueueType == (int)QueueTypes.DeleteDepartment && x.CompletedOn == null);

			return depItem;
		}

		public async Task<List<QueueItem>> GetAllPendingDeleteDepartmentQueueItemsAsync()
		{
			var allItems = await _queueItemsRepository.GetAllAsync();
			var depItems = allItems.Where(x =>
				x.ToBeCompletedOn > DateTime.UtcNow && x.QueueType == (int)QueueTypes.DeleteDepartment && x.CompletedOn == null).ToList();

			return depItems;
		}

		public async Task<QueueItem> EnqueuePendingDeleteDepartmentAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			QueueItem item = new QueueItem();
			item.QueueType = (int)QueueTypes.DeleteDepartment;
			item.SourceId = departmentId.ToString();
			item.QueuedOn = DateTime.UtcNow;
			item.ToBeCompletedOn = item.QueuedOn.AddDays(25);
			item.QueuedByUserId = userId;

			var result = await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);

			return result;
		}

		public async Task<QueueItem> CancelPendingDepartmentDeletionRequest(int departmentId, string name, CancellationToken cancellationToken = default(CancellationToken))
		{
			QueueItem item = await GetPendingDeleteDepartmentQueueItemAsync(departmentId);
			item.CompletedOn = DateTime.UtcNow;
			item.Data = $"{name} cancelled the department deletion request.";

			var result = await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);

			return result;
		}

		public async Task<List<QueueItem>> DequeueAsync(QueueTypes type, CancellationToken cancellationToken = default(CancellationToken))
		{
			var items = await _queueItemsRepository.GetPendingQueueItemsByTypeIdAsync((int)type);

			foreach (var i in items)
			{
				i.PickedUp = DateTime.UtcNow;
				await _queueItemsRepository.SaveOrUpdateAsync(i, cancellationToken);
			}

			return items.ToList();
		}

		public async Task<QueueItem> RequeueAsync(QueueItem item, CancellationToken cancellationToken = default(CancellationToken))
		{
			item.PickedUp = null;
			return await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);
		}

		public async Task<bool> RequeueAllAsync(IEnumerable<QueueItem> items, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (items != null)
			{
				foreach (var i in items)
				{
					if (i.QueueItemId > 0)
					{
						await RequeueAsync(i, cancellationToken);
					}
				}

				return true;
			}

			return false;
		}

		public async Task<bool> EnqueueMessageBroadcastAsync(MessageQueueItem mqi, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!String.IsNullOrWhiteSpace(mqi.Message.ReceivingUserId))
			{
				var dm = await _departmentsService.GetDepartmentMemberAsync(mqi.Message.ReceivingUserId, mqi.DepartmentId);
				//string departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(dm.DepartmentId);
				//mqi.DepartmentTextNumber = departmentNumber;

				if (mqi.Message.ReceivingUser == null)
				{
					var user = mqi.Profiles.FirstOrDefault(x => x.UserId == mqi.Message.ReceivingUserId);

					if (user != null && user.User != null)
						mqi.Message.ReceivingUser = user.User;
				}
			}
			else if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId))
			{
				var dm = await _departmentsService.GetDepartmentMemberAsync(mqi.Message.SendingUserId, mqi.DepartmentId);
				//string departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(dm.DepartmentId);
				//mqi.DepartmentTextNumber = departmentNumber;
			}

			return await _outboundQueueProvider.EnqueueMessage(mqi);
		}

		public async Task<bool> EnqueueCallBroadcastAsync(CallQueueItem cqi, CancellationToken cancellationToken = default(CancellationToken))
		{
			//if (Config.SystemBehaviorConfig.IsAzure)
			//{
			// If we have geolocation data, lets get the approx address now.
			if (!string.IsNullOrEmpty(cqi.Call.GeoLocationData) && cqi.Call.GeoLocationData.Length > 1 && String.IsNullOrWhiteSpace(cqi.Call.Address))
			{
				try
				{
					string[] points = cqi.Call.GeoLocationData.Split(char.Parse(","));

					if (points != null && points.Length == 2)
					{
						cqi.Address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
					}
				}
				catch
				{
					/* Ignore */
				}
			}
			else
			{
				cqi.Address = cqi.Call.Address;
			}

			//if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
			//{
			//	foreach (var dispatch in cqi.Call.Dispatches)
			//	{
			//		if (dispatch.User == null)
			//		{
			//			var user = cqi.Profiles.FirstOrDefault(x => x.UserId == dispatch.UserId);
			//		}
			//	}
			//}

			if (cqi.Call.Attachments != null &&
			    cqi.Call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio) > 0)
			{
				var audio = cqi.Call.Attachments.FirstOrDefault(x =>
					x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);

				if (audio != null)
					cqi.CallDispatchAttachmentId = audio.CallAttachmentId;
			}

			// We can't queue up any attachment data as it'll be too large.
			cqi.Call.Attachments = null;

			return await _outboundQueueProvider.EnqueueCall(cqi);
			//}
			//else
			//{
			//	QueueItem item = new QueueItem();
			//	item.QueueType = (int)QueueTypes.CallBroadcast;
			//	item.SourceId = cqi.Call.CallId.ToString();
			//	item.QueuedOn = DateTime.UtcNow;

			//	await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);
			//}

			//return true;
		}

		public async Task<bool> EnqueueDistributionListBroadcastAsync(DistributionListQueueItem dlqi, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (Config.SystemBehaviorConfig.IsAzure)
			{
				return await _outboundQueueProvider.EnqueueDistributionList(dlqi);
			}
			else
			{
				QueueItem item = new QueueItem();
				item.QueueType = (int)QueueTypes.DistributionListBroadcast;
				item.SourceId = dlqi.Message.MessageID.ToString();
				item.QueuedOn = DateTime.UtcNow;

				await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);
			}

			return true;
		}

		public async Task<QueueItem> SetQueueItemCompletedAsync(int queueItemId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var item = await GetQueueItemByIdAsync(queueItemId);

			item.CompletedOn = DateTime.UtcNow;

			return await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);
		}

		public async Task<QueueItem> UpdateQueueItem(QueueItem item, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _queueItemsRepository.SaveOrUpdateAsync(item, cancellationToken);
		}
	}
}
