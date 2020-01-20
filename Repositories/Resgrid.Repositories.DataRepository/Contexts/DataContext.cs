using System.Configuration;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using Microsoft.AspNet.Identity.EntityFramework6;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Configurations;

namespace Resgrid.Repositories.DataRepository.Contexts
{
	[DbConfigurationType(typeof(WebDbConfiguration))]
	public class DataContext : DbContext, IDbContext
	{
		public DbSet<Department> Departments { get; set; }
		public DbSet<DepartmentMember> DepartmentMembers { get; set; }
		public DbSet<DepartmentSetting> DepartmentSettings { get; set; }
		public DbSet<DepartmentGroup> DepartmentGroups { get; set; }
		public DbSet<DepartmentGroupMember> DepartmentGroupMembers { get; set; }
		public DbSet<ActionLog> ActionLogs { get; set; }
		public DbSet<LogEntry> LogEntries { get; set; }
		public DbSet<Call> Calls { get; set; }
		public DbSet<UserState> UserStates { get; set; }
		public DbSet<Address> Addresses { get; set; }
		public DbSet<Message> Messages { get; set; }
		public DbSet<PushUri> PushUris { get; set; }
		public DbSet<PushLog> PushLogs { get; set; }
		public DbSet<CallDispatch> CallDispatches { get; set; }
		public DbSet<UserProfile> UserProfiles { get; set; }
		public DbSet<QueueItem> QueueItems { get; set; }
		public DbSet<Plan> Plans { get; set; }
		public DbSet<Invite> Invites { get; set; }
		public DbSet<Log> Logs { get; set; }
		public DbSet<CallLog> CallLogs { get; set; }
		public DbSet<Payment> Payments { get; set; }
		public DbSet<PlanLimit> PlanLimits { get; set; }
		public DbSet<CallType> CallTypes { get; set; }
		public DbSet<DepartmentCallEmail> DepartmentCallEmails { get; set; }
		public DbSet<CallAttachment> CallAttachments { get; set; }
		public DbSet<DepartmentCallPruning> DepartmentCallPruning { get; set; }
		public DbSet<Job> Jobs { get; set; }
		public DbSet<Unit> Units { get; set; }
		public DbSet<UnitState> UnitStates { get; set; }
		public DbSet<UnitLog> UnitLogs { get; set; }
		public DbSet<UnitType> UnitTypes { get; set; }
		public DbSet<PersonnelRole> PersonnelRoles { get; set; }
		public DbSet<PersonnelRoleUser> PersonnelRoleUsers { get; set; }
		public DbSet<ScheduledTask> ScheduledTasks { get; set; }
		public DbSet<ScheduledTaskLog> ScheduledTaskLogs { get; set; }
		public DbSet<DistributionList> DistributionLists { get; set; }
		public DbSet<DistributionListMember> DistributionListMembers { get; set; }
		public DbSet<Document> Documents { get; set; }
		public DbSet<PaymentProviderEvent> PaymentProviderEvents { get; set; }
		public DbSet<Note> Notes { get; set; }
		public DbSet<CalendarItem> CalendarItems { get; set; }
		public DbSet<CalendarItemType> CalendarItemTypes { get; set; }
		public DbSet<DepartmentCertificationType> DepartmentCertificationTypes { get; set; }
		public DbSet<PersonnelCertification> PersonnelCertifications { get; set; }
		public DbSet<PushTemplate> PushTemplates { get; set; }
		public DbSet<Affiliate> Affiliates { get; set; }
		public DbSet<InboundMessageEvent> InboundMessageEvents { get; set; }
		public DbSet<UnitRole> UnitRoles { get; set; }
		public DbSet<UnitStateRole> UnitStateRoles { get; set; }
		public DbSet<LogUnit> LogUnits { get; set; }
		public DbSet<LogUser> LogUsers { get; set; }
		public DbSet<DepartmentNotification> DepartmentNotifications { get; set; }
		public DbSet<Shift> Shifts { get; set; }
		public DbSet<ShiftGroup> ShiftGroups { get; set; }
		public DbSet<ShiftGroupRole> ShiftGroupRoles { get; set; }
		public DbSet<ShiftGroupAssignment> ShiftGroupAssignment { get; set; }
		public DbSet<ShiftDay> ShiftDays { get; set; }
		public DbSet<ShiftPerson> ShiftPersons { get; set; }
		public DbSet<ShiftAdmin> ShiftAdmins { get; set; }
		public DbSet<ShiftSignup> ShiftSignups { get; set; }
		public DbSet<Training> Trainings { get; set; }
		public DbSet<TrainingQuestion> TrainingQuestions { get; set; }
		public DbSet<TrainingQuestionAnswer> TrainingQuestionAnswers { get; set; }
		public DbSet<TrainingAttachment> TrainingAttachments { get; set; }
		public DbSet<TrainingUser> TrainingUsers { get; set; }
		public DbSet<CommandDefinition> CommandDefinitions { get; set; }
		public DbSet<CommandDefinitionRole> CommandDefinitionRoles { get; set; }
		public DbSet<CommandDefinitionRoleCert> CommandDefinitionRoleCerts { get; set; }
		public DbSet<CommandDefinitionRolePersonnelRole> CommandDefinitionRolePersonnelRoles { get; set; }
		public DbSet<CommandDefinitionRoleUnitType> CommandDefinitionRoleUnitTypes { get; set; }
		public DbSet<CustomState> CustomStates { get; set; }
		public DbSet<CustomStateDetail> CustomStateDetails { get; set; }
		public DbSet<PoiType> PoiTypes { get; set; }
		public DbSet<ShiftSignupTrade> ShiftSignupTrades { get; set; }
		public DbSet<ShiftSignupTradeUser> ShiftSignupTradeUsers { get; set; }
		public DbSet<ShiftSignupTradeUserShift> ShiftSignupTradeUserShifts { get; set; }
		public DbSet<MessageRecipient> MessageRecipients { get; set; }
		public DbSet<AuditLog> AuditLogs { get; set; }
		public DbSet<Permission> Permissions { get; set; }
		public DbSet<InventoryType> InventoryTypes { get; set; }
		public DbSet<NotificationAlert> NotificationAlerts { get; set; }
		public DbSet<UnitLocation> UnitLocations { get; set; }
		public DbSet<CallNote> CallNotes { get; set; }
		public DbSet<CalendarItemAttendee> CalendarItemAttendees { get; set; }
		public DbSet<CallDispatchGroup> CallDispatchGroups { get; set; }
		public DbSet<CallUnit> CallUnits { get; set; }
		public DbSet<ProcessLog> ProcessLogs { get; set; }
		public DbSet<DepartmentFile> DepartmentFiles { get; set; }
		public DbSet<LogAttachment> LogAttachments { get; set; }
		public DbSet<CallDispatchUnit> CallDispatchUnits { get; set; }
		public DbSet<CallDispatchRole> CallDispatchRoles { get; set; }
		public DbSet<Poi> Pois { get; set; }
		public DbSet<Inventory> Inventories { get; set; }
		public DbSet<DepartmentProfile> DepartmentProfiles { get; set; }
		public DbSet<DepartmentProfileInvite> DepartmentProfileInvites { get; set; }
		public DbSet<DepartmentProfileMessage> DepartmentProfileMessages { get; set; }
		public DbSet<DepartmentProfileArticle> DepartmentProfileArticles { get; set; }
		public DbSet<Incident> Incidents { get; set; }
		public DbSet<ShiftStaffing> ShiftStaffings { get; set; }
		public DbSet<ShiftStaffingPerson> ShiftStaffingPersons { get; set; }
		public DbSet<DepartmentProfileUser> DepartmentProfileUsers { get; set; }
		public DbSet<DepartmentProfileUserFollow> DepartmentProfileUserFollows { get; set; }
		public DbSet<IncidentLog> IncidentLogs { get; set; }
		public DbSet<Rank> Ranks { get; set; }
		public DbSet<Microsoft.AspNet.Identity.EntityFramework6.IdentityUser> ApplicationUsers { get; set; }
		public DbSet<IdentityUserRole> ApplicationUserRoles { get; set; }
		public DbSet<IdentityUserClaim> ApplicationUserClaims { get; set; }
		public DbSet<IdentityRole> ApplicationRoles { get; set; }
		public DbSet<IdentityRoleClaim> ApplicationRoleClaims { get; set; }
		public DbSet<IdentityUserLogin> ApplicationUserLogins { get; set; }
		public DbSet<IdentityUserToken> ApplicationUserTokens { get; set; }
		public DbSet<ApplicationUserExt> ApplicationUserExts { get; set; }
		public DbSet<File> Files { get; set; }
		public DbSet<DepartmentLink> DepartmentLinks { get; set; }
		public DbSet<ResourceOrderSetting> ResourceOrderSettings { get; set; }
		public DbSet<ResourceOrder> ResourceOrders { get; set; }
		public DbSet<ResourceOrderItem> ResourceOrderItems { get; set; }
		public DbSet<ResourceOrderFill> ResourceOrderFills { get; set; }
		public DbSet<ResourceOrderFillUnit> ResourceOrderFillUnits { get; set; }
		public DbSet<DepartmentCallPriority> DepartmentCallPriorities { get; set; }
		public DbSet<Automation> Automations { get; set; }
		public DbSet<CallQuickTemplate> CallQuickTemplates { get; set; }

