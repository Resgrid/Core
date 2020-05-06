using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace ProtocolsServiceTests
	{
		public class with_the_protocol_service : TestBase
		{
			protected IProtocolsService _protocolService;

			protected with_the_protocol_service()
			{
				_protocolService = Resolve<IProtocolsService>();
			}
		}

		[TestFixture]
		public class when_determining_active_triggers_for_call : with_the_protocol_service
		{
			[Test]
			public void should_be_null_for_no_protocol()
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

				var triggers = _protocolService.DetermineActiveTriggers(null, call);

				triggers.Should().BeNull();
			}

			[Test]
			public void should_be_null_for_no_call()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = false;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

				var triggers = _protocolService.DetermineActiveTriggers(procotol, null);

				triggers.Should().BeNull();
			}

			[Test]
			public void should_be_null_for_disabled_protocol()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = true;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

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

				var triggers = _protocolService.DetermineActiveTriggers(procotol, call);

				triggers.Should().BeNull();
			}

			[Test]
			public void should_be_null_for_protocol_with_no_triggers()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = false;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

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

				var triggers = _protocolService.DetermineActiveTriggers(procotol, call);

				triggers.Should().BeNull();
			}

			[Test]
			public void should_be_null_for_out_of_band_start_trigger()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = false;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

				procotol.Triggers = new List<DispatchProtocolTrigger>();

				DispatchProtocolTrigger trigger1 = new DispatchProtocolTrigger();
				trigger1.Type = (int)ProtocolTriggerTypes.CallPriorty;
				trigger1.StartsOn = DateTime.UtcNow.AddDays(14);
				trigger1.EndsOn = DateTime.UtcNow.AddDays(147);
				trigger1.Priority = (int)CallPriority.Emergency;

				procotol.Triggers.Add(trigger1);

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
				call.Priority = (int)CallPriority.Emergency;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				var triggers = _protocolService.DetermineActiveTriggers(procotol, call);

				triggers.Should().BeNull();
			}

			[Test]
			public void should_be_null_for_out_of_band_end_trigger()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = false;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

				procotol.Triggers = new List<DispatchProtocolTrigger>();

				DispatchProtocolTrigger trigger1 = new DispatchProtocolTrigger();
				trigger1.Type = (int)ProtocolTriggerTypes.CallPriorty;
				trigger1.StartsOn = DateTime.UtcNow.AddDays(-140);
				trigger1.EndsOn = DateTime.UtcNow.AddDays(-7);
				trigger1.Priority = (int)CallPriority.Emergency;

				procotol.Triggers.Add(trigger1);

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
				call.Priority = (int)CallPriority.Emergency;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				var triggers = _protocolService.DetermineActiveTriggers(procotol, call);

				triggers.Should().BeNull();
			}


			[Test]
			public void should_have_value_for_priority_trigger()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = false;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

				procotol.Triggers = new List<DispatchProtocolTrigger>();

				DispatchProtocolTrigger trigger1 = new DispatchProtocolTrigger();
				trigger1.Type = (int)ProtocolTriggerTypes.CallPriorty;
				trigger1.StartsOn = null;
				trigger1.EndsOn = null;
				trigger1.Priority = (int)CallPriority.Emergency;

				procotol.Triggers.Add(trigger1);

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
				call.Priority = (int)CallPriority.Emergency;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				var triggers = _protocolService.DetermineActiveTriggers(procotol, call);

				triggers.Should().NotBeNullOrEmpty();
				triggers.Should().HaveCount(1);
			}

			[Test]
			public void should_have_value_for_prioritystartend_trigger()
			{
				DispatchProtocol procotol = new DispatchProtocol();
				procotol.DepartmentId = 1;
				procotol.Name = "";
				procotol.Code = "";
				procotol.IsDisabled = false;
				procotol.Description = "";
				procotol.ProtocolText = "";
				procotol.CreatedOn = DateTime.UtcNow;
				procotol.CreatedByUserId = TestData.Users.TestUser1Id;
				procotol.UpdatedOn = DateTime.UtcNow;
				procotol.MinimumWeight = 10;
				procotol.UpdatedByUserId = TestData.Users.TestUser1Id;

				procotol.Triggers = new List<DispatchProtocolTrigger>();

				DispatchProtocolTrigger trigger1 = new DispatchProtocolTrigger();
				trigger1.Type = (int)ProtocolTriggerTypes.CallPriorty;
				trigger1.StartsOn = DateTime.UtcNow.AddDays(-1);
				trigger1.EndsOn = DateTime.UtcNow.AddDays(1);
				trigger1.Priority = (int)CallPriority.Emergency;

				procotol.Triggers.Add(trigger1);

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
				call.Priority = (int)CallPriority.Emergency;

				CallDispatch cd = new CallDispatch();
				cd.UserId = TestData.Users.TestUser2Id;
				call.Dispatches.Add(cd);

				CallDispatch cd1 = new CallDispatch();
				cd1.UserId = TestData.Users.TestUser3Id;
				call.Dispatches.Add(cd1);

				var triggers = _protocolService.DetermineActiveTriggers(procotol, call);

				triggers.Should().NotBeNullOrEmpty();
				triggers.Should().HaveCount(1);
			}
		}

	}
}
