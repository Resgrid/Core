using Microsoft.AspNet.Identity.EntityFramework6;
using Resgrid.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Contexts;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations;

namespace Resgrid.Repositories.DataRepository.Configurations
{
	public class TestDbConfiguration : DbConfiguration
	{
		public TestDbConfiguration()
		{
			SetDefaultConnectionFactory(new LocalDbConnectionFactory("v11.0"));
			//SetDefaultConnectionFactory(new SqlCeConnectionFactory("System.Data.SqlServerCe.4.0"));
		}
	}

	public sealed class ResgridTestConfiguration : DbMigrationsConfiguration<DataContext>
	{
		public ResgridTestConfiguration()
		{
			AutomaticMigrationsEnabled = true;
			AutomaticMigrationDataLossAllowed = true;
		}

		protected override void Seed(DataContext context)
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


			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "50DEC5DB-2612-4D6A-97E3-2F04B7228C85",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "test123123@test.com",
					NormalizedEmail = "test123123@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "AE50CE3D-F94A-4FB5-A52F-D20BC015ADD7",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser2@test.com",
					NormalizedEmail = "testuser2@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "C67955B4-2B1F-445B-ABC2-59F97CF54822",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser3@test.com",
					NormalizedEmail = "testuser3@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "BC7071AE-4689-4DCC-BC93-F7C86820FEE7",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser4@test.com",
					NormalizedEmail = "testuser4@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "AFA1E979-FCA9-417B-A03D-69C0588FAD71",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser5@test.com",
					NormalizedEmail = "testuser5@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "ACF63412-BD08-421D-AC51-67B7C2E922F6",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser6@test.com",
					NormalizedEmail = "testuser6@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "D2F59135-F132-4D54-89EE-7BB2E4DB6B8B",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser7@test.com",
					NormalizedEmail = "testuser7@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = "1AA0DCF8-4220-4C86-A10B-B6F8B3C0CA44",
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser8@test.com",
					NormalizedEmail = "testuser8@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = TestData.Users.TestUser9Id,
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser9@test.com",
					NormalizedEmail = "testuser9@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = TestData.Users.TestUser10Id,
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser10@test.com",
					NormalizedEmail = "testuser10@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUsers.AddOrUpdate(a => a.Id,
				new IdentityUser()
				{
					Id = TestData.Users.TestUser11Id,
					UserName = "RGTestUser2",
					NormalizedUserName = "RGTestUser2",
					Email = "testuser11@test.com",
					NormalizedEmail = "testuser11@test.com",
					EmailConfirmed = true,
					PasswordHash = "hUBJHvVV1unpl4wz1GH9XnXz9894D1o6mkyT0zLDhVE=",
					SecurityStamp = Guid.NewGuid().ToString(),
					LockoutEnabled = true
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "50DEC5DB-2612-4D6A-97E3-2F04B7228C85",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "AE50CE3D-F94A-4FB5-A52F-D20BC015ADD7",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "C67955B4-2B1F-445B-ABC2-59F97CF54822",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "BC7071AE-4689-4DCC-BC93-F7C86820FEE7",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "AFA1E979-FCA9-417B-A03D-69C0588FAD71",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "ACF63412-BD08-421D-AC51-67B7C2E922F6",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "D2F59135-F132-4D54-89EE-7BB2E4DB6B8B",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = "1AA0DCF8-4220-4C86-A10B-B6F8B3C0CA44",
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = TestData.Users.TestUser9Id,
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = TestData.Users.TestUser10Id,
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});

			context.ApplicationUserRoles.AddOrUpdate(a => a.UserId,
				new IdentityUserRole
				{
					UserId = TestData.Users.TestUser11Id,
					RoleId = Config.DataConfig.UsersIdentityRoleId
				});


			context.Departments.AddOrUpdate(a => a.DepartmentId,
																			new Department
																			{
																				DepartmentId = 1,
																				Name = "A Test Department",
																				Code = "AAAA",
																				ManagingUserId = "50DEC5DB-2612-4D6A-97E3-2F04B7228C85",
																				ShowWelcome = true,
																				TimeZone = "Eastern Standard Time"
																			});

			context.SaveChanges();

