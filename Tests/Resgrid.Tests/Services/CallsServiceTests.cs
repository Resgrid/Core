using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace CallsServiceTests
	{
		public class with_the_calls_service : TestBase
		{
			protected ICallsService _callsService;
			
			protected with_the_calls_service()
			{
				_callsService = Resolve<ICallsService>();
			}
		}

		[TestFixture]
		public class when_saving_a_call : with_the_calls_service
		{
			[Test]
			public async Task should_be_match_to_database()
			{
				Call call = new Call();
				call.DepartmentId = 1;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.Dispatches = new Collection<CallDispatch>();
				call.LoggedOn = DateTime.Now;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				var call2 = await _callsService.SaveCallAsync(call);

				call2.Should().NotBeNull();
				call2.CallId.Should().NotBe(0);
				call2.Dispatches.Should().NotBeNull();
				call2.Dispatches.Count.Should().Be(2);
			}

			[Test]
			public async Task should_be_able_to_get_from_department()
			{
				Call call = new Call();
				call.DepartmentId = 2;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.LoggedOn = DateTime.Now;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				Call call2 = new Call();
				call2.DepartmentId = 2;
				call2.Name = "Priority 1E Cardiac Arrest D12";
				call2.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call2.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call2.MapPage = "22T";
				call2.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call2.LoggedOn = DateTime.Now;
				call2.ReportingUserId = TestData.Users.TestUser1Id;

				await _callsService.SaveCallAsync(call);
				await _callsService.SaveCallAsync(call2);

				var calls = await _callsService.GetActiveCallsForDepartmentAsync(2);

				calls.Should().Be(2);
			}

			[Test]
			public async Task should_be_able_to_get_a_call()
			{
				Call call = new Call();
				call.DepartmentId = 1;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.Dispatches = new Collection<CallDispatch>();
				call.LoggedOn = DateTime.Now;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				await _callsService.SaveCallAsync(call);
				var call2 = await _callsService.GetCallByIdAsync(call.CallId);

				call2.Should().NotBeNull();
				call2.CallId.Should().NotBe(0);
				call2.Dispatches.Should().NotBeNull();
				call2.Dispatches.Count.Should().Be(2);
				call2.Should().BeSameAs(call);
			}

			[Test]
			public async Task should_be_able_to_get_todays_calls()
			{
				Call call = new Call();
				call.DepartmentId = 3;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.Dispatches = new Collection<CallDispatch>();
				call.LoggedOn = DateTime.UtcNow;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				Call call2 = new Call();
				call2.DepartmentId = 3;
				call2.Name = "Priority 1E Cardiac Arrest D12";
				call2.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call2.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call2.MapPage = "22T";
				call2.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call2.Dispatches = new Collection<CallDispatch>();
				call2.LoggedOn = DateTime.UtcNow;
				call2.ReportingUserId = TestData.Users.TestUser1Id;

				Call call3 = new Call();
				call3.DepartmentId = 3;
				call3.Name = "Priority 1E Cardiac Arrest D12";
				call3.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call3.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call3.MapPage = "22T";
				call3.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call3.Dispatches = new Collection<CallDispatch>();
				call3.LoggedOn = DateTime.UtcNow.AddDays(-1);
				call3.ReportingUserId = TestData.Users.TestUser1Id;

				await _callsService.SaveCallAsync(call);
				await _callsService.SaveCallAsync(call2);
				await _callsService.SaveCallAsync(call3);

				var count = await _callsService.GetTodayCallsCountAsync(3);

				count.Should().Be(2);
			}
		}

		[TestFixture]
		public class when_deleting_a_call : with_the_calls_service
		{
			[Test]
			public async Task should_be_able_to_delete_a_Call()
			{
				Call call = new Call();
				call.DepartmentId = 1;
				call.Name = "Priority 1E Cardiac Arrest D12";
				call.NatureOfCall = "RP reports a person lying on the street not breathing.";
				call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
				call.MapPage = "22T";
				call.GeoLocationData = "39.27710789298309,-119.77201511943328";
				call.Dispatches = new Collection<CallDispatch>();
				call.LoggedOn = DateTime.Now;
				call.ReportingUserId = TestData.Users.TestUser1Id;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				var call2 = await _callsService.SaveCallAsync(call);

				call2.Should().NotBeNull();

				await _callsService.DeleteCallByIdAsync(call2.CallId);

				var call3 = await _callsService.GetCallByIdAsync(call2.CallId);

				call3.Should().BeNull();
			}
		}
	}
}
