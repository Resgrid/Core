using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Workers.Framework.Workers.ShiftNotifier
{
	public class ShiftNotifierQueue : IQueue<ShiftNotifierQueueItem>
	{
		private bool _cleared;
		private object _lock;
		private bool _isLocked;
		private static Queue<ShiftNotifierQueueItem> _queue;

		private IShiftsService _shiftsService;
		private IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;

		public ShiftNotifierQueue(/*IShiftsService shiftsService, IUserProfileService userProfileService, */IEventAggregator eventAggregator)
		{
			//_shiftsService = shiftsService;
			//_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			
			_queue = new Queue<ShiftNotifierQueueItem>();
			_cleared = false;
		}

		public void PopulateQueue()
		{
			if (!_isLocked)
			{
				_isLocked = true;

				_shiftsService = Bootstrapper.GetKernel().Resolve<IShiftsService>();
				_userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

				var t1 = new Task(async () =>
				{
					try
					{
						var shifts = await _shiftsService.GetShiftsStartingNextDayAsync(DateTime.UtcNow);

						foreach (var shift in shifts)
						{
							var qi = new ShiftNotifierQueueItem();

							if (shift.Personnel != null && shift.Personnel.Any())
								qi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(shift.Personnel.Select(x => x.UserId).ToList());

							qi.Day = shift.GetShiftDayforDateTime(DateTime.UtcNow.AddDays(1));
							if (qi.Day != null)
							{
								if (qi.Profiles == null)
									qi.Profiles = new List<UserProfile>();

								qi.Signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(qi.Day.ShiftDayId);

								if (qi.Signups != null && qi.Signups.Any())
								{
									qi.Profiles.AddRange(await _userProfileService.GetSelectedUserProfilesAsync(qi.Signups.Select(x => x.UserId).ToList()));

									var users = new List<string>();
									foreach (var signup in qi.Signups)
									{
										if (signup.Trade != null)
										{
											if (!String.IsNullOrWhiteSpace(signup.Trade.UserId))
												users.Add(signup.Trade.UserId);
											else if (signup.Trade.TargetShiftSignup != null)
												users.Add(signup.Trade.TargetShiftSignup.UserId);
										}
									}

									if (users.Any())
										qi.Profiles.AddRange(await _userProfileService.GetSelectedUserProfilesAsync(users));
								}
							}

							qi.Shift = shift;

							_queue.Enqueue(qi);
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
					finally
					{
						_isLocked = false;
						_cleared = false;

						_shiftsService = null;
						_userProfileService = null;
					}
				});

				t1.Start();
			}
		}

		public bool IsLocked
		{
			get { return _isLocked; }
		}

		public void EnsureExist()
		{
			if (_queue == null)
				_queue = new Queue<ShiftNotifierQueueItem>();
		}

		public async Task<bool> Clear()
		{
			_cleared = true;
			_queue.Clear();

			return _cleared;
		}

		public void AddItem(ShiftNotifierQueueItem item)
		{
			_queue.Enqueue(item);
		}

		public ShiftNotifierQueueItem GetItem()
		{
			var item = _queue.Dequeue();

			if (item == null)
				PopulateQueue();

			return item;
		}

		public async Task<IEnumerable<ShiftNotifierQueueItem>> GetItems(int maxItemsToReturn)
		{
			var items = new List<ShiftNotifierQueueItem>();

			_eventAggregator.SendMessage<WorkerHeartbeatEvent>(new WorkerHeartbeatEvent() { WorkerType = (int)JobTypes.ShiftNotifier, Timestamp = DateTime.UtcNow });

			if (_queue.Count <= 0)
				PopulateQueue();

			while (_isLocked)
			{
				Thread.Sleep(1000);
			}

			int count = 0;
			if (_queue.Count < maxItemsToReturn)
				count = _queue.Count;
			else
				count = maxItemsToReturn;

			for (int i = 0; i < count; i++)
			{
				if (_queue.Count > 0)
					items.Add(_queue.Dequeue());
			}

			return items.AsEnumerable();
		}
	}
}
