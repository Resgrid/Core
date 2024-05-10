<p align="center">
  <a href="https://resgrid.com/">
    <img src="https://raw.githubusercontent.com/resgrid/core/master/misc/images/Resgrid_TextLogo.png" alt="Resgrid logo">
  </a>
</p>

<h3 align="center">Resgrid Core</h3>

<p align="center">
  Complete and open source computer aided dispatch, management and logistics for first responders, disaster response, emergency management and companies
  <br>
  <a href="https://docs.resgrid.com/"><strong>View Resgrid docs</strong></a>
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
- [Hosted](#hosted)
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


![Resgrid Main Screen](https://raw.githubusercontent.com/resgrid/core/master/misc/images/ResgridIntro.gif)

## Hosted

If you don't want to run your own instance of Resgrid, we provide a hosted version with both free and paid plans. The same code base provided here runs
the hosted version as well and we update the system every few weeks with the latest features and fixes.

[Sign up for your free Resgrid Account Today!](https://resgrid.com)

## Applications

Below are the repositories for the applications built utilizing the [Resgrid API](https://api.resgrid.com).

###### [BigBoard](https://github.com/Resgrid/BigBoard)
The Resgrid BigBoard is a dashboard system intended to be used in stations or centralized locations to allow for personnel to see the status of the system at a glance. The BigBoard can display Personnel Statuses, Staffing and ETAs, Unit Statuses, Call and Call Information, a map of all activity, and current weather.

![BigBoard](https://raw.githubusercontent.com/resgrid/core/master/misc/images/BigBoard.png)

###### [Relay](https://github.com/Resgrid/Relay)
The Resgrid Relay is a console based application to monitor an audio input, for example from a Scanner, to listen for Tone Frequencies and capture audio for a selected amount of time. Once complete Resgrid Relay will create a call in the Resgrid system and dispatch the groups, or department, associated with the tones. This allows for standing up Resgrid in environments where you cannot modify a dispatch system or a shared dispatch center.

![Relay](https://raw.githubusercontent.com/resgrid/core/master/misc/images/Relay.png)

###### [Dispatch](https://github.com/Resgrid/Dispatch)
The Resgrid is web application that allows Dispatchers a single UI to create calls, set unit statuses, modify and close calls, and monitor activities without ever leaving that single screen. The Dispatch application is intended to streamline live dispatcher operations and avoid excess page navigations and manual page refreshing to get current statuses.

![Dispatch](https://raw.githubusercontent.com/resgrid/core/master/misc/images/Dispatch.png)

## Status

[![CodeFactor](https://www.codefactor.io/repository/github/resgrid/core/badge)](https://www.codefactor.io/repository/github/resgrid/core)
[![996.icu](https://img.shields.io/badge/link-996.icu-red.svg)](https://996.icu)
<a href="https://discord.gg/YDs7tHB"><img src="https://img.shields.io/badge/discord-join-7289DA.svg?logo=discord&longCache=true&style=flat" /></a>
<a href="https://github.com/Resgrid/Core/blob/master/LICENSE"><img src="https://img.shields.io/github/license/resgrid/core.svg" alt="License" /></a>

## Priority Support

We provide support for all of Resgrid's open-source products via GitHub issues without charge. Me make our best effort to address and close issues in a timely fashion. If your organization needs priority support for critical issues please take a look at our Priority Support packages on our Open-Source page.

[View Paid Support Options](https://resgrid.com/Home/OpenSource)


## Copyright and License

Code and documentation copyright 2021 the [Resgrid Core Authors](https://github.com/Resgrid/Core/graphs/contributors) and [Resgrid, LLC.](https://resgrid.com) Code released under the [Apache License 2.0](https://github.com/Resgrid/Core/blob/master/LICENSE).