#######
Development
#######

In this section we will go over all the steps needed to develop against the Resgrid solution. 

.. _development_prerequisites:

Prerequisites & Dependencies
****************************

The following server dependencies need to be installed, configured and functional:

.. note:: Please ensure your Windows system is up to date with all Windows and Microsoft updates before installing the Resgrid System.

* `.Net Framework <https://dotnet.microsoft.com/download/visual-studio-sdks?utm_source=getdotnetsdk&utm_medium=referral>`_ .NET Framework 4.6.2 (Developer Pack)
* `RabbitMQ Server <https://www.rabbitmq.com>`_, version 3.6.0 or newer
* `Microsoft SQL Server <https://www.microsoft.com/en-us/sql-server/default.aspx>`_, version 12.0 (SQL 2014) or newer
* `Microsoft IIS <https://www.iis.net/>`_ version installed on Windows 8 or newer or Windows Server 2012 or newer
* `Docker for Windows Desktop <https://docs.docker.com/docker-for-windows/install/>`_ Docker for Windows Desktop

.. note:: If your not running a Professional (Pro) version of Windows you may not be able to install Docker for Windows Desktop. You will get an error opening up the ResgridCore solution with Visual Studio but your can just unlock the docker project under the Docker solution folder.