			context.Departments.AddOrUpdate(a => a.DepartmentId,
																			new Department
																			{
																				DepartmentId = 2,
																				Name = "B Test Department",
																				Code = "BBBB",
																				ManagingUserId = "AFA1E979-FCA9-417B-A03D-69C0588FAD71",
																				ShowWelcome = true,
																				TimeZone = "Central Standard Time"
																			});

			context.SaveChanges();

			context.Departments.AddOrUpdate(a => a.DepartmentId,
																			new Department
																			{
																				DepartmentId = 3,
																				Name = "C Test Department",
																				Code = "CCCC",
																				ManagingUserId = "1AA0DCF8-4220-4C86-A10B-B6F8B3C0CA44",
																				ShowWelcome = true,
																				TimeZone = "Pacific Standard Time"
																			});

			context.SaveChanges();

			context.Departments.AddOrUpdate(a => a.DepartmentId,
																			new Department
																			{
																				DepartmentId = 4,
																				Name = "D Test Department",
																				Code = "DDDD",
																				ManagingUserId = TestData.Users.TestUser9Id,
																				ShowWelcome = true,
																				TimeZone = "Eastern Standard Time"
																			});

			context.SaveChanges();

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
													new DepartmentMember
													{
														DepartmentId = 1,
														UserId = "50DEC5DB-2612-4D6A-97E3-2F04B7228C85",
														IsAdmin = false
													});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 1,
																							UserId = "AE50CE3D-F94A-4FB5-A52F-D20BC015ADD7",
																							IsAdmin = true
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 1,
																							UserId = "C67955B4-2B1F-445B-ABC2-59F97CF54822",
																							IsAdmin = false
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 1,
																							UserId = "BC7071AE-4689-4DCC-BC93-F7C86820FEE7",
																							IsAdmin = false
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 2,
																							UserId = "AFA1E979-FCA9-417B-A03D-69C0588FAD71",
																							IsAdmin = false
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 2,
																							UserId = "ACF63412-BD08-421D-AC51-67B7C2E922F6",
																							IsAdmin = false
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 3,
																							UserId = "D2F59135-F132-4D54-89EE-7BB2E4DB6B8B",
																							IsAdmin = false
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 3,
																							UserId = "1AA0DCF8-4220-4C86-A10B-B6F8B3C0CA44",
																							IsAdmin = true
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 4,
																							UserId = TestData.Users.TestUser9Id,
																							IsAdmin = true,
																							IsHidden = true
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 4,
																							UserId = TestData.Users.TestUser10Id,
																							IsAdmin = false,
																							IsDisabled = true
																						});

			context.DepartmentMembers.AddOrUpdate(a => new { a.DepartmentId, a.UserId },
																						new DepartmentMember
																						{
																							DepartmentId = 4,
																							UserId = TestData.Users.TestUser11Id,
																							IsAdmin = false
																						});

			context.PushUris.AddOrUpdate(a => a.PushUriId,
																						new PushUri
																						{
																							PushUriId = 1,
																							CreatedOn = DateTime.Parse("2012-07-17 22:09:01.337"),
																							DeviceId = "A9E07B40EA5795E9048F04C7FFFFFFFF",
																							PlatformType = 1,
																							PushLocation = "http://sn1.notify.live.net/throttledthirdparty/01.00/AAGs9YivqlbTSopHfRfYSVWyAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMkUxREQ",
																							UserId = "1AA0DCF8-4220-4C86-A10B-B6F8B3C0CA44"
																						});

			context.PushUris.AddOrUpdate(a => a.PushUriId,
																						new PushUri
																						{
																							PushUriId = 2,
																							CreatedOn = DateTime.Parse("2012-07-17 22:09:01.337"),
																							DeviceId = "A9E07B40EA5795E9048F04C7FFFFFF11",
																							PlatformType = 1,
																							PushLocation = "http://sn1.notify.live.net/throttledthirdparty/01.00/AAGs9YivqlbTSopHfRfYSVWyAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMkUxREQ",
																							UserId = "50DEC5DB-2612-4D6A-97E3-2F04B7228C85"
																						});

			context.PushUris.AddOrUpdate(a => a.PushUriId,
																					new PushUri
																					{
																						PushUriId = 3,
																						CreatedOn = DateTime.Parse("2012-07-17 22:09:01.337"),
																						DeviceId = "A9E07B40EA5795E9048F04C7FFFFFF22",
																						PlatformType = 1,
																						PushLocation = "http://sn1.notify.live.net/throttledthirdparty/01.00/AAGs9YivqlbTSopHfRfYSVWyAgAAAAADAQAAAAQUZm52OkJCMjg1QTg1QkZDMkUxREQ",
																						UserId = "AE50CE3D-F94A-4FB5-A52F-D20BC015ADD7"
																					});

			context.UserProfiles.AddOrUpdate(a => a.UserProfileId,
																					new UserProfile
																					{
																						UserProfileId = 1,
																						UserId = "50DEC5DB-2612-4D6A-97E3-2F04B7228C85",
																						FirstName = "Test",
																						LastName = "User01",
																						MobileCarrier = 1,
																						MobileNumber = "(775) 232-8655",
																						SendEmail = true,
																						SendPush = true,
																						SendSms = true,
																						SendMessageEmail = true,
																						SendMessagePush = true,
																						SendMessageSms = true
																					});

			context.UserProfiles.AddOrUpdate(a => a.UserProfileId,
																					new UserProfile
																					{
																						UserProfileId = 2,
																						UserId = "AE50CE3D-F94A-4FB5-A52F-D20BC015ADD7",
																						FirstName = "Test",
																						LastName = "User02",
																						MobileCarrier = 1,
																						MobileNumber = "(777) 555-5555",
																						SendEmail = true,
																						SendPush = true,
																						SendSms = true,
																						SendMessageEmail = true,
																						SendMessagePush = true,
																						SendMessageSms = true
																					});

			context.Calls.AddOrUpdate(a => a.CallId,
																					new Call
																					{
																						CallId = 1,
																						DepartmentId = 1,
																						Name = "Priority 1E Cardiac Arrest D12",
																						NatureOfCall = "RP reports a person lying on the street not breathing.",
																						Notes = "RP doesn't know how to do CPR, can't roll over patient",
																						MapPage = "22T",
																						GeoLocationData = "39.27710789298309,-119.77201511943328",
																						LoggedOn = DateTime.Now,
																						ReportingUserId = TestData.Users.TestUser1Id,
																					});

			context.CallDispatches.AddOrUpdate(a => a.CallDispatchId,
																					new CallDispatch
																					{
																						CallDispatchId = 1,
																						CallId = 1,
																						UserId = TestData.Users.TestUser1Id
																					});

			context.QueueItems.AddOrUpdate(a => a.QueueItemId,
																					new QueueItem
																					{
																						QueueItemId = 1,
																						QueueType = 1,
																						SourceId = "1",
																						QueuedOn = DateTime.Now
																					});

			context.DepartmentGroups.AddOrUpdate(a => a.DepartmentGroupId,
					new DepartmentGroup
					{
						DepartmentGroupId = 1,
						DepartmentId = 3,
						Name = "Department 3 Group",
						Type = 1
					});

			context.DepartmentGroupMembers.AddOrUpdate(a => a.DepartmentGroupMemberId,
				new DepartmentGroupMember
				{
					DepartmentGroupMemberId = 1,
					DepartmentGroupId = 1,
					UserId = TestData.Users.TestUser7Id,
					IsAdmin = true
				});

			context.DepartmentGroupMembers.AddOrUpdate(a => a.DepartmentGroupMemberId,
				new DepartmentGroupMember
				{
					DepartmentGroupMemberId = 2,
					DepartmentGroupId = 1,
					UserId = TestData.Users.TestUser8Id
				});

			context.DepartmentGroups.AddOrUpdate(a => a.DepartmentGroupId,
					new DepartmentGroup
					{
						DepartmentGroupId = 2,
						DepartmentId = 4,
						Name = "Department 4 Group",
						Type = 1
					});

			context.Plans.AddOrUpdate(a => a.PlanId,
						new Plan
						{
							PlanId = 1,
							Cost = 0.00d,
							Frequency = 1,
							Name = "Forever Free"
						});

			context.PlanLimits.AddOrUpdate(a => a.PlanLimitId,
				new PlanLimit
				{
					PlanLimitId = 1,
					PlanId = 1,
					LimitType = (int)PlanLimitTypes.Personnel,
					LimitValue = int.MaxValue
				}
				);

			context.PlanLimits.AddOrUpdate(a => a.PlanLimitId,
				new PlanLimit
				{
					PlanLimitId = 2,
					PlanId = 1,
					LimitType = (int)PlanLimitTypes.Groups,
					LimitValue = int.MaxValue
				}
				);

			context.PlanLimits.AddOrUpdate(a => a.PlanLimitId,
						new PlanLimit
						{
							PlanLimitId = 3,
							PlanId = 1,
							LimitType = (int)PlanLimitTypes.Units,
							LimitValue = int.MaxValue
						}
						);


			context.Invites.AddOrUpdate(a => a.InviteId,
				new Invite
				{
					InviteId = 1,
					DepartmentId = 1,
					Code = Guid.NewGuid(),
					EmailAddress = "test@test.com",
					SendingUserId = TestData.Users.TestUser1Id,
					SentOn = DateTime.Now.AddDays(-3)
				}
				);

			context.Invites.AddOrUpdate(a => a.InviteId,
						new Invite
						{
							InviteId = 2,
							DepartmentId = 1,
							Code = Guid.NewGuid(),
							EmailAddress = "test1@test.com",
							CompletedOn = DateTime.Now,
							CompletedUserId = TestData.Users.TestUser2Id,
							SendingUserId = TestData.Users.TestUser1Id,
							SentOn = DateTime.Now.AddDays(-3)
						}
						);

			context.Invites.AddOrUpdate(a => a.InviteId,
				new Invite
				{
					InviteId = 3,
					DepartmentId = 2,
					Code = Guid.NewGuid(),
					EmailAddress = "test2@test.com",
					SendingUserId = TestData.Users.TestUser5Id,
					SentOn = DateTime.Now.AddDays(-3)
				}
				);

			#region Staffing Schedule
			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 1,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "1:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Available).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 2,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "2:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Delayed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 3,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "3:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Unavailable).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 4,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "4:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Committed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 5,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "5:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.OnShift).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 6,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "6:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Available).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 7,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "7:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Delayed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 8,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "8:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Unavailable).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 9,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "9:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Committed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 10,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "10:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.OnShift).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 11,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "11:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Available).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 12,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "12:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Delayed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 13,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "1:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Unavailable).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 14,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "2:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Committed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 15,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "3:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.OnShift).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 16,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "4:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Available).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 17,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "5:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Delayed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 18,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "6:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Unavailable).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 19,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "7:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Committed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 20,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "8:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.OnShift).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 21,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "9:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Available).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 22,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "10:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Delayed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 23,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "11:00 PM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Unavailable).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 24,
					UserId = TestData.Users.TestUser2Id,
					ScheduleType = 2,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "12:00 AM",
					Active = true,
					TaskType = 1,
					Data = ((int)UserStateTypes.Committed).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.ScheduledTasks.AddOrUpdate(a => a.ScheduledTaskId,
				new ScheduledTask
				{
					ScheduledTaskId = 25,
					UserId = TestData.Users.TestUser1Id,
					ScheduleType = 0,
					SpecifcDate = null,
					Sunday = true,
					Monday = true,
					Tuesday = true,
					Wednesday = true,
					Thursday = true,
					Friday = true,
					Saturday = true,
					Time = "12:00 AM",
					Active = true,
					TaskType = 2,
					Data = ((int)UserStateTypes.Unavailable).ToString(),
					AddedOn = DateTime.UtcNow
				}
			);

			context.Messages.AddOrUpdate(a => a.MessageId,
				new Message
				{
					MessageId = 1,
					Subject = "Test Message 1",
					IsBroadcast = true,
					IsDeleted = false,
					SendingUserId = TestData.Users.TestUser1Id,
					SentOn = DateTime.UtcNow,
					Body = "Just a test message for Department 1"
				});

			context.MessageRecipients.AddOrUpdate(a => new { a.MessageId, a.UserId },
				new MessageRecipient
				{
					MessageId = 1,
					UserId = TestData.Users.TestUser2Id
				});

			context.MessageRecipients.AddOrUpdate(a => new { a.MessageId, a.UserId },
				new MessageRecipient
				{
					MessageId = 1,
					UserId = TestData.Users.TestUser3Id
				});

			context.Logs.AddOrUpdate(a => a.LogId,
				new Log
				{
					LogId = 1,
					DepartmentId = 1,
					LogType = (int)LogTypes.Run,
					CallId = 1,
					Narrative = "Test Narrative for Call 1 in Department 1",
					LoggedOn = DateTime.UtcNow,
					LoggedByUserId = TestData.Users.TestUser2Id
				});

			context.SaveChanges();

			#endregion
		}
	}
}
