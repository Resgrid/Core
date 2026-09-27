using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.DistributionList;
using Resgrid.Model.Identity;
using System;
using System.Threading.Tasks;
using Autofac;

namespace Resgrid.Workers.Framework.Logic
{
	public class DistributionListEmailImporterLogic
	{
		public async Task<Tuple<bool, string>> Process(DistributionListQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item?.List != null)
			{
				if (item.List.Type == null || item.List.Type == (int)DistributionListTypes.External)
				{
					try
					{
						// Own scope per item: root-scope services would share the process-wide root unit of work.
						using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
						var distributionListProvider = scope.Resolve<IDistributionListProvider>();
						var emailService = scope.Resolve<IEmailService>();
						var usersService = scope.Resolve<IUsersService>();
						var distributionListsService = scope.Resolve<IDistributionListsService>();

						var emails = distributionListProvider.GetNewMessagesFromMailbox(item.List);

						if (emails != null && emails.Count > 0)
						{
							var listMembers = await distributionListsService.GetAllListMembersByListIdAsync(item.List.DistributionListId);
							foreach (var email in emails)
							{
								foreach (var member in listMembers)
								{
									IdentityUser membership = null;
									if (member.User != null && member.User != null)
										membership = member.User;
									else
										membership = usersService.GetMembershipByUserId(member.UserId);

									if (membership != null && !String.IsNullOrWhiteSpace(membership.Email))
										await emailService.SendDistributionListEmail(email, membership.Email, item.List.Name, $"Resgrid ({item.List.Name}) List", $"{item.List.EmailAddress}@{Config.InboundEmailConfig.ListsDomain}");
								}
							}
						}
					}
					catch (Exception ex)
					{
						success = false;
						result = ex.ToString();
					}
				}
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
