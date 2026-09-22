using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Invoicing;

namespace Resgrid.Web.Areas.User.Models.Deployments
{
    public class DeploymentReportsView
    {
        public DeploymentIndexView Operations { get; set; } = new DeploymentIndexView();
        public List<RmsExternalOrder> Orders { get; set; } = new List<RmsExternalOrder>();
        public Department Department { get; set; }
    }

    public class DeploymentReportView
    {
        public Deployment Deployment { get; set; }
        public Department Department { get; set; }
        public List<DeploymentTimeReport> TimeReports { get; set; } = new List<DeploymentTimeReport>();
        public List<DeploymentExpense> Expenses { get; set; } = new List<DeploymentExpense>();
        public List<DeploymentAttachment> Attachments { get; set; } = new List<DeploymentAttachment>();
        public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
        public bool CanManage { get; set; }
    }
}
