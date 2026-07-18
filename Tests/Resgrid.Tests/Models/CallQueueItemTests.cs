using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using ProtoBuf;
using Resgrid.Model;
using Resgrid.Model.Queue;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CallQueueItemTests
	{
		[Test]
		public void SetBroadcastDispatches_WithSelectedDispatches_QueuesOnlyNewEntities()
		{
			// Arrange
			var originalCall = CreateCall();
			var queueItem = new CallQueueItem
			{
				Call = originalCall
			};

			// Act
			queueItem.SetBroadcastDispatches(new[] { "new-user" }, new[] { 12 }, new[] { 22 }, new[] { 32 });

			// Assert
			queueItem.Call.Dispatches.Select(x => x.UserId).Should().Equal("new-user");
			queueItem.Call.GroupDispatches.Select(x => x.DepartmentGroupId).Should().Equal(12);
			queueItem.Call.UnitDispatches.Select(x => x.UnitId).Should().Equal(22);
			queueItem.Call.RoleDispatches.Select(x => x.RoleId).Should().Equal(32);
			originalCall.Dispatches.Should().HaveCount(2);
			originalCall.GroupDispatches.Should().HaveCount(2);
			originalCall.UnitDispatches.Should().HaveCount(2);
			originalCall.RoleDispatches.Should().HaveCount(2);
		}

		[Test]
		public void BroadcastDispatchSelection_AfterQueueSerialization_StillFiltersOldEntities()
		{
			// Arrange
			var queueItem = new CallQueueItem
			{
				Call = CreateCall()
			};
			queueItem.BroadcastOnlySelectedDispatches = true;
			queueItem.BroadcastUserIds = new List<string> { "new-user" };
			queueItem.BroadcastGroupIds = new List<int> { 12 };
			queueItem.BroadcastUnitIds = new List<int> { 22 };
			queueItem.BroadcastRoleIds = new List<int> { 32 };

			// Act
			var deserializedQueueItem = Serializer.DeepClone(queueItem);
			deserializedQueueItem.ApplyBroadcastDispatchFilter();

			// Assert
			deserializedQueueItem.BroadcastOnlySelectedDispatches.Should().BeTrue();
			deserializedQueueItem.Call.Dispatches.Select(x => x.UserId).Should().Equal("new-user");
			deserializedQueueItem.Call.GroupDispatches.Select(x => x.DepartmentGroupId).Should().Equal(12);
			deserializedQueueItem.Call.UnitDispatches.Select(x => x.UnitId).Should().Equal(22);
			deserializedQueueItem.Call.RoleDispatches.Select(x => x.RoleId).Should().Equal(32);
		}

		[Test]
		public void ApplyBroadcastDispatchFilter_WithoutRestriction_KeepsAllEntitiesForRedispatch()
		{
			// Arrange
			var queueItem = new CallQueueItem
			{
				Call = CreateCall()
			};

			// Act
			queueItem.ApplyBroadcastDispatchFilter();

			// Assert
			queueItem.Call.Dispatches.Should().HaveCount(2);
			queueItem.Call.GroupDispatches.Should().HaveCount(2);
			queueItem.Call.UnitDispatches.Should().HaveCount(2);
			queueItem.Call.RoleDispatches.Should().HaveCount(2);
		}

		private static Call CreateCall()
		{
			return new Call
			{
				Dispatches = new List<CallDispatch>
				{
					new CallDispatch { UserId = "existing-user" },
					new CallDispatch { UserId = "new-user" }
				},
				GroupDispatches = new List<CallDispatchGroup>
				{
					new CallDispatchGroup { DepartmentGroupId = 11 },
					new CallDispatchGroup { DepartmentGroupId = 12 }
				},
				UnitDispatches = new List<CallDispatchUnit>
				{
					new CallDispatchUnit { UnitId = 21 },
					new CallDispatchUnit { UnitId = 22 }
				},
				RoleDispatches = new List<CallDispatchRole>
				{
					new CallDispatchRole { RoleId = 31 },
					new CallDispatchRole { RoleId = 32 }
				}
			};
		}
	}
}
