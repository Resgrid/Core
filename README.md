<p align="center">
  <a href="https://resgrid.com/">
    <img src="https://raw.githubusercontent.com/resgrid/core/master/misc/images/Resgrid_TextLogo.png" alt="Resgrid logo">
  </a>
</p>

<h3 align="center">Resgrid Core</h3>

<p align="center">
  Complete and open source computer aided dispatch, management and logistics for first responders, disaster response, emergency management and companies
  <br>
  <a href="https://readthedocs.org/projects/resgrid-core/"><strong>View Resgrid docs</strong></a>
  <br>
  <br>
  <a href="https://github.com/Resgrid/Core/issues/new?template=bug.md">Report bug</a>
  ·
  <a href="https://github.com/Resgrid/Core/issues/new?template=feature.md&labels=feature">Request feature</a>
  ·
  <a href="https://blog.resgrid.com/">Blog</a>
</p>

## Table of Contents

- [Features](#features)
- [Applications](#applications)
- [Initiatives](#initiatives)
- [Status](#status)
- [Copyright and license](#copyright-and-license)

## Features

- **Personnel Management** Define personnel, contact information, details, certification, roles, status and availability for all personnel
- **Unit Support** Support for apparatuses and groups of personnel working as a single unit (i.e. a USAR team) with AVL, accountability and logging
- **Groups and Locations** Create groups and locations and assign personnel or units underneath for management of large or disperse organizations 
- **Computer Aided Dispatch** Create Calls\Incidents and dispatch personnel, units, roles or groups to respond to those incidents both manual and automatic dispatches are supported
- **Messaging** Built in message system to allow for targeted and dynamic communications to personnel
- **Chat** Embedded P2P, Groups, Dispatch to Unit and Command chat system to enable very quick text based communications
- **Duty Shift System** Create both Assigned Shifts and Signup Shifts to manage a static or dynamic workforce, switch swapping and trading support with attendance validation
- **Learning Management** Design Trainings with text based materials, attach documents or presentations or link to external videos and assign questions to validate understanding of material
- **Run Logs and Logging** Record actions of a call, training and meetings to keep tract of actions and events, hours, personnel and units involved
- **Reporting** Generate reports for run logs, calls, training, meetings and more. Ability to use Reporting to create exports to integrate with 3rd party systems
- **Calendar System** Create calendar entries and setup RSVP style events to keep personnel engaged and informed about activities and events
- **Inventory Management** Track any kind of inventory both perishable (like medicine) and durable (like hand tools) equipped on apparatus, issued to personnel or stored at locations
- **Document Storage** Upload and serve documents at a department or group level to members of your organization allowing a centralized place to serve documents from
- **Notifications Service** Flexible notification system to alert of low personnel role or unit availability, staffing or status changes or any other system generated event
- **Department Linking** Create powerful department links to allow for multiple independent organizations (i.e. mutual aid agreements or centralized dispatch center) to cooperate
- **Mobile Apps** Apps available on Google Play and Apple App Store that can work with any standard installation. For Personnel, Units, Stations and Commanders.
- **API** Included API with information about calls allow for easy extension and interaction without having to change code in the Resgrid Core codebase

## Applications

Below are the repositories for the applications built utilizing the [Resgrid API](https://api.resgrid.com).

######[BigBoard](https://github.com/Resgrid/BigBoard)** 
The Resgrid BigBoard is a dashboard system intended to be used in stations or centralized locations to allow for personnel to see the status of the system at a glance. The BigBoard can display Personnel Statuses, Staffing and ETAs, Unit Statuses, Call and Call Information, a map of all activity, and current weather.

![BigBoard](https://raw.githubusercontent.com/resgrid/core/master/misc/images/BigBoard.png)

######[Relay](https://github.com/Resgrid/Relay)** 
The Resgrid Relay is a console based application to monitor an audio input, for example from a Scanner, to listen for Tone Frequencies and capture audio for a selected amount of time. Once complete Resgrid Relay will create a call in the Resgrid system and dispatch the groups, or department, associated with the tones. This allows for standing up Resgrid in environments where you cannot modify a dispatch system or a shared dispatch center.

![Relay](https://raw.githubusercontent.com/resgrid/core/master/misc/images/Relay.png)

## Initiatives

Major initiatives for the Resgrid project in 2019!

* **Open Source**: Get the Resgrid Core system open sourced.
* **Setup Documentation**: Tied to Docker, as that should be the preferred way to stand up Resgrid in a very consistent manner and get it working out of the box regardless of environment or configuration. 
* **Dapper**: We are migrating away from Entity Framework to Dapper, with corresponding Sync and Async calls. The meta-data overheard for Entity Framework has caused some issues along with query design. When we are in a system, for example Calls, and we have to modify the underlying repository calls at that point we start migrating them to Dapper. The first phase goal is to only have EF calls for Adding\Updating objects. Final phase would be utilizing Dapper for all CRUD operations.
* **.Net Core**: The main web application Resgrid.WebCore was migrated to .Net Core 1.1 but the rest of the stack has lagged. The goal would be to migrate the API project (Resgrid.Services) to the latest .Net Core and update the Web application to the latest as well, then move down the stack migrating all assembly projects to the latest .Net Core version as well. The intention here to to allow for deployment of Resgrid on any environment type (Windows, Linux, Unix) instead of just Windows.
* **Docker*: Utilization of containers to setup all the discrete parts of the Resgrid system is vital. This would allow for easy deployments to dev, test and production environments while also making the initial deployment story on on-premises or custom deployments easier. 

## Status


## Copyright and License

Code and documentation copyright 2019 the [Resgrid Core Authors](https://github.com/Resgrid/Core/graphs/contributors) and [Resgrid, LLC.](https://resgrid.com) Code released under the [Apache License 2.0](https://github.com/Resgrid/Core/blob/master/LICENSE).