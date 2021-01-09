##################################
Resgrid Core: The Complete Open Source Computer Aided Dispatch System
##################################

`Resgrid <https://resgrid.com/>`_ is a computer aided dispatch, management and logistics for first responders, disaster response, emergency management and companies.

Originally started as a hosted only solution in 2014, the Resgrid system as processed hundreds of thousands of calls, messages, statuses and staffing updates and much more. With over 4,000 departments signed up Resgrid is the only open source computer aided dispatch system able to run at scale.

Resgrid is written on the Microsoft .Net and .Net Core Frameworks utilizing Microsoft SQL Server as the primary data repository.

Features
********
* Personnel Management: Define personnel, contact information, details, certification, roles, status and availability for all personnel
* Unit Support: Support for apparatuses and groups of personnel working as a single unit (i.e. a USAR team) with AVL, accountability and logging
* Groups and Locations: Create groups and locations and assign personnel or units underneath for management of large or disperse organizations 
* Computer Aided Dispatch: Create Calls\Incidents and dispatch personnel, units, roles or groups to respond to those incidents both manual and automatic dispatches are supported
* Messaging: Built in message system to allow for targeted and dynamic communications to personnel
* Chat: Embedded P2P, Groups, Dispatch to Unit and Command chat system to enable very quick text based communications
* Duty Shift System: Create both Assigned Shifts and Signup Shifts to manage a static or dynamic workforce, switch swapping and trading support with attendance validation
* Learning Management: Design Trainings with text based materials, attach documents or presentations or link to external videos and assign questions to validate understanding of material
* Run Logs and Logging: Record actions of a call, training and meetings to keep tract of actions and events, hours, personnel and units involved
* Reporting** Generate reports for run logs, calls, training, meetings and more. Ability to use Reporting to create exports to integrate with 3rd party systems
* Calendar System: Create calendar entries and setup RSVP style events to keep personnel engaged and informed about activities and events
* Inventory Management: Track any kind of inventory both perishable (like medicine) and durable (like hand tools) equipped on apparatus, issued to personnel or stored at locations
* Document Storage: Upload and serve documents at a department or group level to members of your organization allowing a centralized place to serve documents from
* Notifications Service: Flexible notification system to alert of low personnel role or unit availability, staffing or status changes or any other system generated event
* Department Linking: Create powerful department links to allow for multiple independent organizations (i.e. mutual aid agreements or centralized dispatch center) to cooperate
* Mobile Apps: Apps available on Google Play and Apple App Store that can work with any standard installation. For Personnel, Units, Stations and Commanders.
* API: Included API with information about calls allow for easy extension and interaction without having to change code in the Resgrid Core codebase

Getting started
***************
.. hlist::
   :columns: 2

   * :doc:`/installation/index` -- Install and run the Resgrid system
   * :doc:`/docker/index` -- Install and run the Resgrid system with Docker
   * :doc:`/development/index` -- Start developing with Resgrid
   * :doc:`/contributing/index` -- Contribution guidelines and information
   * :doc:`/system/index` -- Overview of the Resgrid system and features
   * :doc:`/setup/index` -- Walkthrough on setting up your department
   * :doc:`/configuration/index` -- All configuration options for your department
   * :doc:`/apps/index` -- Documentation for our external applications

Links
*****

* `Resgrid Homepage and Hosted Solution <https://resgrid.com>`_
* `Documentation <https://resgrid-core.readthedocs.io/en/latest>`_
* `Source code <https://github.com/Resgrid/Core>`_

Documentation Contents
*************
.. toctree::
   :maxdepth: 2

   /overview/index
   /installation/index
   /docker/index
   /setup/index
   /configuration/index
   /providers/index
   /system/index
   /apps/index
   /development/index
   /contributing/index

Indices and tables
==================

* :ref:`genindex`
* :ref:`modindex`
* :ref:`search`
