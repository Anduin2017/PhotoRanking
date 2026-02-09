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

## Core Scoring Algorithm

PhotoRanking implements a sophisticated multi-layered scoring system that combines user ratings with AI-powered predictions to evaluate photo quality.

### Three Core Score Types

1. **Independent Score** (`IndependentScore`)
   - Direct user rating for a single photo (0-6 scale)
   - Represents the photo's intrinsic quality
   - Becomes `null` if the photo has never been rated
   - **Fixation mechanism**: If the last 3 consecutive ratings are identical, the photo is marked as `IsFixed = true` and the score is locked

2. **Album Score** (`AlbumScore`)
   - Represents the overall quality of the entire album
   - Calculated using the **top 20% highest-scored photos** in the album
   - **Formula**:
     ```
     For all photos in album:
       - Rated photos use IndependentScore
       - Unrated photos use unratedScore = max(0, avgRated - 1)
     
     Sort all photos by score (descending)
     Take top 20% (at least 1 photo)
     AlbumScore = average of top 20% photos
     ```
   - Defaults to `2.5` if no photos have been rated yet
   - This approach emphasizes the album's best content rather than being dragged down by lower-quality photos

3. **Overall Score** (`OverallScore`)
   - The final score used for sorting and recommendations
   - **Formula**:
     ```
     If photo has IndependentScore:
       OverallScore = IndependentScore × 0.7 + AlbumScore × 0.3
     Else:
       OverallScore = AlbumScore
     ```
   - Balances individual photo quality (70%) with album context (30%)

### Feedback Loop

The scoring system forms a **bidirectional feedback loop**:

```
User rates photo → Updates IndependentScore
                 ↓
        Recalculates AlbumScore
                 ↓
    Updates OverallScore for ALL photos in the album
```

When you rate a photo highly, it raises the album's average, which in turn increases the overall scores of all unrated photos in that album.

### AI-Powered Score Prediction

For unrated photos, the system can predict scores using a **Stratified KNN (K-Nearest Neighbors) algorithm** based on image similarity:

1. **Stratified Sampling**: Uses SQL window functions to select the top 20 most similar photos from each score tier (0-6), preventing bias from overrepresented high-score photos

2. **Top-K Similarity Matching**: For each score tier, calculates cosine similarity between feature vectors and takes the **average of the top 3** most similar photos (not all photos), ensuring that even rare cases are properly weighted

3. **Non-linear Confidence Amplification**: Applies `similarity^30` to dramatically amplify small differences in similarity (e.g., 0.80 vs 0.85 becomes a 1:10 ratio instead of 1:1)

4. **SmoothStep Smoothing**: Applies Hermite interpolation to the predicted score for more natural distribution:
   ```
   t = rawScore / 6.0
   smoothed = t² × (3 - 2t)
   finalScore = smoothed × 6.0
   ```
   This makes predicted scores more natural, avoiding clustering at integer values

### Knownness Score

Beyond quality scores, each photo has a **Knownness** metric (0-100) that affects its recommendation priority:

```
If photo is fixed (last 3 ratings identical):
  Knownness = 50 + albumKnownRate × 50

Else:
  ratingScore = min(ratingCount, 5) × 10      // Max 50 points
  albumScore = albumKnownRate × 50             // Max 50 points
  Knownness = ratingScore + albumScore
```

Where `albumKnownRate = ratedPhotosCount / totalPhotosCount`

This encourages the system to show both under-rated photos (low knownness) and uncertain photos (not yet fixed) to the user.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.
