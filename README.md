# PhotoRanking

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://gitlab.aiursoft.com/anduin/photoranking/-/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/anduin/photoranking/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/anduin/photoranking/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/anduin/photoranking/badges/master/coverage.svg)](https://gitlab.aiursoft.com/anduin/photoranking/-/pipelines)
[![Man hours](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/anduin/photoranking.svg)](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/anduin/photoranking.html)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Franking.anduinlab.com)](https://ranking.anduinlab.com)
[![Docker](https://img.shields.io/docker/pulls/anduin2019/photoranking.svg)](https://hub.docker.com/r/anduin2019/photoranking)

PhotoRanking is a private, personal photo taste engine. You casually assign final scores while browsing; the app learns that taste from CLIP image embeddings and ranks the unseen library for you. It is built with ASP.NET Core, Angular, Entity Framework Core, ML.NET, and SQLite.

## Run manually

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/), Node.js 24, and Python 3.11.
2. Generate the CLIP visual model once:

   ```bash
   python3 -m pip install torch transformers onnx onnxscript
   cd scripts
   python3 export_onnx.py
   cd ..
   ```

3. Run `dotnet run --project src/Anduin.PhotoRanking/Anduin.PhotoRanking.csproj`. The build restores and compiles the Angular application automatically.
4. Open [http://localhost:5000](http://localhost:5000).

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

## Personal scoring model

The product has two photo scores:

1. **Final manual score** (`IndependentScore`, exposed as `manualScore`) is the user's source of truth on the complete 0–6 scale. A new rating replaces the previous value; repeated ratings are corrections, never votes to average.
2. **AI prediction** (`EstimatedScore`, exposed as `predictedScore`) is the model's estimate for an unrated photo. The For You feed contains only unrated photos and is ordered by this value descending.

The predictor is a versioned regression ensemble trained from one row per rated photo: the current 512-dimensional CLIP embedding and the current final manual score. One full-data model produces the score while five deterministic bootstrap members measure disagreement. A separate coverage model measures distance from visually represented rating anchors. Disagreement and coverage distance are active-learning metadata, not extra photo scores. Rating history, rating count, album score, knownness, and old overall score are not model inputs. After a quiet period, a new model is trained and predictions are refreshed in resumable batches for unrated photos only.

The prediction shown after rating is the value frozen before the first rating. This provides an unbiased online evaluation sample. Corrections do not rewrite that sample or count as new prediction evaluations.

Album values are reporting statistics only:

- `AverageManualScore` is the raw average of final manual scores in the album.
- `AlbumScore` is a Bayesian-smoothed ranking value so an album with one lucky rating does not dominate the statistics page.
- `RatedRate` is rating coverage.

None of these album values can change a photo's final score or prediction. Old schema columns and API aliases remain temporarily for database and rolling-container compatibility, but application behavior does not read them.

## Product modes

- **Surprise** is the For You feed: unrated photos ordered by personal prediction descending.
- **Enjoy** is a stable random slideshow inside a user-selected final-score range.
- **Work** prioritizes visually uncovered regions, then ensemble disagreement, album diversity, and low prior exposure. It chooses unrated photos whose new manual anchor should teach the model most.
- **Random** browses unrated photos without active-learning priority.

Advanced statistics, directory browsing, exact-score browsing, duplicate review, visual similarity, and image search remain independent tools.

The model contract, upgrade invariants, and current retrospective evaluation are documented in [docs/PERSONALIZATION.md](docs/PERSONALIZATION.md).

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.
