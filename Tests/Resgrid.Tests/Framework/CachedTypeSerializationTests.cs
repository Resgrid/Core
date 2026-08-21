using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Tests.Framework
{
	/// <summary>
	/// ICacheProvider serializes with protobuf-net, so any type handed to RetrieveAsync needs a
	/// [ProtoContract]. Without one the write-back throws "No serializer for type ... is available", which the
	/// provider catches and logs — the caller still gets its data, so the only symptom is a cache that silently
	/// never populates plus a Fatal per request. These round-trips fail loudly at build time instead.
	/// </summary>
	[TestFixture]
	public class CachedTypeSerializationTests
	{
		private static T RoundTrip<T>(T value) =>
			ObjectSerialization.Deserialize<T>(ObjectSerialization.Serialize(value));

		[Test]
		public void GifSearchResultList_RoundTripsThroughTheCacheSerializer()
		{
			var source = new List<GifSearchResult>
			{
				new GifSearchResult
				{
					Id = "abc123",
					Title = "a gif",
					PreviewUrl = "https://media.giphy.com/preview.gif",
					GifUrl = "https://media.giphy.com/full.gif",
					Width = 480,
					Height = 270
				}
			};

			var result = RoundTrip(source);

			result.Should().HaveCount(1);
			result[0].Id.Should().Be("abc123");
			result[0].Title.Should().Be("a gif");
			result[0].PreviewUrl.Should().Be("https://media.giphy.com/preview.gif");
			result[0].GifUrl.Should().Be("https://media.giphy.com/full.gif");
			result[0].Width.Should().Be(480);
			result[0].Height.Should().Be(270);
		}

		[Test]
		public void RunCardList_RoundTripsThroughTheCacheSerializer()
		{
			var addedOn = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
			var source = new List<RunCard>
			{
				new RunCard
				{
					RunCardId = 12,
					DepartmentId = 42,
					Name = "Structure Fire",
					Description = "First alarm assignment",
					IsDisabled = false,
					DispatchModeOverride = 1,
					AutoDispatchOverride = 0,
					MinimumStaffingLevelOverride = 2,
					HomeStationGroupId = 7,
					AddedOn = addedOn,
					AddedByUserId = "user-1",
					Triggers = new List<RunCardTrigger>
					{
						new RunCardTrigger { RunCardTriggerId = 1, RunCardId = 12, TriggerType = 2, Priority = 3, CallTypeId = 9 }
					},
					AlarmLevels = new List<RunCardAlarmLevel>
					{
						new RunCardAlarmLevel
						{
							RunCardAlarmLevelId = 5,
							RunCardId = 12,
							AlarmLevel = 1,
							Name = "Working Fire",
							UnitRequirements = new List<RunCardUnitRequirement>
							{
								new RunCardUnitRequirement { RunCardUnitRequirementId = 3, RunCardAlarmLevelId = 5, UnitTypeId = 4, RequiredCount = 2, SortOrder = 1 }
							},
							RoleRequirements = new List<RunCardRoleRequirement>
							{
								new RunCardRoleRequirement { RunCardRoleRequirementId = 8, RunCardAlarmLevelId = 5, PersonnelRoleId = 6, RequiredCount = 4, SortOrder = 1 }
							}
						}
					},
					AvailabilitySelections = new List<RunCardAvailabilitySelection>
					{
						new RunCardAvailabilitySelection
						{
							RunCardAvailabilitySelectionId = 2, RunCardId = 12, SelectionType = 1,
							UnitTypeId = 4, IsCustomState = true, StateId = 33
						}
					}
				}
			};

			var result = RoundTrip(source);

			result.Should().HaveCount(1);
			var card = result[0];
			card.RunCardId.Should().Be(12);
			card.DepartmentId.Should().Be(42);
			card.Name.Should().Be("Structure Fire");
			card.HomeStationGroupId.Should().Be(7);
			card.AddedOn.Should().Be(addedOn);

			card.Triggers.Should().HaveCount(1);
			card.Triggers.First().CallTypeId.Should().Be(9);

			card.AlarmLevels.Should().HaveCount(1);
			var level = card.AlarmLevels.First();
			level.Name.Should().Be("Working Fire");
			level.UnitRequirements.Should().HaveCount(1);
			level.UnitRequirements.First().RequiredCount.Should().Be(2);
			level.RoleRequirements.Should().HaveCount(1);
			level.RoleRequirements.First().PersonnelRoleId.Should().Be(6);

			card.AvailabilitySelections.Should().HaveCount(1);
			card.AvailabilitySelections.First().StateId.Should().Be(33);
			card.AvailabilitySelections.First().IsCustomState.Should().BeTrue();
		}
	}
}
