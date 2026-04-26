using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class DestinationResolutionHelperTests
	{
		[Test]
		public void Resolve_ShouldNotProbeAcrossEntityTypes_WhenTypeInfoIsMissing()
		{
			var calls = new List<Call>
			{
				new Call
				{
					CallId = 42,
					Name = "Collision Call",
					Number = "C-42",
					Address = "123 Call St"
				}
			};
			var stations = new List<DepartmentGroup>
			{
				new DepartmentGroup
				{
					DepartmentGroupId = 42,
					Name = "Collision Station"
				}
			};
			var pois = new List<Poi>();

			var result = DestinationResolutionHelper.Resolve(42, null, null, calls, stations, pois);

			result.DestinationId.Should().Be(42);
			result.DestinationType.Should().BeNull();
			result.Name.Should().BeNull();
			result.Address.Should().BeNull();
			result.TypeName.Should().BeNull();
		}

		[Test]
		public void Resolve_ShouldUseBlindFallback_WhenExplicitlyOptedIn()
		{
			var stations = new List<DepartmentGroup>
			{
				new DepartmentGroup
				{
					DepartmentGroupId = 42,
					Name = "Collision Station"
				}
			};

			var result = DestinationResolutionHelper.Resolve(42, null, null, new List<Call>(), stations, new List<Poi>(), allowCrossTypeFallback: true);

			result.DestinationId.Should().Be(42);
			result.DestinationType.Should().Be((int)DestinationEntityTypes.Station);
			result.Name.Should().Be("Collision Station");
			result.TypeName.Should().Be(DestinationEntityTypes.Station.GetDisplayName());
		}

		[Test]
		public void GetEffectiveDestinationType_ShouldInferCallForLegacyRespondingToSceneActions()
		{
			var actionLog = new ActionLog
			{
				ActionTypeId = (int)ActionTypes.RespondingToScene
			};

			actionLog.GetEffectiveDestinationType().Should().Be(DestinationEntityTypes.Call);
		}

		[Test]
		public void GetEffectiveDestinationType_ShouldPreferExplicitDestinationType()
		{
			var actionLog = new ActionLog
			{
				ActionTypeId = (int)ActionTypes.RespondingToScene,
				DestinationType = (int)DestinationEntityTypes.Poi
			};

			actionLog.GetEffectiveDestinationType().Should().Be(DestinationEntityTypes.Poi);
		}
	}
}
