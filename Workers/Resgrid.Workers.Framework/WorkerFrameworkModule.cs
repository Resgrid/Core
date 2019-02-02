using Autofac;
using Resgrid.Workers.Framework.Backend;
using Resgrid.Workers.Framework.Backend.Heartbeat;
using Resgrid.Workers.Framework.Backend.Scout;
using Resgrid.Workers.Framework.Workers.DistributionList;
using Resgrid.Workers.Framework.Workers.MessageBroadcast;
using Resgrid.Workers.Framework.Workers.Notification;
using Resgrid.Workers.Framework.Workers.ReportDelivery;
using Resgrid.Workers.Framework.Workers.ShiftNotifier;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;

namespace Resgrid.Workers.Framework
{
	public class WorkerFrameworkModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<CallQueue>().As<CallQueue>().InstancePerLifetimeScope();
			builder.RegisterType<CallBroadcastCommand>().As<CallBroadcastCommand>().InstancePerLifetimeScope();


			builder.RegisterType<CallEmailQueue>().As<CallEmailQueue>().InstancePerLifetimeScope();
			builder.RegisterType<CallEmailCommand>().As<CallEmailCommand>().InstancePerLifetimeScope();

			builder.RegisterType<CallPruneQueue>().As<CallPruneQueue>().InstancePerLifetimeScope();
			builder.RegisterType<CallPruningCommand>().As<CallPruningCommand>().InstancePerLifetimeScope();

			builder.RegisterType<GuardianQueue>().As<GuardianQueue>().InstancePerLifetimeScope();
			builder.RegisterType<GuardianCommand>().As<GuardianCommand>().InstancePerLifetimeScope();

			builder.RegisterType<StaffingScheduleQueue>().As<StaffingScheduleQueue>().InstancePerLifetimeScope();
			builder.RegisterType<StaffingScheduleCommand>().As<StaffingScheduleCommand>().InstancePerLifetimeScope();

			builder.RegisterType<DistributionListQueue>().As<DistributionListQueue>().InstancePerLifetimeScope();
			builder.RegisterType<DistributionListCommand>().As<DistributionListCommand>().InstancePerLifetimeScope();

			builder.RegisterType<ScoutQueue>().As<ScoutQueue>().InstancePerLifetimeScope();
			builder.RegisterType<ScoutCommand>().As<ScoutCommand>().InstancePerLifetimeScope();

			builder.RegisterType<MessageQueue>().As<MessageQueue>().InstancePerLifetimeScope();
			builder.RegisterType<MessageBroadcastCommand>().As<MessageBroadcastCommand>().InstancePerLifetimeScope();

			builder.RegisterType<ReportDeliveryQueue>().As<ReportDeliveryQueue>().InstancePerLifetimeScope();
			builder.RegisterType<ReportDeliveryCommand>().As<ReportDeliveryCommand>().InstancePerLifetimeScope();

			builder.RegisterType<NotificationQueue>().As<NotificationQueue>().InstancePerLifetimeScope();
			builder.RegisterType<NotificationCommand>().As<NotificationCommand>().InstancePerLifetimeScope();

			builder.RegisterType<HeartbeatQueue>().As<HeartbeatQueue>().InstancePerLifetimeScope();
			builder.RegisterType<HeartbeatCommand>().As<HeartbeatCommand>().InstancePerLifetimeScope();

			builder.RegisterType<ShiftNotifierQueue>().As<ShiftNotifierQueue>().InstancePerLifetimeScope();
			builder.RegisterType<ShiftNotifierCommand>().As<ShiftNotifierCommand>().InstancePerLifetimeScope();

			builder.RegisterType<TrainingNotifierQueue>().As<TrainingNotifierQueue>().InstancePerLifetimeScope();
			builder.RegisterType<TrainingNotifierCommand>().As<TrainingNotifierCommand>().InstancePerLifetimeScope();
		}
	}
}
