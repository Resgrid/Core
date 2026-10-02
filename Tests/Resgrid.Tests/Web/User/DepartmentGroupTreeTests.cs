using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.WebCore.Areas.User.Models;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Units and Personnel list pages build their group tree with BSTreeModel.ForDepartmentGroups. bstreeview
	/// appends node text as HTML, so user-entered group names must come out encoded.
	/// </summary>
	[TestFixture]
	public class DepartmentGroupTreeTests
	{
		[Test]
		public void ForDepartmentGroups_EncodesNamesAndKeepsTheTopLevelAndChildStructure()
		{
			var child = new DepartmentGroup { DepartmentGroupId = 2, ParentDepartmentGroupId = 1, Name = "<script>alert(1)</script>" };
			var station = new DepartmentGroup { DepartmentGroupId = 1, Name = "<b>Station 1</b>", Children = new List<DepartmentGroup> { child } };
			var engines = new DepartmentGroup { DepartmentGroupId = 3, Name = "Engine & Ladder" };

			var nodes = BSTreeModel.ForDepartmentGroups(new[] { station, child, engines });

			nodes.Select(n => n.id).Should().Equal("TreeGroup_1", "TreeGroup_3");
			nodes[0].text.Should().Be("&lt;b&gt;Station 1&lt;/b&gt;");
			nodes[1].text.Should().Be("Engine &amp; Ladder");
			nodes[0].nodes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { id = "TreeGroup_2", text = "&lt;script&gt;alert(1)&lt;/script&gt;", icon = "" });
			nodes.Should().OnlyContain(n => n.icon == "");
			nodes[1].nodes.Should().BeEmpty();
		}

		[Test]
		public void ForDepartmentGroups_WithNoGroups_ReturnsNoNodes()
		{
			BSTreeModel.ForDepartmentGroups(null).Should().BeEmpty();
			BSTreeModel.ForDepartmentGroups(new List<DepartmentGroup>()).Should().BeEmpty();
		}
	}
}
