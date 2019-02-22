#######
Installation
#######

In this section we will go over all the steps needed to get Resgrid running on your own environment. 

.. important:: Resgrid requires working **RabbitMQ** and **SQL** servers, more info in :ref:`installation_prerequisites` below and currently only runs on Microsoft Windows operating systems

.. _installation_prerequisites:

Prerequisites & Dependencies
****************************

`Resgrid <https://resgrid.com/>`_ requires Microsoft .Net Framework 4.6.2 and .Net Core 1.1. and running on a Windows environment, Windows Server is recommended but not required. 

.. note:: Please ensure your Windows system is up to date with all Windows and Microsoft updates before installing the Resgrid System.

The following server dependencies need to be installed, configured and functional:

* `.Net Framework <https://dotnet.microsoft.com/download/visual-studio-sdks?utm_source=getdotnetsdk&utm_medium=referral>`_ .NET Framework 4.6.2 (Developer Pack)
* `RabbitMQ Server <https://www.rabbitmq.com>`_, version 3.6.0 or newer
* `Microsoft SQL Server <https://www.microsoft.com/en-us/sql-server/default.aspx>`_, version 12.0 (SQL 2014) or newer
* `Microsoft IIS <https://www.iis.net/>`_ version installed on Windows 8 or newer or Windows Server 2012 or newer
* `Elastic ELK <https://www.elastic.co/guide/en/elastic-stack/current/installing-elastic-stack.html>`_ 6.6.0 or newer
* SMTP Server for sending email

.. note:: Any correctly configured SMTP server will work if it's local or not. If you have an SMTP server provided by your ISP or provider that will also work. For non-server Windows installations (i.e. Windows Home or Professional) we recommended `hMailServer <https://www.hmailserver.com/download>`_.

RabbitMQ 
=======================

To install RabbitMQ follow the `Windows Installation <https://www.rabbitmq.com/install-windows.html>`_ guide. Ensure your firewall is configured to allow the ports listed in that guide through. It is also recommend you `enable the management UI <https://www.rabbitmq.com/management.html>`_ for RabbitMQ.

.. note:: RabbitMQ requires Erlang to be installed. You can download the `Windows installer <https://www.erlang.org/downloads>`_ at their website.

Redis 
=======================

Redis is an standalone, resilient in memory data store that Redis uses to cache data that is shared across multiple servers. Redis is an optional dependency but is highly recommended for production installations of Resgrid. Redis does not run well on Windows and thus needs to be installed a Unix or Linux based system. You can get `Redis Server <http://redis.io/>`_ from their website. Version 4.0 or newer is recommended. 

.. important:: Although Redis is optional, it's recommended for production installations or multi server installations of Resgrid.


Database Installation
****************************



Initial Web Login
****************************

Once you have completed the steps above you will be able to log into the web applications user interface. Open up a web browser and navigate to http://resgrid.local, you will then be prompted by the login screen. Your default administrator credentials are **admin/changeme1234**. Once you log into the system it's recommended that you change your admin password from the Edit Profile page by clicking on the Administrator name in the upper left hand corner. 

