using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.ReportDelivery;
using RestSharp;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Config;

namespace Resgrid.Workers.Framework.Logic
{
	public class ReportDeliveryLogic
	{
		private IScheduledTasksService _scheduledTasksService;
		private IEmailService _emailService;
		private IPdfProvider _pdfProvider;

		public ReportDeliveryLogic()
		{
			_scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
			_emailService = Bootstrapper.GetKernel().Resolve<IEmailService>();
			_pdfProvider = Bootstrapper.GetKernel().Resolve<IPdfProvider>();
		}

		public async Task<Tuple<bool, string>> Process(ReportDeliveryQueueItem item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.ScheduledTask != null)
			{
				try
				{
					if (ConfigHelper.CanTransmit(item.Department.DepartmentId))
					{
						var client = new RestClient(Config.SystemBehaviorConfig.ResgridBaseUrl);
						var request = new RestRequest("User/Reports/InternalRunReport", Method.Get);
						request.AddParameter("type", item.ScheduledTask.Data);
						request.AddParameter("departmentId", item.Department.DepartmentId);

						var response = await client.ExecuteAsync(request);

						if (!string.IsNullOrWhiteSpace(response.Content))
						{
							//var content =
							//	response.Content.Replace(
							//		"<script type=\"text/javascript\">//<![CDATA[try { if (!window.CloudFlare) { var CloudFlare =[{ verbose: 0,p: 0,byc: 0,owlid: \"cf\",bag2: 1,mirage2: 0,oracle: 0,paths: { cloudflare: \"/cdn-cgi/nexp/dok3v=1613a3a185/\"},atok: \"ab24e007e451de77ff7ceb381c2348f0\",petok: \"80b990bdd3e004d336674babd22922ae48e99033-1471055012-1800\",zone: \"resgrid.com\",rocket: \"0\",apps: { \"ga_key\":{ \"ua\":\"UA-5288869-4\",\"ga_bs\":\"2\"},\"abetterbrowser\":{ \"config\":\"none\"} },sha2test: 0}]; !function(a, b){ a = document.createElement(\"script\"),b = document.getElementsByTagName(\"script\")[0],a.async = !0,a.src = \"//ajax.cloudflare.com/cdn-cgi/nexp/dok3v=0489c402f5/cloudflare.min.js\",b.parentNode.insertBefore(a, b)} ()} } catch (e) { };//]]></script>", "");

							Regex rRemScript = new Regex(@"<script[^>]*>[\s\S]*?</script>");
							var content = rRemScript.Replace(response.Content, "");

							var systemNotificaiton = new EmailNotification();
							systemNotificaiton.Subject = string.Format("Resgrid {0} Report for {1} ", ((ReportTypes)int.Parse(item.ScheduledTask.Data)), DateTime.UtcNow.TimeConverter(item.Department));

							string fileName = string.Format("{0}Report_{1}.pdf", ((ReportTypes)int.Parse(item.ScheduledTask.Data)),
								DateTime.UtcNow.TimeConverter(item.Department));

							fileName = fileName.Replace(" ", "_");
							fileName = fileName.Replace("/", "");
							fileName = fileName.Replace(":", "");

							systemNotificaiton.To = item.Email;
							systemNotificaiton.AttachmentName = fileName;
							systemNotificaiton.AttachmentData = _pdfProvider.ConvertHtmlToPdf(content);

							string reportUrl = "";
							switch ((ReportTypes)int.Parse(item.ScheduledTask.Data))
							{
								case ReportTypes.Staffing:
									reportUrl = $"{SystemBehaviorConfig.ResgridBaseUrl}/User/Reports/StaffingReport";
									break;
								case ReportTypes.Personnel:
									reportUrl = $"{SystemBehaviorConfig.ResgridBaseUrl}/User/Reports/PersonnelReport";
									break;
								case ReportTypes.Certifications:
									reportUrl = $"{SystemBehaviorConfig.ResgridBaseUrl}/User/Reports/CertificationsReport";
									break;
								case ReportTypes.ShiftReadiness:
									reportUrl = $"{SystemBehaviorConfig.ResgridBaseUrl}/User/Reports/UpcomingShiftReadinessReport";
									break;
							}

							await _emailService.SendReportDeliveryAsync(systemNotificaiton, item.Department.DepartmentId, reportUrl, ((ReportTypes)int.Parse(item.ScheduledTask.Data)).ToString());
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					result = ex.ToString();
					success = false;
				}

				if (success)
					await _scheduledTasksService.CreateScheduleTaskLogAsync(item.ScheduledTask);
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}
