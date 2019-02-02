using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity;
	using System.Data.Entity.Migrations;
	using System.Linq;

	public sealed class Configuration : DbMigrationsConfiguration<Contexts.DataContext>
	{
		public Configuration()
		{
			CommandTimeout = Int32.MaxValue;
			AutomaticMigrationsEnabled = false;
		}

		protected override void Seed(Contexts.DataContext context)
		{
			context.ApplicationRoles.AddOrUpdate(a => a.Id,
																new IdentityRole
																{
																	Id = Config.DataConfig.UsersIdentityRoleId,
																	Name = "Users",
																	NormalizedName = "Users"
																},
																new IdentityRole
																{
																	Id = Config.DataConfig.AdminsIdentityRoleId,
																	Name = "Admins",
																	NormalizedName = "Admins"
																},
																	new IdentityRole
																	{
																		Id = Config.DataConfig.AffiliatesIdentityRoleId,
																		Name = "Affiliates",
																		NormalizedName = "Affiliates"
																	});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
																			 new IdentityUserRole
																			 {
																				 UserId = Config.DataConfig.SystemTestUser1Id,
																				 RoleId = Config.DataConfig.UsersIdentityRoleId
																			 });

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
																			new IdentityUserRole
																			{
																			 UserId = Config.DataConfig.SystemTestUser2Id,
																			 RoleId = Config.DataConfig.UsersIdentityRoleId
																			});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
																			 new IdentityUserRole
																			 {
																				 UserId = Config.DataConfig.SystemAdminUserId,
																				 RoleId = Config.DataConfig.AdminsIdentityRoleId
																			 });

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
																			 new IdentityUser
																			 {
																				 Id = Config.DataConfig.SystemTestUser1Id,
																				 UserName = Config.DataConfig.SystemTestUser1Username,
																				 NormalizedUserName = Config.DataConfig.SystemTestUser1Username,
																				 Email = Config.DataConfig.SystemTestUser1Email,
																				 NormalizedEmail = Config.DataConfig.SystemTestUser1Email,
																				 EmailConfirmed = true,
																				 PasswordHash = Config.DataConfig.SystemTestUser1PasswordHash,
																				 SecurityStamp = Config.DataConfig.SystemTestUser1SecurityStamp,
																				 LockoutEnabled = true
																			 });

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
																			new IdentityUser
																			{
																				 Id = Config.DataConfig.SystemTestUser2Id,
																				 UserName = Config.DataConfig.SystemTestUser2Username,
																				 NormalizedUserName = Config.DataConfig.SystemTestUser2Username,
																				 Email = Config.DataConfig.SystemTestUser2Email,
																				 NormalizedEmail = Config.DataConfig.SystemTestUser2Email,
																				 EmailConfirmed = true,
																				 PasswordHash = Config.DataConfig.SystemTestUser2PasswordHash,
																				 SecurityStamp = Config.DataConfig.SystemTestUser2SecurityStamp,
																				 LockoutEnabled = true
																			});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
																			 new IdentityUser
																			 {
																				 Id = Config.DataConfig.SystemAdminUserId,
																				 UserName = Config.DataConfig.SystemAdminUserUsername,
																				 NormalizedUserName = Config.DataConfig.SystemAdminUserUsername,
																				 Email = Config.DataConfig.SystemAdminUserEmail,
																				 NormalizedEmail = Config.DataConfig.SystemAdminUserEmail,
																				 EmailConfirmed = true,
																				 PasswordHash = Config.DataConfig.SystemAdminUserPasswordHash,
																				 SecurityStamp = Config.DataConfig.SystemAdminUserSecurityStamp,
																				 LockoutEnabled = true
																			 });
		}
	}
}
