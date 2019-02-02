using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;


namespace Resgrid.Tests.Framework
{
    [TestFixture]
    public class ObjectSerializationTests
    {
        public class TestObject
        {
            public int Id { get; set; }
            public string Data { get; set; }
        }

        //[Test]
        public void TestSerialization()
        {
            TestObject testObj = new TestObject()
            {
                Id = 500,
                Data = "This is just a test object. TestStringToSearchOn"
            };

            var test = ObjectSerialization.Serialize(testObj);
            test.Should().NotBeNullOrWhiteSpace();
            test.Should().Contain("500");
            test.Should().Contain("TestStringToSearchOn");
        }

        //[Test]
        public void TestDeserialization()
        {
            string test = ﻿"<?xml version=\"1.0\" encoding=\"utf-8\"?><TestObject xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Id>500</Id><Data>This is just a test object. TestStringToSearchOn</Data></TestObject>";
            var testObj = ObjectSerialization.Deserialize<TestObject>(test);

            testObj.Should().NotBeNull();
            testObj.Id.Should().Be(500);
            testObj.Data.Should().Be("This is just a test object. TestStringToSearchOn");
        }
    }
}
