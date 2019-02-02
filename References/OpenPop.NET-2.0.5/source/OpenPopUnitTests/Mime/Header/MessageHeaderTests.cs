using System.Collections.Specialized;
using NUnit.Framework;
using OpenPop.Mime.Header;

namespace OpenPopUnitTests.Mime.Header
{
	[TestFixture]
	public class MessageHeaderTests
	{
		#region In-Reply-To
		[Test]
		public void TestNoInReplyToListIsEmpty()
		{
			NameValueCollection collection = new NameValueCollection();
			MessageHeader header = new MessageHeader(collection);

			Assert.NotNull(header.InReplyTo);
			Assert.IsEmpty(header.InReplyTo);
		}

		[Test]
		public void TestSingleInReplyTo()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("In-Reply-To", "<test@test.com>");

			MessageHeader header = new MessageHeader(collection);

			Assert.IsNotEmpty(header.InReplyTo);
			Assert.AreEqual(1, header.InReplyTo.Count);
			Assert.AreEqual("test@test.com", header.InReplyTo[0]);
		}

		[Test]
		public void TestMultipleInReplyToWithWhiteSpaceSeparation()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("In-Reply-To", "<test@test.com> <test2@test2.com>");

			MessageHeader header = new MessageHeader(collection);

			Assert.IsNotEmpty(header.InReplyTo);
			Assert.AreEqual(2, header.InReplyTo.Count);

			Assert.AreEqual("test@test.com", header.InReplyTo[0]);
			Assert.AreEqual("test2@test2.com", header.InReplyTo[1]);
		}

		[Test]
		public void TestMultipleInReplyToNoWhitespaceSeparation()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("In-Reply-To", "<test@test.com><test2@test2.com>");

			MessageHeader header = new MessageHeader(collection);

			Assert.IsNotEmpty(header.InReplyTo);
			Assert.AreEqual(2, header.InReplyTo.Count);

			Assert.AreEqual("test@test.com", header.InReplyTo[0]);
			Assert.AreEqual("test2@test2.com", header.InReplyTo[1]);
		}
		#endregion

		#region References
		[Test]
		public void TestNoReferencesListIsEmpty()
		{
			NameValueCollection collection = new NameValueCollection();
			MessageHeader header = new MessageHeader(collection);

			Assert.NotNull(header.References);
			Assert.IsEmpty(header.References);
		}

		[Test]
		public void TestSingleReference()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("References", "<test@test.com>");

			MessageHeader header = new MessageHeader(collection);

			Assert.IsNotEmpty(header.References);
			Assert.AreEqual(1, header.References.Count);
			Assert.AreEqual("test@test.com", header.References[0]);
		}

		[Test]
		public void TestMultipleReferencesWithWhiteSpaceSeparation()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("References", "<test@test.com> <test2@test2.com>");

			MessageHeader header = new MessageHeader(collection);

			Assert.IsNotEmpty(header.References);
			Assert.AreEqual(2, header.References.Count);

			Assert.AreEqual("test@test.com", header.References[0]);
			Assert.AreEqual("test2@test2.com", header.References[1]);
		}

		[Test]
		public void TestMultipleReferencesNoWhitespaceSeparation()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("References", "<test@test.com><test2@test2.com>");

			MessageHeader header = new MessageHeader(collection);

			Assert.IsNotEmpty(header.References);
			Assert.AreEqual(2, header.References.Count);

			Assert.AreEqual("test@test.com", header.References[0]);
			Assert.AreEqual("test2@test2.com", header.References[1]);
		}
		#endregion

		#region Keywords
		[Test]
		public void TestNoKeywordsEmptyList()
		{
			NameValueCollection collection = new NameValueCollection();
			MessageHeader header = new MessageHeader(collection);

			Assert.NotNull(header.Keywords);
			Assert.IsEmpty(header.Keywords);
		}

		[Test]
		public void TestSingleKeyword()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("Keywords", "test");

			MessageHeader header = new MessageHeader(collection);

			Assert.AreEqual(1, header.Keywords.Count);
			Assert.AreEqual("test", header.Keywords[0]);
		}

		[Test]
		public void TestMultipleKeywords()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("Keywords", "test, hi");

			MessageHeader header = new MessageHeader(collection);

			Assert.AreEqual(2, header.Keywords.Count);
			
			Assert.AreEqual("test", header.Keywords[0]);
			Assert.AreEqual("hi", header.Keywords[1]);
		}
		#endregion

		#region Disposition-Notification-To
		[Test]
		public void TestComplexDispositionNotificationTo()
		{
			NameValueCollection collection = new NameValueCollection();
			collection.Add("Disposition-Notification-To", "\"Test with, comma\" <test@test.com>, Foo <foo@bar.com>");

			MessageHeader header = new MessageHeader(collection);
			Assert.NotNull(header.DispositionNotificationTo);
			Assert.AreEqual(2, header.DispositionNotificationTo.Count);

			Assert.AreEqual("Test with, comma", header.DispositionNotificationTo[0].DisplayName);
			Assert.AreEqual("test@test.com", header.DispositionNotificationTo[0].Address);

			Assert.AreEqual("Foo", header.DispositionNotificationTo[1].DisplayName);
			Assert.AreEqual("foo@bar.com", header.DispositionNotificationTo[1].Address);
			
		}
		#endregion

		#region Subject
		[Test]
		public void TestSubjectCorrectWhenThreadTopicIsIncluded()
		{
			NameValueCollection collection = new NameValueCollection();
			// A real-life example included a Subject that was different than Thread-Topic
			collection.Add("Subject", "Foo");
			collection.Add("Thread-Topic", "Bar");

			MessageHeader header = new MessageHeader(collection);

			Assert.AreEqual("Foo", header.Subject);
		}
		#endregion
	}
}