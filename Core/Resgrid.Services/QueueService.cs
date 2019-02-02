using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class QueueService : IQueueService
	{
		private readonly IGenericDataRepository<QueueItem> _queueItemsRepository;
		private readonly IOutboundQueueProvider _outboundQueueProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IGeoLocationProvider _geoLocationProvider;

		public QueueService(IGenericDataRepository<QueueItem> queueItemsRepository, IOutboundQueueProvider outboundQueueProvider, IDepartmentSettingsService departmentSettingsService, 
			IDepartmentsService departmentsService, IGeoLocationProvider geoLocationProvider)
		{
			_queueItemsRepository = queueItemsRepository;
			_outboundQueueProvider = outboundQueueProvider;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_geoLocationProvider = geoLocationProvider;
		}

		public QueueItem GetQueueItemById(int queueItemId)
		{
			return _queueItemsRepository.GetAll().FirstOrDefault(x => x.QueueItemId == queueItemId);
		}

		public List<QueueItem> Dequeue(QueueTypes type)
		{
			var items = (from q in _queueItemsRepository.GetAll()
									 where q.QueueType == (int)type && q.PickedUp == null && q.CompletedOn == null
									 select q).ToList();

			foreach (var i in items)
			{
				i.PickedUp = DateTime.Now.ToUniversalTime();
				_queueItemsRepository.SaveOrUpdate(i);
			}

			return items;
		}

		public void Requeue(QueueItem item)
		{
			item.PickedUp = null;
			_queueItemsRepository.SaveOrUpdate(item);
		}

		public void RequeueAll(IEnumerable<QueueItem> items)
		{
			if (items != null)
			{
				foreach (var i in items)
				{
					if (i.QueueItemId > 0)
					{
						i.PickedUp = null;
						_queueItemsRepository.SaveOrUpdate(i);
					}
				}
			}
		}

		public void EnqueueMessageBroadcast(MessageQueueItem mqi)
		{
			if (Config.SystemBehaviorConfig.IsAzure)
			{
				if (!String.IsNullOrWhiteSpace(mqi.Message.ReceivingUserId))
				{
					var dm = _departmentsService.GetDepartmentMember(mqi.Message.ReceivingUserId, mqi.DepartmentId);
					string departmentNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(dm.DepartmentId);
					mqi.DepartmentTextNumber = departmentNumber;

					if (mqi.Message.ReceivingUser == null)
					{
						var user = mqi.Profiles.FirstOrDefault(x => x.UserId == mqi.Message.ReceivingUserId);

						if (user != null && user.User != null)
							mqi.Message.ReceivingUser = user.User;
					}
				}
				else if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId))
				{
					var dm = _departmentsService.GetDepartmentMember(mqi.Message.SendingUserId, mqi.DepartmentId);
					string departmentNumber = _departmentSettingsService.GetTextToCallNumberForDepartment(dm.DepartmentId);
					mqi.DepartmentTextNumber = departmentNumber;
				}

				_outboundQueueProvider.EnqueueMessage(mqi);
			}
			else
			{
				QueueItem item = new QueueItem();
				item.QueueType = (int)QueueTypes.MessageBroadcast;
				item.SourceId = mqi.Message.MessageId.ToString();
				item.QueuedOn = DateTime.UtcNow;

				_queueItemsRepository.SaveOrUpdate(item);
			}
		}

		public void EnqueueCallBroadcast(CallQueueItem cqi)
		{
			if (Config.SystemBehaviorConfig.IsAzure)
			{
				// If we have geolocation data, lets get the approx address now.
				if (!string.IsNullOrEmpty(cqi.Call.GeoLocationData) && String.IsNullOrWhiteSpace(cqi.Call.Address))
				{
					try
					{
						string[] points = cqi.Call.GeoLocationData.Split(char.Parse(","));

						if (points != null && points.Length == 2)
						{
							cqi.Address = _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(points[0]), double.Parse(points[1]));
						}
					}
					catch
					{
					}
				}
				else
				{
					cqi.Address = cqi.Call.Address;
				}

				if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
				{
					foreach (var dispatch in cqi.Call.Dispatches)
					{
						if (dispatch.User == null)
						{
							var user = cqi.Profiles.FirstOrDefault(x => x.UserId == dispatch.UserId);
						}
					}
				}

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

				_outboundQueueProvider.EnqueueCall(cqi);
			}
			else
			{
				QueueItem item = new QueueItem();
				item.QueueType = (int)QueueTypes.CallBroadcast;
				item.SourceId = cqi.Call.CallId.ToString();
				item.QueuedOn = DateTime.UtcNow;

				_queueItemsRepository.SaveOrUpdate(item);
			}
		}

		public void EnqueueDistributionListBroadcast(DistributionListQueueItem dlqi)
		{
			if (Config.SystemBehaviorConfig.IsAzure)
			{
				_outboundQueueProvider.EnqueueDistributionList(dlqi);
			}
			else
			{
				QueueItem item = new QueueItem();
				item.QueueType = (int)QueueTypes.DistributionListBroadcast;
				item.SourceId = dlqi.Message.MessageID.ToString();
				item.QueuedOn = DateTime.UtcNow;

				_queueItemsRepository.SaveOrUpdate(item);
			}
		}

		public void SetQueueItemCompleted(int queueItemId)
		{
			var item = GetQueueItemById(queueItemId);

			item.CompletedOn = DateTime.Now.ToUniversalTime();

			_queueItemsRepository.SaveOrUpdate(item);
		}
	}
}
