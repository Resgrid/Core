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
* `Microsoft Visual Studio 2017 <https://visualstudio.microsoft.com/downloads/>`_ Community or Higher
* `Google Chrome <https://www.google.com/chrome/>`_ Chrome 71 or newer
* `Elastic ELK <https://www.elastic.co/guide/en/elastic-stack/current/installing-elastic-stack.html>`_ 6.6.0 or newer

.. note:: If your not running a Professional (Pro) version of Windows you may not be able to install Docker for Windows Desktop. You will get an error opening up the ResgridCore solution with Visual Studio but your can just unlock the docker project under the Docker solution folder.

Getting the Code
****************************

You can download the Resgrid Core source from our GitHub page `Resgrid Core Github <https://github.com/Resgrid/Core>`_.

Opening in Visual Studio
****************************

Open the ResgridCore.sln file in your version of Microsoft Visual Studio 2017. You will be prompted by a "Security Warning" dialog box, you can confirm for every project, but if you uncheck "Ask me for every project in this solution" you only need to be prompted once.

If you get an error opening the solution up Visual Studio this could be because you don't have Docker installed (it can't be installed on all versions of Windows) so you can expand the Docker folder in the Solution Explorer right click "docker-compose" and click "Unload Project". This will allow you to open and compile the solution without any error. If you do have Docker installed on your computer, ensure that it's running.

Open up the Web folder in the "Solution Explorer" and right click the "Resgrid.WebCore" project and select "Set as Startup Project". This will mean that when you run or debug the solution a web browser will open up defaulting you to the Web project.

Restoring Dependencies 
=======================

Once you have the solution open correctly you need to download all the dependencies for the project. Right click the "ResgridCore" solution and click "Restore Nuget Packages", this will download all the .Net dependencies for the solution.

Next you need to restore the bower and npm dependencies for the Resgrid.WebCore project. Expand the Web folder in the "Solution Explorer" and expand the "Resgrid.WebCore" project, At the root of that project there are 2 files; bower.json and package.json that we will be working with. Right click "bower.json" and select "Restore Packages" this will download all the bower dependencies. Next right click "package.json" and select "Restore Packages". 

Solution
****************************

Folders 
=======================

.. list-table:: Solution Folders
   :header-rows: 1

   * - Folder
     - Description
   * - Common
     - Contains common files that may be included in other projects, like the AssemblyInfo file
   * - Core
     - Central libraries utilized throughout the system
   * - Docker
     - Projects related to setting up and managing Docker
   * - Documentation
     - Notes and Documentation
   * - Providers
     - High level external integrations, like Geolocation or Text Messaging
   * - Repositories
     - Data Storage Repo
   * - Tests
     - Unit and Integration Tests
   * - Tools
     - Non-Web UI tools and applications
   * - Web
     - The main applications, the web application and the services (api) application
   * - Workers
     - Backend workers

Projects 
=======================

.. list-table:: Solution Projects
   :header-rows: 1

   * - Project
     - Description
   * - Resgrid.Config
     - Primary system configuration options controlling the entire system
   * - Resgrid.Framework
     - Shared helpers and common functions, like error logging that are used in every layer
   * - Resgrid.Model
     - Data model objects, event objects, interfaces for services\providers and system metadata, like enumerations. 
   * - Resgrid.Services
     - Business logic layer services, both discrete and composite 
   * - Resgrid.Providers.AddressVerification
     - External address verification providers
   * - Resgrid.Providers.Audio
     - External audio manipulation providers
   * - Resgrid.Providers.Bus
     - Azure Service Bus and System Eventing
   * - Resgrid.Providers.Bus.Rabbit
     - RabbitMQ bus provider
   * - Resgrid.Providers.Cache
     - Redis and Internal (In Memory) caching provider
   * - Resgrid.Providers.Claims
     - Rights and Claims system for the Web Application
   * - Resgrid.Providers.EmailProvider
     - External email providers (Postmark)
   * - Resgrid.Providers.Firebase
     - Firebase external provider used for the real-time database (Chatting)
   * - Resgrid.Providers.GeoProvider
     - Geolocation provider for getting Latitude and Longitude for Addresses and vice versa
   * - Resgrid.Providers.Marketing
     - External provider for working with an email marking system
   * - Resgrid.Providers.NumberProvider
     - Number, SMS\MMS provider (Twilio and Nexemo)
   * - Resgrid.Providers.PdfProvider
     - External PDF integration provider
   * - Resgrid.Providers.AddressVerification
     - Address verification, testing if address are correct
   * - Resgrid.Repositories.DataRepository
     - Primary Data Store, SQL Server both Entity Framework and Dapper
   * - Resgrid.Tests
     - Unit Testing
   * - Resgrid.Console
     - CLI Application for interacting with the Resgrid system
   * - Resgrid.Web.Services
     - RESTful APIs (Services)
   * - Resgrid.WebCore
     - Primary Web Application (User Interface\Website) that users will interact with
   * - Resgrid.Workers.Console
     - CLI Application that needs to be running at all times, contains back end workers for the Message Bus
   * - Resgrid.Workers.Framework
     - Logic for the async workers that the Workers.Console runs\monitors