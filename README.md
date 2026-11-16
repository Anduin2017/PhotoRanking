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
2. **AI prediction** (`EstimatedScore`, exposed as `predictedScore`) is the model's estimate for an unrated photo. The For You feed contains only unrated photos. Each browsing session uses a new seed to perform a stable prediction-weighted rotation inside the high-quality candidate pool, so scrolling is duplicate-free while reopening the feed produces a fresh selection.

The predictor is a versioned regression ensemble trained from one row per rated photo: the current 512-dimensional CLIP embedding and the current final manual score. One full-data model produces the score while five deterministic bootstrap members measure disagreement. A separate coverage model measures distance from visually represented rating anchors. Disagreement and coverage distance are active-learning metadata, not extra photo scores. Rating history, rating count, album score, knownness, and old overall score are not model inputs. After a quiet period, a new model is trained and predictions are refreshed in resumable batches for unrated photos only.

The prediction shown after rating is the value frozen before the first rating. This provides an unbiased online evaluation sample. Corrections do not rewrite that sample or count as new prediction evaluations.

Album values are reporting statistics only:

- `AverageManualScore` is the raw average of final manual scores in the album.
- `AlbumScore` is a Bayesian-smoothed ranking value so an album with one lucky rating does not dominate the statistics page.
- `RatedRate` is rating coverage.

None of these album values can change a photo's final score or prediction. Old schema columns and API aliases remain temporarily for database and rolling-container compatibility, but application behavior does not read them.

## Product modes

- **Surprise** is the For You feed: unrated, high-prediction photos with deterministic per-session rotation for freshness.
- **Enjoy** is a stable random slideshow inside a user-selected final-score range.
- **Work** prioritizes visually uncovered regions, then ensemble disagreement, album diversity, and low prior exposure. It chooses unrated photos whose new manual anchor should teach the model most.
- **Random** browses unrated photos without active-learning priority.

Advanced statistics, directory browsing, exact-score browsing, duplicate review, visual similarity, and image search remain independent tools.

The model contract, upgrade invariants, and current retrospective evaluation are documented in [docs/PERSONALIZATION.md](docs/PERSONALIZATION.md).

## Three-prompt photo summaries

In **Advanced**, edit three prompts and save them. The background worker sends the
photo with the first prompt, then carries the image and all previous questions and
answers into the next two rounds. All three answers are stored together, with the
third answer retained as the photo's final summary for compatibility. For example,
use the first two prompts to describe and interpret a photo,
then ask the third prompt to write a poem.

The coverage slider defaults to the top 3% of the whole library, ranked by
`DisplayScore = IndependentScore ?? EstimatedScore` (manual score takes precedence,
including zero). The target size is rounded up; ties use photo ID. Photos with
neither score wait until a score becomes available. Increasing coverage fills the
new range; decreasing it keeps existing summaries. Set coverage to 0% to pause new
work. The worker checks every 10 seconds when idle, processes photos serially, and
persists each completed photo so restarts resume missing work. A failed photo is
retried on the next sweep without blocking other photos.

Saving changed prompts immediately invalidates every old summary. Only photos in
the current target range are regenerated. UTC prompt-modified and summary-generated
timestamps determine freshness; a final atomic check prevents a response using old
prompts from being published after an edit. Saving identical prompts or changing
only coverage does not invalidate summaries. A useful workflow is to tune prompts
at 1%, then increase coverage to 40% once satisfied.

The viewer plays the saved answers in reverse order: third, second, first, then
repeats. Each answer starts scrolling after two seconds at one rendered line every
two seconds, scrolls to its end and remains there for four seconds before
the next one starts. Use the round buttons to select an answer directly; reduced
motion disables automatic playback while keeping these buttons available. Unchanged
background refreshes preserve the current answer and reading position.

Existing photos with only a final answer remain viewable. The background worker
regenerates their full three-answer conversation within the current coverage range,
then atomically replaces all three answers. Shrinking coverage still keeps existing
answers outside the target. Progress counts photos with all three current answers.

The viewer displays summaries as gently scrolling text beside the photo, with three
clear lines and four fading lines. Drag vertically, use the mouse wheel, or focus
the text and use the keyboard to read at your own pace. Automatic scrolling pauses
after interaction and respects reduced-motion preferences. The bottom-bar **显示图片摘要**
checkbox is enabled by default; photos without a current summary show **此图片尚未摘要**.

The bottom-bar **自动播放** checkbox is off by default. Enable it to generate and
read the current third-round answer once per photo. It stays on the third round
until narration ends, then resumes the text cycle. Selecting an earlier round,
changing photos, closing the viewer or disabling playback stops the audio. If the
browser blocks playback, use **点击播放**; failed requests offer **重试语音**.
Speech is generated only on demand, through the authenticated PhotoRanking API;
existing IndexTTS caching avoids repeating identical synthesis. Changed or expired
summaries are rejected before and after generation.

Configure the server's existing `/data/appsettings.json` (or environment variables):

```json
"Summary": {
  "Endpoint": "https://ollama.aiursoft.com/api/chat",
  "Model": "aiursoft-localization:latest",
  "ApiKey": "YOUR_DEDICATED_GATEWAY_KEY"
}
```

The equivalent environment keys are `Summary__Endpoint`, `Summary__Model`, and
`Summary__ApiKey`. Environment variables override `appsettings.json`; recreate the
container after changing its environment. `Endpoint` is the complete Ollama chat URL
(including `/api/chat`), `Model` is the gateway model name, and `ApiKey` is sent as a
Bearer token. For example:

```yaml
environment:
  Summary__Endpoint: https://ollama.aiursoft.com/api/chat
  Summary__Model: aiursoft-localization:latest
  Summary__ApiKey: ${PHOTORANKING_AI_TOKEN}
```

Keep the token in the deployment's private environment file, outside Git. An `.env`
file keeps secrets separate from source control; it does not encrypt them. Generation
waits until all three settings are valid. Advanced settings refresh progress and up
to six recent, current summary examples every ten seconds. Click an example to open
the photo and read its summary. Prompt edits also immediately hide stale examples.
Credentials are
never returned by the settings API. PhotoRanking remains a private library behind
its existing login proxy; these settings apply to that library. Generation sends a
JPEG preview (at most 1536 pixels on the longest side, without source metadata) to
the configured model endpoint. Keep the server behind authenticated access as for
the existing photo and rating APIs.

Speech uses the native IndexTTS form endpoint so inference does not block its
health checks. Configure these server settings; the key is never sent to the browser:

```json
"Tts": {
  "Endpoint": "https://tts.aiursoft.com/api/tts",
  "Model": "broadcasting",
  "Voice": "",
  "ApiKey": "YOUR_TTS_KEY"
}
```

Use `Tts__Endpoint`, `Tts__Model`, `Tts__Voice` and `Tts__ApiKey` for environment
overrides. For this single-engine service, `Model` names the voice profile used by
Yuefu's reader (`broadcasting`); optional `Voice` overrides that profile. The native
request sends `voice` and the stored third-round text, and receives WAV audio.
Keep `ApiKey` in the deployment's private `.env.prod`; missing configuration leaves
text summaries available and reports speech as unavailable when requested. There
is no audio database migration or new background generation job.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.
