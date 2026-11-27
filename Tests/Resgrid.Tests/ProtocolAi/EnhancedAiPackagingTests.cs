using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Subscription;
using File = System.IO.File;

namespace Resgrid.Tests.ProtocolAi
{
	/// <summary>
	/// Round 7A packaging: the Enhanced AI add-on card lists the protocol and training value that needs only Enhanced AI, and says which AI
	/// features also need the LMS (every locale is checked by AddonLocalizationTests); the Admin Assist tutor entry no longer requires the LMS.
	/// </summary>
	[TestFixture]
	public class EnhancedAiPackagingTests
	{
		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		[Test]
		public void The_add_on_card_lists_the_protocol_tools_and_the_LMS_only_AI_features()
		{
			var addon = AddonCatalog.Get(PlanAddonTypes.EnhancedAi);
			addon.FeatureKeys.Should().Contain(new[] { "EnhancedAiFeature7", "EnhancedAiFeature8" });

			var english = XDocument.Load(Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Addons", "Addons.en.resx"))
				.Root.Elements("data").ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"));
			english["EnhancedAiFeature7"].Should().Contain("Protocol").And.Contain("translations").And.Contain("flashcards");
			english["EnhancedAiFeature8"].Should().Contain("LMS");
		}

		[Test]
		public void The_admin_assist_tutor_needs_only_Enhanced_AI_and_the_protocol_tools_are_catalogued()
		{
			var catalog = File.ReadAllText(Path.Combine(RepositoryRoot(), "Core", "Resgrid.AdminAssist", "Catalog", "ai.yaml"));
			using var document = System.Text.Json.JsonDocument.Parse(catalog);
			var capabilities = document.RootElement.GetProperty("Capabilities").EnumerateArray().ToList();
			string[] Requirements(string id) => capabilities.Single(c => c.GetProperty("Id").GetString() == id).GetProperty("Requirements").EnumerateArray()
				.Select(r => r.GetProperty("Kind").GetString() + ":" + r.GetProperty("Id").GetString()).ToArray();

			Requirements("ai-tutor").Should().BeEquivalentTo(new[] { "addon:EnhancedAi", "flag:Ai.Knowledge" });
			Requirements("ai-protocol-tools").Should().BeEquivalentTo(new[] { "addon:EnhancedAi", "flag:Ai.Knowledge" });
			Requirements("ai-course-drafts").Should().Contain("addon:Lms", "course drafts work on LMS-only content");
		}
	}
}
