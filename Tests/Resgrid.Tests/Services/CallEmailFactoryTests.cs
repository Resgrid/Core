using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.CallEmailTemplates;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Tests.Services
{
	namespace CallEmailFactoryTests
	{
		public class with_the_calls_email_factory : TestBase
		{
			protected ICallEmailFactory _callEmailFactory;

			protected Department _department;
			protected List<IdentityUser> _dispatchUsers;
			protected List<Call> _activeCalls;
			protected List<Unit> _dispatchedUnits;

			protected with_the_calls_email_factory()
			{
				_callEmailFactory = new CallEmailFactory();

				_dispatchUsers = new List<IdentityUser>();
				_dispatchUsers.Add(new IdentityUser()
				{
					UserId = Guid.NewGuid().ToString()
				});
				_dispatchUsers.Add(new IdentityUser()
				{
					UserId = Guid.NewGuid().ToString()
				});
				_dispatchUsers.Add(new IdentityUser()
				{
					UserId = Guid.NewGuid().ToString()
				});

				_activeCalls = new List<Call>();
				_activeCalls.Add(new Call()
				{
					CallId = 1,
					Name = "Test Call 1",

				});
				_activeCalls.Add(new Call()
				{
					CallId = 2,
					Name = "72C01 - Water Rescue/ Sinking Vehicle/Vehicle in Floodwater",
					LoggedOn = new DateTime(2018, 5, 14, 15, 53, 16, DateTimeKind.Utc),
					DispatchCount = 1,
					ReportingUserId = "bef090f5-3c18-4a52-9c56-45e597d1c91b",
					SourceIdentifier = "102",
					CallSource = (int)CallSources.EmailImport,
					GeoLocationData = "54.1425, -115.687"
				});
				_activeCalls.Add(new Call()
				{
					CallId = 3,
					Name = "Test Call 3",

				});

				_department = new Department();
				_department.Name = "Test Department 1";
				_department.TimeZone = "Eastern Standard Time";

				_dispatchedUnits = new List<Unit>();
				_dispatchedUnits.Add(new Unit()
				{
					UnitId = 1,
					Name = "Test Unit 1",
					DepartmentId = 1,
					Department = _department
				});
				_dispatchedUnits.Add(new Unit()
				{
					UnitId = 2,
					Name = "Test Unit 2",
					DepartmentId = 1,
					Department = _department
				});
			}
		}

		[TestFixture]
		public class when_importing_a_hancock_county_call : with_the_calls_email_factory
		{
			[Test]
			public void should_work_with_address_with_commas()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody =
					"(29), Medical/EMS, 521 W MAIN CROSS ST # 108, ARLINGTON, OH 45814,// MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.";
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int) CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.HancockCounty, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.IncidentNumber.Should().Be("29");
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Type.Should().Be("Medical/EMS");
				call.Address.Should().Be("521 W MAIN CROSS ST # 108, ARLINGTON, OH 45814");
				call.NatureOfCall.Should().Be("MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.");
				call.Name.Should().Be("29-Medical/EMS");
			}

			[Test]
			public void should_work_with_address_without_commas()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody =
					"(29), Medical/EMS, 521 W MAIN CROSS ST # 108 ARLINGTON OH 45814,// MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.";
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.HancockCounty, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.IncidentNumber.Should().Be("29");
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Type.Should().Be("Medical/EMS");
				call.Address.Should().Be("521 W MAIN CROSS ST # 108 ARLINGTON OH 45814");
				call.NatureOfCall.Should().Be("MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.");
				call.Name.Should().Be("29-Medical/EMS");
			}

			[Test]
			public void should_work_with_gps_coordinates_commas()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody =
					"(29), Medical/EMS, 39.14086268299356,-119.7583809782715,// MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.";
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.HancockCounty, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.IncidentNumber.Should().Be("29");
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Type.Should().Be("Medical/EMS");
				call.Address.Should().BeNullOrWhiteSpace();
				call.GeoLocationData.Should().Be("39.14086268299356,-119.7583809782715");
				call.NatureOfCall.Should().Be("MEDICAL ALARM SHOWS UNIT IS VACANT NO PX NUMBER FOR ACCOUNT.");
				call.Name.Should().Be("29-Medical/EMS");
			}
		}

		[TestFixture]
		public class when_importing_a_calfire_scu_call : with_the_calls_email_factory
		{
			[Test]
			public void should_process_elec_haz_email()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody =
					"HAZ, ELECTRICAL; 4140 FELTER RD ,SPRING_VALLEY ; C42; PWLD- TREE 12\"; 4800 BLK MARSH RD; Map: 794_J_6/C41; Inc# 000571; Tac: TAC 2; Air: AIR TAC 5; Grd: A/G 3 CDF; SPV; NO TEXT <a href=\"http://maps.google.com/?q=37.440316,-121.835281\">Map;</a> X: -121 50.1168 Y: 37 26.4189; ";
				email.Body = email.TextBody;
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.CalFireSCU, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.IncidentNumber.Should().Be("000571");
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Type.Should().Be("HAZ, ELECTRICAL");
				call.Address.Should().Be("4140 FELTER RD ,SPRING_VALLEY");
				call.NatureOfCall.Should().Be("HAZ, ELECTRICAL   C42   PWLD- TREE 12\"");
				call.Name.Should().Be("HAZ, ELECTRICAL");
				call.MapPage.Should().Be("794_J_6/C41");
				call.GeoLocationData.Should().Be("37.440316,-121.835281");
			}

			[Test]
			public void should_process_medial_assist_email()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody = "     MED, ASSIST; 3091 CALAVERAS RD ,MILPITAS ; Z201; ;  UNNAMED ST; Map: 794_G_5/B40; Inc# 000612; Tac: TAC2; Air: AIR TAC 5; Grd: A/G 3 CDF; SPV; NO TEXT <a href=\"http://maps.google.com/?q=37.448472,-121.846455\">Map;</a> X: -121 50.7873 Y: 37 26.9083; ";
				email.Body = email.TextBody;
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.CalFireSCU, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.IncidentNumber.Should().Be("000612");
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Type.Should().Be("MED, ASSIST");
				call.Address.Should().Be("3091 CALAVERAS RD ,MILPITAS");
				call.NatureOfCall.Should().Be("MED, ASSIST   Z201");
				call.Name.Should().Be("MED, ASSIST");
				call.MapPage.Should().Be("794_G_5/B40");
				call.GeoLocationData.Should().Be("37.448472,-121.846455");
			}

			[Test]
			public void should_process_medial_assist_aid()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody = " MEDICAL AID; 3464 SPRING CREEK LN ,SPRING_VALLEY ; C42; OLINGER B &amp; W; UNNAMED ST; Map: 794_G_5/C40; Inc# 001072; Tac: ; Air: ; Grd: ; MLPE87 SPV; ---- External Remarks from XSC for XSC:CAXSC:17043020 Status WAI at 02/12/2017-10:25:44 ----<a href=\"http://maps.google.com/?q=37.443783,-121.846332\">Map;</a> X: -121 50.7799 Y: 37 26.6269;";
				email.Body = email.TextBody;
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.CalFireSCU, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.IncidentNumber.Should().Be("001072");
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Type.Should().Be("MEDICAL AID");
				call.Address.Should().Be("3464 SPRING CREEK LN ,SPRING_VALLEY");
				call.NatureOfCall.Should().Be("MEDICAL AID   C42   OLINGER B &amp");
				call.Name.Should().Be("MEDICAL AID");
				call.MapPage.Should().Be("794_G_5/C40");
				call.GeoLocationData.Should().Be("37.443783,-121.846332");
			}

			[Test]
			public void should_not_process_close()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody = "RES: SPV; RES: SPV; CLOSE: Inc# 001072; MEDICAL AID; 3464 SPRING CREEK LN ,SPRING_VALLEY ; C42;DSP: 2-12-10:26; AIQ: 2-12-11:37; ";
				email.Body = email.TextBody;
				email.Subject = "Call Email";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.CalFireSCU, email, mmId, _dispatchUsers, null, null, null, priority);

				call.Should().BeNull();
			}
		}

		public class when_importing_an_enhanced_parkland_call : with_the_calls_email_factory
		{
			public void should_process_initial_callout()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody = @"Date: 2018-05-14 10:53:16
				Type: 72C01 - Water Rescue / Sinking Vehicle / Vehicle in Floodwater
				Location: 5205 51 ST, WHITECOURT
				Business Name:
				Subdivision:
				Common Place:
				(54.1425, -115.687)


				Units Responding: WCT Fire";
				email.Body = email.TextBody;
				email.Subject = "Incident Message";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.ParklandCounty2, email, mmId, _dispatchUsers, _department, _activeCalls, _dispatchedUnits, priority);

				call.Should().NotBeNull();
				call.ReportingUserId.Should().Be(mmId);
				call.SourceIdentifier.Should().Be("100");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.NatureOfCall.Should().NotBeNull();
				call.Priority.Should().Be(2);
				call.LoggedOn.Should().BeCloseTo(new DateTime(2018, 5, 14, 15, 53, 16, DateTimeKind.Utc));
				call.Name.Should().Be("72C01 - Water Rescue / Sinking Vehicle / Vehicle in Floodwater");
				call.GeoLocationData.Should().Be("54.1425, -115.687");
			}

			public void should_process_second_alarm()
			{
				CallEmail email = new CallEmail();
				email.MessageId = "100";
				email.TextBody = @"Date: 2018-05-14 10:53:16
				Type: 72C01 - Water Rescue/ Sinking Vehicle/Vehicle in Floodwater
				Location: 5205 51 ST, WHITECOURT
				Business Name:
				Subdivision:
				Common Place:
				(54.1425, -115.687)


				Units Responding: WCT FC1, WCT FC2, WCT FC3, WCT Fire, WCT2ndPage";
				email.Body = email.TextBody;
				email.Subject = "Incident Message";

				string mmId = Guid.NewGuid().ToString();
				var priority = (int)CallPriority.High;

				var call = _callEmailFactory.GenerateCallFromEmailText(CallEmailTypes.ParklandCounty2, email, mmId, _dispatchUsers, _department, _activeCalls, _dispatchedUnits, priority);

				call.Should().NotBeNull();
				call.CallId.Should().Be(2);
				call.DispatchCount.Should().Be(2);
				call.ReportingUserId.Should().Be("bef090f5-3c18-4a52-9c56-45e597d1c91b");
				call.SourceIdentifier.Should().Be("102");
				call.CallSource.Should().Be((int)CallSources.EmailImport);
				call.Priority.Should().Be(3);
				call.LoggedOn.Should().BeCloseTo(new DateTime(2018, 5, 14, 15, 53, 16, DateTimeKind.Utc));
				call.Name.Should().Be("72C01 - Water Rescue/ Sinking Vehicle/Vehicle in Floodwater");
				call.GeoLocationData.Should().Be("54.1425, -115.687");
			}
		}
	}
}
