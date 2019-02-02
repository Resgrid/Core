using NUnit.Framework;
using OpenPop.Pop3;

namespace OpenPopUnitTests.Pop3
{
	[TestFixture]
	public class CramMD5Tests
	{
		[Test]
		public void TestCramMd5RFC()
		{
			const string username = "tim";
			const string password = "tanstaaftanstaaf";
			const string challenge = "PDE4OTYuNjk3MTcwOTUyQHBvc3RvZmZpY2UucmVzdG9uLm1jaS5uZXQ+";

			const string expected = "dGltIGI5MTNhNjAyYzdlZGE3YTQ5NWI0ZTZlNzMzNGQzODkw";
			string actual = CramMd5.ComputeDigest(username, password, challenge);

			Assert.AreEqual(expected, actual);
		}

		[Test]
		public void TestCramMd5FoundExample()
		{
			const string username = "foo@baar";
			const string password = "PaZZword";
			const string challenge = "PDMyLjEzMjM0MTIzQG1haWxob3N0Pg==";

			const string expected = "Zm9vQGJhYXIgM2I4YTc4ODZkNGYxNzFhNDIxMWZlODU5NDQ0MWZjZmI=";
			string actual = CramMd5.ComputeDigest(username, password, challenge);

			Assert.AreEqual(expected, actual);
		}

		[Test]
		public void TestShortPassword()
		{
			const string username = "foobar";
			const string password = "t";
			const string challenge = "PGNoYWxsZW5nZT4=";

			const string expected = "Zm9vYmFyIDY5YjBhZjAyZmQ4M2RlNzEyNjRjOWJkMWRmNWQ4OTYy";
			string actual = CramMd5.ComputeDigest(username, password, challenge);

			Assert.AreEqual(expected, actual);
		}

		[Test]
		public void TestLongPassword()
		{
			const string username = "foens";
			const string password = "thisIsAnInsanelyLongPasswordManWoot";
			const string challenge = "PHRoaXMuaXMudGhlLmJhc2U2NC5lbmNvZGVkLmNoYWxsZW5nZUBzZXJ2ZXIuY29tPg==";

			const string expected = "Zm9lbnMgNTAyNDU5OTU1NjMwNTliNWUxZWQyMmMzMzQzYzYxNDg=";
			string actual = CramMd5.ComputeDigest(username, password, challenge);

			Assert.AreEqual(expected, actual);
		}

		[Test]
		public void TestVeryLongPassword()
		{
			const string username = "killerEyes";
			const string password = "thisIsAnInsanelyLongPasswordManWoothisIsAnInsanelyLongPasswordManWootthisIsAnInsanelyLongPasswordManWoott";
			const string challenge = "PHRlc3RpbmdTb21ldGhpbmdAZ21haWxNYXliZS5jb20+";

			const string expected = "a2lsbGVyRXllcyA5NmFlMmNmYzcxYzU3Yjk3NGUxNWI3YWI2YmYwMjVmOQ==";
			string actual = CramMd5.ComputeDigest(username, password, challenge);

			Assert.AreEqual(expected, actual);
		}

		[Test]
		public void TestPasswordLength64InBytes()
		{
			const string username = "lovelyUsername";
			const string password = "thisIsAnInsanelyLongPasswordManWoothisIsAnInsanelyLongPasswordFO";
			const string challenge = "PGRvZXNUaGlzQ3JhbU1ENVdvcmtAY3JhbT4=";

			const string expected = "bG92ZWx5VXNlcm5hbWUgYWU5MzNmOTRjZWJhY2NmYjFiNjNhY2ZjMzcyMmQ0ZmE=";
			string actual = CramMd5.ComputeDigest(username, password, challenge);

			Assert.AreEqual(expected, actual);
		}
	}
}