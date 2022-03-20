using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System.Collections.Generic;

namespace Resgrid.Workers.Framework.Logic
{
	public class BroadcastMessageLogic
	{
		public static async Task<bool> ProcessMessageQueueItem(MessageQueueItem mqi)
		{
			var _communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();

			if (mqi != null && mqi.Message == null && mqi.MessageId != 0)
			{
				var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
				mqi.Message = await messageService.GetMessageByIdAsync(mqi.MessageId);
			}

			if (mqi != null && mqi.Message != null)
			{
				if (mqi.Message.MessageRecipients == null || mqi.Message.MessageRecipients.Count <= 0)
				{
					var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
					mqi.Message = await messageService.GetMessageByIdAsync(mqi.Message.MessageId);
				}

				// If we didn't get any profiles chances are the message size was too big for Azure, get selected profiles now.
				if (mqi.Profiles == null)
				{
					var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();

					if (mqi.Message.MessageRecipients != null && mqi.Message.MessageRecipients.Any())
					{
						List<string> profilesToGet = new List<string>(mqi.Message.MessageRecipients.Select(x => x.UserId));
						profilesToGet.Add(mqi.Message.SendingUserId);

						mqi.Profiles = (await userProfileService.GetSelectedUserProfilesAsync(profilesToGet)).ToList();
					}
					else
					{
						mqi.Profiles = (await userProfileService.GetAllProfilesForDepartmentAsync(mqi.DepartmentId)).Select(x => x.Value).ToList();
					}
				}

				string name = string.Empty;
				if (!String.IsNullOrWhiteSpace(mqi.Message.SendingUserId))
				{
					var profile = mqi.Profiles.FirstOrDefault(x => x.UserId == mqi.Message.SendingUserId);

					if (profile != null)
						name = profile.FullName.AsFirstNameLastName;
				}

				if (mqi.Message.ReceivingUserId != null && (mqi.Message.Recipients == null || !mqi.Message.Recipients.Any()))
				{
					if (mqi.Profiles != null)
					{
						var sendingToProfile = mqi.Profiles.FirstOrDefault(x => x.UserId == mqi.Message.ReceivingUserId);

						if (sendingToProfile != null)
						{
							await _communicationService.SendMessageAsync(mqi.Message, name, mqi.DepartmentTextNumber, mqi.DepartmentId, sendingToProfile);
						}
						else
						{
							var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
							var sender = await userProfileService.GetProfileByUserIdAsync(mqi.Message.SendingUserId);

							if (sender != null)
								name = sender.FullName.AsFirstNameLastName;
						}
					}
				}
				else if (mqi.Message.MessageRecipients != null && mqi.Message.MessageRecipients.Any())
				{
					foreach (var recipient in mqi.Message.MessageRecipients)
					{
						var sendingToProfile = mqi.Profiles.FirstOrDefault(x => x.UserId == recipient.UserId);
						mqi.Message.ReceivingUserId = recipient.UserId;

						if (sendingToProfile != null)
						{
							await _communicationService.SendMessageAsync(mqi.Message, name, mqi.DepartmentTextNumber, mqi.DepartmentId, sendingToProfile);
						}
					}
				}
			}

			_communicationService = null;
			return true;
		}
	}
}
