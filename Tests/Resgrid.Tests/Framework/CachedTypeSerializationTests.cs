using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using ProtoBuf;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Reporting;

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

		[TestCase(-1)]
		[TestCase(0)] // Low: the value an initializer of -1 would lose without [DefaultValue(-1)]
		[TestCase(3)]
		public void CallPriorityBaseType_RoundTripsThroughTheCacheSerializer(int baseType)
		{
			var result = RoundTrip(new List<DepartmentCallPriority> { new DepartmentCallPriority { DepartmentCallPriorityId = 57, Name = "Structure Fire", BaseType = baseType } });

			result.Single().BaseType.Should().Be(baseType);
		}

		[Test]
		public void CallTypeId_RoundTripsThroughTheCacheSerializer()
		{
			RoundTrip(new Call { CallId = 1, Type = "Structure Fire", CallTypeId = 77 }).CallTypeId.Should().Be(77);
			RoundTrip(new Call { CallId = 2, Type = "Unknown" }).CallTypeId.Should().BeNull();
		}

		[TestCase(0, 0)] // no alerts: the common case, must still be a non-empty payload (empty reads as a miss)
		[TestCase(1, 0)]
		[TestCase(0, 3)]
		[TestCase(2, 5)]
		public void WeatherAlertSeverityCounts_RoundTripsThroughTheCacheSerializer(int extreme, int severe)
		{
			var payload = ObjectSerialization.Serialize(new WeatherAlertSeverityCounts { Extreme = extreme, Severe = severe });

			payload.Should().NotBeNullOrEmpty();

			var result = ObjectSerialization.Deserialize<WeatherAlertSeverityCounts>(payload);
			result.Extreme.Should().Be(extreme);
			result.Severe.Should().Be(severe);
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

		// PlatformReportingService caches all four of these. Without contracts every widget load recomputed the report
		// and logged "Type is not expected, and no contract can be inferred: Resgrid.Model.Reporting.ResponseTimeReport".
		[TestCase(typeof(ResponseTimeReport))]
		[TestCase(typeof(DashboardReport))]
		[TestCase(typeof(UtilizationReport))]
		[TestCase(typeof(ParticipationReport))]
		public void PlatformReport_EveryTypeInTheGraphIsAProtoContract(Type root)
		{
			var missing = new List<string>();
			var seen = new HashSet<Type>();
			var pending = new Stack<Type>();
			pending.Push(root);

			while (pending.Count > 0)
			{
				var type = pending.Pop();
				if (!seen.Add(type))
					continue;

				if (!Attribute.IsDefined(type, typeof(ProtoContractAttribute)))
					missing.Add(type.FullName);

				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(p => Attribute.IsDefined(p, typeof(ProtoMemberAttribute))))
				{
					var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
					if (propertyType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
						propertyType = propertyType.GetGenericArguments()[0];

					if (propertyType.IsClass && propertyType != typeof(string))
						pending.Push(propertyType);
				}
			}

			missing.Should().BeEmpty("every type a cached report carries must be a [ProtoContract]");
		}

		[Test]
		public void ResponseTimeReport_RoundTripsThroughTheCacheSerializer()
		{
			var start = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
			var end = new DateTime(2026, 11, 12, 23, 59, 59, DateTimeKind.Utc);
			var generated = new DateTime(2026, 11, 12, 20, 15, 30, DateTimeKind.Utc);

			var result = RoundTrip(new ResponseTimeReport
			{
				DepartmentId = 2948,
				StartUtc = start,
				EndUtc = end,
				GeneratedUtc = generated,
				Metrics = new List<ResponseTimeMetric>
				{
					new ResponseTimeMetric { Key = ReportingMetrics.TotalResponseSeconds, MeanSeconds = 402.5, P50Seconds = 410, P90Seconds = 700, SampleCount = 12, ThresholdSeconds = 360 },
					new ResponseTimeMetric { Key = ReportingMetrics.TurnoutSeconds, P90Seconds = 0, SampleCount = 3, ThresholdSeconds = 0, CompliancePercent = 0 }
				},
				Breakdowns = new List<ResponseTimeBreakdown>
				{
					new ResponseTimeBreakdown { Key = "byCallType", Items = new List<ResponseTimeBreakdownItem> { new ResponseTimeBreakdownItem { Label = "Fire", Id = 0, P90Seconds = 300, SampleCount = 4 } } }
				}
			});

			result.DepartmentId.Should().Be(2948);
			result.StartUtc.Should().Be(start);
			result.EndUtc.Should().Be(end);
			result.GeneratedUtc.Should().Be(generated);
			// A hit must keep the kind a miss has, or the JSON loses its "Z".
			result.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
			result.GeneratedUtc.Kind.Should().Be(DateTimeKind.Utc);

			result.Metrics.Should().HaveCount(2);
			var total = result.Metrics[0];
			total.Key.Should().Be(ReportingMetrics.TotalResponseSeconds);
			total.MeanSeconds.Should().Be(402.5);
			total.P50Seconds.Should().Be(410);
			total.P90Seconds.Should().Be(700);
			total.SampleCount.Should().Be(12);
			total.ThresholdSeconds.Should().Be(360);
			total.CompliancePercent.Should().BeNull();

			// A set-but-zero nullable is not the same as unset.
			result.Metrics[1].ThresholdSeconds.Should().Be(0);
			result.Metrics[1].CompliancePercent.Should().Be(0);

			result.Breakdowns.Single().Items.Single().Id.Should().Be(0);
			result.Breakdowns.Single().Items.Single().Label.Should().Be("Fire");
		}

		[Test]
		public void ResponseTimeReport_WithNoMetrics_IsStillACacheHit()
		{
			// A department with no rollups yet: the empty-payload check would turn a zero-byte write into a miss forever.
			var payload = ObjectSerialization.Serialize(new ResponseTimeReport
			{
				StartUtc = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc),
				EndUtc = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc),
				GeneratedUtc = new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc)
			});

			payload.Should().NotBeNullOrEmpty();
			var result = ObjectSerialization.Deserialize<ResponseTimeReport>(payload);
			result.DepartmentId.Should().BeNull();
			result.Metrics.Should().BeEmpty();
			result.Breakdowns.Should().BeEmpty();
		}

		[Test]
		public void DashboardReport_RoundTripsThroughTheCacheSerializer()
		{
			var bucket = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

			var result = RoundTrip(new DashboardReport
			{
				DepartmentId = null,
				StartUtc = bucket,
				EndUtc = bucket.AddMonths(1),
				Granularity = ReportGranularity.Month,
				GeneratedUtc = bucket.AddDays(11),
				TimeZone = "America/Toronto",
				Totals = new ReportTotals { CallsAllTime = 1000, CallsInWindow = 40, PersonnelTotal = 25, PersonnelAvailable = 10, UnitsCommitted = 2, DepartmentsTotal = 300, NewDepartmentsInWindow = 0 },
				Series = new List<MetricSeries>
				{
					new MetricSeries { Key = "calls", Granularity = ReportGranularity.Month, Points = new List<MetricPoint> { new MetricPoint(bucket, 0), new MetricPoint(bucket.AddMonths(1), 40) } }
				},
				Breakdowns = new List<Breakdown>
				{
					new Breakdown
					{
						Key = "unitsByStatus",
						Items = new List<BreakdownItem>
						{
							new BreakdownItem { Label = "Available", Id = 0, Count = 3, Availability = AvailabilityClass.Unknown },
							new BreakdownItem { Label = "Other", Count = 7, IsOther = true }
						}
					}
				}
			});

			result.DepartmentId.Should().BeNull();
			result.Granularity.Should().Be(ReportGranularity.Month);
			result.GeneratedUtc.Kind.Should().Be(DateTimeKind.Utc);
			// Per-caller, set after retrieval: never cached, or the next caller would see this one's zone.
			result.TimeZone.Should().BeNull();

			result.Totals.CallsAllTime.Should().Be(1000);
			result.Totals.CallsInWindow.Should().Be(40);
			result.Totals.PersonnelTotal.Should().Be(25);
			result.Totals.PersonnelAvailable.Should().Be(10);
			result.Totals.UnitsCommitted.Should().Be(2);
			result.Totals.DepartmentsTotal.Should().Be(300);
			result.Totals.NewDepartmentsInWindow.Should().Be(0);

			var points = result.Series.Single().Points;
			points.Should().HaveCount(2);
			points[0].BucketUtc.Should().Be(bucket);
			points[0].BucketUtc.Kind.Should().Be(DateTimeKind.Utc);
			points[0].Value.Should().Be(0);
			points[1].Value.Should().Be(40);

			var items = result.Breakdowns.Single().Items;
			items[0].Id.Should().Be(0);
			items[0].Availability.Should().Be(AvailabilityClass.Unknown);
			items[1].Id.Should().BeNull();
			items[1].Availability.Should().BeNull();
			items[1].IsOther.Should().BeTrue();
		}

		[Test]
		public void UtilizationAndParticipationReports_RoundTripThroughTheCacheSerializer()
		{
			var start = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

			var utilization = RoundTrip(new UtilizationReport
			{
				DepartmentId = 7,
				StartUtc = start,
				EndUtc = start.AddDays(30),
				GeneratedUtc = start.AddDays(30),
				AggregateUhu = 0.25,
				PeakConcurrentCalls = 3,
				Units = new List<UnitUtilization> { new UnitUtilization { UnitId = 5, UnitName = "Engine 1", BusySeconds = 3600, InServiceSeconds = 14400, Uhu = 0.25, CallCount = 2 } },
				WorkloadByHour = new List<MetricPoint> { new MetricPoint(start.AddHours(13), 4) }
			});

			utilization.AggregateUhu.Should().Be(0.25);
			utilization.PeakConcurrentCalls.Should().Be(3);
			utilization.Units.Single().UnitName.Should().Be("Engine 1");
			utilization.Units.Single().Uhu.Should().Be(0.25);
			utilization.WorkloadByHour.Single().BucketUtc.Should().Be(start.AddHours(13));

			var participation = RoundTrip(new ParticipationReport
			{
				DepartmentId = 7,
				StartUtc = start,
				EndUtc = start.AddDays(30),
				GeneratedUtc = start.AddDays(30),
				CallsInWindow = 40,
				MembersWithExpiredCertifications = 1,
				Members = new List<MemberParticipation> { new MemberParticipation { UserId = "user-1", Name = "A Member", CallsResponded = 10, ResponseRate = 0.25, ExpiredCertifications = 1 } }
			});

			participation.CallsInWindow.Should().Be(40);
			participation.MembersWithExpiredCertifications.Should().Be(1);
			participation.Members.Single().UserId.Should().Be("user-1");
			participation.Members.Single().ResponseRate.Should().Be(0.25);
			participation.Members.Single().ExpiredCertifications.Should().Be(1);
		}
	}
}
