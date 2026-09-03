using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class DeleteRepositorySchemaTests
	{
		private static string RepositorySource()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the tests must be able to find the repository root");
			var path = Path.Combine(directory!.FullName, "Repositories",
				"Resgrid.Repositories.DataRepository", "DeleteRepository.cs");
			return File.ReadAllText(path);
		}

		[Test]
		public void Department_delete_uses_the_current_active_department_storage()
		{
			RepositorySource().Should().NotContain("[dbo].[ActiveDepartments]",
				"active-department state is stored on DepartmentMembers.IsActive");
		}

		[Test]
		public void Global_user_data_is_deleted_only_after_the_last_membership_is_removed()
		{
			var source = RepositorySource();
			const string deleteMembership =
				"DELETE FROM [dbo].[DepartmentMembers] WHERE UserId = @UserId AND DepartmentId = @DepartmentId";
			const string noMembershipsRemain =
				"IF (SELECT COUNT(*) FROM DepartmentMembers WHERE UserId = @UserId) = 0";

			var deleteMembershipIndex = source.IndexOf(deleteMembership, StringComparison.Ordinal);
			var remainingMembershipCheckIndex = source.IndexOf(noMembershipsRemain, StringComparison.Ordinal);

			deleteMembershipIndex.Should().BeGreaterThan(0);
			remainingMembershipCheckIndex.Should().BeGreaterThan(deleteMembershipIndex,
				"the current department membership must be removed before checking for another membership");
			source.Should().NotContain(
				"IF (SELECT COUNT(*) FROM DepartmentMembers WHERE UserId = @UserId) = 1");
		}
	}
}
