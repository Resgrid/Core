using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using Resgrid;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Framework
{
		[TestFixture]
		public class ExtensionTests
		{

				[Test]
				public void TestToCollectionExtension()
				{
						var testList = new List<int> {1, 2, 3, 5, 8, 13};
						var collection = testList.ToCollection();

						collection.Should().NotBeNull();
						collection.Should().NotBeEmpty();
						collection.Should().HaveCount(testList.Count);
				}

				[Test]
				public void TestJsonSerializerSettings()
				{
						JsonSerializationExtensions.JsonSerializerSettings.Should().NotBeNull();
				}

				[Test]
				public void TestJsonDeserializer()
				{
						object deserializedObject;

						using (var reader = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "\\Data\\TestCall.json"))
						{
								deserializedObject = JsonSerializationExtensions.DeserializeStream(reader.BaseStream, typeof (Call));
						}

						deserializedObject.Should().NotBeNull();
						deserializedObject.Should().BeOfType<Call>();

						var callObject = deserializedObject as Call;
						callObject.Name.Should().Be("Priority 1E Cardiac Arrest D12");
				}

				[Test]
				public void TestJsonSerializer()
				{
						var call = new Call();
			call.DepartmentId = 1;
			call.Name = "Priority 1E Cardiac Arrest D12";
			call.NatureOfCall = "RP reports a person lying on the street not breathing.";
			call.Notes = "RP doesn't know how to do CPR, can't roll over patient";
			call.MapPage = "22T";
			call.GeoLocationData = "39.27710789298309,-119.77201511943328";
			call.Dispatches = new Collection<CallDispatch>();
			call.LoggedOn = DateTime.UtcNow;
			call.ReportingUserId = Guid.NewGuid().ToString();

						var serialization = JsonSerializationExtensions.SerializeJson(call);
						serialization.Should().NotBeNull();
						serialization.Should().NotBeEmpty();
				}

				[Test]
				public void TestDateTimeHelpersSetToMidnight()
				{
						var timestamp = new DateTime(2014, 2, 15, 16, 36, 11, 8);
						var midnight = timestamp.SetToMidnight();

						midnight.Should().Be(new DateTime(2014, 2, 15, 00, 00, 00, 00));
				}

				[Test]
				public void TestDateTimeHelpersSetToEndOfDate()
				{
						var timestamp = new DateTime(2014, 2, 15, 16, 36, 11, 8);
						var endOfDate = timestamp.SetToEndOfDay();

						endOfDate.Should().Be(new DateTime(2014, 2, 15, 23, 59, 00, 00));
				}

				[Test]
				public void TestDateTimeHelpersConvertWindowsTimeToIana()
				{
						var timeZone = DateTimeHelpers.WindowsToIana("Pacific Standard Time");

						timeZone.Should().Be("America/Los_Angeles");
				}

				[Test]
				public void TestDateTimeHelpersConvertIanaToWindowsTime()
				{
						var timeZone = DateTimeHelpers.IanaToWindows("America/Los_Angeles");

						timeZone.Should().Be("Pacific Standard Time");
				}

				[Test]
				public void TestGetVersionExtension()
				{
						var version = typeof(RandomGeneratorTests).ProductVersion();
						version.Should().NotBeNullOrWhiteSpace();
				}
		}
}