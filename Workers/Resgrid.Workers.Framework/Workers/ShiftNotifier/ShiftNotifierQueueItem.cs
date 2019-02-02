using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Workers.Framework.Workers.ShiftNotifier
{
	public class ShiftNotifierQueueItem : QueueItem
	{
		public Shift Shift { get; set; }
		public ShiftDay Day { get; set; }
		public List<ShiftSignup> Signups { get; set; }
		public List<UserProfile> Profiles { get; set; }
	}
}