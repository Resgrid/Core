using System.Collections.Generic;
using NUnit.Framework;
using OpenPop.Mime.Header;

namespace OpenPopUnitTests.Mime.Header
{
	/// <summary>
	/// Tests for <see cref="RfcMailAddress"/>
	/// </summary>
	[TestFixture]
	public class RFCMailAddressTest
	{
		/// <summary>
		/// Test that we can parse a fully qualified mail address
		/// </summary>
		[Test]
		public void ParsingMailAddressFull()
		{
			const string address = "\"John McDaniel\" <jmcdaniel@spam.teltronics.com>";
			const string expectedRaw = address;
			const string expectedMailAddress = "jmcdaniel@spam.teltronics.com";
			const string expectedDisplayName = "John McDaniel";

			RfcMailAddress rfcAddress = RfcMailAddress.ParseMailAddress(address);

			Assert.AreEqual(expectedRaw, rfcAddress.Raw);
			Assert.IsTrue(rfcAddress.HasValidMailAddress);
			Assert.AreEqual(expectedMailAddress, rfcAddress.MailAddress.Address);
			Assert.AreEqual(expectedDisplayName, rfcAddress.MailAddress.DisplayName);

			// The address and displayname should be equal
			Assert.IsTrue(rfcAddress.Address.Equals(rfcAddress.MailAddress.Address));
			Assert.IsTrue(rfcAddress.DisplayName.Equals(rfcAddress.MailAddress.DisplayName));
		}

		/// <summary>
		/// Test that we can parse an address which only contains a display name
		/// </summary>
		[Test]
		public void ParsingMailAddressNameOnly()
		{
			const string address = "Ralph Wiggins";
			const string expectedDisplayName = address;
			const string expectedRaw = address;

			RfcMailAddress rfcAddress = RfcMailAddress.ParseMailAddress(address);

			Assert.AreEqual(expectedRaw, rfcAddress.Raw);
			Assert.AreEqual(expectedDisplayName, rfcAddress.DisplayName);
			Assert.IsEmpty(rfcAddress.Address);

			// We should not be able to parse this into a MailAddress object
			Assert.IsFalse(rfcAddress.HasValidMailAddress);
			Assert.IsNull(rfcAddress.MailAddress);
		}

		/// <summary>
		/// Test that we can parse an email address which is not surrounded by brackets
		/// </summary>
		[Test]
		public void ParsingMailAddressNoBrackets()
		{
			const string address = "snoopy@peanuts.com";
			const string expectedAddress = address;

			RfcMailAddress rfcAddress = RfcMailAddress.ParseMailAddress(address);

			Assert.IsTrue(rfcAddress.HasValidMailAddress);
			Assert.AreEqual(expectedAddress, rfcAddress.Address);
			Assert.AreEqual(expectedAddress, rfcAddress.MailAddress.Address);
			Assert.IsEmpty(rfcAddress.MailAddress.DisplayName);
		}

		/// <summary>
		/// Test that we can parse a list of email addresses separated by commas
		/// </summary>
		[Test]
		public void ParsingMailAddressList()
		{
			const string addressList = "\"John McDaniel\" <jmcdaniel@nospam.teltronics.com>, snoopy@peanuts.com, <bob@builder.org>";
			string[] expectedAddress = {"jmcdaniel@nospam.teltronics.com", "snoopy@peanuts.com", "bob@builder.org"};
			string[] expectedRaw = {"\"John McDaniel\" <jmcdaniel@nospam.teltronics.com>", "snoopy@peanuts.com", "<bob@builder.org>"};
			string[] expectedDisplay = {"John McDaniel", "", "", ""};
			bool[] expectedValidMailAddress = {true, true, true, true};
			List<RfcMailAddress> list = RfcMailAddress.ParseMailAddresses(addressList);
			Assert.AreEqual(expectedAddress.Length, list.Count, "Number of items parsed");
			for (int i = 0; i < list.Count; i++)
			{
				Assert.IsTrue(list[i].HasValidMailAddress, string.Format("HasValidMailAddress: {0}", i));
				Assert.AreEqual(expectedAddress[i], list[i].MailAddress.Address, string.Format("Email Address: {0}", i));
				Assert.AreEqual(expectedDisplay[i], list[i].DisplayName);
				Assert.AreEqual(expectedRaw[i], list[i].Raw, string.Format("Email Raw: {0}", i));
				Assert.AreEqual(expectedValidMailAddress[i], list[i].HasValidMailAddress);
				if(expectedValidMailAddress[i])
				{
					Assert.IsTrue(list[i].Address.Equals(list[i].MailAddress.Address));
					Assert.IsTrue(list[i].DisplayName.Equals(list[i].MailAddress.DisplayName));
				}
			}
		}

		/// <summary>
		/// Test that we can parse an email address that contains a comma in the display name
		/// </summary>
		[Test]
		public void ParsingMailAddressListDisplayNameHasComma()
		{
			const string address = "\"McDaniel, John\" <jmcdaniel@spam.teltronics.com>";
			const string expectedRaw = address;
			const string expectedMailAddress = "jmcdaniel@spam.teltronics.com";
			const string expectedMailName = "McDaniel, John";
			List<RfcMailAddress> list = RfcMailAddress.ParseMailAddresses(address);
			Assert.AreEqual(1, list.Count, "Number of items parsed");
			Assert.IsTrue(list[0].HasValidMailAddress);
			Assert.AreEqual(expectedRaw, list[0].Raw, "Raw input");
			Assert.AreEqual(expectedMailAddress, list[0].MailAddress.Address, "MailAddress");
			Assert.AreEqual(expectedMailName, list[0].MailAddress.DisplayName, "MailAddress Display Name");
		}
	}
}