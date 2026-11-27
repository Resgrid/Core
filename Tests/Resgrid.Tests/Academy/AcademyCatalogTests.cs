using System;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Model.Academy;

namespace Resgrid.Tests.Academy
{
	/// <summary>Academy A0 (protocols-training-knowledge-plan §15): contextual lesson links in the Help menu.</summary>
	[TestFixture]
	public class AcademyCatalogTests
	{
		[Test]
		public void Lesson_ids_are_unique()
		{
			AcademyCatalog.Lessons.Select(x => x.Id.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
		}

		[Test]
		public void Every_mapped_controller_names_a_known_lesson()
		{
			foreach (var pair in AcademyCatalog.ControllerLessons)
				AcademyCatalog.Find(pair.Value).Should().NotBeNull($"controller {pair.Key} maps to lesson {pair.Value}");
		}

		[Test]
		public void Every_mapped_controller_exists_in_the_user_area()
		{
			var userControllers = typeof(Resgrid.Web.Areas.User.Controllers.ProtocolsController).Assembly.GetTypes()
				.Where(t => !t.IsAbstract && typeof(Controller).IsAssignableFrom(t) && t.Namespace != null && t.Namespace.StartsWith("Resgrid.Web.Areas.User.Controllers", StringComparison.Ordinal))
				.Select(t => t.Name.EndsWith("Controller", StringComparison.Ordinal) ? t.Name.Substring(0, t.Name.Length - "Controller".Length) : t.Name)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			AcademyCatalog.ControllerLessons.Keys.Where(k => !userControllers.Contains(k))
				.Should().BeEmpty("a renamed or removed controller leaves a dead Academy mapping");
		}

		[Test]
		public void Lesson_links_open_the_home_page_lesson_and_walkthroughs_open_their_own_page()
		{
			AcademyCatalog.LessonUrl("https://academy.resgrid.com", AcademyCatalog.Find("05-calls"))
				.Should().Be("https://academy.resgrid.com/#lesson=05-calls");

			AcademyCatalog.LessonUrl("https://academy.resgrid.com/", AcademyCatalog.Find("06-dispatch-settings"))
				.Should().Be("https://academy.resgrid.com/deep-dive/lessons/06-dispatch-settings/");
		}

		[Test]
		public void A_page_can_name_a_closer_lesson_than_its_controller()
		{
			AcademyCatalog.ForPage("Department", "06-dispatch-settings").Id.Should().Be("06-dispatch-settings");
			AcademyCatalog.ForPage("Department").Id.Should().Be("02-department-settings");
			AcademyCatalog.ForPage("Department", "no-such-lesson").Id.Should().Be("02-department-settings");
		}

		[Test]
		public void An_unmapped_page_or_a_blank_academy_url_shows_no_lesson_link()
		{
			AcademyCatalog.ForPage("NoSuchModule").Should().BeNull();
			AcademyCatalog.HomeUrl("").Should().BeNull();
			AcademyCatalog.LessonUrl(" ", AcademyCatalog.Find("00-start")).Should().BeNull();
		}
	}
}
