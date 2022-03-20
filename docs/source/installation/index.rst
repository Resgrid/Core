#######
Installation
#######

In this section we will go over all the steps needed to get Resgrid running on your own environment. 

.. _requirements:

Requirements Notice
****************************

It is highly recommended that Resgrid is installed and setup by an IT Professional. There is a large amount of system configuration, tweaking and setup that is required to be done before you install Resgrid. Below is a list of technologies that you should have skilled professionals available to you or requisite knowledge before installing Resgrid. Resgrid does not provide support or configuration guidance for those systems outside of the minimum needed to get the system functional. The steps outlined below will get the system in a bare minimum functional state to ensure it's working on your enviroment, to be production ready will reqire more effort then is outlined in this documentation.

* Windows or Linux
* Docker, Kubernetes
* SQL Server or PostgreSQL
* DNS, hostname mapping, proxy configuration
* RabbitMQ
* Redis
* Elastic
* Mail Server SMTP, POP3
* Firewall and system hardning

.. _system_requirements:

System Requirements
****************************

The all-in-one docker installation is suitable for a deparment of around 50 personnel on a machine with 32GB of RAM, 500GB of storage and a 8 logical processors. But depending on call volume or user ineraction patterns may require more.

We do not recommend that mission critial systems be installed on a single machine. Resgrid is split into multiple containers to allow for multiple machines to be used.

A mission-critial production environment will require a minimum of 10 servers:
* 2 Load Balanced Web servers
* 2 Load Balanced API servers
* 1 Microsoft Sql Server
* 1 Worker server
* 1 Events server
* 1 Redis server
* 1 RabbitMQ server
* 1 Elasticsearch server (ELK)

Sizing of these servers will depend on your departments amount of users and call volume.

.. _installation_prerequisites:

Prerequisites & Dependencies
****************************

To run the Resgrid containers you will need the following:

* Docker
Install `Docker <https://docker.com/>`, either using a native package or Docker Desktop.

.. note:: All Resgrid container images are based on Linux, users of Docker for Windows will need to ensure that `Docker is using Linux containers <https://docs.docker.com/docker-for-windows/#switch-between-windows-and-linux-containers>`.

* A minimum of 24GB RAM assigned to Docker

	With Docker for Mac, the amount of RAM dedicated to Docker can be set using the UI: see `How to increase docker-machine memory Mac <http://stackoverflow.com/questions/32834082/how-to-increase-docker-machine-memory-mac/39720010#39720010>`.

	In Docker Desktop for Windows, `use the *Advanced* tab to adjust limits on resources available to Docker <https://docs.docker.com/docker-for-windows/#:~:text=Memory%3A%20By%20default%2C%20Docker%20Desktop,swap%20file%20size%20as%20needed>`.

* A limit on mmap counts equal to 262,144 or more
  
  On Linux, use `sysctl vm.max_map_count` on the host to view the current value, and see `Elasticsearch's documentation on virtual memory <https://www.elastic.co/guide/en/elasticsearch/reference/5.0/vm-max-map-count.html#vm-max-map-count>` for guidance on how to change this value. Note that the limits **must be changed on the host**; they cannot be changed from within a container.

	If using Docker for Mac, then you will need to start the container with the `MAX_MAP_COUNT` environment variable (set to at least 262144 (using e.g. `docker`'s `-e` option) to make it sets the limits on mmap counts at start-up time.

* Docker Compose 
Install `Docker Compose <https://docs.docker.com/compose/install/>`

* Open Ports 5151 through 5165
* SMTP Server for sending email

.. note:: Any correctly configured SMTP server will work if it's local or not. If you have an SMTP server provided by your ISP or provider that will also work.

.. _docker_compose:

Docker Compose Setup
****************************

Download and Extract Package
================

Download the resgrid.tgz Asset file from the latest `Resgrid GitHub Release <https://github.com/Resgrid/Core/releases>`::

  wget https://github.com/Resgrid/Core/releases/download/vXX.XX.XX/resgrid.tgz

.. note:: Esnure you replace vXX.XX.XX in that url to the version number of the Github release you are trying to download.

Extract the tgz package file::

  tar -xvzf resgrid.tgz

You should now have a folder called resgrid in your current directory.

Setting Enviorment Variables
================

Resgrid's docker containers are configured using enviorment variables defined in the ``resgrid.env`` file within the resgrid folder. Edit this file and configure the variables as needed for your enviorment. Please pay speical attention to the the (required) variables.

Run the Docker Compose
================
Once you have setup the enviorment variables you can now run the docker compose file.::

  docker-compose up

That will run the ineractive version of the containers, Crtl+C will stop the containers.

If you want to run the containers in the background, use the `-d` option::

  docker-compose up -d

The Resgrid system will take about 5 minutes to start up fully, this is due to the startup order of the containers. The last container to startup will be the `web` container, once that one is ready, you can now access the system.

Important Note About Support
****************************

Resgrid is a complex system that can scale from a single instance to dozens of systems to service thousands of users. These installation setups get your system into a state where you can test and validate locally on the install system. To get Resgrid up and running to service non-local users you will need to reconfigure and harden the system. To complete those steps and configuration the system to your orginizational needs you will require an IT professional. We do not provide installation support outside this guide via our Github page.

Initial Web Login
****************************

Once you have completed the steps above you will be able to log into the web applications user interface. Open up a web browser and navigate to http://localhost:5151, you will then be prompted by the login screen. Your default administrator credentials are **admin/changeme1234**. Once you log into the system it's recommended that you change your admin password from the Edit Profile page by clicking on the Administrator name in the upper left hand corner. 

