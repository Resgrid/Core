using System;
using System.Linq;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using System.Web;
using MimeKit;
using System.IO;
using System.Threading.Tasks;

namespace Resgrid.Workers.Framework.Logic
{
	public class DistributionListLogic
	{
		public static async Task<bool> ProcessDistributionListQueueItem(DistributionListQueueItem dlqi)
		{
			var emailService = Bootstrapper.GetKernel().Resolve<IEmailService>();
			var distributionListsService = Bootstrapper.GetKernel().Resolve<IDistributionListsService>();
			var fileService = Bootstrapper.GetKernel().Resolve<IFileService>();

			if (dlqi != null && dlqi.List != null && dlqi.Message != null)
			{
				// If we didn't get any profiles chances are the message size was too big for Azure, get selected profiles now.
				if (dlqi.Users == null)
				{
					var departmentsService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
					dlqi.Users = await departmentsService.GetAllUsersForDepartmentAsync(dlqi.List.DepartmentId);
				}

				var mailMessage = new MimeMessage();
				var builder = new BodyBuilder();


				if (!String.IsNullOrWhiteSpace(dlqi.Message.HtmlBody))
					builder.HtmlBody = HttpUtility.HtmlDecode(dlqi.Message.HtmlBody);

				if (!String.IsNullOrWhiteSpace(dlqi.Message.TextBody))
					builder.TextBody = dlqi.Message.TextBody;

				mailMessage.Subject = dlqi.Message.Subject;

				//mailMessage.From = new EmailAddress($"{dlqi.List.EmailAddress}@{Config.InboundEmailConfig.ListsDomain}", $"Resgrid ({dlqi.List.Name}) List");

				try
				{
					if (dlqi.FileIds != null && dlqi.FileIds.Any())
					{
						foreach (var fileId in dlqi.FileIds)
						{
							var file = await fileService.GetFileByIdAsync(fileId);

							// create an image attachment for the file located at path
							var attachment = new MimePart(file.FileType)
							{
								Content = new MimeContent(new MemoryStream(file.Data), ContentEncoding.Default),
								ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
								ContentTransferEncoding = ContentEncoding.Base64,
								FileName = file.FileName
							};

							builder.Attachments.Add(attachment);

							//mailMessage.Attachments.Add(file.Data, file.FileName, file.ContentId, file.FileType,
							//	new HeaderCollection(),	NewAttachmentOptions.None, MailTransferEncoding.None);

							await fileService.DeleteFileAsync(file);
						}
					}
				}
				catch { }

				mailMessage.Body = builder.ToMessageBody();

				if (dlqi.List.Members == null)
					dlqi.List = await distributionListsService.GetDistributionListByIdAsync(dlqi.List.DistributionListId);

				foreach (var member in dlqi.List.Members)
				{
					try
					{
						var user = dlqi.Users.FirstOrDefault(x => x.UserId == member.UserId);

						if (user != null && !String.IsNullOrWhiteSpace(user.Email))
							await emailService.SendDistributionListEmail(mailMessage, user.Email, dlqi.List.Name, dlqi.List.Name, $"{dlqi.List.EmailAddress}@lists.resgrid.com");
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			emailService = null;
			distributionListsService = null;
			fileService = null;

			return true;
		}
	}
}
