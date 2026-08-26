# Personalization design

## Product contract

There are exactly two photo scores:

- `IndependentScore` / `manualScore`: the current final human judgment.
- `EstimatedScore` / `predictedScore`: the AI estimate made before the photo was first rated.

Everything else is metadata or reporting. In particular, prediction uncertainty, visual novelty, album averages, coverage rates, view counts, and legacy schema fields must never alter either score.

A correction overwrites the manual score. It does not create a vote, increase confidence, average history, or move the frozen pre-rating prediction. `RatingLog` is an audit/evaluation stream, not training data. The trainer always reads one current row per rated photo.

## Prediction lifecycle

1. The image pipeline emits a 512-dimensional L2-normalized CLIP vector.
2. After 20 minutes without a rating, the worker takes a rating watermark and trains from final scores at or before it.
3. An SDCA ridge-regression head trained on the latest 8,000 final ratings produces the public prediction. This window tracks current taste and scoring calibration; CLIP vectors enter it in their native unit-normalized geometry.
4. Five deterministic bootstrap SDCA models estimate model disagreement.
5. L2-normalized K-means centroids trained from all valid historical anchors describe the parts of visual space already covered. Distance to the nearest centroid is the novelty signal.
6. The complete versioned model bundle is stored in SQLite. Unrated photos are refreshed in batches and tagged with that version, so a container restart resumes rather than starts over. A changed algorithm or embedding version deliberately invalidates the bundle and retrains after upgrade.
7. Rated photos are never refreshed. Their last pre-rating AI estimate remains available for comparison and unbiased online evaluation.

The Surprise feed is strictly unrated photos sorted by prediction descending. The Work queue is strictly unrated and prioritizes visual novelty, with ensemble disagreement as a secondary signal, one-photo-per-album diversity on the first pass, and a penalty for repeatedly viewed candidates.

## Evaluation

Offline model selection must use a chronological holdout, not a random split: train on older final ratings and test on newer ones. The primary metric is mean absolute error (MAE); secondary product metrics are within-one-point rate, top-of-feed precision, score-distribution calibration, and coverage by album/content region.

The read-only production retrospective contained 13,966 rated photos and 399,471 CLIP-indexed photos at benchmark time. On the same chronological 80/20 split:

| Predictor | Holdout MAE |
|---|---:|
| Previous balanced similarity/KNN path | 0.780 |
| Full-history embedding ridge regression | 0.607 |
| Recent-8,000 embedding ridge regression | 0.585 |

An active-learning replay trained on the first 60%, selected 500 anchors from the next 20%, and evaluated the final 20%:

| Anchor selection | Final holdout MAE |
|---|---:|
| Random, five-run range | 0.632–0.635 |
| Ensemble disagreement + album diversity | 0.631 |
| Visual novelty + album diversity | 0.628 |

This is retrospective evidence, not a promise of online performance. The application therefore freezes predictions at first rating and reports live evaluation count, MAE, and within-one-point rate. Future model changes should beat the deployed model on both chronological replay and fresh frozen predictions before replacing it.

## Upgrade invariants

- Existing photo files, final scores, old predictions, and rating logs are never rewritten by a migration.
- Schema evolution is additive. Legacy `OverallScore`, `Knownness`, `RatingCount`, `IsFixed`, and API aliases remain inert for rolling-container compatibility.
- Startup applies EF Core migrations to the mounted `/data/app.db` before background work begins.
- A newly trained model only refreshes unrated rows. Training and refresh are resumable by model version and rating watermark.
- If fewer than 20 valid rated embeddings exist, the app keeps working without a personal model and waits for more anchors.
