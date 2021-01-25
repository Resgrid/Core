#######
Docker
#######

In this section we will go over setup of the Resgrid system using Docker containers.

.. important:: Resgrid requires working **RabbitMQ**, **Redis** and **SQL** servers, more info in :ref:`installation_prerequisites` below and currently only runs on Microsoft Windows operating systems

This documentation is for installation of Resgrid from compile source. If you want to install Resgrid from Docker containers please review that section instead.

.. _requirements:

Requirements Notice
****************************

It is highly recommended that Resgrid is installed and setup by an IT Professional. There is a large amount of system configuration, tweaking and setup that is required to be done before you install Resgrid. Below is a list of technologies that you should have skilled professionals available to you or requisite knowledge before installing Resgrid. Resgrid does not provide support or configuration guidance for those systems outside of the minimum needed to get the system functional. The steps outlined below will get the system in a bare minimum functional state to ensure it's working on your enviroment, to be production ready will reqire more effort then is outlined in this documentation.

* Windows or Linux
* Docker, Kubernetes, Rancher, K8s
* SQL Server or PostgreSQL
* DNS, hostname mapping, proxy configuration
* RabbitMQ
* Redis
* Elastic
* Mail Server SMTP, POP3
* Firewall and system hardning

.. _docker_container_images:

Docker Container Images
****************************

Resgrid is split into 3 distinct Docker containers. All of our container images are available under the Resgrid, LLC organization on the  `Docker Hub <https://hub.docker.com/u/resgridllc>`_.

**resgridwebcore**
This is the web application docker image and is used to host the website application that users will interact with.

Docker Pull Command::

  docker pull resgridllc/resgridwebcore

**resgridwebservices**
This is the web api that is used by the website and applications to communicate with the Resgrid system

Docker Pull Command::

  docker pull resgridllc/resgridwebservices

**resgridworkersconsole**
This is the backend workers that are used to process operations from RabbitMQ or scheduled tasks. 

Docker Pull Command::

  docker pull resgridllc/resgridworkersconsole

.. _settings:

Settings
****************************

To configure the Resgrid system in a Docker or Kubernetes context we recommend using environment variables. To see all the config options availabe you can take a look at our Github repo <https://github.com/Resgrid/Core/tree/master/Core/Resgrid.Config>, every `static` class in the Resgrid.Congfig project can be set by an environment variable.

The pattern for how Resgrid processes environment is as follows:

RESGRID__{CLASSNAME}__{PROPERTYNAME}

Resgrid at the start of the name must be in all caps, there are two (2) underscores seperating the parts, in between RESGRID and classname and classname and popertyname. 

.. list-table:: Resgrid Environment Variables
   :header-rows: 1

   * - Variables
     - Required
     - Description
   * - RESGRID__CacheConfig__RedisConnectionString
     - Yes 
     - The full connection string to the Redis server or cluster
   * - RESGRID__DataConfig__ConnectionString
     - Yes
     - The connection string to the Microsoft SQL Server
   * - RESGRID__ExternalErrorConfig__ExternalErrorServiceUrl
     - No
     - Url for Sentry.io error reporting
   * - RESGRID__InboundEmailConfig__DispatchDomain
     - No
     - Domain name to put at the end of the dispatch email address
   * - RESGRID__InboundEmailConfig__GroupMessageDomain
     - No 
     - Domain name to put at the end of the group message email address
   * - RESGRID__InboundEmailConfig__GroupsDomain
     - No
     - Domain name to put at the end of the group dispatch email address
   * - RESGRID__InboundEmailConfig__ListsDomain
     - No
     - Domain name to put at the end of the distribution list email address
   * - RESGRID__ServiceBusConfig__RabbbitExchange
     - Yes
     - RabbitMQ exchange name (can be blank)
   * - RESGRID__ServiceBusConfig__RabbitHostname
     - Yes
     - Hostname or IP Address of the RabbitMQ server or cluster
   * - RESGRID__ServiceBusConfig__RabbitUsername
     - Yes
     - Login for RabbitMQ that has permissions to create queues and publish and recieve messages
   * - RESGRID__ServiceBusConfig__RabbbitPassword
     - Yes
     - Password for the RabbitMQ login
   * - RESGRID__SystemBehaviorConfig__ApiTokenEncryptionPassphrase
     - Yes
     - Passphrase to encrypt API tokens with
   * - RESGRID__SystemBehaviorConfig__DoNotBroadcast
     - Yes
     - True/False prevents any communications from being sent if set to True
   * - RESGRID__SystemBehaviorConfig__ResgridApiBaseUrl
     - Yes
     - URL for the Resgrid API for this Resgrid install
   * - RESGRID__SystemBehaviorConfig__ResgridBaseUrl
     - Yes
     - Base url to access the web install of Resgrid

.. note:: The above is only a partial list to get the Resgrid system functional. You may need to set others to get the system fully operational within your environment. At a minimum, Microsoft SQL Server, Redis and RabbitMQ are required as well as setting the ResgridAPI url and ResgridBase web url.
