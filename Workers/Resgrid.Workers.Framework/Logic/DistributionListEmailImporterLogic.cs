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
		private IDistributionListProvider _distributionListProvider;
		private IEmailService _emailService;
		private IUsersService _usersService;
		private IDistributionListsService _distributionListsService;

		public DistributionListEmailImporterLogic()
		{
			_distributionListProvider = Bootstrapper.GetKernel().Resolve<IDistributionListProvider>();
			_emailService = Bootstrapper.GetKernel().Resolve<IEmailService>();
			_usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();
			_distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();
		}

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
						var emails = _distributionListProvider.GetNewMessagesFromMailbox(item.List);

						if (emails != null && emails.Count > 0)
						{
							var listMembers = await _distributionListsService.GetAllListMembersByListIdAsync(item.List.DistributionListId);
							foreach (var email in emails)
							{
								foreach (var member in listMembers)
								{
									IdentityUser membership = null;
									if (member.User != null && member.User != null)
										membership = member.User;
									else
										membership = _usersService.GetMembershipByUserId(member.UserId);

									if (membership != null && !String.IsNullOrWhiteSpace(membership.Email))
										await _emailService.SendDistributionListEmail(email, membership.Email, item.List.Name, $"Resgrid ({item.List.Name}) List", $"{item.List.EmailAddress}@{Config.InboundEmailConfig.ListsDomain}");
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
