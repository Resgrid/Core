using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	[ProtoContract]
	public class CallQueueItem
	{
		[ProtoMember(1)]
		public QueueItem QueueItem { get; set; }

		[ProtoMember(2)]
		public Call Call { get; set; }

		[ProtoMember(3)]
		public List<UserProfile> Profiles { get; set; }

		[ProtoMember(4)]
		public string DepartmentTextNumber { get; set; }

		[ProtoMember(5)]
		public string Address { get; set; }

		[ProtoMember(6)]
		public int CallDispatchAttachmentId { get; set; }

		[ProtoMember(7)]
		public bool BroadcastOnlySelectedDispatches { get; set; }

		[ProtoMember(8)]
		public List<string> BroadcastUserIds { get; set; }

		[ProtoMember(9)]
		public List<int> BroadcastGroupIds { get; set; }

		[ProtoMember(10)]
		public List<int> BroadcastUnitIds { get; set; }

		[ProtoMember(11)]
		public List<int> BroadcastRoleIds { get; set; }

		public void SetBroadcastDispatches(IEnumerable<string> userIds, IEnumerable<int> groupIds,
			IEnumerable<int> unitIds, IEnumerable<int> roleIds)
		{
			BroadcastOnlySelectedDispatches = true;
			BroadcastUserIds = userIds?.Distinct().ToList() ?? new List<string>();
			BroadcastGroupIds = groupIds?.Distinct().ToList() ?? new List<int>();
			BroadcastUnitIds = unitIds?.Distinct().ToList() ?? new List<int>();
			BroadcastRoleIds = roleIds?.Distinct().ToList() ?? new List<int>();

			if (Call != null)
			{
				Call = Serializer.DeepClone(Call);
				ApplyBroadcastDispatchFilter();
			}
		}

		public void ApplyBroadcastDispatchFilter()
		{
			if (!BroadcastOnlySelectedDispatches || Call == null)
				return;

			var userIds = new HashSet<string>(BroadcastUserIds ?? Enumerable.Empty<string>());
			var groupIds = new HashSet<int>(BroadcastGroupIds ?? Enumerable.Empty<int>());
			var unitIds = new HashSet<int>(BroadcastUnitIds ?? Enumerable.Empty<int>());
			var roleIds = new HashSet<int>(BroadcastRoleIds ?? Enumerable.Empty<int>());

			Call.Dispatches = (Call.Dispatches ?? Enumerable.Empty<CallDispatch>())
				.Where(x => userIds.Contains(x.UserId)).ToList();
			Call.GroupDispatches = (Call.GroupDispatches ?? Enumerable.Empty<CallDispatchGroup>())
				.Where(x => groupIds.Contains(x.DepartmentGroupId)).ToList();
			Call.UnitDispatches = (Call.UnitDispatches ?? Enumerable.Empty<CallDispatchUnit>())
				.Where(x => unitIds.Contains(x.UnitId)).ToList();
			Call.RoleDispatches = (Call.RoleDispatches ?? Enumerable.Empty<CallDispatchRole>())
				.Where(x => roleIds.Contains(x.RoleId)).ToList();
		}
	}
}
