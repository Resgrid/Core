 --
-- Create table "public"."userstates"
--
CREATE TABLE public.userstates(
  userstateid serial,
  userid citext NOT NULL,
  state integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  note citext,
  departmentid integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.userstates 
  ADD PRIMARY KEY (userstateid);

--
-- Create table "public"."userprofiles"
--
CREATE TABLE public.userprofiles(
  userprofileid serial,
  userid citext NOT NULL,
  firstname citext,
  lastname citext,
  timezone citext,
  mobilenumber citext,
  mobilecarrier integer NOT NULL,
  sendemail boolean NOT NULL,
  sendpush boolean NOT NULL,
  sendsms boolean NOT NULL,
  sendmessageemail boolean NOT NULL DEFAULT false,
  sendmessagepush boolean NOT NULL DEFAULT false,
  sendmessagesms boolean NOT NULL DEFAULT false,
  donotrecievenewsletters boolean NOT NULL DEFAULT false,
  homenumber citext,
  homeaddressid integer,
  mailingaddressid integer,
  identificationnumber citext,
  sendnotificationemail boolean NOT NULL DEFAULT false,
  sendnotificationpush boolean NOT NULL DEFAULT false,
  sendnotificationsms boolean NOT NULL DEFAULT false,
  voiceforcall boolean NOT NULL DEFAULT false,
  voicecallmobile boolean NOT NULL DEFAULT false,
  voicecallhome boolean NOT NULL DEFAULT false,
  image bytea,
  lastupdated timestamp without time zone,
  custompushsounds boolean NOT NULL DEFAULT false,
  startdate timestamp without time zone,
  enddate timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.userprofiles 
  ADD PRIMARY KEY (userprofileid);

--
-- Create table "public"."scheduledtasks"
--
CREATE TABLE public.scheduledtasks(
  userid citext NOT NULL,
  scheduletype integer NOT NULL,
  specifcdate timestamp without time zone,
  sunday boolean NOT NULL,
  monday boolean NOT NULL,
  tuesday boolean NOT NULL,
  wednesday boolean NOT NULL,
  thursday boolean NOT NULL,
  friday boolean NOT NULL,
  saturday boolean NOT NULL,
  active boolean NOT NULL,
  tasktype integer NOT NULL,
  data citext,
  scheduledtaskid serial,
  "time" citext,
  addedon timestamp without time zone,
  note citext,
  departmentid integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.scheduledtasks 
  ADD PRIMARY KEY (scheduledtaskid);

--
-- Create table "public"."scheduledtasklogs"
--
CREATE TABLE public.scheduledtasklogs(
  scheduledtasklogid serial,
  scheduledtaskid integer NOT NULL,
  rundate timestamp without time zone NOT NULL,
  successful boolean NOT NULL,
  data citext
);

--
-- Create primary key
--
ALTER TABLE public.scheduledtasklogs 
  ADD PRIMARY KEY (scheduledtasklogid);

--
-- Create foreign key
--
ALTER TABLE public.scheduledtasklogs 
  ADD CONSTRAINT "fk_dbo.scheduledtasklogs_dbo.scheduledtasks_scheduledtaskid" FOREIGN KEY (scheduledtaskid)
    REFERENCES public.scheduledtasks(scheduledtaskid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."queueitems"
--
CREATE TABLE public.queueitems(
  queueitemid serial,
  queuetype integer NOT NULL,
  sourceid citext,
  queuedon timestamp without time zone NOT NULL,
  pickedup timestamp without time zone,
  completedon timestamp without time zone,
  receipt citext
);

--
-- Create primary key
--
ALTER TABLE public.queueitems 
  ADD PRIMARY KEY (queueitemid);

--
-- Create table "public"."pushuris"
--
CREATE TABLE public.pushuris(
  pushuriid serial,
  userid citext NOT NULL,
  platformtype integer NOT NULL,
  pushlocation citext NOT NULL,
  createdon timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  deviceid citext,
  unitid integer
);

--
-- Create primary key
--
ALTER TABLE public.pushuris 
  ADD PRIMARY KEY (pushuriid);

--
-- Create table "public"."pushtemplates"
--
CREATE TABLE public.pushtemplates(
  pushtemplateid serial,
  platformtype integer NOT NULL,
  template citext
);

--
-- Create primary key
--
ALTER TABLE public.pushtemplates 
  ADD PRIMARY KEY (pushtemplateid);

--
-- Create table "public"."pushlogs"
--
CREATE TABLE public.pushlogs(
  pushlogid serial,
  channeluri citext NOT NULL,
  status citext NOT NULL,
  connection citext NOT NULL,
  subscription citext NOT NULL,
  notification citext NOT NULL,
  exception citext NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  messageid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.pushlogs 
  ADD PRIMARY KEY (pushlogid);

--
-- Create table "public"."processlogs"
--
CREATE TABLE public.processlogs(
  processlogid serial,
  type integer NOT NULL,
  sourceid integer NOT NULL,
  targetruntime timestamp without time zone NOT NULL,
  "timestamp" timestamp without time zone NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.processlogs 
  ADD PRIMARY KEY (processlogid);

--
-- Create table "public"."plans"
--
CREATE TABLE public.plans(
  planid serial,
  name citext,
  cost double precision NOT NULL,
  frequency integer NOT NULL,
  externalid citext
);

--
-- Create primary key
--
ALTER TABLE public.plans 
  ADD PRIMARY KEY (planid);

--
-- Create table "public"."planlimits"
--
CREATE TABLE public.planlimits(
  planlimitid serial,
  planid integer NOT NULL,
  limittype integer NOT NULL,
  limitvalue integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.planlimits 
  ADD PRIMARY KEY (planlimitid);

--
-- Create foreign key
--
ALTER TABLE public.planlimits 
  ADD CONSTRAINT "fk_dbo.planlimits_dbo.plans_planid" FOREIGN KEY (planid)
    REFERENCES public.plans(planid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."paymentproviderevents"
--
CREATE TABLE public.paymentproviderevents(
  paymentprovidereventid serial,
  providertype integer NOT NULL,
  customerid citext NOT NULL,
  recievedon timestamp without time zone NOT NULL,
  data citext NOT NULL,
  type citext,
  processed boolean
);

--
-- Create primary key
--
ALTER TABLE public.paymentproviderevents 
  ADD PRIMARY KEY (paymentprovidereventid);

--
-- Create table "public"."messages"
--
CREATE TABLE public.messages(
  messageid serial,
  subject citext NOT NULL,
  isbroadcast boolean NOT NULL,
  sendinguserid citext,
  body citext NOT NULL,
  senton timestamp without time zone NOT NULL,
  recipients citext,
  receivinguserid citext,
  isdeleted boolean NOT NULL DEFAULT false,
  readon timestamp without time zone,
  systemgenerated boolean NOT NULL DEFAULT false,
  type integer NOT NULL DEFAULT 0,
  expireon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.messages 
  ADD PRIMARY KEY (messageid);

--
-- Create table "public"."messagerecipients"
--
CREATE TABLE public.messagerecipients(
  messagerecipientid serial,
  userid citext NOT NULL,
  isdeleted boolean NOT NULL,
  readon timestamp without time zone,
  response citext,
  note citext,
  latitude numeric,
  longitude numeric,
  message_messageid integer,
  messageid integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.messagerecipients 
  ADD PRIMARY KEY (messagerecipientid);

--
-- Create foreign key
--
ALTER TABLE public.messagerecipients 
  ADD CONSTRAINT "fk_dbo.messagerecipients_dbo.messages_message_messageid" FOREIGN KEY (message_messageid)
    REFERENCES public.messages(messageid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.messagerecipients 
  ADD CONSTRAINT fk_messagerecipients_messages_messageid FOREIGN KEY (messageid)
    REFERENCES public.messages(messageid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."logentries"
--
CREATE TABLE public.logentries(
  id serial,
  "timestamp" timestamp without time zone NOT NULL,
  message citext,
  level citext,
  logger citext
);

--
-- Create primary key
--
ALTER TABLE public.logentries 
  ADD PRIMARY KEY (id);

--
-- Create table "public"."jobs"
--
CREATE TABLE public.jobs(
  jobid serial,
  jobtype integer NOT NULL,
  checkinterval integer NOT NULL,
  starttimestamp timestamp without time zone,
  lastchecktimestamp timestamp without time zone,
  dorestart boolean,
  restartrequestedtimestamp timestamp without time zone,
  lastresettimestamp timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.jobs 
  ADD PRIMARY KEY (jobid);

--
-- Create table "public"."inboundmessageevents"
--
CREATE TABLE public.inboundmessageevents(
  inboundmessageeventid serial,
  messagetype integer NOT NULL,
  customerid citext NOT NULL,
  recievedon timestamp without time zone NOT NULL,
  data citext NOT NULL,
  type citext,
  processed boolean
);

--
-- Create primary key
--
ALTER TABLE public.inboundmessageevents 
  ADD PRIMARY KEY (inboundmessageeventid);

--
-- Create table "public"."auditlogs"
--
CREATE TABLE public.auditlogs(
  auditlogid serial,
  logtype integer NOT NULL,
  departmentid integer NOT NULL,
  userid citext,
  data citext,
  message citext,
  loggedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.auditlogs 
  ADD PRIMARY KEY (auditlogid);

--
-- Create table "public"."affiliates"
--
CREATE TABLE public.affiliates(
  affiliateid serial,
  affiliatemailingaddressid integer,
  affiliatecode citext,
  firstname citext,
  lastname citext,
  emailaddress citext,
  companyordepartment citext,
  country citext,
  region citext,
  experiance citext,
  qualifications citext,
  approved boolean NOT NULL,
  rejectreason citext,
  paypaladdress citext,
  taxidentifier citext,
  active boolean NOT NULL,
  deactivatereason citext,
  timezone citext,
  createdon timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  approvedon timestamp without time zone,
  deactivatedon timestamp without time zone,
  userid uuid,
  rejected boolean NOT NULL DEFAULT false,
  rejectedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.affiliates 
  ADD PRIMARY KEY (affiliateid);

--
-- Create table "public"."addresses"
--
CREATE TABLE public.addresses(
  addressid serial,
  address1 citext NOT NULL,
  city citext NOT NULL,
  state citext NOT NULL,
  postalcode citext,
  country citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.addresses 
  ADD PRIMARY KEY (addressid);

--
-- Create table "public"."departments"
--
CREATE TABLE public.departments(
  departmentid serial,
  name citext NOT NULL,
  code citext,
  managinguserid citext NOT NULL,
  showwelcome boolean NOT NULL,
  createdon timestamp without time zone,
  updatedon timestamp without time zone,
  timezone citext,
  apikey citext,
  departmenttype citext,
  addressid integer,
  publicapikey citext,
  referringdepartmentid integer,
  affiliatecode citext,
  sharedsecret citext,
  use24hourtime boolean,
  linkcode citext
);

--
-- Create primary key
--
ALTER TABLE public.departments 
  ADD PRIMARY KEY (departmentid);

--
-- Create foreign key
--
ALTER TABLE public.departments 
  ADD CONSTRAINT "fk_dbo.departments_dbo.addresses_addressid" FOREIGN KEY (addressid)
    REFERENCES public.addresses(addressid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."unittypes"
--
CREATE TABLE public.unittypes(
  unittypeid serial,
  departmentid integer NOT NULL,
  type citext NOT NULL,
  customstatesid integer
);

--
-- Create primary key
--
ALTER TABLE public.unittypes 
  ADD PRIMARY KEY (unittypeid);

--
-- Create foreign key
--
ALTER TABLE public.unittypes 
  ADD CONSTRAINT "fk_dbo.unittypes_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."trainings"
--
CREATE TABLE public.trainings(
  trainingid serial,
  departmentid integer NOT NULL,
  createdbyuserid citext NOT NULL,
  name citext NOT NULL,
  description citext NOT NULL,
  trainingtext citext NOT NULL,
  minimumscore double precision NOT NULL,
  createdon timestamp without time zone NOT NULL,
  userstoadd citext,
  groupstoadd citext,
  rolestoadd citext,
  tobecompletedby timestamp without time zone,
  notified timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.trainings 
  ADD PRIMARY KEY (trainingid);

--
-- Create foreign key
--
ALTER TABLE public.trainings 
  ADD CONSTRAINT "fk_dbo.trainings_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."trainingusers"
--
CREATE TABLE public.trainingusers(
  traininguserid serial,
  trainingid integer NOT NULL,
  userid citext NOT NULL,
  complete boolean NOT NULL,
  score double precision NOT NULL,
  viewed boolean NOT NULL DEFAULT false,
  viewedon timestamp without time zone,
  completedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.trainingusers 
  ADD PRIMARY KEY (traininguserid);

--
-- Create foreign key
--
ALTER TABLE public.trainingusers 
  ADD CONSTRAINT "fk_dbo.trainingusers_dbo.trainings_trainingid" FOREIGN KEY (trainingid)
    REFERENCES public.trainings(trainingid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."trainingquestions"
--
CREATE TABLE public.trainingquestions(
  trainingquestionid serial,
  trainingid integer NOT NULL,
  question citext
);

--
-- Create primary key
--
ALTER TABLE public.trainingquestions 
  ADD PRIMARY KEY (trainingquestionid);

--
-- Create foreign key
--
ALTER TABLE public.trainingquestions 
  ADD CONSTRAINT "fk_dbo.trainingquestions_dbo.trainings_trainingid" FOREIGN KEY (trainingid)
    REFERENCES public.trainings(trainingid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."trainingquestionanswers"
--
CREATE TABLE public.trainingquestionanswers(
  trainingquestionanswerid serial,
  trainingquestionid integer NOT NULL,
  answer citext,
  correct boolean NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.trainingquestionanswers 
  ADD PRIMARY KEY (trainingquestionanswerid);

--
-- Create foreign key
--
ALTER TABLE public.trainingquestionanswers 
  ADD CONSTRAINT "fk_dbo.trainingquestionanswers_dbo.trainingquestions_trainingqu" FOREIGN KEY (trainingquestionid)
    REFERENCES public.trainingquestions(trainingquestionid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."trainingattachments"
--
CREATE TABLE public.trainingattachments(
  trainingattachmentid serial,
  trainingid integer NOT NULL,
  filename citext,
  data bytea,
  filetype citext
);

--
-- Create primary key
--
ALTER TABLE public.trainingattachments 
  ADD PRIMARY KEY (trainingattachmentid);

--
-- Create foreign key
--
ALTER TABLE public.trainingattachments 
  ADD CONSTRAINT "fk_dbo.trainingattachments_dbo.trainings_trainingid" FOREIGN KEY (trainingid)
    REFERENCES public.trainings(trainingid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shifts"
--
CREATE TABLE public.shifts(
  shiftid serial,
  name citext,
  code citext,
  scheduletype integer NOT NULL,
  assignmenttype integer NOT NULL,
  color citext,
  startday timestamp without time zone NOT NULL,
  starttime citext,
  departmentid integer NOT NULL DEFAULT 0,
  allowpartials boolean,
  requireapproval boolean,
  endtime citext,
  hours integer
);

--
-- Create primary key
--
ALTER TABLE public.shifts 
  ADD PRIMARY KEY (shiftid);

--
-- Create foreign key
--
ALTER TABLE public.shifts 
  ADD CONSTRAINT "fk_dbo.shifts_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftstaffings"
--
CREATE TABLE public.shiftstaffings(
  shiftstaffingid serial,
  departmentid integer NOT NULL,
  shiftid integer NOT NULL,
  shiftday timestamp without time zone NOT NULL,
  note citext,
  addedbyuserid citext NOT NULL,
  addedon timestamp without time zone NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.shiftstaffings 
  ADD PRIMARY KEY (shiftstaffingid);

--
-- Create foreign key
--
ALTER TABLE public.shiftstaffings 
  ADD CONSTRAINT "fk_dbo.shiftstaffings_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftstaffings 
  ADD CONSTRAINT "fk_dbo.shiftstaffings_dbo.shifts_shiftid" FOREIGN KEY (shiftid)
    REFERENCES public.shifts(shiftid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftdays"
--
CREATE TABLE public.shiftdays(
  shiftdayid serial,
  shiftid integer NOT NULL,
  day timestamp without time zone NOT NULL,
  processed boolean
);

--
-- Create primary key
--
ALTER TABLE public.shiftdays 
  ADD PRIMARY KEY (shiftdayid);

--
-- Create foreign key
--
ALTER TABLE public.shiftdays 
  ADD CONSTRAINT "fk_dbo.shiftdays_dbo.shifts_shiftid" FOREIGN KEY (shiftid)
    REFERENCES public.shifts(shiftid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftadmins"
--
CREATE TABLE public.shiftadmins(
  shiftadminid serial,
  shiftid integer NOT NULL,
  userid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.shiftadmins 
  ADD PRIMARY KEY (shiftadminid);

--
-- Create foreign key
--
ALTER TABLE public.shiftadmins 
  ADD CONSTRAINT "fk_dbo.shiftadmins_dbo.shifts_shiftid" FOREIGN KEY (shiftid)
    REFERENCES public.shifts(shiftid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."resourceorders"
--
CREATE TABLE public.resourceorders(
  resourceorderid serial,
  departmentid integer NOT NULL,
  type integer NOT NULL,
  allowpartialfills boolean NOT NULL,
  title citext,
  incidentnumber citext,
  incidentname citext,
  incidentaddress citext,
  incidentlatitude numeric,
  incidentlongitude numeric,
  summary citext,
  opendate timestamp without time zone NOT NULL,
  neededby timestamp without time zone NOT NULL,
  meetupdate timestamp without time zone,
  closedate timestamp without time zone,
  contactname citext,
  contactnumber citext,
  specialinstructions citext,
  meetuplocation citext,
  financialcode citext,
  automaticfillacceptance boolean NOT NULL,
  visibility integer NOT NULL DEFAULT 0,
  range integer NOT NULL DEFAULT 0,
  originlatitude double precision,
  originlongitude double precision
);

--
-- Create primary key
--
ALTER TABLE public.resourceorders 
  ADD PRIMARY KEY (resourceorderid);

--
-- Create foreign key
--
ALTER TABLE public.resourceorders 
  ADD CONSTRAINT "fk_dbo.resourceorders_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."resourceorderitems"
--
CREATE TABLE public.resourceorderitems(
  resourceorderitemid serial,
  resourceorderid integer NOT NULL,
  resource citext,
  min integer NOT NULL,
  max integer NOT NULL,
  financialcode citext,
  specialneeds citext,
  requirements citext
);

--
-- Create primary key
--
ALTER TABLE public.resourceorderitems 
  ADD PRIMARY KEY (resourceorderitemid);

--
-- Create foreign key
--
ALTER TABLE public.resourceorderitems 
  ADD CONSTRAINT "fk_dbo.resourceorderitems_dbo.resourceorders_resourceorderid" FOREIGN KEY (resourceorderid)
    REFERENCES public.resourceorders(resourceorderid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."ranks"
--
CREATE TABLE public.ranks(
  rankid serial,
  departmentid integer NOT NULL,
  name citext,
  code citext,
  sortweight integer NOT NULL,
  tradeweight integer NOT NULL,
  image bytea,
  color citext
);

--
-- Create primary key
--
ALTER TABLE public.ranks 
  ADD PRIMARY KEY (rankid);

--
-- Create foreign key
--
ALTER TABLE public.ranks 
  ADD CONSTRAINT "fk_dbo.ranks_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentmembers"
--
CREATE TABLE public.departmentmembers(
  departmentmemberid serial,
  departmentid integer NOT NULL,
  userid citext NOT NULL,
  isadmin boolean,
  isdisabled boolean,
  ishidden boolean,
  isdefault boolean NOT NULL DEFAULT false,
  isactive boolean NOT NULL DEFAULT false,
  isdeleted boolean NOT NULL DEFAULT false,
  rankid integer
);

--
-- Create primary key
--
ALTER TABLE public.departmentmembers 
  ADD PRIMARY KEY (departmentmemberid);

--
-- Create foreign key
--
ALTER TABLE public.departmentmembers 
  ADD CONSTRAINT "fk_dbo.departmentmembers_dbo.ranks_rankid" FOREIGN KEY (rankid)
    REFERENCES public.ranks(rankid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentmembers 
  ADD CONSTRAINT fk_departmentmembers_departments_departmentid FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."poitypes"
--
CREATE TABLE public.poitypes(
  poitypeid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  image citext,
  color citext,
  isdestination boolean NOT NULL DEFAULT false,
  marker citext,
  size integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.poitypes 
  ADD PRIMARY KEY (poitypeid);

--
-- Create foreign key
--
ALTER TABLE public.poitypes 
  ADD CONSTRAINT "fk_dbo.poitypes_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."pois"
--
CREATE TABLE public.pois(
  poiid serial,
  poitypeid integer NOT NULL,
  longitude double precision NOT NULL,
  latitude double precision NOT NULL,
  note citext
);

--
-- Create primary key
--
ALTER TABLE public.pois 
  ADD PRIMARY KEY (poiid);

--
-- Create foreign key
--
ALTER TABLE public.pois 
  ADD CONSTRAINT "fk_dbo.pois_dbo.poitypes_poitypeid" FOREIGN KEY (poitypeid)
    REFERENCES public.poitypes(poitypeid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."personnelroles"
--
CREATE TABLE public.personnelroles(
  personnelroleid serial,
  name citext NOT NULL,
  description citext,
  departmentid integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.personnelroles 
  ADD PRIMARY KEY (personnelroleid);

--
-- Create foreign key
--
ALTER TABLE public.personnelroles 
  ADD CONSTRAINT "fk_dbo.personnelroles_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."resourceordersettings"
--
CREATE TABLE public.resourceordersettings(
  resourceordersettingid serial,
  departmentid integer NOT NULL,
  visibility integer NOT NULL,
  donotreceiveorders boolean NOT NULL,
  roleallowedtofulfilordersroleid integer,
  limitstaffingleveltoreceivenotifications boolean NOT NULL,
  allowedstaffingleveltoreceivenotifications integer NOT NULL,
  defaultresourceordermanageruserid citext,
  latitude numeric,
  longitude numeric,
  range integer NOT NULL,
  boundrygeofence citext,
  targetdepartmenttype citext,
  automaticfillacceptance boolean NOT NULL,
  importemailcode citext,
  notifyusers boolean NOT NULL,
  useridstonotifyonorders citext
);

--
-- Create primary key
--
ALTER TABLE public.resourceordersettings 
  ADD PRIMARY KEY (resourceordersettingid);

--
-- Create foreign key
--
ALTER TABLE public.resourceordersettings 
  ADD CONSTRAINT "fk_dbo.resourceordersettings_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.resourceordersettings 
  ADD CONSTRAINT "fk_dbo.resourceordersettings_dbo.personnelroles_roleallowedtofu" FOREIGN KEY (roleallowedtofulfilordersroleid)
    REFERENCES public.personnelroles(personnelroleid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."personnelroleusers"
--
CREATE TABLE public.personnelroleusers(
  personnelroleuserid serial,
  personnelroleid integer NOT NULL,
  userid citext NOT NULL,
  departmentid integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.personnelroleusers 
  ADD PRIMARY KEY (personnelroleuserid);

--
-- Create foreign key
--
ALTER TABLE public.personnelroleusers 
  ADD CONSTRAINT "fk_dbo.personnelroleusers_dbo.personnelroles_personnelroleid" FOREIGN KEY (personnelroleid)
    REFERENCES public.personnelroles(personnelroleid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."personnelcertifications"
--
CREATE TABLE public.personnelcertifications(
  personnelcertificationid serial,
  departmentid integer NOT NULL,
  userid citext NOT NULL,
  name citext NOT NULL,
  number citext,
  type citext,
  area citext,
  expireson timestamp without time zone,
  recievedon timestamp without time zone,
  issuedby citext,
  filetype citext,
  filename citext,
  data bytea
);

--
-- Create primary key
--
ALTER TABLE public.personnelcertifications 
  ADD PRIMARY KEY (personnelcertificationid);

--
-- Create foreign key
--
ALTER TABLE public.personnelcertifications 
  ADD CONSTRAINT "fk_dbo.personnelcertifications_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."permissions"
--
CREATE TABLE public.permissions(
  permissionid serial,
  departmentid integer NOT NULL,
  permissiontype integer NOT NULL,
  action integer NOT NULL,
  data citext,
  updatedby citext,
  updatedon timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  locktogroup boolean NOT NULL DEFAULT false
);

--
-- Create primary key
--
ALTER TABLE public.permissions 
  ADD PRIMARY KEY (permissionid);

--
-- Create foreign key
--
ALTER TABLE public.permissions 
  ADD CONSTRAINT "fk_dbo.permissions_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."payments"
--
CREATE TABLE public.payments(
  paymentid serial,
  departmentid integer NOT NULL,
  planid integer NOT NULL,
  method integer NOT NULL,
  istrial boolean NOT NULL,
  purchaseon timestamp without time zone NOT NULL,
  purchasinguserid citext NOT NULL,
  transactionid citext,
  successful boolean NOT NULL,
  data citext,
  isupgrade boolean NOT NULL DEFAULT false,
  description citext,
  effectiveon timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  amount double precision NOT NULL DEFAULT '0'::double precision,
  payment_paymentid integer,
  endingon timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  cancelled boolean NOT NULL DEFAULT false,
  cancelledon timestamp without time zone,
  cancelleddata citext,
  upgradedpaymentid integer,
  subscriptionid citext
);

--
-- Create primary key
--
ALTER TABLE public.payments 
  ADD PRIMARY KEY (paymentid);

--
-- Create foreign key
--
ALTER TABLE public.payments 
  ADD CONSTRAINT "fk_dbo.payments_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.payments 
  ADD CONSTRAINT "fk_dbo.payments_dbo.payments_payment_paymentid" FOREIGN KEY (payment_paymentid)
    REFERENCES public.payments(paymentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.payments 
  ADD CONSTRAINT "fk_dbo.payments_dbo.plans_planid" FOREIGN KEY (planid)
    REFERENCES public.plans(planid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."notes"
--
CREATE TABLE public.notes(
  noteid serial,
  departmentid integer NOT NULL,
  userid citext,
  title citext NOT NULL,
  body citext NOT NULL,
  isadminonly boolean NOT NULL,
  color citext,
  expireson timestamp without time zone,
  addedon timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  catery citext,
  startson timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.notes 
  ADD PRIMARY KEY (noteid);

--
-- Create foreign key
--
ALTER TABLE public.notes 
  ADD CONSTRAINT "fk_dbo.notes_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."invites"
--
CREATE TABLE public.invites(
  inviteid serial,
  departmentid integer NOT NULL,
  code uuid NOT NULL,
  emailaddress citext NOT NULL,
  sendinguserid citext NOT NULL,
  senton timestamp without time zone NOT NULL,
  completedon timestamp without time zone,
  completeduserid citext
);

--
-- Create primary key
--
ALTER TABLE public.invites 
  ADD PRIMARY KEY (inviteid);

--
-- Create foreign key
--
ALTER TABLE public.invites 
  ADD CONSTRAINT "fk_dbo.invites_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."inventorytypes"
--
CREATE TABLE public.inventorytypes(
  inventorytypeid serial,
  departmentid integer NOT NULL,
  type citext NOT NULL,
  expiresdays integer NOT NULL,
  description citext,
  unitofmesasure citext
);

--
-- Create primary key
--
ALTER TABLE public.inventorytypes 
  ADD PRIMARY KEY (inventorytypeid);

--
-- Create foreign key
--
ALTER TABLE public.inventorytypes 
  ADD CONSTRAINT "fk_dbo.inventorytypes_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."files"
--
CREATE TABLE public.files(
  fileid serial,
  departmentid integer NOT NULL,
  messageid integer,
  filename citext,
  filetype citext,
  data bytea,
  "timestamp" timestamp without time zone NOT NULL,
  contentid citext
);

--
-- Create primary key
--
ALTER TABLE public.files 
  ADD PRIMARY KEY (fileid);

--
-- Create foreign key
--
ALTER TABLE public.files 
  ADD CONSTRAINT "fk_dbo.files_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.files 
  ADD CONSTRAINT "fk_dbo.files_dbo.messages_messageid" FOREIGN KEY (messageid)
    REFERENCES public.messages(messageid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."documents"
--
CREATE TABLE public.documents(
  documentid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  catery citext,
  description citext,
  adminsonly boolean NOT NULL,
  data bytea NOT NULL,
  userid citext NOT NULL,
  addedon timestamp without time zone NOT NULL,
  removeon timestamp without time zone,
  type citext,
  filename citext
);

--
-- Create primary key
--
ALTER TABLE public.documents 
  ADD PRIMARY KEY (documentid);

--
-- Create foreign key
--
ALTER TABLE public.documents 
  ADD CONSTRAINT "fk_dbo.documents_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."distributionlists"
--
CREATE TABLE public.distributionlists(
  distributionlistid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  isdisabled boolean NOT NULL,
  hostname citext,
  port integer,
  usessl boolean,
  username citext,
  password citext,
  lastcheck timestamp without time zone,
  isfailure boolean NOT NULL,
  errormessage citext,
  emailaddress citext,
  type integer
);

--
-- Create primary key
--
ALTER TABLE public.distributionlists 
  ADD PRIMARY KEY (distributionlistid);

--
-- Create foreign key
--
ALTER TABLE public.distributionlists 
  ADD CONSTRAINT "fk_dbo.distributionlists_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."distributionlistmembers"
--
CREATE TABLE public.distributionlistmembers(
  distributionlistmemberid serial,
  distributionlistid integer NOT NULL,
  userid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.distributionlistmembers 
  ADD PRIMARY KEY (distributionlistmemberid);

--
-- Create foreign key
--
ALTER TABLE public.distributionlistmembers 
  ADD CONSTRAINT "fk_dbo.distributionlistmembers_dbo.distributionlists_distributi" FOREIGN KEY (distributionlistid)
    REFERENCES public.distributionlists(distributionlistid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."dispatchprotocols"
--
CREATE TABLE public.dispatchprotocols(
  dispatchprotocolid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  code citext NOT NULL,
  isdisabled boolean NOT NULL,
  description citext,
  protocoltext citext,
  createdon timestamp without time zone NOT NULL,
  createdbyuserid citext NOT NULL,
  updatedon timestamp without time zone,
  minimumweight integer NOT NULL,
  updatedbyuserid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.dispatchprotocols 
  ADD PRIMARY KEY (dispatchprotocolid);

--
-- Create foreign key
--
ALTER TABLE public.dispatchprotocols 
  ADD CONSTRAINT "fk_dbo.dispatchprotocols_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."dispatchprotocoltriggers"
--
CREATE TABLE public.dispatchprotocoltriggers(
  dispatchprotocoltriggerid serial,
  dispatchprotocolid integer NOT NULL,
  type integer NOT NULL,
  startson timestamp without time zone,
  endson timestamp without time zone,
  priority integer,
  calltype citext,
  geofence citext
);

--
-- Create primary key
--
ALTER TABLE public.dispatchprotocoltriggers 
  ADD PRIMARY KEY (dispatchprotocoltriggerid);

--
-- Create foreign key
--
ALTER TABLE public.dispatchprotocoltriggers 
  ADD CONSTRAINT "fk_dbo.dispatchprotocoltriggers_dbo.dispatchprotocols_dispatchp" FOREIGN KEY (dispatchprotocolid)
    REFERENCES public.dispatchprotocols(dispatchprotocolid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."dispatchprotocolquestions"
--
CREATE TABLE public.dispatchprotocolquestions(
  dispatchprotocolquestionid serial,
  dispatchprotocolid integer NOT NULL,
  question citext
);

--
-- Create primary key
--
ALTER TABLE public.dispatchprotocolquestions 
  ADD PRIMARY KEY (dispatchprotocolquestionid);

--
-- Create foreign key
--
ALTER TABLE public.dispatchprotocolquestions 
  ADD CONSTRAINT "fk_dbo.dispatchprotocolquestions_dbo.dispatchprotocols_dispatch" FOREIGN KEY (dispatchprotocolid)
    REFERENCES public.dispatchprotocols(dispatchprotocolid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."dispatchprotocolquestionanswers"
--
CREATE TABLE public.dispatchprotocolquestionanswers(
  dispatchprotocolquestionanswerid serial,
  dispatchprotocolquestionid integer NOT NULL,
  answer citext,
  weight integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.dispatchprotocolquestionanswers 
  ADD PRIMARY KEY (dispatchprotocolquestionanswerid);

--
-- Create foreign key
--
ALTER TABLE public.dispatchprotocolquestionanswers 
  ADD CONSTRAINT "fk_dbo.dispatchprotocolquestionanswers_dbo.dispatchprotocolques" FOREIGN KEY (dispatchprotocolquestionid)
    REFERENCES public.dispatchprotocolquestions(dispatchprotocolquestionid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."dispatchprotocolattachments"
--
CREATE TABLE public.dispatchprotocolattachments(
  dispatchprotocolattachmentid serial,
  dispatchprotocolid integer NOT NULL,
  filename citext,
  filetype citext,
  data bytea
);

--
-- Create primary key
--
ALTER TABLE public.dispatchprotocolattachments 
  ADD PRIMARY KEY (dispatchprotocolattachmentid);

--
-- Create foreign key
--
ALTER TABLE public.dispatchprotocolattachments 
  ADD CONSTRAINT "fk_dbo.dispatchprotocolattachments_dbo.dispatchprotocols_dispat" FOREIGN KEY (dispatchprotocolid)
    REFERENCES public.dispatchprotocols(dispatchprotocolid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentsettings"
--
CREATE TABLE public.departmentsettings(
  departmentsettingid serial,
  departmentid integer NOT NULL,
  settingtype integer NOT NULL,
  setting citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.departmentsettings 
  ADD PRIMARY KEY (departmentsettingid);

--
-- Create foreign key
--
ALTER TABLE public.departmentsettings 
  ADD CONSTRAINT fk_departmentsettings_departments_departmentid FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentprofiles"
--
CREATE TABLE public.departmentprofiles(
  departmentprofileid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  code citext NOT NULL,
  shortname citext,
  description citext,
  incaseofemergency citext,
  servicearea citext,
  servicesprovided citext,
  founded timestamp without time zone,
  lo bytea,
  keywords citext,
  inviteonly boolean NOT NULL,
  allowmessages boolean NOT NULL,
  volunteerpositionsavailable boolean NOT NULL,
  sharestats boolean NOT NULL,
  volunteerkeywords citext,
  volunteerdescription citext,
  volunteercontactname citext,
  volunteercontactinfo citext,
  geofence citext,
  addressid integer,
  latitude citext,
  longitude citext,
  what3words citext,
  disabled boolean NOT NULL DEFAULT false,
  facebook citext,
  twitter citext,
  ogleplus citext,
  linkedin citext,
  instagram citext,
  youtube citext,
  website citext,
  phonenumber citext
);

--
-- Create primary key
--
ALTER TABLE public.departmentprofiles 
  ADD PRIMARY KEY (departmentprofileid);

--
-- Create foreign key
--
ALTER TABLE public.departmentprofiles 
  ADD CONSTRAINT "fk_dbo.departmentprofiles_dbo.addresses_addressid" FOREIGN KEY (addressid)
    REFERENCES public.addresses(addressid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentprofiles 
  ADD CONSTRAINT "fk_dbo.departmentprofiles_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentprofileinvites"
--
CREATE TABLE public.departmentprofileinvites(
  departmentprofileinviteid serial,
  departmentprofileid integer NOT NULL,
  code citext,
  usedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.departmentprofileinvites 
  ADD PRIMARY KEY (departmentprofileinviteid);

--
-- Create foreign key
--
ALTER TABLE public.departmentprofileinvites 
  ADD CONSTRAINT "fk_dbo.departmentprofileinvites_dbo.departmentprofiles_departme" FOREIGN KEY (departmentprofileid)
    REFERENCES public.departmentprofiles(departmentprofileid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentprofilearticles"
--
CREATE TABLE public.departmentprofilearticles(
  departmentprofilearticleid serial,
  departmentprofileid integer NOT NULL,
  title citext NOT NULL,
  body citext NOT NULL,
  smallimage bytea,
  largeimage bytea,
  createdon timestamp without time zone NOT NULL,
  expireson timestamp without time zone,
  keywords citext,
  createdbyuserid citext NOT NULL,
  starton timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  deleted boolean NOT NULL DEFAULT false
);

--
-- Create primary key
--
ALTER TABLE public.departmentprofilearticles 
  ADD PRIMARY KEY (departmentprofilearticleid);

--
-- Create foreign key
--
ALTER TABLE public.departmentprofilearticles 
  ADD CONSTRAINT "fk_dbo.departmentprofilearticles_dbo.departmentprofiles_departm" FOREIGN KEY (departmentprofileid)
    REFERENCES public.departmentprofiles(departmentprofileid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentnotifications"
--
CREATE TABLE public.departmentnotifications(
  departmentnotificationid serial,
  departmentid integer NOT NULL,
  eventtype integer NOT NULL,
  userstonotify citext,
  rolestonotify citext,
  groupstonotify citext,
  locktogroup boolean NOT NULL,
  selectedgroupsadminsonly boolean NOT NULL DEFAULT false,
  departmentadmins boolean NOT NULL DEFAULT false,
  everyone boolean NOT NULL DEFAULT false,
  disabled boolean NOT NULL DEFAULT false,
  data citext,
  beforedata citext,
  currentdata citext,
  upperlimit integer,
  lowerlimit integer
);

--
-- Create primary key
--
ALTER TABLE public.departmentnotifications 
  ADD PRIMARY KEY (departmentnotificationid);

--
-- Create foreign key
--
ALTER TABLE public.departmentnotifications 
  ADD CONSTRAINT "fk_dbo.departmentnotifications_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentlinks"
--
CREATE TABLE public.departmentlinks(
  departmentlinkid serial,
  departmentid integer NOT NULL,
  departmentcolor citext,
  departmentsharecalls boolean NOT NULL,
  departmentshareunits boolean NOT NULL,
  departmentsharepersonnel boolean NOT NULL,
  linkeddepartmentid integer NOT NULL,
  linkenabled boolean NOT NULL,
  linkeddepartmentcolor citext,
  linkeddepartmentsharecalls boolean NOT NULL,
  linkeddepartmentshareunits boolean NOT NULL,
  linkeddepartmentsharepersonnel boolean NOT NULL,
  linkcreated timestamp without time zone NOT NULL,
  linkaccepted timestamp without time zone,
  departmentshareorders boolean NOT NULL DEFAULT false,
  linkeddepartmentshareorders boolean NOT NULL DEFAULT false
);

--
-- Create primary key
--
ALTER TABLE public.departmentlinks 
  ADD PRIMARY KEY (departmentlinkid);

--
-- Create foreign key
--
ALTER TABLE public.departmentlinks 
  ADD CONSTRAINT "fk_dbo.departmentlinks_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentlinks 
  ADD CONSTRAINT "fk_dbo.departmentlinks_dbo.departments_linkeddepartmentid" FOREIGN KEY (linkeddepartmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentgroups"
--
CREATE TABLE public.departmentgroups(
  departmentgroupid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  type integer,
  addressid integer,
  parentdepartmentgroupid integer,
  geofence citext,
  geofencecolor citext,
  dispatchemail citext,
  messageemail citext,
  latitude citext,
  longitude citext,
  what3words citext,
  dispatchtoprinter boolean NOT NULL DEFAULT false,
  printerdata citext,
  dispatchtofax boolean NOT NULL DEFAULT false,
  faxnumber citext
);

--
-- Create primary key
--
ALTER TABLE public.departmentgroups 
  ADD PRIMARY KEY (departmentgroupid);

--
-- Create foreign key
--
ALTER TABLE public.departmentgroups 
  ADD CONSTRAINT "fk_dbo.departmentgroups_dbo.addresses_addressid" FOREIGN KEY (addressid)
    REFERENCES public.addresses(addressid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentgroups 
  ADD CONSTRAINT "fk_dbo.departmentgroups_dbo.departmentgroups_parentdepartmentgr" FOREIGN KEY (parentdepartmentgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentgroups 
  ADD CONSTRAINT fk_departmentgroups_departments_departmentid FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."units"
--
CREATE TABLE public.units(
  unitid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  type citext,
  stationgroupid integer,
  vin citext,
  platenumber citext,
  fourwheel boolean,
  specialpermit boolean
);

--
-- Create primary key
--
ALTER TABLE public.units 
  ADD PRIMARY KEY (unitid);

--
-- Create foreign key
--
ALTER TABLE public.units 
  ADD CONSTRAINT "fk_dbo.units_dbo.departmentgroups_stationgroupid" FOREIGN KEY (stationgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.units 
  ADD CONSTRAINT "fk_dbo.units_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."unitstates"
--
CREATE TABLE public.unitstates(
  unitstateid serial,
  unitid integer NOT NULL,
  state integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  geolocationdata citext,
  destinationid integer,
  "localtimestamp" timestamp without time zone,
  note citext,
  latitude numeric,
  longitude numeric,
  accuracy numeric,
  altitude numeric,
  altitudeaccuracy numeric,
  speed numeric,
  heading numeric
);

--
-- Create primary key
--
ALTER TABLE public.unitstates 
  ADD PRIMARY KEY (unitstateid);

--
-- Create foreign key
--
ALTER TABLE public.unitstates 
  ADD CONSTRAINT "fk_dbo.unitstates_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."unitstateroles"
--
CREATE TABLE public.unitstateroles(
  unitstateroleid serial,
  unitstateid integer NOT NULL,
  role citext,
  userid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.unitstateroles 
  ADD PRIMARY KEY (unitstateroleid);

--
-- Create foreign key
--
ALTER TABLE public.unitstateroles 
  ADD CONSTRAINT "fk_dbo.unitstateroles_dbo.unitstates_unitstateid" FOREIGN KEY (unitstateid)
    REFERENCES public.unitstates(unitstateid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."unitroles"
--
CREATE TABLE public.unitroles(
  unitroleid serial,
  unitid integer NOT NULL,
  name citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.unitroles 
  ADD PRIMARY KEY (unitroleid);

--
-- Create foreign key
--
ALTER TABLE public.unitroles 
  ADD CONSTRAINT "fk_dbo.unitroles_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."unitlogs"
--
CREATE TABLE public.unitlogs(
  unitlogid serial,
  unitid integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  narrative citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.unitlogs 
  ADD PRIMARY KEY (unitlogid);

--
-- Create foreign key
--
ALTER TABLE public.unitlogs 
  ADD CONSTRAINT "fk_dbo.unitlogs_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."unitlocations"
--
CREATE TABLE public.unitlocations(
  unitlocationid serial,
  unitid integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  latitude numeric,
  longitude numeric,
  accuracy numeric,
  altitude numeric,
  altitudeaccuracy numeric,
  speed numeric,
  heading numeric
);

--
-- Create primary key
--
ALTER TABLE public.unitlocations 
  ADD PRIMARY KEY (unitlocationid);

--
-- Create foreign key
--
ALTER TABLE public.unitlocations 
  ADD CONSTRAINT "fk_dbo.unitlocations_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftstaffingpersons"
--
CREATE TABLE public.shiftstaffingpersons(
  shiftstaffingpersonid serial,
  shiftstaffingid integer NOT NULL,
  userid citext NOT NULL,
  assigned boolean NOT NULL,
  groupid integer
);

--
-- Create primary key
--
ALTER TABLE public.shiftstaffingpersons 
  ADD PRIMARY KEY (shiftstaffingpersonid);

--
-- Create foreign key
--
ALTER TABLE public.shiftstaffingpersons 
  ADD CONSTRAINT "fk_dbo.shiftstaffingpersons_dbo.departmentgroups_groupid" FOREIGN KEY (groupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftstaffingpersons 
  ADD CONSTRAINT "fk_dbo.shiftstaffingpersons_dbo.shiftstaffings_shiftstaffingid" FOREIGN KEY (shiftstaffingid)
    REFERENCES public.shiftstaffings(shiftstaffingid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftsignups"
--
CREATE TABLE public.shiftsignups(
  shiftsignupid serial,
  shiftid integer NOT NULL,
  userid citext NOT NULL,
  signuptimestamp timestamp without time zone NOT NULL,
  shiftday timestamp without time zone NOT NULL,
  denied boolean NOT NULL,
  departmentgroupid integer
);

--
-- Create primary key
--
ALTER TABLE public.shiftsignups 
  ADD PRIMARY KEY (shiftsignupid);

--
-- Create foreign key
--
ALTER TABLE public.shiftsignups 
  ADD CONSTRAINT "fk_dbo.shiftsignups_dbo.departmentgroups_departmentgroupid" FOREIGN KEY (departmentgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftsignups 
  ADD CONSTRAINT "fk_dbo.shiftsignups_dbo.shifts_shiftid" FOREIGN KEY (shiftid)
    REFERENCES public.shifts(shiftid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftsignuptrades"
--
CREATE TABLE public.shiftsignuptrades(
  shiftsignuptradeid serial,
  sourceshiftsignupid integer NOT NULL,
  targetshiftsignupid integer,
  denied boolean NOT NULL,
  userid citext,
  note citext
);

--
-- Create primary key
--
ALTER TABLE public.shiftsignuptrades 
  ADD PRIMARY KEY (shiftsignuptradeid);

--
-- Create foreign key
--
ALTER TABLE public.shiftsignuptrades 
  ADD CONSTRAINT "fk_dbo.shiftsignuptrades_dbo.shiftsignups_sourceshiftsignupid" FOREIGN KEY (sourceshiftsignupid)
    REFERENCES public.shiftsignups(shiftsignupid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftsignuptrades 
  ADD CONSTRAINT "fk_dbo.shiftsignuptrades_dbo.shiftsignups_targetshiftsignupid" FOREIGN KEY (targetshiftsignupid)
    REFERENCES public.shiftsignups(shiftsignupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftsignuptradeusers"
--
CREATE TABLE public.shiftsignuptradeusers(
  shiftsignuptradeuserid serial,
  shiftsignuptradeid integer NOT NULL,
  userid citext NOT NULL,
  declined boolean NOT NULL DEFAULT false,
  reason citext,
  offered boolean NOT NULL DEFAULT false
);

--
-- Create primary key
--
ALTER TABLE public.shiftsignuptradeusers 
  ADD PRIMARY KEY (shiftsignuptradeuserid);

--
-- Create foreign key
--
ALTER TABLE public.shiftsignuptradeusers 
  ADD CONSTRAINT "fk_dbo.shiftsignuptradeusers_dbo.shiftsignuptrades_shiftsignupt" FOREIGN KEY (shiftsignuptradeid)
    REFERENCES public.shiftsignuptrades(shiftsignuptradeid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftsignuptradeusershifts"
--
CREATE TABLE public.shiftsignuptradeusershifts(
  shiftsignuptradeusershiftid serial,
  shiftsignuptradeuserid integer NOT NULL,
  shiftsignupid integer
);

--
-- Create primary key
--
ALTER TABLE public.shiftsignuptradeusershifts 
  ADD PRIMARY KEY (shiftsignuptradeusershiftid);

--
-- Create foreign key
--
ALTER TABLE public.shiftsignuptradeusershifts 
  ADD CONSTRAINT "fk_dbo.shiftsignuptradeusershifts_dbo.shiftsignups_shiftsignupi" FOREIGN KEY (shiftsignupid)
    REFERENCES public.shiftsignups(shiftsignupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftsignuptradeusershifts 
  ADD CONSTRAINT "fk_dbo.shiftsignuptradeusershifts_dbo.shiftsignuptradeusers_shi" FOREIGN KEY (shiftsignuptradeuserid)
    REFERENCES public.shiftsignuptradeusers(shiftsignuptradeuserid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftpersons"
--
CREATE TABLE public.shiftpersons(
  shiftpersonid serial,
  shiftid integer NOT NULL,
  userid citext NOT NULL,
  groupid integer
);

--
-- Create primary key
--
ALTER TABLE public.shiftpersons 
  ADD PRIMARY KEY (shiftpersonid);

--
-- Create foreign key
--
ALTER TABLE public.shiftpersons 
  ADD CONSTRAINT "fk_dbo.shiftpersons_dbo.departmentgroups_groupid" FOREIGN KEY (groupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftpersons 
  ADD CONSTRAINT "fk_dbo.shiftpersons_dbo.shifts_shiftid" FOREIGN KEY (shiftid)
    REFERENCES public.shifts(shiftid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftgroups"
--
CREATE TABLE public.shiftgroups(
  shiftgroupid serial,
  shiftid integer NOT NULL,
  departmentgroupid integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.shiftgroups 
  ADD PRIMARY KEY (shiftgroupid);

--
-- Create foreign key
--
ALTER TABLE public.shiftgroups 
  ADD CONSTRAINT "fk_dbo.shiftgroups_dbo.departmentgroups_departmentgroupid" FOREIGN KEY (departmentgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftgroups 
  ADD CONSTRAINT "fk_dbo.shiftgroups_dbo.shifts_shiftid" FOREIGN KEY (shiftid)
    REFERENCES public.shifts(shiftid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftgrouproles"
--
CREATE TABLE public.shiftgrouproles(
  shiftgrouproleid serial,
  shiftgroupid integer NOT NULL,
  personnelroleid integer NOT NULL,
  required integer NOT NULL,
  optional integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.shiftgrouproles 
  ADD PRIMARY KEY (shiftgrouproleid);

--
-- Create foreign key
--
ALTER TABLE public.shiftgrouproles 
  ADD CONSTRAINT "fk_dbo.shiftgrouproles_dbo.personnelroles_personnelroleid" FOREIGN KEY (personnelroleid)
    REFERENCES public.personnelroles(personnelroleid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.shiftgrouproles 
  ADD CONSTRAINT "fk_dbo.shiftgrouproles_dbo.shiftgroups_shiftgroupid" FOREIGN KEY (shiftgroupid)
    REFERENCES public.shiftgroups(shiftgroupid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."shiftgroupassignments"
--
CREATE TABLE public.shiftgroupassignments(
  shiftgroupassignmentid serial,
  shiftgroupid integer NOT NULL,
  userid citext NOT NULL,
  assigned boolean NOT NULL,
  shiftday timestamp without time zone NOT NULL,
  "timestamp" timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  assignedbyuserid uuid NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.shiftgroupassignments 
  ADD PRIMARY KEY (shiftgroupassignmentid);

--
-- Create foreign key
--
ALTER TABLE public.shiftgroupassignments 
  ADD CONSTRAINT "fk_dbo.shiftgroupassignments_dbo.shiftgroups_shiftgroupid" FOREIGN KEY (shiftgroupid)
    REFERENCES public.shiftgroups(shiftgroupid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."notificationalerts"
--
CREATE TABLE public.notificationalerts(
  notificationalertid serial,
  departmentid integer NOT NULL,
  departmentgroupid integer,
  eventtype integer NOT NULL,
  opened timestamp without time zone NOT NULL,
  closed timestamp without time zone,
  manuallyclosed boolean NOT NULL,
  data citext,
  manualnote citext
);

--
-- Create primary key
--
ALTER TABLE public.notificationalerts 
  ADD PRIMARY KEY (notificationalertid);

--
-- Create foreign key
--
ALTER TABLE public.notificationalerts 
  ADD CONSTRAINT "fk_dbo.notificationalerts_dbo.departmentgroups_departmentgroupi" FOREIGN KEY (departmentgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.notificationalerts 
  ADD CONSTRAINT "fk_dbo.notificationalerts_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentgroupmembers"
--
CREATE TABLE public.departmentgroupmembers(
  departmentgroupmemberid serial,
  departmentgroupid integer NOT NULL,
  userid citext NOT NULL,
  isadmin boolean,
  departmentid integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.departmentgroupmembers 
  ADD PRIMARY KEY (departmentgroupmemberid);

--
-- Create foreign key
--
ALTER TABLE public.departmentgroupmembers 
  ADD CONSTRAINT fk_departmentgroupmembers_departmentgroups_departmentgroupid FOREIGN KEY (departmentgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentfiles"
--
CREATE TABLE public.departmentfiles(
  departmentfileid uuid NOT NULL,
  departmentid integer NOT NULL,
  type integer NOT NULL,
  filename citext NOT NULL,
  data bytea NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.departmentfiles 
  ADD PRIMARY KEY (departmentfileid);

--
-- Create foreign key
--
ALTER TABLE public.departmentfiles 
  ADD CONSTRAINT "fk_dbo.departmentfiles_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentcertificationtypes"
--
CREATE TABLE public.departmentcertificationtypes(
  departmentcertificationtypeid serial,
  departmentid integer NOT NULL,
  type citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.departmentcertificationtypes 
  ADD PRIMARY KEY (departmentcertificationtypeid);

--
-- Create foreign key
--
ALTER TABLE public.departmentcertificationtypes 
  ADD CONSTRAINT "fk_dbo.departmentcertificationtypes_dbo.departments_departmenti" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentcallpruning"
--
CREATE TABLE public.departmentcallpruning(
  departmentcallpruningid serial,
  departmentid integer NOT NULL,
  pruneuserenteredcalls boolean,
  usercallpruneinterval integer,
  pruneemailimportedcalls boolean,
  emailimportcallpruneinterval integer
);

--
-- Create primary key
--
ALTER TABLE public.departmentcallpruning 
  ADD PRIMARY KEY (departmentcallpruningid);

--
-- Create foreign key
--
ALTER TABLE public.departmentcallpruning 
  ADD CONSTRAINT "fk_dbo.departmentcallpruning_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentcallpriorities"
--
CREATE TABLE public.departmentcallpriorities(
  departmentcallpriorityid serial,
  departmentid integer NOT NULL,
  name citext,
  color citext,
  sort integer NOT NULL,
  isdeleted boolean NOT NULL,
  isdefault boolean NOT NULL,
  pushnotificationsound bytea,
  shortnotificationsound bytea,
  dispatchpersonnel boolean NOT NULL DEFAULT false,
  dispatchunits boolean NOT NULL DEFAULT false,
  forcenotifyallpersonnel boolean NOT NULL DEFAULT false,
  iospushnotificationsound bytea,
  tone integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.departmentcallpriorities 
  ADD PRIMARY KEY (departmentcallpriorityid);

--
-- Create foreign key
--
ALTER TABLE public.departmentcallpriorities 
  ADD CONSTRAINT "fk_dbo.departmentcallpriorities_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentcallemails"
--
CREATE TABLE public.departmentcallemails(
  departmentcallemailid serial,
  departmentid integer NOT NULL,
  hostname citext,
  port integer NOT NULL,
  usessl boolean NOT NULL,
  username citext,
  password citext,
  lastcheck timestamp without time zone,
  isfailure boolean NOT NULL DEFAULT false,
  errormessage citext,
  formattype integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.departmentcallemails 
  ADD PRIMARY KEY (departmentcallemailid);

--
-- Create foreign key
--
ALTER TABLE public.departmentcallemails 
  ADD CONSTRAINT "fk_dbo.departmentcallemails_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."customstates"
--
CREATE TABLE public.customstates(
  departmentid integer NOT NULL,
  type integer NOT NULL,
  name citext NOT NULL,
  description citext,
  isdeleted boolean NOT NULL DEFAULT false,
  customstateid serial
);

--
-- Create primary key
--
ALTER TABLE public.customstates 
  ADD PRIMARY KEY (customstateid);

--
-- Create foreign key
--
ALTER TABLE public.customstates 
  ADD CONSTRAINT "fk_dbo.customstates_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."customstatedetails"
--
CREATE TABLE public.customstatedetails(
  customstatedetailid serial,
  customstateid integer NOT NULL,
  buttontext citext NOT NULL,
  buttoncolor citext NOT NULL,
  gpsrequired boolean NOT NULL,
  notetype integer NOT NULL,
  detailtype integer NOT NULL,
  isdeleted boolean NOT NULL,
  textcolor citext,
  "order" integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.customstatedetails 
  ADD PRIMARY KEY (customstatedetailid);

--
-- Create foreign key
--
ALTER TABLE public.customstatedetails 
  ADD CONSTRAINT "fk_dbo.customstatedetails_dbo.customstates_customstateid" FOREIGN KEY (customstateid)
    REFERENCES public.customstates(customstateid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calltypes"
--
CREATE TABLE public.calltypes(
  calltypeid serial,
  departmentid integer NOT NULL,
  type citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.calltypes 
  ADD PRIMARY KEY (calltypeid);

--
-- Create foreign key
--
ALTER TABLE public.calltypes 
  ADD CONSTRAINT "fk_dbo.calltypes_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."commanddefinitions"
--
CREATE TABLE public.commanddefinitions(
  commanddefinitionid serial,
  departmentid integer NOT NULL,
  calltypeid integer,
  timer boolean NOT NULL,
  timerminutes integer NOT NULL,
  name citext,
  description citext
);

--
-- Create primary key
--
ALTER TABLE public.commanddefinitions 
  ADD PRIMARY KEY (commanddefinitionid);

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitions 
  ADD CONSTRAINT "fk_dbo.commanddefinitions_dbo.calltypes_calltypeid" FOREIGN KEY (calltypeid)
    REFERENCES public.calltypes(calltypeid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitions 
  ADD CONSTRAINT "fk_dbo.commanddefinitions_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."commanddefinitionroles"
--
CREATE TABLE public.commanddefinitionroles(
  commanddefinitionroleid serial,
  commanddefinitionid integer NOT NULL,
  name citext,
  description citext,
  minunitpersonnel integer NOT NULL,
  maxunitpersonnel integer NOT NULL,
  maxunits integer NOT NULL,
  mintimeinrole integer NOT NULL,
  maxtimeinrole integer NOT NULL,
  forcerequirements boolean NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.commanddefinitionroles 
  ADD PRIMARY KEY (commanddefinitionroleid);

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionroles 
  ADD CONSTRAINT "fk_dbo.commanddefinitionroles_dbo.commanddefinitions_commanddef" FOREIGN KEY (commanddefinitionid)
    REFERENCES public.commanddefinitions(commanddefinitionid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."commanddefinitionroleunittypes"
--
CREATE TABLE public.commanddefinitionroleunittypes(
  commanddefinitionroleunittypeid serial,
  commanddefinitionroleid integer NOT NULL,
  unittypeid integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.commanddefinitionroleunittypes 
  ADD PRIMARY KEY (commanddefinitionroleunittypeid);

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionroleunittypes 
  ADD CONSTRAINT "fk_dbo.commanddefinitionroleunittypes_dbo.commanddefinitionrole" FOREIGN KEY (commanddefinitionroleid)
    REFERENCES public.commanddefinitionroles(commanddefinitionroleid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionroleunittypes 
  ADD CONSTRAINT "fk_dbo.commanddefinitionroleunittypes_dbo.unittypes_unittypeid" FOREIGN KEY (unittypeid)
    REFERENCES public.unittypes(unittypeid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."commanddefinitionrolepersonnelroles"
--
CREATE TABLE public.commanddefinitionrolepersonnelroles(
  commanddefinitionrolepersonnelroleid serial,
  commanddefinitionroleid integer NOT NULL,
  personnelroleid integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.commanddefinitionrolepersonnelroles 
  ADD PRIMARY KEY (commanddefinitionrolepersonnelroleid);

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionrolepersonnelroles 
  ADD CONSTRAINT "fk_dbo.commanddefinitionrolepersonnelroles_dbo.commanddefinitio" FOREIGN KEY (commanddefinitionroleid)
    REFERENCES public.commanddefinitionroles(commanddefinitionroleid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionrolepersonnelroles 
  ADD CONSTRAINT "fk_dbo.commanddefinitionrolepersonnelroles_dbo.personnelroles_p" FOREIGN KEY (personnelroleid)
    REFERENCES public.personnelroles(personnelroleid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."commanddefinitionrolecerts"
--
CREATE TABLE public.commanddefinitionrolecerts(
  commanddefinitionrolecertid serial,
  commanddefinitionroleid integer NOT NULL,
  departmentcertificationtypeid integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.commanddefinitionrolecerts 
  ADD PRIMARY KEY (commanddefinitionrolecertid);

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionrolecerts 
  ADD CONSTRAINT "fk_dbo.commanddefinitionrolecerts_dbo.commanddefinitionroles_co" FOREIGN KEY (commanddefinitionroleid)
    REFERENCES public.commanddefinitionroles(commanddefinitionroleid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.commanddefinitionrolecerts 
  ADD CONSTRAINT "fk_dbo.commanddefinitionrolecerts_dbo.departmentcertificationty" FOREIGN KEY (departmentcertificationtypeid)
    REFERENCES public.departmentcertificationtypes(departmentcertificationtypeid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calls"
--
CREATE TABLE public.calls(
  callid serial,
  departmentid integer NOT NULL,
  reportinguserid citext NOT NULL,
  name citext NOT NULL,
  natureofcall citext NOT NULL,
  notes citext,
  loggedon timestamp without time zone NOT NULL,
  priority integer NOT NULL DEFAULT 0,
  iscritical boolean NOT NULL DEFAULT false,
  mappage citext,
  completednotes citext,
  address citext,
  geolocationdata citext,
  closedbyuserid citext,
  closedon timestamp without time zone,
  state integer NOT NULL DEFAULT 0,
  isdeleted boolean NOT NULL DEFAULT false,
  type citext,
  incidentnumber citext,
  callsource integer NOT NULL DEFAULT 0,
  sourceidentifier citext,
  number citext,
  dispatchcount integer NOT NULL DEFAULT 0,
  lastdispatchedon timestamp without time zone,
  w3w citext,
  contactname citext,
  contactnumber citext,
  public boolean NOT NULL DEFAULT false,
  externalidentifier citext,
  referencenumber citext
);

--
-- Create primary key
--
ALTER TABLE public.calls 
  ADD PRIMARY KEY (callid);

--
-- Create foreign key
--
ALTER TABLE public.calls 
  ADD CONSTRAINT fk_calls_departments_departmentid FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."incidents"
--
CREATE TABLE public.incidents(
  incidentid serial,
  callid integer NOT NULL,
  commanddefinitionid integer NOT NULL DEFAULT 0,
  name citext,
  start timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  "end" timestamp without time zone,
  state integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.incidents 
  ADD PRIMARY KEY (incidentid);

--
-- Create foreign key
--
ALTER TABLE public.incidents 
  ADD CONSTRAINT "fk_dbo.incidents_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.incidents 
  ADD CONSTRAINT "fk_dbo.incidents_dbo.commanddefinitions_commanddefinitionid" FOREIGN KEY (commanddefinitionid)
    REFERENCES public.commanddefinitions(commanddefinitionid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."incidentlogs"
--
CREATE TABLE public.incidentlogs(
  incidentlogid serial,
  incidentid integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  description citext,
  unitid integer NOT NULL DEFAULT 0,
  type integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.incidentlogs 
  ADD PRIMARY KEY (incidentlogid);

--
-- Create foreign key
--
ALTER TABLE public.incidentlogs 
  ADD CONSTRAINT "fk_dbo.incidentlogs_dbo.incidents_incidentid" FOREIGN KEY (incidentid)
    REFERENCES public.incidents(incidentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.incidentlogs 
  ADD CONSTRAINT "fk_dbo.incidentlogs_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentprofilemessages"
--
CREATE TABLE public.departmentprofilemessages(
  departmentprofilemessageid serial,
  departmentprofileid integer NOT NULL,
  userid citext,
  name citext NOT NULL,
  contactinfo citext NOT NULL,
  message citext NOT NULL,
  senton timestamp without time zone NOT NULL,
  image bytea,
  latitude citext,
  longitude citext,
  response citext,
  closed boolean NOT NULL,
  convertedtocall boolean NOT NULL,
  replytomessageid integer,
  readon timestamp without time zone,
  callid integer,
  spam boolean NOT NULL DEFAULT false,
  deleted boolean NOT NULL DEFAULT false,
  isusersender boolean NOT NULL DEFAULT false,
  conversationid uuid NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.departmentprofilemessages 
  ADD PRIMARY KEY (departmentprofilemessageid);

--
-- Create foreign key
--
ALTER TABLE public.departmentprofilemessages 
  ADD CONSTRAINT "fk_dbo.departmentprofilemessages_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentprofilemessages 
  ADD CONSTRAINT "fk_dbo.departmentprofilemessages_dbo.departmentprofilemessages_" FOREIGN KEY (replytomessageid)
    REFERENCES public.departmentprofilemessages(departmentprofilemessageid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentprofilemessages 
  ADD CONSTRAINT "fk_dbo.departmentprofilemessages_dbo.departmentprofiles_departm" FOREIGN KEY (departmentprofileid)
    REFERENCES public.departmentprofiles(departmentprofileid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."callunits"
--
CREATE TABLE public.callunits(
  callunitid serial,
  callid integer NOT NULL,
  unitid integer NOT NULL,
  dispatchcount integer NOT NULL,
  lastdispatchedon timestamp without time zone,
  unitstateid integer
);

--
-- Create primary key
--
ALTER TABLE public.callunits 
  ADD PRIMARY KEY (callunitid);

--
-- Create foreign key
--
ALTER TABLE public.callunits 
  ADD CONSTRAINT "fk_dbo.callunits_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.callunits 
  ADD CONSTRAINT "fk_dbo.callunits_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.callunits 
  ADD CONSTRAINT "fk_dbo.callunits_dbo.unitstates_unitstateid" FOREIGN KEY (unitstateid)
    REFERENCES public.unitstates(unitstateid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."callprotocols"
--
CREATE TABLE public.callprotocols(
  callprotocolid serial,
  callid integer NOT NULL,
  dispatchprotocolid integer NOT NULL,
  score integer NOT NULL,
  trigger integer NOT NULL,
  data citext
);

--
-- Create primary key
--
ALTER TABLE public.callprotocols 
  ADD PRIMARY KEY (callprotocolid);

--
-- Create foreign key
--
ALTER TABLE public.callprotocols 
  ADD CONSTRAINT "fk_dbo.callprotocols_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.callprotocols 
  ADD CONSTRAINT "fk_dbo.callprotocols_dbo.dispatchprotocols_dispatchprotocolid" FOREIGN KEY (dispatchprotocolid)
    REFERENCES public.dispatchprotocols(dispatchprotocolid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."callnotes"
--
CREATE TABLE public.callnotes(
  callnoteid serial,
  callid integer NOT NULL,
  userid citext NOT NULL,
  note citext,
  source integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  latitude numeric,
  longitude numeric
);

--
-- Create primary key
--
ALTER TABLE public.callnotes 
  ADD PRIMARY KEY (callnoteid);

--
-- Create foreign key
--
ALTER TABLE public.callnotes 
  ADD CONSTRAINT "fk_dbo.callnotes_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calllogs"
--
CREATE TABLE public.calllogs(
  calllogid serial,
  departmentid integer NOT NULL,
  narrative citext NOT NULL,
  callid integer NOT NULL,
  loggedon timestamp without time zone NOT NULL,
  loggedbyuserid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.calllogs 
  ADD PRIMARY KEY (calllogid);

--
-- Create foreign key
--
ALTER TABLE public.calllogs 
  ADD CONSTRAINT "fk_dbo.calllogs_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.calllogs 
  ADD CONSTRAINT "fk_dbo.calllogs_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calldispatchunits"
--
CREATE TABLE public.calldispatchunits(
  calldispatchunitid serial,
  callid integer NOT NULL,
  unitid integer NOT NULL,
  dispatchcount integer NOT NULL,
  lastdispatchedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.calldispatchunits 
  ADD PRIMARY KEY (calldispatchunitid);

--
-- Create foreign key
--
ALTER TABLE public.calldispatchunits 
  ADD CONSTRAINT "fk_dbo.calldispatchunits_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.calldispatchunits 
  ADD CONSTRAINT "fk_dbo.calldispatchunits_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calldispatchroles"
--
CREATE TABLE public.calldispatchroles(
  calldispatchroleid serial,
  callid integer NOT NULL,
  roleid integer NOT NULL,
  dispatchcount integer NOT NULL,
  lastdispatchedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.calldispatchroles 
  ADD PRIMARY KEY (calldispatchroleid);

--
-- Create foreign key
--
ALTER TABLE public.calldispatchroles 
  ADD CONSTRAINT "fk_dbo.calldispatchroles_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.calldispatchroles 
  ADD CONSTRAINT "fk_dbo.calldispatchroles_dbo.personnelroles_roleid" FOREIGN KEY (roleid)
    REFERENCES public.personnelroles(personnelroleid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calldispatchgroups"
--
CREATE TABLE public.calldispatchgroups(
  calldispatchgroupid serial,
  callid integer NOT NULL,
  departmentgroupid integer NOT NULL,
  dispatchcount integer NOT NULL,
  lastdispatchedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.calldispatchgroups 
  ADD PRIMARY KEY (calldispatchgroupid);

--
-- Create foreign key
--
ALTER TABLE public.calldispatchgroups 
  ADD CONSTRAINT "fk_dbo.calldispatchgroups_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.calldispatchgroups 
  ADD CONSTRAINT "fk_dbo.calldispatchgroups_dbo.departmentgroups_departmentgroupi" FOREIGN KEY (departmentgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."callattachments"
--
CREATE TABLE public.callattachments(
  callattachmentid serial,
  callid integer NOT NULL,
  callattachmenttype integer NOT NULL,
  data bytea,
  filename citext,
  userid citext,
  "timestamp" timestamp without time zone,
  name citext,
  size integer,
  latitude numeric,
  longitude numeric
);

--
-- Create primary key
--
ALTER TABLE public.callattachments 
  ADD PRIMARY KEY (callattachmentid);

--
-- Create foreign key
--
ALTER TABLE public.callattachments 
  ADD CONSTRAINT "fk_dbo.callattachments_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calendaritemtypes"
--
CREATE TABLE public.calendaritemtypes(
  calendaritemtypeid serial,
  departmentid integer NOT NULL,
  name citext NOT NULL,
  color citext
);

--
-- Create primary key
--
ALTER TABLE public.calendaritemtypes 
  ADD PRIMARY KEY (calendaritemtypeid);

--
-- Create foreign key
--
ALTER TABLE public.calendaritemtypes 
  ADD CONSTRAINT "fk_dbo.calendaritemtypes_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calendaritems"
--
CREATE TABLE public.calendaritems(
  calendaritemid serial,
  departmentid integer NOT NULL,
  title citext NOT NULL,
  start timestamp without time zone NOT NULL,
  "end" timestamp without time zone NOT NULL,
  starttimezone citext,
  endtimezone citext,
  description citext,
  recurrenceid citext,
  recurrencerule citext,
  recurrenceexception citext,
  itemtype integer NOT NULL,
  isallday boolean NOT NULL DEFAULT false,
  location citext,
  signuptype integer NOT NULL DEFAULT 0,
  reminder integer NOT NULL DEFAULT 0,
  lockediting boolean NOT NULL DEFAULT false,
  entities citext,
  requiredattendes citext,
  optionalattendes citext,
  remindersent boolean NOT NULL DEFAULT false,
  creatoruserid citext,
  public boolean NOT NULL DEFAULT false,
  isv2schedule boolean NOT NULL DEFAULT false,
  recurrencetype integer NOT NULL DEFAULT 0,
  recurrenceend timestamp without time zone,
  sunday boolean NOT NULL DEFAULT false,
  monday boolean NOT NULL DEFAULT false,
  tuesday boolean NOT NULL DEFAULT false,
  wednesday boolean NOT NULL DEFAULT false,
  thursday boolean NOT NULL DEFAULT false,
  friday boolean NOT NULL DEFAULT false,
  saturday boolean NOT NULL DEFAULT false,
  repeatonday integer NOT NULL DEFAULT 0,
  repeatonweek integer NOT NULL DEFAULT 0,
  repeatonmonth integer NOT NULL DEFAULT 0
);

--
-- Create primary key
--
ALTER TABLE public.calendaritems 
  ADD PRIMARY KEY (calendaritemid);

--
-- Create foreign key
--
ALTER TABLE public.calendaritems 
  ADD CONSTRAINT "fk_dbo.calendaritems_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calendaritemattendees"
--
CREATE TABLE public.calendaritemattendees(
  calendaritemattendeeid serial,
  calendaritemid integer NOT NULL,
  userid citext NOT NULL,
  attendeetype integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  note citext
);

--
-- Create primary key
--
ALTER TABLE public.calendaritemattendees 
  ADD PRIMARY KEY (calendaritemattendeeid);

--
-- Create foreign key
--
ALTER TABLE public.calendaritemattendees 
  ADD CONSTRAINT "fk_dbo.calendaritemattendees_dbo.calendaritems_calendaritemid" FOREIGN KEY (calendaritemid)
    REFERENCES public.calendaritems(calendaritemid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."automations"
--
CREATE TABLE public.automations(
  automationid serial,
  departmentid integer NOT NULL,
  automationtype integer NOT NULL,
  isdisabled boolean NOT NULL,
  targettype citext,
  groupid integer,
  data citext,
  createdbyuserid citext,
  createdon timestamp without time zone NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.automations 
  ADD PRIMARY KEY (automationid);

--
-- Create foreign key
--
ALTER TABLE public.automations 
  ADD CONSTRAINT "fk_dbo.automations_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."actionlogs"
--
CREATE TABLE public.actionlogs(
  actionlogid serial,
  userid citext NOT NULL,
  departmentid integer NOT NULL,
  actiontypeid integer NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  geolocationdata citext,
  destinationid integer,
  destinationtype integer,
  note citext
);

--
-- Create primary key
--
ALTER TABLE public.actionlogs 
  ADD PRIMARY KEY (actionlogid);

--
-- Create foreign key
--
ALTER TABLE public.actionlogs 
  ADD CONSTRAINT fk_actionlogs_departments_departmentid FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."calldispatches"
--
CREATE TABLE public.calldispatches(
  calldispatchid serial,
  callid integer NOT NULL,
  userid citext NOT NULL,
  groupid integer,
  actionlogid integer,
  dispatchcount integer NOT NULL DEFAULT 0,
  lastdispatchedon timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.calldispatches 
  ADD PRIMARY KEY (calldispatchid);

--
-- Create foreign key
--
ALTER TABLE public.calldispatches 
  ADD CONSTRAINT "fk_dbo.calldispatches_dbo.actionlogs_actionlogid" FOREIGN KEY (actionlogid)
    REFERENCES public.actionlogs(actionlogid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.calldispatches 
  ADD CONSTRAINT "fk_dbo.calldispatches_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."departmentprofileusers"
--
CREATE TABLE public.departmentprofileusers(
  departmentprofileuserid citext NOT NULL,
  identity citext NOT NULL,
  name citext NOT NULL,
  email citext
);

--
-- Create primary key
--
ALTER TABLE public.departmentprofileusers 
  ADD PRIMARY KEY (departmentprofileuserid);

--
-- Create table "public"."departmentprofileuserfollows"
--
CREATE TABLE public.departmentprofileuserfollows(
  departmentprofileuserfollowid serial,
  departmentprofileuserid citext NOT NULL,
  departmentprofileid integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.departmentprofileuserfollows 
  ADD PRIMARY KEY (departmentprofileuserfollowid);

--
-- Create foreign key
--
ALTER TABLE public.departmentprofileuserfollows 
  ADD CONSTRAINT "fk_dbo.departmentprofileuserfollows_dbo.departmentprofiles_depa" FOREIGN KEY (departmentprofileid)
    REFERENCES public.departmentprofiles(departmentprofileid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.departmentprofileuserfollows 
  ADD CONSTRAINT "fk_dbo.departmentprofileuserfollows_dbo.departmentprofileusers_" FOREIGN KEY (departmentprofileuserid)
    REFERENCES public.departmentprofileusers(departmentprofileuserid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."aspnetusertokens"
--
CREATE TABLE public.aspnetusertokens(
  userid citext NOT NULL,
  loginprovider citext NOT NULL,
  name citext NOT NULL,
  value citext
);

--
-- Create primary key
--
ALTER TABLE public.aspnetusertokens 
  ADD PRIMARY KEY (userid);

--
-- Create table "public"."aspnetusersext"
--
CREATE TABLE public.aspnetusersext(
  userid citext NOT NULL,
  securityquestion citext,
  securityanswer citext,
  securityanswersalt citext,
  createdate timestamp without time zone NOT NULL DEFAULT '1753-01-01 00:00:00'::timestamp without time zone,
  lastactivitydate timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.aspnetusersext 
  ADD PRIMARY KEY (userid);

--
-- Create table "public"."aspnetusers"
--
CREATE TABLE public.aspnetusers(
  id citext NOT NULL,
  username citext,
  normalizedusername citext,
  email citext,
  normalizedemail citext,
  emailconfirmed boolean NOT NULL,
  passwordhash citext,
  securitystamp citext,
  concurrencystamp citext,
  phonenumber citext,
  phonenumberconfirmed boolean NOT NULL,
  twofactorenabled boolean NOT NULL,
  lockoutend timestamp with time zone,
  lockoutenabled boolean,
  accessfailedcount integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.aspnetusers 
  ADD PRIMARY KEY (id);

--
-- Create table "public"."resourceorderfills"
--
CREATE TABLE public.resourceorderfills(
  resourceorderfillid serial,
  departmentid integer NOT NULL,
  resourceorderitemid integer NOT NULL,
  fillinguserid citext,
  note citext,
  contactname citext,
  contactnumber citext,
  filledon timestamp without time zone NOT NULL,
  accepted boolean NOT NULL,
  acceptedon timestamp without time zone,
  leaduserid citext,
  accepteduserid citext
);

--
-- Create primary key
--
ALTER TABLE public.resourceorderfills 
  ADD PRIMARY KEY (resourceorderfillid);

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfills 
  ADD CONSTRAINT "fk_dbo.resourceorderfills_dbo.aspnetusers_accepteduserid" FOREIGN KEY (accepteduserid)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfills 
  ADD CONSTRAINT "fk_dbo.resourceorderfills_dbo.aspnetusers_fillinguserid" FOREIGN KEY (fillinguserid)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfills 
  ADD CONSTRAINT "fk_dbo.resourceorderfills_dbo.aspnetusers_leaduserid" FOREIGN KEY (leaduserid)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfills 
  ADD CONSTRAINT "fk_dbo.resourceorderfills_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfills 
  ADD CONSTRAINT "fk_dbo.resourceorderfills_dbo.resourceorderitems_resourceorderi" FOREIGN KEY (resourceorderitemid)
    REFERENCES public.resourceorderitems(resourceorderitemid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."resourceorderfillunits"
--
CREATE TABLE public.resourceorderfillunits(
  resourceorderfillunitid serial,
  resourceorderfillid integer NOT NULL,
  unitid integer NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.resourceorderfillunits 
  ADD PRIMARY KEY (resourceorderfillunitid);

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfillunits 
  ADD CONSTRAINT "fk_dbo.resourceorderfillunits_dbo.resourceorderfills_resourceor" FOREIGN KEY (resourceorderfillid)
    REFERENCES public.resourceorderfills(resourceorderfillid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.resourceorderfillunits 
  ADD CONSTRAINT "fk_dbo.resourceorderfillunits_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."logs"
--
CREATE TABLE public.logs(
  logid serial,
  departmentid integer NOT NULL,
  narrative citext NOT NULL,
  startedon timestamp without time zone,
  endedon timestamp without time zone,
  loggedon timestamp without time zone NOT NULL,
  loggedbyuserid citext,
  logtype integer,
  externalid citext,
  initialreport citext,
  type citext,
  stationgroupid integer,
  course citext,
  coursecode citext,
  instructors citext,
  cause citext,
  investigatedbyuserid citext,
  contactname citext,
  contactnumber citext,
  officeruserid citext,
  callid integer,
  otherpersonnel citext,
  location citext,
  otheragencies citext,
  otherunits citext,
  bodylocation citext,
  pronounceddeceasedby citext
);

--
-- Create primary key
--
ALTER TABLE public.logs 
  ADD PRIMARY KEY (logid);

--
-- Create foreign key
--
ALTER TABLE public.logs 
  ADD CONSTRAINT "fk_dbo.logs_dbo.aspnetusers_loggedbyuserid" FOREIGN KEY (loggedbyuserid)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.logs 
  ADD CONSTRAINT "fk_dbo.logs_dbo.calls_callid" FOREIGN KEY (callid)
    REFERENCES public.calls(callid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.logs 
  ADD CONSTRAINT "fk_dbo.logs_dbo.departmentgroups_stationgroupid" FOREIGN KEY (stationgroupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.logs 
  ADD CONSTRAINT "fk_dbo.logs_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."logusers"
--
CREATE TABLE public.logusers(
  loguserid serial,
  logid integer NOT NULL,
  unitid integer,
  userid citext NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.logusers 
  ADD PRIMARY KEY (loguserid);

--
-- Create foreign key
--
ALTER TABLE public.logusers 
  ADD CONSTRAINT "fk_dbo.logusers_dbo.logs_logid" FOREIGN KEY (logid)
    REFERENCES public.logs(logid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.logusers 
  ADD CONSTRAINT "fk_dbo.logusers_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."logunits"
--
CREATE TABLE public.logunits(
  logunitid serial,
  logid integer NOT NULL,
  unitid integer NOT NULL,
  dispatched timestamp without time zone,
  enroute timestamp without time zone,
  onscene timestamp without time zone,
  released timestamp without time zone,
  inquarters timestamp without time zone
);

--
-- Create primary key
--
ALTER TABLE public.logunits 
  ADD PRIMARY KEY (logunitid);

--
-- Create foreign key
--
ALTER TABLE public.logunits 
  ADD CONSTRAINT "fk_dbo.logunits_dbo.logs_logid" FOREIGN KEY (logid)
    REFERENCES public.logs(logid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.logunits 
  ADD CONSTRAINT "fk_dbo.logunits_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."logattachments"
--
CREATE TABLE public.logattachments(
  logattachmentid serial,
  logid integer NOT NULL,
  filename citext,
  data bytea,
  userid citext,
  "timestamp" timestamp without time zone NOT NULL,
  size integer NOT NULL,
  type citext
);

--
-- Create primary key
--
ALTER TABLE public.logattachments 
  ADD PRIMARY KEY (logattachmentid);

--
-- Create foreign key
--
ALTER TABLE public.logattachments 
  ADD CONSTRAINT "fk_dbo.logattachments_dbo.logs_logid" FOREIGN KEY (logid)
    REFERENCES public.logs(logid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."inventories"
--
CREATE TABLE public.inventories(
  inventoryid serial,
  departmentid integer NOT NULL,
  groupid integer NOT NULL,
  typeid integer NOT NULL,
  batch citext,
  note citext,
  amount double precision NOT NULL,
  "timestamp" timestamp without time zone NOT NULL,
  addedbyuserid citext,
  inventorytype_inventorytypeid integer,
  unitid integer,
  location citext
);

--
-- Create primary key
--
ALTER TABLE public.inventories 
  ADD PRIMARY KEY (inventoryid);

--
-- Create foreign key
--
ALTER TABLE public.inventories 
  ADD CONSTRAINT "fk_dbo.inventories_dbo.aspnetusers_addedbyuserid" FOREIGN KEY (addedbyuserid)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.inventories 
  ADD CONSTRAINT "fk_dbo.inventories_dbo.departmentgroups_groupid" FOREIGN KEY (groupid)
    REFERENCES public.departmentgroups(departmentgroupid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.inventories 
  ADD CONSTRAINT "fk_dbo.inventories_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.inventories 
  ADD CONSTRAINT "fk_dbo.inventories_dbo.inventorytypes_inventorytype_inventoryty" FOREIGN KEY (inventorytype_inventorytypeid)
    REFERENCES public.inventorytypes(inventorytypeid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.inventories 
  ADD CONSTRAINT "fk_dbo.inventories_dbo.inventorytypes_typeid" FOREIGN KEY (typeid)
    REFERENCES public.inventorytypes(inventorytypeid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.inventories 
  ADD CONSTRAINT "fk_dbo.inventories_dbo.units_unitid" FOREIGN KEY (unitid)
    REFERENCES public.units(unitid) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."callquicktemplates"
--
CREATE TABLE public.callquicktemplates(
  callquicktemplateid serial,
  departmentid integer NOT NULL,
  isdisabled boolean NOT NULL,
  name citext NOT NULL,
  callname citext,
  callnature citext,
  calltype citext,
  callpriority integer NOT NULL,
  createdbyuserid citext,
  createdon timestamp without time zone NOT NULL
);

--
-- Create primary key
--
ALTER TABLE public.callquicktemplates 
  ADD PRIMARY KEY (callquicktemplateid);

--
-- Create foreign key
--
ALTER TABLE public.callquicktemplates 
  ADD CONSTRAINT "fk_dbo.callquicktemplates_dbo.aspnetusers_createdbyuserid" FOREIGN KEY (createdbyuserid)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.callquicktemplates 
  ADD CONSTRAINT "fk_dbo.callquicktemplates_dbo.departments_departmentid" FOREIGN KEY (departmentid)
    REFERENCES public.departments(departmentid) ON DELETE CASCADE ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."aspnetuserlogins"
--
CREATE TABLE public.aspnetuserlogins(
  userid citext NOT NULL,
  id citext,
  loginprovider citext NOT NULL,
  providerkey citext NOT NULL,
  providerdisplayname citext
);

--
-- Create primary key
--
ALTER TABLE public.aspnetuserlogins 
  ADD PRIMARY KEY (userid);

--
-- Create foreign key
--
ALTER TABLE public.aspnetuserlogins 
  ADD CONSTRAINT "fk_dbo.aspnetuserlogins_dbo.aspnetusers_id" FOREIGN KEY (id)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."aspnetuserclaims"
--
CREATE TABLE public.aspnetuserclaims(
  id serial,
  userid citext NOT NULL,
  claimtype citext,
  claimvalue citext,
  identityuser_id citext
);

--
-- Create primary key
--
ALTER TABLE public.aspnetuserclaims 
  ADD PRIMARY KEY (id);

--
-- Create foreign key
--
ALTER TABLE public.aspnetuserclaims 
  ADD CONSTRAINT "fk_dbo.aspnetuserclaims_dbo.aspnetusers_identityuser_id" FOREIGN KEY (identityuser_id)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."aspnetroles"
--
CREATE TABLE public.aspnetroles(
  id citext NOT NULL,
  name citext,
  normalizedname citext,
  concurrencystamp citext
);

--
-- Create primary key
--
ALTER TABLE public.aspnetroles 
  ADD PRIMARY KEY (id);

--
-- Create table "public"."aspnetuserroles"
--
CREATE TABLE public.aspnetuserroles(
  id serial,
  userid citext NOT NULL,
  roleid citext NOT NULL,
  identityrole_id citext,
  identityuser_id citext
);

--
-- Create primary key
--
ALTER TABLE public.aspnetuserroles 
  ADD PRIMARY KEY (id);

--
-- Create foreign key
--
ALTER TABLE public.aspnetuserroles 
  ADD CONSTRAINT "fk_dbo.aspnetuserroles_dbo.aspnetroles_identityrole_id" FOREIGN KEY (identityrole_id)
    REFERENCES public.aspnetroles(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create foreign key
--
ALTER TABLE public.aspnetuserroles 
  ADD CONSTRAINT "fk_dbo.aspnetuserroles_dbo.aspnetusers_identityuser_id" FOREIGN KEY (identityuser_id)
    REFERENCES public.aspnetusers(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;

--
-- Create table "public"."aspnetroleclaims"
--
CREATE TABLE public.aspnetroleclaims(
  id citext NOT NULL,
  roleid citext NOT NULL,
  claimtype citext,
  claimvalue citext,
  identityrole_id citext
);

--
-- Create primary key
--
ALTER TABLE public.aspnetroleclaims 
  ADD PRIMARY KEY (id);

--
-- Create foreign key
--
ALTER TABLE public.aspnetroleclaims 
  ADD CONSTRAINT "fk_dbo.aspnetroleclaims_dbo.aspnetroles_identityrole_id" FOREIGN KEY (identityrole_id)
    REFERENCES public.aspnetroles(id) ON DELETE NO ACTION ON UPDATE NO ACTION INITIALLY IMMEDIATE;


BEGIN;

--
-- Dropping constraints from "public"."aspnetuserroles"
--
ALTER TABLE public.aspnetuserroles
  DROP CONSTRAINT "fk_dbo.aspnetuserroles_dbo.aspnetroles_identityrole_id";
ALTER TABLE public.aspnetuserroles
  DROP CONSTRAINT "fk_dbo.aspnetuserroles_dbo.aspnetusers_identityuser_id";

--
-- Dropping constraints from "public"."departmentcallpriorities"
--
ALTER TABLE public.departmentcallpriorities
  DROP CONSTRAINT "fk_dbo.departmentcallpriorities_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentmembers"
--
ALTER TABLE public.departmentmembers
  DROP CONSTRAINT "fk_dbo.departmentmembers_dbo.ranks_rankid";
ALTER TABLE public.departmentmembers
  DROP CONSTRAINT fk_departmentmembers_departments_departmentid;

--
-- Dropping constraints from "public"."departments"
--
ALTER TABLE public.departments
  DROP CONSTRAINT "fk_dbo.departments_dbo.addresses_addressid";

--
-- Dropping constraints from "public"."departmentsettings"
--
ALTER TABLE public.departmentsettings
  DROP CONSTRAINT fk_departmentsettings_departments_departmentid;

--
-- Dropping constraints from "public"."payments"
--
ALTER TABLE public.payments
  DROP CONSTRAINT "fk_dbo.payments_dbo.departments_departmentid";
ALTER TABLE public.payments
  DROP CONSTRAINT "fk_dbo.payments_dbo.payments_payment_paymentid";
ALTER TABLE public.payments
  DROP CONSTRAINT "fk_dbo.payments_dbo.plans_planid";

--
-- Dropping constraints from "public"."planlimits"
--
ALTER TABLE public.planlimits
  DROP CONSTRAINT "fk_dbo.planlimits_dbo.plans_planid";

--
-- Dropping constraints from "public"."aspnetroleclaims"
--
ALTER TABLE public.aspnetroleclaims
  DROP CONSTRAINT "fk_dbo.aspnetroleclaims_dbo.aspnetroles_identityrole_id";

--
-- Dropping constraints from "public"."aspnetuserclaims"
--
ALTER TABLE public.aspnetuserclaims
  DROP CONSTRAINT "fk_dbo.aspnetuserclaims_dbo.aspnetusers_identityuser_id";

--
-- Dropping constraints from "public"."aspnetuserlogins"
--
ALTER TABLE public.aspnetuserlogins
  DROP CONSTRAINT "fk_dbo.aspnetuserlogins_dbo.aspnetusers_id";

--
-- Dropping constraints from "public"."callquicktemplates"
--
ALTER TABLE public.callquicktemplates
  DROP CONSTRAINT "fk_dbo.callquicktemplates_dbo.aspnetusers_createdbyuserid";

--
-- Dropping constraints from "public"."inventories"
--
ALTER TABLE public.inventories
  DROP CONSTRAINT "fk_dbo.inventories_dbo.aspnetusers_addedbyuserid";

--
-- Dropping constraints from "public"."logs"
--
ALTER TABLE public.logs
  DROP CONSTRAINT "fk_dbo.logs_dbo.aspnetusers_loggedbyuserid";

--
-- Dropping constraints from "public"."resourceorderfills"
--
ALTER TABLE public.resourceorderfills
  DROP CONSTRAINT "fk_dbo.resourceorderfills_dbo.aspnetusers_accepteduserid";

--
-- Dropping constraints from "public"."actionlogs"
--
ALTER TABLE public.actionlogs
  DROP CONSTRAINT fk_actionlogs_departments_departmentid;

--
-- Dropping constraints from "public"."automations"
--
ALTER TABLE public.automations
  DROP CONSTRAINT "fk_dbo.automations_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."calendaritems"
--
ALTER TABLE public.calendaritems
  DROP CONSTRAINT "fk_dbo.calendaritems_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."calendaritemtypes"
--
ALTER TABLE public.calendaritemtypes
  DROP CONSTRAINT "fk_dbo.calendaritemtypes_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."calllogs"
--
ALTER TABLE public.calllogs
  DROP CONSTRAINT "fk_dbo.calllogs_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."calls"
--
ALTER TABLE public.calls
  DROP CONSTRAINT fk_calls_departments_departmentid;

--
-- Dropping constraints from "public"."calltypes"
--
ALTER TABLE public.calltypes
  DROP CONSTRAINT "fk_dbo.calltypes_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."commanddefinitions"
--
ALTER TABLE public.commanddefinitions
  DROP CONSTRAINT "fk_dbo.commanddefinitions_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."customstates"
--
ALTER TABLE public.customstates
  DROP CONSTRAINT "fk_dbo.customstates_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentcallemails"
--
ALTER TABLE public.departmentcallemails
  DROP CONSTRAINT "fk_dbo.departmentcallemails_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentcallpruning"
--
ALTER TABLE public.departmentcallpruning
  DROP CONSTRAINT "fk_dbo.departmentcallpruning_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentcertificationtypes"
--
ALTER TABLE public.departmentcertificationtypes
  DROP CONSTRAINT "fk_dbo.departmentcertificationtypes_dbo.departments_departmenti";

--
-- Dropping constraints from "public"."departmentfiles"
--
ALTER TABLE public.departmentfiles
  DROP CONSTRAINT "fk_dbo.departmentfiles_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentgroups"
--
ALTER TABLE public.departmentgroups
  DROP CONSTRAINT fk_departmentgroups_departments_departmentid;

--
-- Dropping constraints from "public"."departmentlinks"
--
ALTER TABLE public.departmentlinks
  DROP CONSTRAINT "fk_dbo.departmentlinks_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentnotifications"
--
ALTER TABLE public.departmentnotifications
  DROP CONSTRAINT "fk_dbo.departmentnotifications_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."departmentprofiles"
--
ALTER TABLE public.departmentprofiles
  DROP CONSTRAINT "fk_dbo.departmentprofiles_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."dispatchprotocols"
--
ALTER TABLE public.dispatchprotocols
  DROP CONSTRAINT "fk_dbo.dispatchprotocols_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."distributionlists"
--
ALTER TABLE public.distributionlists
  DROP CONSTRAINT "fk_dbo.distributionlists_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."documents"
--
ALTER TABLE public.documents
  DROP CONSTRAINT "fk_dbo.documents_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."files"
--
ALTER TABLE public.files
  DROP CONSTRAINT "fk_dbo.files_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."inventorytypes"
--
ALTER TABLE public.inventorytypes
  DROP CONSTRAINT "fk_dbo.inventorytypes_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."invites"
--
ALTER TABLE public.invites
  DROP CONSTRAINT "fk_dbo.invites_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."notes"
--
ALTER TABLE public.notes
  DROP CONSTRAINT "fk_dbo.notes_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."notificationalerts"
--
ALTER TABLE public.notificationalerts
  DROP CONSTRAINT "fk_dbo.notificationalerts_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."permissions"
--
ALTER TABLE public.permissions
  DROP CONSTRAINT "fk_dbo.permissions_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."personnelcertifications"
--
ALTER TABLE public.personnelcertifications
  DROP CONSTRAINT "fk_dbo.personnelcertifications_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."personnelroles"
--
ALTER TABLE public.personnelroles
  DROP CONSTRAINT "fk_dbo.personnelroles_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."poitypes"
--
ALTER TABLE public.poitypes
  DROP CONSTRAINT "fk_dbo.poitypes_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."ranks"
--
ALTER TABLE public.ranks
  DROP CONSTRAINT "fk_dbo.ranks_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."resourceorders"
--
ALTER TABLE public.resourceorders
  DROP CONSTRAINT "fk_dbo.resourceorders_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."resourceordersettings"
--
ALTER TABLE public.resourceordersettings
  DROP CONSTRAINT "fk_dbo.resourceordersettings_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."shifts"
--
ALTER TABLE public.shifts
  DROP CONSTRAINT "fk_dbo.shifts_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."shiftstaffings"
--
ALTER TABLE public.shiftstaffings
  DROP CONSTRAINT "fk_dbo.shiftstaffings_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."trainings"
--
ALTER TABLE public.trainings
  DROP CONSTRAINT "fk_dbo.trainings_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."units"
--
ALTER TABLE public.units
  DROP CONSTRAINT "fk_dbo.units_dbo.departments_departmentid";

--
-- Dropping constraints from "public"."unittypes"
--
ALTER TABLE public.unittypes
  DROP CONSTRAINT "fk_dbo.unittypes_dbo.departments_departmentid";

--
-- Inserting data into table "public"."aspnetroles"
--
INSERT INTO public.aspnetroles(id, name, normalizedname, concurrencystamp) VALUES
(E'1F6A03A8-62F4-4179-80FC-2EB96266CF04', E'Admins', E'Admins', E'8C03DB29-F46E-483B-907D-15E85262F5D5'),
(E'38B461D7-E848-46EF-8C06-ECE5B618D9D1', E'Users', E'Users', E'1E663D87-3A14-48B5-B6AD-B4685353DD56'),
(E'3ABA8863-E46D-40CC-AB86-309F9C3E4F97', E'Affiliates', E'Affiliates', E'36BB5EDE-09C8-4FAF-ACCA-63767C604EC0');

--
-- Inserting data into table "public"."aspnetuserroles"
--
INSERT INTO public.aspnetuserroles(id, userid, roleid, identityrole_id, identityuser_id) VALUES
(1, E'474468CD-6BFD-4717-8302-60BBE3530FDB', E'38B461D7-E848-46EF-8C06-ECE5B618D9D1', NULL, NULL),
(2, E'C4D78E63-AA6E-4C38-9F03-A0A6311B4AA5', E'38B461D7-E848-46EF-8C06-ECE5B618D9D1', NULL, NULL),
(3, E'88B16E75-A5CA-4489-8B38-EBA1E4CDCBA0', E'38B461D7-E848-46EF-8C06-ECE5B618D9D1', NULL, NULL),
(4, E'66352698-A346-4A99-BB9A-12AC35AAD62E', E'1F6A03A8-62F4-4179-80FC-2EB96266CF04', NULL, NULL);

SELECT setval('aspnetuserroles_id_seq', (SELECT max(id) FROM public.aspnetuserroles));

--
-- Inserting data into table "public"."aspnetusers"
--
INSERT INTO public.aspnetusers(id, username, normalizedusername, email, normalizedemail, emailconfirmed, passwordhash, securitystamp, concurrencystamp, phonenumber, phonenumberconfirmed, twofactorenabled, lockoutend, lockoutenabled, accessfailedcount) VALUES
(E'474468CD-6BFD-4717-8302-60BBE3530FDB', E'TestAccount1', E'TestAccount1', E'testaccount1@yourcompany.local', E'testaccount1@yourcompany.local', true, E'AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==', E'AFA823DF-6084-4A43-9356-997450D84BB0', E'3F0A5D6E-B075-423D-856B-9522EF7F0CCC', NULL, false, false, NULL, true, 0),
(E'66352698-A346-4A99-BB9A-12AC35AAD62E', E'Administrator', E'Administrator', E'administrator@yourcompany.local', E'administrator@yourcompany.local', true, E'AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==', E'39FEF507-3DD3-4C78-B8D2-D59E5B1F963E', E'54EBD189-52EE-465D-BAE6-CE6823A97526', NULL, false, false, NULL, true, 0),
(E'88B16E75-A5CA-4489-8B38-EBA1E4CDCBA0', E'admin', E'ADMIN', E'admin@yourcompany.local', E'ADMIN@YOURCOMPANY.LOCAL', true, E'AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==', E'6E526153-A336-478C-9ADE-D9EBCBC9748E', NULL, NULL, false, false, NULL, true, 0),
(E'C4D78E63-AA6E-4C38-9F03-A0A6311B4AA5', E'TestAccount2', E'TestAccount2', E'testaccount2@yourcompany.local', E'testaccount2@yourcompany.local', true, E'AQAAAAEAACcQAAAAEB3WroDEDRMBLpq6xkKCCQCF1nfwjsIrTb2AmO1/+0PaMdfJYZSfq33DSYws5wF4Xg==', E'80CABB04-9435-4B52-8F5C-B59D9D551F5C', E'B732F44F-C591-472C-8E4A-F7377A3859E0', NULL, false, false, NULL, true, 0);

--
-- Inserting data into table "public"."departmentcallpriorities"
--
INSERT INTO public.departmentcallpriorities(departmentcallpriorityid, departmentid, name, color, sort, isdeleted, isdefault, pushnotificationsound, shortnotificationsound, dispatchpersonnel, dispatchunits, forcenotifyallpersonnel, iospushnotificationsound, tone) VALUES
(0, 1, E'Low', E'#028602', 0, false, false, NULL, NULL, false, false, false, NULL, 0),
(1, 1, E'Medium', E'#DBDB2E', 1, false, false, NULL, NULL, false, false, false, NULL, 0),
(2, 1, E'High', E'#f9a203', 2, false, false, NULL, NULL, false, false, false, NULL, 0),
(3, 1, E'Emergency', E'#fd0303', 3, false, true, NULL, NULL, false, false, false, NULL, 0);

SELECT setval('departmentcallpriorities_departmentcallpriorityid_seq', (SELECT max(departmentcallpriorityid) FROM public.departmentcallpriorities));

--
-- Inserting data into table "public"."departmentmembers"
--
INSERT INTO public.departmentmembers(departmentmemberid, departmentid, userid, isadmin, isdisabled, ishidden, isdefault, isactive, isdeleted, rankid) VALUES
(1, 1, E'474468CD-6BFD-4717-8302-60BBE3530FDB', true, false, false, true, true, false, NULL),
(2, 1, E'C4D78E63-AA6E-4C38-9F03-A0A6311B4AA5', false, false, false, true, true, false, NULL),
(3, 2, E'88B16E75-A5CA-4489-8B38-EBA1E4CDCBA0', true, false, false, true, true, false, NULL);

SELECT setval('departmentmembers_departmentmemberid_seq', (SELECT max(departmentmemberid) FROM public.departmentmembers));

--
-- Inserting data into table "public"."departments"
--
INSERT INTO public.departments(departmentid, name, code, managinguserid, showwelcome, createdon, updatedon, timezone, apikey, departmenttype, addressid, publicapikey, referringdepartmentid, affiliatecode, sharedsecret, use24hourtime, linkcode) VALUES
(1, E'Resgrid System Department', E'XXXX', E'474468CD-6BFD-4717-8302-60BBE3530FDB', false, NULL, NULL, E'Pacific Standard Time', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, E'E61C8E'),
(2, E'Your Department', E'ABCD', E'88B16E75-A5CA-4489-8B38-EBA1E4CDCBA0', false, NULL, NULL, E'Pacific Standard Time', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);

SELECT setval('departments_departmentid_seq', (SELECT max(departmentid) FROM public.departments));

--
-- Inserting data into table "public"."departmentsettings"
--
INSERT INTO public.departmentsettings(departmentsettingid, departmentid, settingtype, setting) VALUES
(1, 1, 15, E'F0D74B');

SELECT setval('departmentsettings_departmentsettingid_seq', (SELECT max(departmentsettingid) FROM public.departmentsettings));

--
-- Inserting data into table "public"."payments"
--
INSERT INTO public.payments(paymentid, departmentid, planid, method, istrial, purchaseon, purchasinguserid, transactionid, successful, data, isupgrade, description, effectiveon, amount, payment_paymentid, endingon, cancelled, cancelledon, cancelleddata, upgradedpaymentid, subscriptionid) VALUES
(1, 2, 1, 4, false, '2020-11-03 08:32:16.0430000', E'88B16E75-A5CA-4489-8B38-EBA1E4CDCBA0', E'SYSTEM', true, NULL, false, E'Default Forever Plan', '2020-11-02 08:32:16.0430000', 0, NULL, '9999-12-31 23:59:59.9970000', false, NULL, NULL, NULL, NULL);

SELECT setval('payments_paymentid_seq', (SELECT max(paymentid) FROM public.payments));

--
-- Inserting data into table "public"."planlimits"
--
INSERT INTO public.planlimits(planlimitid, planid, limittype, limitvalue) VALUES
(1, 1, 1, 2147483647),
(2, 1, 2, 2147483647),
(3, 1, 3, 2147483647),
(4, 1, 4, 2147483647);

SELECT setval('planlimits_planlimitid_seq', (SELECT max(planlimitid) FROM public.planlimits));

--
-- Inserting data into table "public"."plans"
--
INSERT INTO public.plans(planid, name, cost, frequency, externalid) VALUES
(1, E'Default Plan', 0, 1, E'');

SELECT setval('plans_planid_seq', (SELECT max(planid) FROM public.plans));

--
-- Inserting data into table "public"."userprofiles"
--
INSERT INTO public.userprofiles(userprofileid, userid, firstname, lastname, timezone, mobilenumber, mobilecarrier, sendemail, sendpush, sendsms, sendmessageemail, sendmessagepush, sendmessagesms, donotrecievenewsletters, homenumber, homeaddressid, mailingaddressid, identificationnumber, sendnotificationemail, sendnotificationpush, sendnotificationsms, voiceforcall, voicecallmobile, voicecallhome, image, lastupdated, custompushsounds, startdate, enddate) VALUES
(1, E'474468CD-6BFD-4717-8302-60BBE3530FDB', E'Test', E'User', NULL, NULL, 0, true, false, false, true, false, false, false, NULL, NULL, NULL, NULL, false, false, false, false, false, false, NULL, NULL, false, NULL, NULL),
(2, E'C4D78E63-AA6E-4C38-9F03-A0A6311B4AA5', E'Test', E'User2', NULL, NULL, 0, true, false, false, true, false, false, false, NULL, NULL, NULL, NULL, false, false, false, false, false, false, NULL, NULL, false, NULL, NULL),
(3, E'88B16E75-A5CA-4489-8B38-EBA1E4CDCBA0', E'Department', E'Admin', NULL, NULL, 0, false, false, false, false, false, false, false, NULL, NULL, NULL, NULL, false, false, false, false, false, false, NULL, NULL, false, NULL, NULL);

SELECT setval('userprofiles_userprofileid_seq', (SELECT max(userprofileid) FROM userprofiles));

--
-- Creating constraints for "public"."aspnetuserroles"
--
ALTER TABLE public.aspnetuserroles
   ADD CONSTRAINT "fk_dbo.aspnetuserroles_dbo.aspnetroles_identityrole_id" FOREIGN KEY (identityrole_id) REFERENCES aspnetroles (id);
ALTER TABLE public.aspnetuserroles
   ADD CONSTRAINT "fk_dbo.aspnetuserroles_dbo.aspnetusers_identityuser_id" FOREIGN KEY (identityuser_id) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."departmentcallpriorities"
--
ALTER TABLE public.departmentcallpriorities
   ADD CONSTRAINT "fk_dbo.departmentcallpriorities_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentmembers"
--
ALTER TABLE public.departmentmembers
   ADD CONSTRAINT "fk_dbo.departmentmembers_dbo.ranks_rankid" FOREIGN KEY (rankid) REFERENCES ranks (rankid);
ALTER TABLE public.departmentmembers
   ADD CONSTRAINT fk_departmentmembers_departments_departmentid FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departments"
--
ALTER TABLE public.departments
   ADD CONSTRAINT "fk_dbo.departments_dbo.addresses_addressid" FOREIGN KEY (addressid) REFERENCES addresses (addressid);

--
-- Creating constraints for "public"."departmentsettings"
--
ALTER TABLE public.departmentsettings
   ADD CONSTRAINT fk_departmentsettings_departments_departmentid FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."payments"
--
ALTER TABLE public.payments
   ADD CONSTRAINT "fk_dbo.payments_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);
ALTER TABLE public.payments
   ADD CONSTRAINT "fk_dbo.payments_dbo.payments_payment_paymentid" FOREIGN KEY (payment_paymentid) REFERENCES payments (paymentid);
ALTER TABLE public.payments
   ADD CONSTRAINT "fk_dbo.payments_dbo.plans_planid" FOREIGN KEY (planid) REFERENCES plans (planid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."planlimits"
--
ALTER TABLE public.planlimits
   ADD CONSTRAINT "fk_dbo.planlimits_dbo.plans_planid" FOREIGN KEY (planid) REFERENCES plans (planid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."aspnetroleclaims"
--
ALTER TABLE public.aspnetroleclaims
   ADD CONSTRAINT "fk_dbo.aspnetroleclaims_dbo.aspnetroles_identityrole_id" FOREIGN KEY (identityrole_id) REFERENCES aspnetroles (id);

--
-- Creating constraints for "public"."aspnetuserclaims"
--
ALTER TABLE public.aspnetuserclaims
   ADD CONSTRAINT "fk_dbo.aspnetuserclaims_dbo.aspnetusers_identityuser_id" FOREIGN KEY (identityuser_id) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."aspnetuserlogins"
--
ALTER TABLE public.aspnetuserlogins
   ADD CONSTRAINT "fk_dbo.aspnetuserlogins_dbo.aspnetusers_id" FOREIGN KEY (id) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."callquicktemplates"
--
ALTER TABLE public.callquicktemplates
   ADD CONSTRAINT "fk_dbo.callquicktemplates_dbo.aspnetusers_createdbyuserid" FOREIGN KEY (createdbyuserid) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."inventories"
--
ALTER TABLE public.inventories
   ADD CONSTRAINT "fk_dbo.inventories_dbo.aspnetusers_addedbyuserid" FOREIGN KEY (addedbyuserid) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."logs"
--
ALTER TABLE public.logs
   ADD CONSTRAINT "fk_dbo.logs_dbo.aspnetusers_loggedbyuserid" FOREIGN KEY (loggedbyuserid) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."resourceorderfills"
--
ALTER TABLE public.resourceorderfills
   ADD CONSTRAINT "fk_dbo.resourceorderfills_dbo.aspnetusers_accepteduserid" FOREIGN KEY (accepteduserid) REFERENCES aspnetusers (id);

--
-- Creating constraints for "public"."actionlogs"
--
ALTER TABLE public.actionlogs
   ADD CONSTRAINT fk_actionlogs_departments_departmentid FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."automations"
--
ALTER TABLE public.automations
   ADD CONSTRAINT "fk_dbo.automations_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."calendaritems"
--
ALTER TABLE public.calendaritems
   ADD CONSTRAINT "fk_dbo.calendaritems_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."calendaritemtypes"
--
ALTER TABLE public.calendaritemtypes
   ADD CONSTRAINT "fk_dbo.calendaritemtypes_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."calllogs"
--
ALTER TABLE public.calllogs
   ADD CONSTRAINT "fk_dbo.calllogs_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."calls"
--
ALTER TABLE public.calls
   ADD CONSTRAINT fk_calls_departments_departmentid FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."calltypes"
--
ALTER TABLE public.calltypes
   ADD CONSTRAINT "fk_dbo.calltypes_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."commanddefinitions"
--
ALTER TABLE public.commanddefinitions
   ADD CONSTRAINT "fk_dbo.commanddefinitions_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."customstates"
--
ALTER TABLE public.customstates
   ADD CONSTRAINT "fk_dbo.customstates_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentcallemails"
--
ALTER TABLE public.departmentcallemails
   ADD CONSTRAINT "fk_dbo.departmentcallemails_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentcallpruning"
--
ALTER TABLE public.departmentcallpruning
   ADD CONSTRAINT "fk_dbo.departmentcallpruning_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentcertificationtypes"
--
ALTER TABLE public.departmentcertificationtypes
   ADD CONSTRAINT "fk_dbo.departmentcertificationtypes_dbo.departments_departmenti" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentfiles"
--
ALTER TABLE public.departmentfiles
   ADD CONSTRAINT "fk_dbo.departmentfiles_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentgroups"
--
ALTER TABLE public.departmentgroups
   ADD CONSTRAINT fk_departmentgroups_departments_departmentid FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentlinks"
--
ALTER TABLE public.departmentlinks
   ADD CONSTRAINT "fk_dbo.departmentlinks_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."departmentnotifications"
--
ALTER TABLE public.departmentnotifications
   ADD CONSTRAINT "fk_dbo.departmentnotifications_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."departmentprofiles"
--
ALTER TABLE public.departmentprofiles
   ADD CONSTRAINT "fk_dbo.departmentprofiles_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."dispatchprotocols"
--
ALTER TABLE public.dispatchprotocols
   ADD CONSTRAINT "fk_dbo.dispatchprotocols_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."distributionlists"
--
ALTER TABLE public.distributionlists
   ADD CONSTRAINT "fk_dbo.distributionlists_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."documents"
--
ALTER TABLE public.documents
   ADD CONSTRAINT "fk_dbo.documents_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."files"
--
ALTER TABLE public.files
   ADD CONSTRAINT "fk_dbo.files_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."inventorytypes"
--
ALTER TABLE public.inventorytypes
   ADD CONSTRAINT "fk_dbo.inventorytypes_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."invites"
--
ALTER TABLE public.invites
   ADD CONSTRAINT "fk_dbo.invites_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."notes"
--
ALTER TABLE public.notes
   ADD CONSTRAINT "fk_dbo.notes_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."notificationalerts"
--
ALTER TABLE public.notificationalerts
   ADD CONSTRAINT "fk_dbo.notificationalerts_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."permissions"
--
ALTER TABLE public.permissions
   ADD CONSTRAINT "fk_dbo.permissions_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."personnelcertifications"
--
ALTER TABLE public.personnelcertifications
   ADD CONSTRAINT "fk_dbo.personnelcertifications_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."personnelroles"
--
ALTER TABLE public.personnelroles
   ADD CONSTRAINT "fk_dbo.personnelroles_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."poitypes"
--
ALTER TABLE public.poitypes
   ADD CONSTRAINT "fk_dbo.poitypes_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."ranks"
--
ALTER TABLE public.ranks
   ADD CONSTRAINT "fk_dbo.ranks_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."resourceorders"
--
ALTER TABLE public.resourceorders
   ADD CONSTRAINT "fk_dbo.resourceorders_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."resourceordersettings"
--
ALTER TABLE public.resourceordersettings
   ADD CONSTRAINT "fk_dbo.resourceordersettings_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;

--
-- Creating constraints for "public"."shifts"
--
ALTER TABLE public.shifts
   ADD CONSTRAINT "fk_dbo.shifts_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."shiftstaffings"
--
ALTER TABLE public.shiftstaffings
   ADD CONSTRAINT "fk_dbo.shiftstaffings_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."trainings"
--
ALTER TABLE public.trainings
   ADD CONSTRAINT "fk_dbo.trainings_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."units"
--
ALTER TABLE public.units
   ADD CONSTRAINT "fk_dbo.units_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid);

--
-- Creating constraints for "public"."unittypes"
--
ALTER TABLE public.unittypes
   ADD CONSTRAINT "fk_dbo.unittypes_dbo.departments_departmentid" FOREIGN KEY (departmentid) REFERENCES departments (departmentid) ON DELETE CASCADE;
