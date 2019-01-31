So, ended up moving confg elements to this project that are updated via scripts/replacements on the CI server.
The reason for this, say over Settings or Properties, which are also used in the project, is that is allows for
consistnat appliction over the Assembly\DLL files, instead of risking updates at the CI stage don't update the
DefaultSettingValueAttribute from the Designer files.

This also gives me a good place to visually see all the different configration elements and where they are used,
going through this project to make it ready for OSS I found that there were so many different configration options
and they were located in different places it was hard to keep track of all of them.

Application and Web projects will still use the web.config, app.config and settings.json files.
