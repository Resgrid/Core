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

		/// <summary>
		/// Everyone on the day's resolved roster (assigned staff, approved signups, supervisor edits and trades applied).
		/// When set, these are the people reminded.
		/// </summary>
		public List<string> UserIds { get; set; }
	}
}