		public DataContext()
			: base(ConfigurationManager.ConnectionStrings.Cast<ConnectionStringSettings>().FirstOrDefault(x => x.Name == "ResgridContext").ConnectionString)
		{
		}

		public DataContext(string connectionStringName)
			: base(connectionStringName)
		{
		}

		public static bool SuspendExecutionStrategy
		{
			get
			{
				return (bool?)CallContext.LogicalGetData("SuspendExecutionStrategy") ?? false;
			}
			set
			{
				CallContext.LogicalSetData("SuspendExecutionStrategy", value);
			}
		}

		protected override void OnModelCreating(DbModelBuilder modelBuilder)
		{
			//modelBuilder.Conventions.Remove<OneToManyCascadeDeleteConvention>();
			//modelBuilder.Conventions.Remove<ManyToManyCascadeDeleteConvention>();

			//modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();

			modelBuilder.Configurations.Add(new DepartmentMember_Mapping());
			modelBuilder.Configurations.Add(new DepartmentGroupMember_Mapping());
			modelBuilder.Configurations.Add(new ActionLog_Mapping());
			modelBuilder.Configurations.Add(new Call_Mapping());
			modelBuilder.Configurations.Add(new UserState_Mapping());
			modelBuilder.Configurations.Add(new CallDispatch_Mapping());
			modelBuilder.Configurations.Add(new Invite_Mapping());
			modelBuilder.Configurations.Add(new Log_Mapping());
			modelBuilder.Configurations.Add(new CallLog_Mapping());
			modelBuilder.Configurations.Add(new Payment_Mapping());
			modelBuilder.Configurations.Add(new PlanLimit_Mapping());
			modelBuilder.Configurations.Add(new CallAttachment_Mapping());
			modelBuilder.Configurations.Add(new Unit_Mapping());
			modelBuilder.Configurations.Add(new PersonnelRoleUser_Mapping());
			modelBuilder.Configurations.Add(new DistributionListMember_Mapping());
			modelBuilder.Configurations.Add(new Document_Mapping());
			modelBuilder.Configurations.Add(new PersonnelCertification_Mapping());
			modelBuilder.Configurations.Add(new UnitState_Mapping());
			modelBuilder.Configurations.Add(new LogUser_Mapping());
			modelBuilder.Configurations.Add(new ShiftGroup_Mapping());
			modelBuilder.Configurations.Add(new Shift_Mapping());
			modelBuilder.Configurations.Add(new Training_Mapping());
			modelBuilder.Configurations.Add(new CommandDefinition_Mapping());
			modelBuilder.Configurations.Add(new ShiftSignup_Mapping());
			modelBuilder.Configurations.Add(new ShiftSignupTradeUser_Mapping());
			modelBuilder.Configurations.Add(new ShiftSignupTrade_Mapping());
			modelBuilder.Configurations.Add(new MessageRecipient_Mapping());
			modelBuilder.Configurations.Add(new CallNote_Mapping());
			modelBuilder.Configurations.Add(new CalendarItemAttendee_Mapping());
			modelBuilder.Configurations.Add(new CallDispatchGroup_Mapping());
			modelBuilder.Configurations.Add(new CallDispatchRole_Mapping());
			modelBuilder.Configurations.Add(new Inventory_Mapping());
			modelBuilder.Configurations.Add(new ShiftStaffing_Mapping());
			modelBuilder.Configurations.Add(new DepartmentProfileArticle_Mapping());
			modelBuilder.Configurations.Add(new ResourceOrder_Mapping());

			modelBuilder.Configurations.Add(new ApplicationUserToken_Mapping());
			modelBuilder.Configurations.Add(new ApplicationUserRole_Mapping());
			modelBuilder.Configurations.Add(new ApplicationUserLogin_Mapping());
			modelBuilder.Configurations.Add(new ApplicationUserClaim_Mapping());
			modelBuilder.Configurations.Add(new ApplicationUser_Mapping());
			modelBuilder.Configurations.Add(new ApplicationRoleClaim_Mapping());
			modelBuilder.Configurations.Add(new ApplicationRole_Mapping());
			modelBuilder.Configurations.Add(new DepartmentLink_Mapping());

			//modelBuilder.Entity<IdentityUserRole>().HasKey(r => new { r.RoleId, r.UserId });

			//modelBuilder.Configurations.Add(new ShiftGroupPerson_Mapping());

			//foreach (Type classType in from t in Assembly.GetAssembly(typeof(DecimalPrecisionAttribute)).GetTypes()
			//                           where t.IsClass && t.Namespace.Contains("Resgrid.Model")
			//                           select t)
			//{
			//    foreach (var propAttr in classType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<DecimalPrecisionAttribute>() != null).Select(
			//           p => new { prop = p, attr = p.GetCustomAttribute<DecimalPrecisionAttribute>(true) }))
			//    {

			//        var entityConfig = modelBuilder.GetType().GetMethod("Entity").MakeGenericMethod(classType).Invoke(modelBuilder, null);
			//        ParameterExpression param = ParameterExpression.Parameter(classType, "c");
			//        Expression property = Expression.Property(param, propAttr.prop.Name);
			//        LambdaExpression lambdaExpression = Expression.Lambda(property, true,
			//                                                                 new ParameterExpression[] { param });
			//        DecimalPropertyConfiguration decimalConfig;
			//        if (propAttr.prop.PropertyType.IsGenericType && propAttr.prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
			//        {
			//            MethodInfo methodInfo = entityConfig.GetType().GetMethods().Where(p => p.Name == "Property").ToList()[7];
			//            decimalConfig = methodInfo.Invoke(entityConfig, new[] { lambdaExpression }) as DecimalPropertyConfiguration;
			//        }
			//        else
			//        {
			//            MethodInfo methodInfo = entityConfig.GetType().GetMethods().Where(p => p.Name == "Property").ToList()[6];
			//            decimalConfig = methodInfo.Invoke(entityConfig, new[] { lambdaExpression }) as DecimalPropertyConfiguration;
			//        }

			//        if (decimalConfig != null)
			//            decimalConfig.HasPrecision(propAttr.attr.Precision, propAttr.attr.Scale);
			//    }
			//}

			base.OnModelCreating(modelBuilder);
		}

		public string CreateDatabaseScript()
		{
			return ((IObjectContextAdapter)this).ObjectContext.CreateDatabaseScript();
		}

		public new IDbSet<TEntity> Set<TEntity>() where TEntity : class
		{
			return base.Set<TEntity>();
		}

		public bool IsSqlCe()
		{
			return Database.Connection.ConnectionString.Contains(".sdf");
		}
	}
}
