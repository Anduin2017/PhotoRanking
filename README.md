# PhotoRanking - A sample project

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://gitlab.aiursoft.com/anduin/photoranking/-/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/anduin/photoranking/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/anduin/photoranking/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/anduin/photoranking/badges/master/coverage.svg)](https://gitlab.aiursoft.com/anduin/photoranking/-/pipelines)
[![Man hours](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/anduin/photoranking.svg)](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/anduin/photoranking.html)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Franking.anduinlab.com)](https://ranking.anduinlab.com)
[![Docker](https://img.shields.io/docker/pulls/anduin2019/photoranking.svg)](https://hub.docker.com/r/anduin2019/photoranking)

PhotoRanking is a simple web application that allows users to upload photos and have others rank them. It is built using ASP.NET Core and Entity Framework Core, with a SQLite database for data storage.

## Run manually

Requirements about how to run

1. Install [.NET 10 SDK](http://dot.net/) and [Node.js](https://nodejs.org/).
2. Execute `npm install` at `wwwroot` folder to install the dependencies.
3. Execute `dotnet run` to run the app.
4. Use your browser to view [http://localhost:5000](http://localhost:5000).

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5` to run the app.

## Run in Docker

First, install Docker [here](https://docs.docker.com/get-docker/).

Then run the following commands in a Linux shell:

```bash
image=anduin2019/photoranking
appName=photoranking
sudo docker pull $image
sudo docker run -d --name $appName --restart unless-stopped -p 5000:5000 -v /var/www/$appName:/data -v /your-photos:/photos $image
```

That will start a web server at `http://localhost:5000` and you can test the app.

The docker image has the following context:

| Properties    | Value                           |
|---------------|---------------------------------|
| Image         | anduin2019/photoranking         |
| Ports         | 5000                            |
| Binary path   | /app                            |
| Data path     | /data                           |
| Photos import | /photos                           |
| Config path   | /data/appsettings.json          |

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.
