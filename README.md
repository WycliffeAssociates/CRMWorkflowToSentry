# CRMWorkflowToSentry
Send failed workflows to sentry

# Desciption
This tool sends failed workflows as events to sentry

# Building
The easiest way to build is to use Visual Studio to compile the solution.

# Usage
This is a command line tool so you are going to need to open an instance of command prompt or powershell in order to run it.

Usage:

`CRMWorkflowToSentry.exe --sentry=<sentrydsn> --crm=<connection-string> --days=<days> --smart `

The --smart option is optional. When it is supplied it will create a timestamp file that contains the date and time of when it last ran so it will only create events for workflows that have failed since when it last ran.

Example:

`CRMWorkflowToSentry.exe --sentry="http://dsn:dsn@sentry.domain.com/project" --crm="AuthType=Office365;Username=user@domain.com; Password=password;Url=https://environment.crm.dynamics.com" --days=1 --smart`

This example will find all of the failed workflows in the environment.crm.dynamics.com dynamics instance within the past day and only send those that have happened since the last time that I ran so that duplicates aren't created.

# Known issues
Canceled workflows look just failed workflows, so canceled workflows get picked up along with failed workflows. I have yet to get this figured out.

# Contributing
If you find a bug or need additional functionality please submit an issue. If you are feeling
generous or adventurous feel free to fix bugs or add functionlity in your own fork and send us
a pull request.