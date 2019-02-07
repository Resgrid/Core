#######
Installation
#######

In this section we will go over all the steps needed to get Resgrid running on your own environment. 

.. important:: Resgrid requires working **RabbitMQ**, **Redis** and **SQL** servers, more info in :ref:`installation_prerequisites` below and currently only runs on Microsoft Windows operating systems

.. _installation_prerequisites:

Prerequisites & Dependencies
****************************

`Resgrid <https://resgrid.com/>`_ requires Microsoft .Net Framework 4.6.2 and .Net Core 1.1. and running on a Windows environment, Windows Server is recommended but not required. 

The following server dependencies need to be installed, configured and functional:

* `RabbitMQ Server <https://www.rabbitmq.com>`_, version 3.6.0 or newer
* `Redis Server <http://redis.io/>`_, version 4.0 or newer
* `Microsoft SQL Server <https://www.microsoft.com/en-us/sql-server/default.aspx>`_, version 12.0 (SQL 2014) or newer
* `Microsoft IIS <https://www.iis.net/>`_ version installed on Windows 8 or newer or Windows Server 2012 or newer