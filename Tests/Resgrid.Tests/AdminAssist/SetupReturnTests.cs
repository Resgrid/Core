using System;
using Microsoft.AspNetCore.DataProtection;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class SetupReturnTests
	{
		private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
		private readonly AdminAssistActor _actor = new(7, "admin");
		private readonly DateTimeOffset _now = new(2026,9,24,12,0,0,TimeSpan.Zero);
		[TestCase("wizard")] [TestCase("report")]
		public void Return_context_is_actor_and_department_bound_and_expires(string page)
		{
			var token = AdminAssistReturnLink.Create(_protection,_actor,page,_now);
			Assert.That(AdminAssistReturnLink.Read(_protection,_actor,token,_now.AddMinutes(10)), Is.EqualTo(page));
			Assert.That(AdminAssistReturnLink.Read(_protection,new(8,"admin"),token,_now), Is.Null);
			Assert.That(AdminAssistReturnLink.Read(_protection,new(7,"other"),token,_now), Is.Null);
			Assert.That(AdminAssistReturnLink.Read(_protection,_actor,token,_now.AddMinutes(30)), Is.Null);
			Assert.That(AdminAssistReturnLink.Read(_protection,_actor,"tampered"+token,_now), Is.Null);
		}
		[TestCase("https://example.com")] [TestCase("//example.com")] [TestCase("../Settings")]
		public void Arbitrary_return_destinations_are_rejected(string destination) =>
			Assert.Throws<ArgumentException>(() => AdminAssistReturnLink.Create(_protection,_actor,destination,_now));
	}
}
