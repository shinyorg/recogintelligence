# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Three on-device intelligence stacks for .NET (MAUI + native), built on the Shiny stack:

1. **Face intelligence** — face **enrollment + recognition**. "Training" here is **enrollment**, not model training: you store ArcFace embeddings per person and recognition is a nearest-neighbor vector lookup with a cosine-distance threshold. No model is trained on-device. Split into a **core abstractions package** plus **swappable embedder and store packages**, composed via a registration builder, so a consumer pulls only what they use (e.g. a platform-native embedder without ONNX, or Postgres/pgvector server-side without sqlite).
2. **Voice intelligence** — speaker (voice biometric) **enrollment + recognition**, the audio twin of the face stack: store ECAPA-TDNN speaker embeddings ("voiceprints") per person, recognition is the same nearest-neighbor vector lookup + cosine-distance threshold. Same four-package split (core + `.Onnx` + `.DocumentDb` + `.DocumentDb.Sqlite`), same registration-builder shape. See [Voice intelligence](#voice-intelligence-speaker-recognition--shinyvoiceintelligence).
3. **Document intelligence** — a native **modal document scanner** (`Shiny.DocumentIntelligence`) behind a single `IDocumentScanner`. Multi-targeted with a platform-native implementation per OS; no MAUI dependency (it gets platform context natively).

Note on naming: types/namespaces use **FaceIntelligence** as the product brand (`Shiny.FaceIntelligence`, `IFaceIntelligence`, `FaceIntelligenceManager`, `AddFaceIntelligence`), but the **operation verbs and result types keep their recognition names** — `Enroll`/`Recognize` and `RecognitionResult`/`FaceMatch` describe the action and data, not the brand. The concrete orchestrator is `FaceIntelligenceManager` (not `FaceIntelligence`, which would collide with the namespace). Solution: `Recognition Intelligence.slnx`. The **voice** stack follows the identical convention (`Shiny.VoiceIntelligence`, `IVoiceIntelligence`, `VoiceIntelligenceManager`, `AddVoiceIntelligence`; verbs `Enroll`/`Recognize`; results `RecognitionResult`/`VoiceMatch`; document `Speaker`; embedder `ISpeakerEmbedder`).

| Project | TFM(s) | Role / deps |
|---|---|---|
| `src/Shiny.FaceIntelligence` | `net10.0` | **Core** package: contracts (`IFaceEmbedder`, `IFaceStore`, `IFaceIntelligence`), `Person`, `FaceBox`, `RecognitionResult`, `FaceMatch`, `FaceImaging`, `FaceIntelligenceManager` orchestration, and `FaceIntelligenceRegistrationBuilder` + `AddFaceIntelligence`. Deps: SkiaSharp + DI.Abstractions only. **No ONNX, no DocumentDb.** |
| `src/Shiny.FaceIntelligence.Onnx` | `net10.0` | `OnnxArcFaceEmbedder` + `UseOnnxEmbedder`. Deps: core + Microsoft.ML.OnnxRuntime. **Ships the iOS linker targets.** |
| `src/Shiny.FaceIntelligence.DocumentDb` | `net10.0` | `DocumentDbFaceStore` + `UseDocumentDbStore(providerFactory)`. Provider-agnostic; deps: core + Shiny.DocumentDb (abstractions). |
| `src/Shiny.FaceIntelligence.DocumentDb.Sqlite` | `net10.0` | Turnkey `UseSqliteStore` (sqlite-vec). Deps: `.DocumentDb` + Shiny.DocumentDb.Sqlite. |
| `src/Shiny.FaceIntelligence.Maui` | `net10.0-android;net10.0-ios;net10.0-maccatalyst` | **Live face controls**: `FaceRecognitionView` (continuous identify) + `FaceEnrollmentView` (guided multi-shot wizard), `FaceRecognitionAnalyzer : FrameAnalyzer`, `AnalyzedFace`/`FaceRecognizedEventArgs`/`FaceEnrollmentStep`, `FrameQuality`, plus the per-platform `CameraFrame`→upright-RGB bridge. Deps: face core + Shiny.Maui.Controls.Camera. Multi-targeted because the frame arrives as a native buffer per OS. See [Live recognition](#live-recognition-the-frame-analyzer-shinyfaceintelligencemaui). |
| `src/Shiny.VoiceIntelligence` | `net10.0` | **Core** (voice): contracts (`ISpeakerEmbedder`, `IVoiceStore`, `IVoiceIntelligence`), `Speaker`, `RecognitionResult`, `VoiceMatch`, `VoiceIntelligenceManager`, the guided-enrollment wizard (`VoiceEnrollmentSession` + `VoiceEnrollmentOptions`/`VoiceQuality`), `VoiceIntelligenceRegistrationBuilder` + `AddVoiceIntelligence`. Deps: **DI.Abstractions only** (no SkiaSharp — audio has no image stage). **No ONNX, no DocumentDb, no audio capture.** |
| `src/Shiny.VoiceIntelligence.Onnx` | `net10.0` | `OnnxEcapaEmbedder` (waveform **and** fbank models, auto-detected) + `KaldiFbank` + `UseOnnxEmbedder`. Deps: voice core + Microsoft.ML.OnnxRuntime. **Ships the iOS linker targets** (target name suffixed `_Voice` so it coexists with the face `.Onnx` targets — see below). |
| `src/Shiny.VoiceIntelligence.DocumentDb` | `net10.0` | `DocumentDbVoiceStore` + `UseDocumentDbStore(providerFactory)`. Provider-agnostic; deps: voice core + Shiny.DocumentDb. |
| `src/Shiny.VoiceIntelligence.DocumentDb.Sqlite` | `net10.0` | Turnkey `UseSqliteStore` (sqlite-vec). Deps: voice `.DocumentDb` + Shiny.DocumentDb.Sqlite(.VectorSupport). |
| `src/Shiny.VoiceIntelligence.Maui` | `net10.0-android;net10.0-ios;net10.0-maccatalyst` | **Guided voice enrollment control**: `VoiceEnrollmentView` (shows the sentence list, records each one, runs until the voiceprints agree) + the `IVoiceRecorder` seam the app implements. Deps: voice core + Microsoft.Maui.Controls. **No platform code and no audio capture** — see [the control](#the-voice-enrollment-control-shinyvoiceintelligencemaui). |
| `src/Shiny.DocumentIntelligence` | `net10.0;net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-macos` | **Native document scanner**: `IDocumentScanner` + `AddDocumentIntelligence`. VisionKit (iOS/Catalyst), ML Kit (Android), Vision segmentation (macOS AppKit), throwing stub (bare net10.0). Dep: DI.Abstractions (+ ML Kit binding on Android only). See [Document scanning](#document-scanning-shinydocumentintelligence). |
| `Sample` (repo root) | `net10.0-android;net10.0-ios` (`maccatalyst` commented out; `windows` only on a Windows host) | MAUI app, **MVVM via Shiny.Maui.Shell**, organized **by feature** (`Features/{Face,Voice,Documents}/`), each with a per-feature `IMauiModule` (`Shiny.Extensions.MauiHosting` → `AddInfrastructureModules(...)`). Tabs: **Recognize/Enroll/People** (face) · **Voice ID/Voice Enroll/Speakers** (voice) · **Scan** (page↔ViewModel maps, `ShinyShell`). Refs face+voice `.Onnx` + `.Sqlite` + `.DocumentIntelligence` (under `..\src\`) via `ProjectReference`. See [Sample structure](#sample-structure-feature-folders). |

**Repo layout**: shippable packages in `src/`, the demo app in `Sample/` at the root, tests/benchmarks in `tests/`. **Central Package Management** is on — all versions live in `Directory.Packages.props` (CPM), so `<PackageReference>` elements carry **no `Version=`**; add/bump versions there. `Directory.Build.props` hoists the shared `ImplicitUsings`/`Nullable` (per-project settings like `TargetFramework`, `IsAotCompatible`, `IsPackable` stay in each csproj). Note: with two feeds + CPM, restore emits `NU1507` (package-source-mapping advisory) — benign.

**Two version pins are deliberate (don't "update" them blindly)** — both are documented inline in `Directory.Packages.props`:
- **`Microsoft.ML.OnnxRuntime` is held at `1.20.1`.** ORT `1.27.0` (and other newer revs) break the **Android** manifest merge with `AMM0000` "namespace used in multiple modules" (duplicate `com.google.*` namespaces) on the .NET Android 36.x manifest merger — the app won't build. iOS/tests are fine; the block is Android-only. Revisit when ORT ships an Android AAR compatible with the current merger.
- **`Microsoft.Maui.Controls` is held at `10.0.71`** to match the prerelease **Shiny camera betas** (`Camera`/`Camera.Face` 0141, `Controls` 0121). Bumping MAUI (e.g. 10.0.80) and/or the camera betas (0142) shifts transitive AndroidX/play-services/firebase versions and re-triggers the same `AMM0000` Android breakage. Bump MAUI and the camera package **together**, and only after verifying the Android head. Everything else in CPM tracks latest stable (xunit v3 4.0 / BenchmarkDotNet 0.16 are prerelease-only, so those stay on latest stable too).

## Build & run

```bash
# Everything (solution name has a space in it)
dotnet build "Recognition Intelligence.slnx"

# What CI builds: the 11 shippable packages only, packed on build (see Packaging & CI)
dotnet build build.slnf /restore -m -property:Configuration=Release -property:PublicRelease=true

# Sample app heads (Sample/ is at the repo root, not under src/)
dotnet build Sample/Sample.csproj -f net10.0-android
dotnet build Sample/Sample.csproj -f net10.0-ios
```

```bash
# End-to-end tests (real sqlite-vec store + fake embedder)
dotnet test tests/Shiny.FaceIntelligence.Tests/Shiny.FaceIntelligence.Tests.csproj

# Run one test
dotnet test tests/Shiny.FaceIntelligence.Tests/Shiny.FaceIntelligence.Tests.csproj \
  --filter "FullyQualifiedName~MaxDistance_Threshold_IsEnforced"

# Benchmarks (BenchmarkDotNet — Release only). --job dry validates the harness fast.
dotnet run --project tests/Shiny.FaceIntelligence.Benchmarks -c Release
dotnet run --project tests/Shiny.FaceIntelligence.Benchmarks -c Release -- --job dry
```

## Debugging the Sample on a physical iPhone (MAUI DevFlow)

The Sample has **MAUI DevFlow** wired in so an agent can drive/inspect the running app (visual tree, logs, network, taps, screenshots). Registration is `builder.AddMauiDevFlowAgent()` in `MauiProgram.cs` inside `#if DEBUG`; package `Microsoft.Maui.DevFlow.Agent` in CPM, **pinned to the installed `maui` CLI version** (`maui devflow version` — mismatched CLI/agent versions won't connect). Standard MAUI app → no Blazor/GTK DevFlow packages.

**Test device is the physical "ACR iPhone"** (iPhone 15 Pro, UDID `00008130-000E243E3AD1001C`), **not the simulator** — the app *crashes at launch on the iOS simulator* (`ArgumentNullException` via `ObjCRuntime.Runtime.RethrowManagedException`) but runs fine on-device.

The connection recipe that actually works (each step matters — the obvious paths fail):

```bash
# 1. Build for the device RID. Do NOT use -t:Run here — mlaunch --installdev HANGS and installs nothing.
dotnet build Sample/Sample.csproj -f net10.0-ios -p:RuntimeIdentifier=ios-arm64

# 2. Install + launch with devicectl (reliable; gives real errors, unlike mlaunch).
xcrun devicectl device install app --device 00008130-000E243E3AD1001C \
  Sample/bin/Debug/net10.0-ios/ios-arm64/Sample.app
xcrun devicectl device process launch --terminate-existing \
  --device 00008130-000E243E3AD1001C org.shiny.recogiq

# 3. Tunnel the in-app agent port over USB. The broker only auto-forwards for Android
#    (its --device flag is an ADB serial); a physical iPhone needs iproxy (brew install libimobiledevice).
iproxy 9223 9223 -u 00008130-000E243E3AD1001C &

# 4. Drive it. Use --platform ios so the CLI talks to localhost:9223 (the tunnel).
maui devflow agent status --platform ios      # => "running": true
maui devflow ui tree --platform ios --depth 2
maui devflow logs --platform ios
```

Notes: the tunnel (`iproxy`) must stay up for the whole session — relaunching the app or unplugging the phone drops it, just restart iproxy. `maui devflow list` stays `[]` (that's broker auto-discovery, which isn't used on this path); rely on `agent status --platform ios` instead. The **Android head does not build** (ORT/MAUI pin violations, see `Directory.Packages.props`), so device debugging is iOS-only for now.

## Tests & benchmarks

`tests/` mirrors the sibling `~/Desktop/dev/DocumentDb` repo's conventions: **xUnit v3**, and the sqlite-vec native binary committed at `tests/runtimes/osx-arm64/native/vec0.dylib` (osx-arm64 only — CI must provision other RIDs). Three projects:

- `Shiny.FaceIntelligence.TestKit` — shared, no test deps: `FakeEmbedder` (deterministic `IFaceEmbedder`), `TestFaces`, `Vec0Locator`.
- `Shiny.FaceIntelligence.Tests` — E2E. Builds an `IFaceIntelligence` through the **real builder wiring** (`AddFaceIntelligence(face => { face.UseEmbedder(new FakeEmbedder(dim)); face.UseSqliteStore(...); })`) against the **real sqlite-vec store** (file-backed temp DB), fake embedder for controlled geometry. Refs core + `.Sqlite` + TestKit (no ONNX). Covers match/no-match, the cosine-distance threshold (confirms sqlite-vec's `0 = identical` convention the code relies on), multi-shot nearest-neighbor, the name-as-identity conflation (the TODO), `Forget`/`GetAll`, and that the vector dimension is read from the embedder.
- `Shiny.FaceIntelligence.Benchmarks` — BenchmarkDotNet; `Recognize` latency vs gallery size (100/1k/10k) at the real 512-d width.
- `Shiny.VoiceIntelligence.TestKit` / `Shiny.VoiceIntelligence.Tests` — the voice twins. Same shape (`AddVoiceIntelligence(voice => { voice.UseEmbedder(new FakeSpeakerEmbedder(dim)); voice.UseSqliteStore(...); })` against the real sqlite-vec store), and they **share the same committed `tests/runtimes/.../vec0` binary**. 29 tests, all passing: match/no-match, empty store, cosine-distance geometry, threshold enforcement, multi-utterance nearest-neighbor, `Forget`/`GetAll`, a 192-d dimension round-trip, `KaldiFbankTests`, plus the guided wizard (`VoiceEnrollmentSessionTests` — completion, nothing-stored-until-done, outlier rejection, bad-first-clip rescue, give-up-and-prune) and `VoiceQualityTests` (synthesized tone/silence/noise/clipping). **No fake-image trick needed** (see below) — the voice manager does no decode, so `FakeSpeakerEmbedder` reads the sample buffer directly as the vector and `TestVoices.Utterance(...)` just hands a vector in.

Key design points when extending:
- **The fake-image trick**: `FaceIntelligenceManager.Enroll` decodes the image with SkiaSharp for its thumbnail, independent of the embedder. So `TestFaces.Image(...)` produces a *real* PNG with the embedding appended as a trailing payload — Skia decodes the PNG, `FakeEmbedder` reads the trailing block. Don't pass raw float buffers as "images"; enrollment will throw on decode.
- **vec0 is required at runtime**; tests `Assert.Skip` when `Vec0Locator.Find()` returns null (except `Vec0Binary_IsAvailable_OnDeveloperMachine`, which asserts presence to document the dependency). `Vec0Locator` searches next to the assembly and walks up to the committed `runtimes/` folder so BenchmarkDotNet's generated subprocess finds it.
- These need **no ONNX model** — the model only matters for the real embedder, which isn't exercised here.

`nuget.config` clears sources and restores from **nuget.org only** (the dnceng `dotnet10` feed it once carried is gone — everything the repo needs now ships publicly). Still don't bump MAUI independently of the prerelease Shiny camera package: they move together, see `Directory.Packages.props`.

## Packaging & CI

`.github/workflows/build.yml` (macos-latest, `ios/android/maccatalyst/macos` workloads — `macos` is there for `Shiny.DocumentIntelligence`'s AppKit target) builds **`build.slnf`** in Release and pushes to nuget.org from `main` and `v*` branches using the `NUGETAPIKEY` secret. Adapted from `~/Desktop/dev/music`, which is the reference layout for every Shiny repo.

- **`build.slnf` is the shipping surface** — the 11 `src/` projects, nothing else. The Sample never builds in CI (it needs the gitignored ONNX models, and its Android head doesn't build at all), and neither do the tests. **Add a new package to `build.slnf` or it is silently never published.**
- **Versioning is Nerdbank.GitVersioning** (`version.json`, `1.0.0-beta.{height}`), referenced globally from `Directory.Build.props`. The workflow needs `fetch-depth: 0` — a shallow clone has no height. `PublicRelease=true` is passed on the command line, which is what drops the `-g<commit>` suffix.
- **Packing happens on build, not via `dotnet pack`**: `Directory.Build.props` sets `GeneratePackageOnBuild` + `PackageOutputPath=artifacts/` for `Configuration=Release` only, so every package (and `.snupkg`) lands in one folder the workflow uploads and pushes. Debug builds pack nothing.
- Shared package metadata (authors, MIT, icon, readme, repo url) lives in `Directory.Build.props`; each csproj supplies only its own `<Description>`. `nuget.png`/`nuget.txt` are the standard Shiny package assets. Tests and the Sample carry `IsPackable=false`.

## Architecture (the parts that span files)

**Type/interface placement convention.** Namespaces are **flat** (folder ≠ namespace — e.g. everything in `Shiny.DocumentIntelligence/Extraction/` is `namespace Shiny.DocumentIntelligence`), so folders are purely organizational and moving a file between them is a same-namespace, zero-code change. Files are split by audience:
  - **Root (+ topical folders like `Extraction/`)** — the **consumer-facing** surface: contracts you resolve and *invoke* (`IFaceIntelligence`, `IVoiceIntelligence`, `IDocumentScanner`, `IDocumentExtractor`), the registration builders/`Add*` extensions, options, and data/result types (`Person`, `Speaker`, `RecognitionResult`, `ExtractedDocument`, `Barcode`, …).
  - **`Infrastructure/`** — the provider/platform **seam interfaces** that other packages *implement* and the library wires internally: `IFaceEmbedder`/`IFaceStore`, `ISpeakerEmbedder`/`IVoiceStore`, `ITextRecognizer`/`IBarcodeReader`.
  - **`Internals/`** — concrete implementations and plumbing **not meant for external consumption**: the orchestrators (`FaceIntelligenceManager`, `VoiceIntelligenceManager`), `DocumentExtractor` + `Parsing/`, the source-gen `FacesJsonContext`/`VoicesJsonContext`, and the internal `FaceImaging` helper. (Some stay `public` only because a sibling package references them across assembly boundaries — e.g. `FacesJsonContext`, `FaceImaging`; the folder, not the modifier, marks intent. Platform impls stay under `Platforms/` — the csproj gates those per-TFM.)

  New file? Ask: does the app **call** it (root) → **implement/plug** it (`Infrastructure/`) → or is it **internal machinery** (`Internals/`)?

**The pipeline is composed via a builder.** `AddFaceIntelligence(this IServiceCollection, Action<FaceIntelligenceRegistrationBuilder>)` (`FaceIntelligenceServiceCollectionExtensions.cs`) runs the action, then registers `FaceIntelligenceOptions` + `IFaceIntelligence→FaceIntelligenceManager` and **validates** that an `IFaceEmbedder` and an `IFaceStore` were registered (else throws naming `UseOnnxEmbedder`/`UseSqliteStore`). The builder (`FaceIntelligenceRegistrationBuilder`) exposes `Options`, generic `UseEmbedder(...)`/`UseStore(...)` seams, and `Services`; the embedder/store packages add `UseOnnxEmbedder` / `UseDocumentDbStore` / `UseSqliteStore` extension methods on it.

- **`IFaceEmbedder`** (image+box → L2-normalized vector). `Shiny.FaceIntelligence.Onnx.OnnxArcFaceEmbedder` is the default impl; `UseEmbedder(...)` plugs in a native one or a test fake.
- **`IFaceStore`** (`IFaceStore.cs`: `Add`/`FindNearest`/`GetAll`/`RemoveByName`, returning `FaceMatch(Person, Distance)`) — the store seam. `DocumentDbFaceStore` (in `.DocumentDb`) is the default impl over `IDocumentStore`. **`FaceIntelligenceManager` depends only on `IFaceEmbedder` + `IFaceStore` + `FaceIntelligenceOptions`** — no ONNX, no DocumentDb in core.
- **Vector dimension is read from the embedder**: `UseDocumentDbStore` builds the `IDocumentStore` inside the store, resolving `IFaceEmbedder` and mapping `Person.Embedding` via `MapVectorProperty<Person>(p => p.Embedding, embedder.Dimensions, Cosine)`, so it always matches the model.
- **Each stack's `IDocumentStore` is PRIVATE — never registered in the container.** `UseDocumentDbStore` builds the `DocumentStore` and hands it straight to `DocumentDbFaceStore(IDocumentStore)`, registering only `IFaceStore`. This is deliberate: registering a shared `IDocumentStore` singleton breaks composition — an app using **both** face and voice (the Sample) would have two `IDocumentStore` registrations, and `GetRequiredService<IDocumentStore>()` returns the **last** one, so the face store would silently resolve the voice store (Speaker-mapped, `voices.db`) and vice versa. Regression-guarded by `tests/Shiny.RecognitionIntelligence.IntegrationTests` (registers both stacks, asserts no shared `IDocumentStore` leaks and the stores stay isolated).
- **No lazy wrapper on the store (settled).** `DocumentDbFaceStore`/`DocumentDbVoiceStore` take a ready `IDocumentStore` — no `Lazy<>`/factory. It's safe to build `new DocumentStore(options)` eagerly at `IFaceStore` resolution because that ctor only creates the connection **object** + mapping metadata (all in-memory); `DocumentStore` opens the connection and loads the native vector extension (`vec0`) itself, lazily, on the **first** operation (`EnsureSharedConnectionInitializedAsync`). So the DB-open/vec0-load already lands inside the first enroll/recognize call where the pages catch it — an extra `Lazy` here would only re-defer something that was never eager.

**Embedding** (`OnnxArcFaceEmbedder` + core `FaceImaging`): crop the face box (25% margin, clamped) → 112×112 RGB → normalize `(px - 127.5) / 128` → NCHW `[1,3,112,112]` → ONNX `Run` → **L2-normalize**. Dimension from the model's output metadata (falls back to 512 for a dynamic axis).

**Storage / matching** (`FaceIntelligenceManager`, `Person`, `DocumentDbFaceStore`): each `Enroll` is **one `Person` document = one shot** (fresh GUID). `Person.Embedding` is `[JsonIgnore]`d — it lives **only in the sqlite-vec sidecar table**, never the JSON blob, and comes back empty from document queries. `Recognize` calls `store.FindNearest(query, CandidateCount)` and returns **only the single nearest** `FaceMatch` if its `Distance` is within `MaxDistance`, else `NoMatch`. `CandidateCount` (default 5) only widens the internal candidate pull; the result is always one name or none.

**Detection: one detector, two entry points.** ArcFace is an **embedder**, not a detector — it always returns a vector for whatever `FaceBox` it's handed and never reports "bad face." So a face box must come from somewhere, and since 2026-07-22 that is **always** `IFaceDetector` (the in-library ONNX detector). `Shiny.FaceIntelligence.IFaceDetector` (Infrastructure seam) → `Onnx.OnnxUltraFaceDetector` (UltraFace RFB-320: resize → `(px-127)/128` → NCHW → scores`[1,N,2]`+boxes`[1,N,4]` → threshold + NMS → pixel `FaceBox`es with confidence). Registered via `UseOnnxDetector(...)`/`UseDetector(...)` — **optional** for the box-based overloads; the manager takes `IEnumerable<IFaceDetector>` and only the **no-box** overloads use it, but `FaceRecognitionAnalyzer` requires it outright.
- **Enrollment** goes through `FaceCameraView.EnrollAsync` (the box-based `Enroll(name, imageData, face)` overload on the analyzed frame — no re-detect, so the crop matches recognition byte for byte). The no-box `Enroll(name, imageData, allowDuplicate=false)` overload still exists for server-side/still workflows: it runs the detector and applies the gates in `FaceIntelligenceOptions` (`MinDetectionConfidence`, `MinFaceSizeFraction`, `RejectMultipleFaces`, `GateEnrollmentOnRecognition`), throwing `FaceDetectionException{Reason: NoFace|LowConfidence|MultipleFaces|TooSmall}` or `FaceEnrollmentConflictException(match)` (re-call with `allowDuplicate:true` to force).
- **Recognition** runs the same detector per *frame* inside `FaceRecognitionAnalyzer` — see below. No still capture at all.

**The camera-side `FaceAnalyzer` is gone (settled).** The Sample used to reference `Shiny.Maui.Controls.Camera.Face`, subscribe to `FacesDetected`, and call `CapturePhotoAsync()` per detection. That is removed — package reference, `FaceDetectionExtensions.cs`, and all of it. It meant two detectors (Vision *and* ours), two coordinate spaces (the camera's normalized `0..1` bounds vs. the library's pixel `FaceBox`), a full still capture per detected frame, and a `capturing` re-entrancy guard that latched forever if a capture ever wedged. `FaceRecognitionAnalyzer` replaces the whole arrangement with one detector in one coordinate space and no capture. Don't reintroduce the camera Face package for recognition.

**JSON / AOT**: all packages are `IsAotCompatible`. `FacesJsonContext` (in **core**) is the source-generated `JsonSerializerContext` for `Person`; `UseDocumentDbStore` feeds `FacesJsonContext.Default.Options` to the store for serialization + the LINQ expression visitor. Do **not** add another `[JsonSerializerContext]` — it's inherited. New persisted document types must be added as `[JsonSerializable]` here.

## Live recognition: the frame analyzer (`Shiny.FaceIntelligence.Maui`)

Recognition runs against **live camera frames**. There is no still capture anywhere in the face feature — enroll and recognize both read the analyzed frame.

**`FaceRecognitionView` / `FaceEnrollmentView` are the APIs consumers should use** — see [Two controls](#two-controls-recognize-vs-enroll). They wrap `CameraView` + permission + start/stop lifecycle + the analyzer and resolve services from `Handler.MauiContext.Services`, so there is nothing to inject or wire:

```xml
<fi:FaceRecognitionView FaceRecognized="OnFaceRecognized" CameraFailed="OnCameraFailed" />
<fi:FaceEnrollmentView PersonIdentifier="{Binding Name}" Completed="OnEnrolled" />
```

`FaceRecognitionView.EnrollAsync(name)` still exists for a one-off single-shot capture off the current frame; the guided sequence is `FaceEnrollmentView.BeginEnrollment()`.

Registration is still one line in the module — `builder.Services.AddTransient<FaceRecognitionAnalyzer>()` (transient: it holds per-camera state). Everything else the control does itself. Drop to the raw `FaceRecognitionAnalyzer` + `CameraView.Analyzer` only when you need a camera configured in ways the wrapper doesn't expose.

**Enroll and recognize MUST share the pipeline (settled — this was a real bug).** `EnrollAsync` stores `analyzer.LastFace` — the exact JPEG bytes and pixel `FaceBox` the recognizer is handed. Enrolling from a separately captured still instead (what the Enroll page used to do via `CapturePhotoAsync`) put the template through *different* preprocessing than every probe it would later be compared against: different orientation handling, and on the front camera a mirror the analyzer corrects and the still does not. That is a systematic offset applied to every distance in the gallery, and no amount of `MaxDistance` tuning fixes it. If you add another enrollment entry point, route it through `LastFace` too.

**Per-frame flow.** `AnalyzeAsync` → `FrameImageConverter.ToUpright(frame, MaxAnalysisWidth)` → JPEG-encode once → `IFaceDetector.Detect` (cheap, every frame) → return an `OverlayBox` for the live preview. The **full** pipeline (`IFaceIntelligence.Recognize(bytes, box)` = ArcFace embed + sqlite-vec query) only runs once the face has held steady, throttled after that. Tunables on the analyzer: `StabilityFrames` (3), `StabilityTolerance` (0.05 of the frame), `RecognitionInterval` (2 s), `MaxAnalysisWidth` (720), `MinConfidence` (0.7), `MatchColor`/`UnknownColor`/`UnknownText`. `FaceRecognized` fires for **every** attempt including no-match, so the UI can say "Unknown" rather than holding a stale name. The camera pipeline runs analyzers max-one-in-flight and drops frames while busy, so the analyzer self-paces — it may take a whole frame interval without backing up the camera.

**One coordinate space.** `FrameImageConverter` produces an **upright, mirror-corrected** `SKBitmap`, and *everything* — the detector's pixel `FaceBox`, the embed crop, and the normalized `OverlayBox.Rect` — is expressed against that single bitmap. This is what kills the old normalized-vs-pixel and front-camera-mirroring bugs: rotation and mirroring are applied once, up front, from `CameraFrame.Rotation`/`IsMirrored`. The mirror is applied in **sensor** space (before the rotation) because that's where the flip physically happens — Skia composes so the last transform applied is the innermost.

**The converter is the only per-platform code**, a `sealed partial class` with one `ToUpright` per OS (the `DocumentImageExtractor` pattern from `Shiny.Maui.Controls.Camera.Ai`):
- **Apple** — `AppleCameraFrame.Bgra` is already a managed BGRA copy, so it's a `Marshal.Copy` into an `SKBitmap`. No CGImage/CoreImage round-trip.
- **Android** — `AndroidCameraFrame.Proxy` is CameraX `YUV_420_888`; converted in managed code (BT.601, honoring row/pixel strides). The loop **subsamples straight to the target width** rather than converting full-res then resizing, so cost scales with `MaxAnalysisWidth`, not sensor size.

Only `net10.0-android;net10.0-ios;net10.0-maccatalyst` — there are no camera frames on bare `net10.0`, and Windows is unbuilt/untested (add a `Platforms/Windows` converter for `WindowsCameraFrame.SoftwareBitmap` if a Windows head is ever wanted).

### Two controls: recognize vs enroll

`FaceRecognitionView` and `FaceEnrollmentView` are deliberately separate — they want opposite things. Recognition wants one fast confident answer; enrollment wants a *diverse* gallery and needs steps, progress and instructions. A single control with a `Mode` flag would leave half its API dead in either mode. (`FaceCameraView` remains as an `[Obsolete]` subclass of `FaceRecognitionView` so existing XAML keeps compiling.)

```xml
<fi:FaceRecognitionView FaceRecognized="OnRecognized" />
<fi:FaceEnrollmentView PersonIdentifier="{Binding Name}" Completed="OnEnrolled" />
```

Both resolve their services from `Handler.MauiContext.Services`, so neither needs anything injected.

**The wizard gates on what is measurable — and head angle is not.** `IFaceDetector` returns a box and a confidence; there are no landmarks and no yaw/pitch, so a prompt like "turn your head slightly left" can be *shown* but never *verified*. `FaceEnrollmentView` therefore checks:

| Gate | Verifiable? | Notes |
|---|---|---|
| Fitting the target oval (`FaceGuide`) | yes | position **and** size; the primary gate — see [the overlay section](#the-enrollment-face-hole-overlay-and-the-aspectfill-trap) |
| Face size vs frame (`MinFaceFraction`/`MaxFaceFraction`) | yes | older, coarser distance gate; still honoured |
| Steadiness (`RequiredStableFrames`) | yes | reuses the analyzer's stability counter |
| Sharpness + brightness (`FrameQuality`) | yes | variance-of-Laplacian at a fixed 96×96 working size so the threshold is scale-independent |
| **Novelty** (`MinNoveltyDistance`, default 0.06) | yes | embeds the candidate and rejects it if within that cosine distance of any shot already captured |
| Head angle | **no** | instructed only |

Novelty is the gate that does the real work. The purpose of a varied gallery *is* embedding spread, so measuring spread directly beats trusting that someone turned their head — it's what stops six near-identical front-on shots. `FaceEnrollmentResult.MinPairwiseDistance` reports the tightest pair so a caller can tell whether the sequence actually achieved variety.

**Storage is incremental, and steps can be skipped.** Each accepted shot is enrolled immediately rather than batched to the end — batching meant one unsatisfiable step (e.g. "move back" when a phone at arm's length can't make the face small enough) discarded every shot already captured, so the person completed five prompts and got nothing. `StepTimeout` (12 s) skips a step that can't be satisfied and `FaceEnrollmentResult.SkippedSteps` reports it. Consequently `CancelEnrollment` no longer discards anything — use `Forget` to undo.

**Pacing is a visible countdown**, not a silent delay: `StepCountdown` (3 s) shows "Get ready… 3/2/1" then "Hold still…", and captures are only accepted after it. A wizard that fires before the instruction can be read just takes N shots of whatever was already in frame — the first cut completed all six steps in about six frames.

**`MinNoveltyDistance` must clear the model's frame-to-frame jitter.** It defaults to 0.18. The first attempt used 0.06, which is *inside* the noise: the same face measured 0.084–0.526 apart across seconds on this device, so every frame read as "novel". Measured gate output at 0.18 shows it discriminating properly (`nearest 0.1298` rejected, `0.2184` accepted).

Per-frame cost is kept off the UI thread and single-flight: cheap geometric gates run inline on the analyzer's new `FaceDetected` event, and only once those pass does the control decode/measure/embed inside a `Task.Run` (`evaluating` guards re-entry). Enrollment uses the box-based `Enroll(name, imageData, box)` on the analyzed frame, so templates and probes share preprocessing. Shots after the first are enrolled without the duplicate gate — the sequence is explicitly one person, and shots 2..n *should* match shot 1.

Adding real pose verification later means a landmark/head-pose model behind a new seam; that would also unlock the 5-point alignment TODO. Until then, don't write code that claims to check angle.

### The enrollment "face hole" overlay, and the AspectFill trap

`FaceEnrollmentView` layers a `GraphicsView` (`FaceGuideDrawable`) over the `CameraView`: everything outside the step's target oval is dimmed, the outline is amber-dashed when off-target and solid green the moment the face fits, and the live detection is drawn as a faint box so the person can see which way to move. `FaceGuide.Correction(...)` turns a miss into a directional hint ("Move left into the outline", "Move closer — fill the outline").

**This is what makes guided steps checkable.** Head angle can't be verified (no landmarks), but "is the face inside this oval at roughly this size" is plain geometry. Moving the *target* around the frame also produces genuine pose variation — the person physically moves, so the camera sees them from a different angle — which is why the default sequence is now outline positions (centre / left / right / top / big / small) rather than "turn your head slightly left". Alignment gates capture before the quality and novelty checks.

**The trap: guides are authored in VISIBLE-preview coordinates, not full-frame coordinates.** The preview is AspectFill, so the frame is scaled to *cover* the view and the overflow is cropped — a 720×1280 analyzed frame in a 393×490 view renders 393×699 and loses ~200 px vertically. Guides written in full-frame normalized coords therefore render oversized and clipped off-screen, because the cropped-away region still counts toward "1.0". `FaceGuide.ToImageSpace(imageAspect, viewAspect)` re-projects a visible-space guide into the full-frame space detections arrive in; `FaceEnrollmentView.EffectiveGuide(...)` calls it per frame with the live view aspect. Verified on-device — before the fix the oval ran off the top-left and covered ~76% of the preview height instead of 52%.

Two related details that are easy to get wrong the same way:
- **Oval width is derived in view pixels, not normalized units.** `FaceGuide.AspectRatio` (0.78) multiplies the oval's *pixel* height. Deriving a normalized width from a normalized height conflates the axes and draws a squashed oval on any non-square frame.
- **`AnalyzedFace` carries `ImageWidth`/`ImageHeight`** (hence `Aspect`) precisely so the overlay can do this mapping; without them there is no way to reproduce the AspectFill transform.

### Critical gotcha: `CameraView` silently no-ops before its handler connects

`CameraView` routes every call through `Controller => this.Handler as ICameraViewController`, and **every method null-guards to a silent success**: `RequestPermissionAsync` → `Task.FromResult(false)`, `StartAsync` → `Task.CompletedTask`. So calling them before the handler is connected does nothing, reports nothing, and throws nothing.

`ContentPage.OnAppearing` fires **before** the handler exists on these pages (`BindingContext` is null there too, so a status message written to the VM in `OnAppearing` is also dropped). Starting the camera only from `OnAppearing` therefore left the preview black forever with no error anywhere — it looked exactly like "the page does nothing". It presents as a **permission** problem, which it is not: `AVCaptureDevice.GetAuthorizationStatus(Video)` returns `Authorized` while `RequestPermissionAsync()` returns `false` and no prompt appears. That mismatch is the tell.

The fix, in both `RecognizePage` and `EnrollPage`: an idempotent `StartCamera()` called from **both** `OnAppearing` and `Camera.HandlerChanged`, guarded by a `started` flag (reset in `OnDisappearing` and on any failure path) so whichever happens last wins.

```csharp
this.Camera.HandlerChanged += (_, _) => this.StartCamera();   // ctor
protected override void OnAppearing() { base.OnAppearing(); this.StartCamera(); }
async void StartCamera()
{
    if (this.started || this.Camera.Handler is null) return;   // <- the guard that matters
    ...
}
```

Only `CapturePhotoAsync` throws (`InvalidOperationException("CameraView handler is not connected")`) instead of no-opping. Everything else fails silently — so when a camera page appears dead, **check `Camera.Handler` first**.

**Debugging note.** `maui devflow logs` surfaces only `console.out` — `builder.Logging.AddDebug()` output does **not** appear. Trace camera/analyzer stages with `Console.WriteLine`. `RecognizePage.Trace()` writes to both console.out and the VM's `DiagnosticText`, which is rendered as a small second line on the page, so pipeline state is visible on-device without a debugger.


## Runtime assets you must supply (not in the repo)

**`./Sample/fetch-models.sh` downloads the two face models** (idempotent — skips what's already there): ArcFace from InsightFace **buffalo_s** (`w600k_mbf`/MobileFaceNet, ~13 MB, in `[-1,3,112,112]` → out `[1,512]`) and **UltraFace version-RFB-320** from the ONNX model zoo (~1.3 MB, in `[1,3,240,320]` → `scores[1,4420,2]`+`boxes[1,4420,4]`, matching `OnnxDetectorOptions`' defaults exactly). It deliberately does **not** fetch `ecapa.onnx` — supply the speaker embedder yourself. The app launches without any of these; enroll/recognize surface a "model missing" message rather than crashing.
1. **ArcFace ONNX model** (112×112 in, 512-d out). Drop it at `Sample/Resources/Raw/arcface.onnx` (gitignored — supply per build); the `Resources\Raw\**` glob bundles it. The Sample loads it **as bytes** and configures `face.UseOnnxEmbedder(o => o.ModelBytesProvider = () => LoadBundledModel("arcface.onnx"))` (reads via `FileSystem.OpenAppPackageFileAsync`). Bundled assets aren't real file paths on iOS/Android, so use `ModelBytesProvider`/`ModelBytes` rather than `ModelPath` — `OnnxEmbedderOptions` supports all three (priority: provider → bytes → path), and `OnnxArcFaceEmbedder` has matching `byte[]` (bundled/server-stream) and `string` (file/server) constructors. The provider runs lazily on first enroll/recognize, so a missing model surfaces there (pages catch `FileNotFoundException`), not at startup. Model size dominates app size — prefer a compact ArcFace (MobileFaceNet/`w600k_mbf`, single-digit MB) over `w600k_r50` (~166 MB) for on-device; download-on-first-run is the alternative for large models.
2. **UltraFace detector ONNX model** (needed by **both** enrollment and the live recognize analyzer). Drop it at `Sample/Resources/Raw/face_detector.onnx` (gitignored — supply per build). Same lazy-bytes flow as ArcFace: `face.UseOnnxDetector(o => o.ModelBytesProvider = () => LoadBundledModel("face_detector.onnx"))`, loaded on first enroll (missing → `FileNotFoundException`, caught by the page). Defaults target **UltraFace version-RFB-320 / slim-320** (input `1×3×240×320`, scores`[1,N,2]`+boxes`[1,N,4]` normalized); tune `OnnxDetectorOptions.InputWidth/Height/Mean/Std/ScoreThreshold/IouThreshold`. A detector with a different output layout (SCRFD/RetinaFace/YuNet) needs its own `IFaceDetector` via `UseDetector(...)`. Tiny (~1 MB), so negligible next to ArcFace.
3. **sqlite-vec native binary** (`vec0.dylib` / `vec0.so` per RID), loadable by `SqliteConnection.LoadExtension`. Set `SqliteFaceStoreOptions.VectorExtensionPath` (default `"vec0"`; loader searches app dir + OS paths) in `UseSqliteStore`.

**App-size note.** The full `Microsoft.ML.OnnxRuntime` native runtime is the fixed floor: ~33 MB (iOS arm64 static lib, force-loaded) / ~17 MB (Android `libonnxruntime.so`, arm64-v8a) per shipped architecture, uncompressed; the managed binding and `Shiny.FaceIntelligence.dll` are negligible (~0.2 MB / ~36 KB). The model is the swing factor — `w600k_r50` ~166 MB vs MobileFaceNet ~4–5 MB. If that ~17–33 MB ORT floor is a real constraint, a **reduced/minimal ONNX Runtime build** shrinks the native lib by stripping operators and types down to only what your model uses:
- Build ORT from source with `--minimal_build` + `--include_ops_by_config <ops.config>` (and optionally `--enable_reduced_operator_type_support`); the op config is generated from your model. This requires a **`.ort`-format model** (convert the `.onnx` with ORT's `convert_onnx_models_to_ort` tool), and the minimal runtime only supports `.ort` models. Re-run the reduction whenever the model's op set changes, or inference throws "operator not found" at load.
- The old prebuilt `Microsoft.ML.OnnxRuntime.Mobile` NuGet (a ready-made reduced build) was **deprecated** after ~1.13/1.14, so for 1.20 the custom-from-source route is the supported path. Worth it only when binary size genuinely matters — otherwise stay on the full package.

## Critical gotcha: ONNX Runtime iOS/MacCatalyst linker fix

`Microsoft.ML.OnnxRuntime`'s managed binding P/Invokes `RegisterCustomOps`, a symbol the **desktop** ORT static lib does not define (it ships in `onnxruntime-extensions`). On iOS/MacCatalyst the .NET registrar force-references every P/Invoke target with a `-u` linker flag, so the **app-head** native link fails with `Undefined symbols for architecture arm64: _RegisterCustomOps`. (Android is unaffected — it loads the `.so` dynamically.)

The fix lives in the **`.Onnx` package** (the only one that pulls ONNX): **`src/Shiny.FaceIntelligence.Onnx/build/Shiny.FaceIntelligence.Onnx.targets`**, packed into both `build/` and `buildTransitive/` so it flows into any consuming app head automatically (direct or transitive NuGet reference). It:
- adds `-Wl,-U,_RegisterCustomOps` (allow undefined — belt-and-suspenders), and
- in a target between `_LoadLinkerOutput` and `_ComputeLinkNativeExecutableInputs`, removes `_RegisterCustomOps` from `@(ReferenceNativeSymbol)`/`@(_ProcessedReferenceNativeSymbol)` so the hard `-u` require is never emitted (a plain `-U` can't override `-u` on the Xcode 16+/26 linker).

Notes:
- Safe **only because the ONNX package never registers custom ops**. A consumer that does should set `DisableOnnxRegisterCustomOpsWorkaround=true`.
- The Sample uses a `ProjectReference`, which does **not** consume packaged `buildTransitive` targets, so `Sample.csproj` `<Import>`s `..\src\Shiny.FaceIntelligence.Onnx\build\Shiny.FaceIntelligence.Onnx.targets` directly — so the Sample build exercises exactly what consumers receive. Keep that import.
- It hooks **internal, non-contract** iOS SDK target names (`_LoadLinkerOutput`, `_ComputeLinkNativeExecutableInputs`). Verified on iOS SDK 26.5 / .NET 10.0.8. If a future iOS workload renames them the target silently no-ops and the link error returns — re-test on workload bumps.

### Don't multi-target the packages to "fix" this (settled)

Adding `net10.0-ios`/`-maccatalyst` TFMs to any of these packages does **not** help and does **not** remove the targets file. The `_RegisterCustomOps` failure is an **app-head native-link** problem, and a class library (`OutputType=Library`) never performs that link — ORT's own iOS targets even gate the force-loaded `NativeReference` on `'$(OutputType)'!='Library'`, so it wouldn't be added in `.Onnx` regardless of TFM. The `-u` flag comes from the registrar force-referencing the P/Invoke in `Microsoft.ML.OnnxRuntime.Managed` in the **consuming app head**, unaffected by package TFMs. The `buildTransitive` targets file is both required and the correct home for app-head linker behavior.

Keep the **face** packages at **`net10.0` only**. They already serve every consumer (MAUI android/ios/maccatalyst/windows heads **and** server-side enrollment, which has no linker issue since desktop/server loads ORT dynamically). Only multi-target a package if/when it gains genuinely **platform-specific code** — `Shiny.DocumentIntelligence` is exactly that case (it has a native impl per OS, so it multi-targets); a future native `IFaceEmbedder` package (iOS Vision feature print, Android-native) would be another. Core and the existing face packages stay `net10.0`.

## Voice intelligence (speaker recognition — `Shiny.VoiceIntelligence`)

The audio twin of the face stack, deliberately built to the **same architecture** so the two read alike. Speaker (voice biometric) enrollment + recognition: an ECAPA-TDNN / x-vector ONNX model turns a mono utterance into an L2-normalized "voiceprint", stored and matched by the **same sqlite-vec nearest-neighbor + cosine-distance** machinery as faces. It originated as a scaffold in the `~/Desktop/dev/speech` repo (`Shiny.Speech.Biometrics`, brute-force cosine over Shiny.DocumentDb) and was **moved here and re-based on this repo's real ANN vector store** — a strict upgrade — then that scaffold was deleted from the speech repo.

**Parallel to face, one-to-one.** Same builder + validation (`AddVoiceIntelligence` throws unless an `ISpeakerEmbedder` and an `IVoiceStore` are registered, naming `UseOnnxEmbedder`/`UseSqliteStore`), same lazy-model and lazy-store deferral, same "vector dimension read from the embedder" (`MapVectorProperty<Speaker>(s => s.Embedding, embedder.Dimensions, VectorDistance.Cosine)`), same `[JsonIgnore]` embedding living only in the vec0 sidecar, same "one document = one utterance, keyed by `PersonIdentifier`" model. `VoicesJsonContext` is the source-gen `JsonSerializerContext` for `Speaker`. Registration reads identically:

```csharp
services.AddVoiceIntelligence(voice =>
{
    voice.Options.MaxDistance = 0.7f;                                            // see tuning caveat below
    voice.UseOnnxEmbedder(o =>
    {
        o.ModelBytesProvider = () => LoadBundled("ecapa.onnx");
        o.Dimensions = 512;   // MUST match the model (512 = CAM++/WeSpeaker, 192 = many ECAPA exports)
    });
    voice.UseSqliteStore(o => o.ConnectionString = "Data Source=voices.db");
});
```

**Key differences from face (all deliberate):**
- **Capture-agnostic core.** Face core takes `byte[] imageData` + `FaceBox`; voice core takes a `float[]` **sample buffer** (mono PCM, [-1,1], at `ISpeakerEmbedder.SampleRate`, default 16 kHz). The library **never touches audio hardware** — capturing (mic/file/stream) is the app's job, exactly as the camera is for face. So voice core has **no SkiaSharp** and no image/thumbnail stage; `Speaker` has no thumbnail.
- **Embedder input is waveform *or* fbank, detected from the model.** `OnnxEcapaEmbedder` reads the declared input rank and either feeds `[1, samples]` directly or runs `KaldiFbank` to build `[1, frames, 80]` first → `Run` → L2-normalize. See [the auto-detect section](#the-onnx-speaker-embedder-auto-detects-waveform-vs-fbank-input-fixed-2026-07-22). Dimension hint defaults to **192**; the bundled CAM++ model is **512**, and ArcFace's 512 is coincidence, not transfer.
- **The ArcFace model does NOT transfer** — different modality (image→vector vs audio→vector). What transferred is the **ONNX plumbing**: the options/lazy-provider pattern, `UseOnnxEmbedder`, the bundled-asset flow, and the iOS linker `.targets`. You still supply an ECAPA `.onnx`.

**Shared iOS linker `.targets` — the one real cross-package gotcha.** Both `.Onnx` packages auto-import their `build/<PackageId>.targets`, and an app that references **both** (this repo's Sample will) would hit a **duplicate MSBuild target name** error. So `Shiny.VoiceIntelligence.Onnx.targets` is a copy of the face one with the `Target Name` suffixed **`_DropOnnxRegisterCustomOpsForcedSymbol_Voice`**. The `DisableOnnxRegisterCustomOpsWorkaround` property and the `-Wl,-U,_RegisterCustomOps` flag are intentionally **identical** across both (one toggle governs both; the linker de-dupes the repeated `-U`). Everything in [the ONNX linker section](#critical-gotcha-onnx-runtime-iosmaccatalyst-linker-fix) applies verbatim.

**Threshold tuning.** `VoiceIntelligenceOptions.MaxDistance` still defaults to **`0.7`** in the package purely as a placeholder; the Sample overrides it from `VoiceTuning.MaxDistance` (0.45, measured — see [Audio capture is the fragile part](#audio-capture-is-the-fragile-part-not-the-threshold-settled--measured-2026-07-22)). Speaker-embedding score distributions vary far more by model/channel than face does — this **must** be tuned against your actual ECAPA export with measured FAR/FRR before it means anything. Don't ship the default.

### Audio capture is the fragile part, not the threshold (settled — measured 2026-07-22)

Speaker matching failed end-to-end (same person, ~0.88 distance = orthogonal) and the cause was entirely in **capture**, upstream of the model. Three defects in `Sample/Platforms/iOS/AppleAudioSource.cs` + `VoiceRecorder`, all now fixed:

1. **`AVAudioSessionMode.VoiceChat`** turned on Apple's voice-processing chain — AGC, noise suppression, echo cancellation. It is adaptive, non-linear, and exists to normalise away speaker and channel characteristics, which is exactly what a speaker embedding encodes; because it adapts per session, two recordings of one person came out different. Use **`Measurement`** (disables the chain). Expect noticeably lower levels afterwards — that's AGC being gone, not a regression (measured rms ~0.01 vs ~0.07 before).
2. **`AllowBluetooth`/`AllowBluetoothA2DP`** let a paired headset move the route to 8 kHz HFP, so captured bandwidth depended on what was connected. Dropped; verify the route is `MicrophoneBuiltIn` (it is logged at capture start).
3. **Linear-interpolation resampling** 48 kHz → 16 kHz with no anti-alias filter. That discards two of every three samples and folds everything above 8 kHz back into the speech band; fricatives (/s/, /ʃ/, /f/) live there, and because aliasing is signal-dependent each recording is corrupted *differently*. Replaced with a windowed-sinc resampler with the low-pass folded into the kernel.

**Measured same-speaker distances after the fix** (8 recordings, one speaker, built-in mic, 28 pairs): min 0.011, median 0.110, **max 0.351**. Hence `VoiceTuning.MaxDistance = 0.40` (FRR 0% on that set).

**FAR remains unmeasured, and TTS cannot stand in for it.** An attempt to synthesise impostors from macOS
voices was invalid: the model rates the legacy MacinTalk synths as the same speaker (Junior vs Kathy = 0.093)
and even the modern voices sit only 0.31–0.43 apart, so synthetic speech doesn't occupy the same region of
the embedding space as real speech. A real false-accept rate needs a **second human** enrolled on-device.
Clip quality matters too: 6 of the 8 recordings clustered within 0.11, while 2 quieter ones sat 0.20–0.35 out
and drifted toward a generic centroid (they were also the ones closest to TTS voices).

**The model and feature front end were never the problem** and were validated independently: same TTS voice, two different sentences, clean 16 kHz → distance **0.138**. `KaldiFbank` is correct as written, and adding CMN made it *worse* — the "no cepstral mean normalization" note there is right, don't "fix" it. When voice matching regresses, **check capture first**: `[Audio]` console lines report rate/channels/route and every recording is dumped to `Library/recordings/*.wav` under `#if DEBUG`, pullable with `xcrun devicectl device copy from --domain-type appDataContainer --domain-identifier org.shiny.recogiq --source Library/recordings`.

### The ONNX speaker embedder auto-detects waveform vs fbank input (fixed 2026-07-22)

`OnnxEcapaEmbedder` used to feed the raw waveform as `[1, samples]` unconditionally, which threw
`InvalidArgument: Invalid rank for input: feats Got: 2 Expected: 3` against any standard WeSpeaker export —
the model family it is named for. It now supports **both** shapes and picks between them from the model's
own declared input rank when the session loads (so lazy loading is unchanged):

| Model input | Handling |
|---|---|
| `[batch, samples]` (rank 2) | raw mono waveform, fed through |
| `[batch, frames, 80]` (rank 3, usually named `feats`) | `KaldiFbank` computes the filterbank first |

`OnnxEmbedderOptions.InputMode` (`Auto`/`Waveform`/`Fbank80`) forces it if a model's declared shape lies; a
rank-3 model wanting anything other than 80 bins throws a `NotSupportedException` naming `UseEmbedder(...)`.
`KaldiFbank` lives in `src/Shiny.VoiceIntelligence.Onnx/Internals/` (it was previously duplicated in the
Sample) and is covered by `KaldiFbankTests` — bin count, kaldi `snip_edges=false` frame count, determinism,
and level dependence (which fails if someone adds CMN, deliberately).

**`Dimensions` must match the model's real output width** — 512 for CAM++/WeSpeaker, 192 for many ECAPA
exports. It sizes the vector store *before* the model loads, so it can't be inferred; a mismatch now throws
on the first embed rather than silently corrupting stored voiceprints. Probe an unfamiliar model with
`InferenceSession.InputMetadata` / `OutputMetadata`, and read `ModelMetadata.CustomMetadataMap` — WeSpeaker
and sherpa-onnx exports carry `framework`, `output_dim`, `sample_rate` and `normalize_samples` there.

### Anti-spoofing (findings — not yet built in either repo)

Plain voiceprint matching is **defeated by a recording or a voice clone** — treat it as a convenience factor, not a security gate. A `grep` confirms **no liveness/anti-spoofing (PAD) exists anywhere in this repo yet**. Roadmap, cheapest-and-strongest first:
1. **Challenge–response (biggest win, no new model).** Generate a random prompt (random digits) per attempt; require the user to say *that*, and gate on (a) an STT transcript matching the prompt **and** (b) the speaker embedding matching. A recording/stockpiled voicebank can't answer an unknown prompt — kills the whole replay class. Needs a speech-to-text service (the speech repo's `ISpeechToTextService`, or platform STT).
2. **A countermeasure (CM) model** — an `ISpoofDetector` seam (bonafide-vs-spoof classifier: AASIST / RawNet2 / LCNN from ASVspoof, ONNX, same plumbing as the embedder). Passive net for synthetic audio; generalizes poorly to unseen attacks, so a filter not a wall.
3. **Multimodal liveness** — this repo's real edge: combine **face liveness + the voice challenge** ("look at the camera and say 7-4-1-9"). Strong on-device gate that neither modality gives alone; an argument for keeping face + voice in one repo.

### Guided enrollment: `VoiceEnrollmentSession` (the voice wizard)

`voice.CreateEnrollment(name)` returns a **`VoiceEnrollmentSession`** — the voice twin of `FaceEnrollmentView`'s step sequence: show a sentence, record it, submit it, repeat **until the session says the voiceprints agree well enough to stop**. It lives in **core**, not in a control, because the library never touches audio hardware (the app records and hands over `float[]`, same as `Enroll`) — which also makes it usable server-side and testable without a mic.

```csharp
var session = voice.CreateEnrollment("Allan");
while (!session.IsComplete)
{
    Show(session.CurrentPrompt);                          // rotates every attempt
    var step = await session.Submit(await recorder.RecordAsync(VoiceTuning.RecordFor));
    Show(step.Hint);                                      // "" when accepted
}
var result = session.Result!;                             // stored; result.Cohesion is the quality number
```

**The gate is inverted vs. face, and that's the whole idea.** Face enrollment wants *spread* (varied poses) so it rejects shots too **similar** to ones it has. A speaker embedding is supposed to be the same whatever the person says, so voice rejects clips that **disagree** — agreement is simultaneously the quality gate and the stop condition. What it checks per recording (`VoiceEnrollmentOptions`, `VoiceQuality`):

| Gate | Verifiable? | Notes |
|---|---|---|
| Duration / speech vs silence (`MinSpeechSeconds`) | yes | energy-framed, no VAD model; catches "tapped record and said nothing" |
| Speech level (`MinSpeechLevel`, measured over speech frames only) | yes | quiet clips are the documented drift-to-centroid failure; whole-buffer RMS would punish a pause before speaking |
| Clipping (`MaxClippedFraction`) | yes | mic too close / gain too high |
| SNR (`MinSnrDb`) | yes | 90th vs 10th percentile frame energy — rough, can't tell a quiet room from a steady hum |
| **Agreement** (`MaxOutlierDistance`, then `MaxCohesionDistance`) | yes | the one that carries the weight |
| The prompt was actually read | **no** | needs STT; the model is text-independent anyway |
| The voice is live, not a replay/clone | **no** | no anti-spoofing anywhere in this stack |

**Distance settings are derived from the match threshold, never hardcoded** — `VoiceEnrollmentOptions.ForThreshold(maxDistance)` (what `CreateEnrollment` uses) sets `MaxCohesionDistance = 0.75 × threshold` and `MaxOutlierDistance = threshold`. Reasoning: a probe is compared against these templates, so any spread the templates already have comes out of the matching budget; and a clip that wouldn't even *recognize* as the same person is a bad capture or a different person either way.

Session mechanics worth knowing:
- **Nothing is stored until it completes** (`Reset()`/abandon leaves no half-enrolled speaker). On completion it writes the already-computed embeddings straight to `IVoiceStore` rather than re-calling `Enroll`, which would re-run inference on every clip for an identical result.
- **Bad-first-clip rescue**: if the *first* accepted recording was the broken one, everything after it gets rejected as inconsistent — deadlock. So a clip rejected as `Inconsistent` is held for one round: if the next one agrees with *it* rather than with the lone survivor, those two out-vote the survivor and it's dropped.
- **Ran out of attempts** (`MaxSamples`, default 6): drops the worst-disagreeing clips down to `MinSamples` (default 3) and stores what's left with `IsConfident = false` — enrollment succeeds, flagged.
- `VoiceEnrollmentResult.Cohesion` (worst pairwise distance) is the headline quality number, and the Sample shows it as "agreement".
- Cohesion is measured **within one session only** — re-enrolling a name says nothing about how the new clips relate to the stored ones.

Covered by `VoiceEnrollmentSessionTests` (real sqlite-vec store + fake embedder, audio gates off since fake "recordings" are vectors, not sound) and `VoiceQualityTests` (synthesized tone/silence/noise/clipping).

### The voice enrollment control (`Shiny.VoiceIntelligence.Maui`)

`VoiceEnrollmentView` is the UI twin of `FaceEnrollmentView`: set `PersonIdentifier`, call `BeginEnrollment()`, and it shows the sentences, counts down, records, checks each clip and **keeps going until the voiceprints agree** — then raises `Completed` with the `VoiceEnrollmentResult`.

```xml
<vi:VoiceEnrollmentView PersonIdentifier="{Binding Name}" Completed="OnEnrolled" Failed="OnFailed" />
```

**It is a driver, not a decision-maker.** Every judgement — is this clip usable, does it agree, is that enough — stays in `VoiceEnrollmentSession` in core. The control adds pacing, the sentence list and the stop/failure handling. Drive the session directly for a server-side or non-MAUI flow.

**`IVoiceRecorder` is why a control can exist at all.** The rule that put the session in core — *this library never opens a microphone* — still holds: the app implements `RecordAsync(TimeSpan) → float[]` and the control resolves it from `Handler.MauiContext.Services`. There is no published Shiny audio-capture package to depend on, and hard-wiring one platform's capture would make the control useless to an app with its own audio pipeline. The Sample registers its vendored `VoiceRecorder` behind the seam. **Record enrollment and recognition through the same path** — the embedding encodes the channel, so a template captured differently from the probe carries an offset no threshold can fix.

**The whole sentence list is shown from construction, not once a run starts.** Someone should be able to read what they'll be asked to say before committing. `ForThreshold` only adjusts distance gates, so the previewed prompts are the ones the session actually uses. Each row shows `○` pending / `▶` current / `✓` kept.

**A rejected recording stays on the same sentence.** `VoiceEnrollmentSession.CurrentPromptIndex` advances on an *accepted* clip, not on every attempt — the index lives in core so the session and the control can't disagree about which sentence is current. Rotating per attempt read as "the wizard is cycling and nothing I do matters", and there was nothing to gain from moving on: the model is text-independent, so varying the words buys the embedding nothing, while re-reading a line you already know isolates what actually failed.

**Restarting mid-run needs a generation guard.** A cancelled loop is still parked inside `RecordAsync` for up to `RecordFor`, so without `runGeneration` it wakes up afterwards and writes its prompt index, ticks and status over the run that replaced it — which looks exactly like the wizard losing track of where it is (observed on-device: sentence 2 marked current with no tick on sentence 1). `Stop(showIdle:)` bumps the generation; every post-await UI write checks `Stale()` first.

**The countdown is not cosmetic** (`Countdown`, 3 s): recording the instant a sentence appears captures someone still reading it, and that near-silent clip is exactly what the session then rejects. Same lesson as the face wizard's pacing. It is drawn at 34pt — at 15pt inline it was missed entirely, and a countdown nobody notices is a countdown that isn't doing its job. Alongside it a `ProgressBar` fills across the recording window, because "Recording" for five silent seconds gives no sense of how much longer to keep talking.

**Prompts are PAIRS of sentences, and that fixed a real bug.** One Harvard sentence takes ~2–2.5 s to read; `MinSpeechSeconds` wants 2.5 s of detected speech. Single-sentence prompts therefore sat *exactly on the gate*: 16 clips measured off-device gave 2.14–3.62 s of speech, median ~2.45 s, with healthy level (RMS 0.014–0.033 vs a 0.004 floor) and 30+ dB SNR. So roughly half were rejected as "mostly silence" from someone who had read the line perfectly — and only the longest sentence passed reliably, which presented as *"it accepts the first one and then nothing works"*. **The gate was not lowered**: a speaker embedding needs voiced material, and dropping `MinSpeechSeconds` would trade a visible rejection for a quietly weaker template. The prompts were lengthened instead. When a clip is rejected, measure the dumped WAV before touching a threshold.

**The VU meter is scaled in dB and marks the gate.** `VuMeterDrawable` maps −60…0 dBFS across 24 segments: speech RMS ~0.02 and a 0.004 gate both live in the bottom 3% of a *linear* bar, where nothing visibly moves. Segments below `MinSpeechLevel` draw amber, above it green, and the top of the range red. Marking the gate is the whole point — a bare level bar says there is sound, a bar with the threshold on it says whether you are loud enough to be *accepted*. Ballistics are fast-attack/slow-release (`OnLevel`), because raw per-chunk RMS flickers on every syllable gap and reads as a fault.

**Live level needs a seam addition, done as a default interface method.** `IVoiceRecorder.RecordAsync(TimeSpan, IProgress<float>, CancellationToken)` reports **linear RMS in [0,1]** per captured chunk; the default implementation forwards to the plain overload, so a recorder that can't report levels needs no change and simply shows no meter. The recorder reports the *captured* chunk before resampling — the meter should show what the mic hears now, not what the pipeline emits at the end. Scaling to dB stays in the control so an implementer needn't know how it's drawn.

**Two ceilings, and they are different things.** `VoiceEnrollmentOptions.MaxSamples` caps *accepted* clips; the control's `MaxAttempts` (12) caps *recordings asked for*, accepted or not. Only the latter can stop a bad room, where clips are rejected forever and `MaxSamples` is never reached. When `Options` is left null the control also raises `MaxSamples` to `MaxAttempts` so a struggling run keeps trying rather than settling at the default six; supply `Options` to control that yourself.

**Hitting the ceiling stores something rather than nothing.** `VoiceEnrollmentSession.Finish()` (added for this) prunes the worst-disagreeing clips to `MinSamples` and stores with `IsConfident=false` — the same path the session takes when it runs out of attempts on its own. Without it, abandoning a run stores *nothing*: `Submit` only writes on the call that completes the session, so someone who recorded four usable clips in a noisy room would walk away with no enrollment. It returns null (and stores nothing) below `MinSamples` — a one-clip "enrollment" is the weak template the session exists to prevent, and the control reports that as `Failed`.

Covered by `VoiceEnrollmentSessionTests` (`Finish_StoresTheBestSubset_WhenTheCallerStopsAsking`, `Finish_StoresNothing_WhenTooFewRecordingsWereAccepted`, `Finish_OnACompletedSession_IsANoOp`). The control itself is verified on-device only — there is no MAUI UI test project.

### Voice in the Sample (built)

Voice now has three Sample tabs — **Voice ID** (`VoiceRecognizePage`), **Voice Enroll** (`VoiceEnrollPage`), **Speakers** (`SpeakersPage`) — under `Sample/Features/Voice/` (see [Sample structure](#sample-structure-feature-folders)). They mirror the face pages but are **button-driven, not continuous** (you can't passively sample a voice), and record through mic capture instead of the camera.

**Voice Enroll is now just the control**: the page hosts [`VoiceEnrollmentView`](#the-voice-enrollment-control-shinyvoiceintelligencemaui), supplies the name as the identifier, and reports `Completed`/`Failed` — no recording loop, no prompt rotation, no Record button. It mirrors the face `EnrollPage` exactly (page starts the control, VM renders the outcome). `VoiceEnrollPage` sets `RecordFor` from `VoiceTuning.RecordFor` rather than leaving the control's default, so enroll and identify can't drift apart. (`IsNotEnrolling` on the VM exists purely so the XAML can hide the name entry without an inverse-bool converter.)

**Mic capture is vendored — but only until the next `Shiny.Audio` beta lands (2026-07-27).** `Shiny.Audio` *is* published now (`3.0.0-beta-0017`), so the vendored copies are going away. What blocked the swap: its `AppleAudioSource` hard-coded `AVAudioSessionMode.VoiceChat` + `AllowBluetooth`/`A2DP` — precisely the two settings [measured here](#audio-capture-is-the-fragile-part-not-the-threshold-settled--measured-2026-07-22) as the cause of speaker-match failure, and `AudioProcessingOptions.None` didn't turn them off. `~/Desktop/dev/speech` has been patched (mode is `Measurement` unless processing is requested; new `AudioProcessingOptions.AllowBluetooth` + `AudioProcessingOptions.Analysis`); once a beta ships with it, delete `Sample/Platforms/{iOS,Android}/*AudioSource.cs` + `Features/Voice/Audio/{IAudioSource,PipeStream,NullAudioSource,AudioCaptureRegistration}.cs`, call `AddAudioServices()`, and reduce `VoiceRecorder` to permission + PCM16→float (the package already emits **16 kHz mono PCM16** via `AVAudioConverter`, so the hand-rolled resampler goes too). **Pass `AudioProcessingOptions.Analysis`** — the default still allows Bluetooth. Until then, the vendored path below is what ships: `Sample/Platforms/iOS/AppleAudioSource.cs` (`AVAudioEngine` tap) + `Sample/Platforms/Android/AndroidAudioSource.cs` (`AudioRecord`), both `public class … : IAudioSource` in namespace `Sample.Features.Voice.Audio` so `AddAudioCapture()` picks the right one per-TFM via `#if IOS/ANDROID` (a `NullAudioSource` stub covers MacCatalyst/Windows). **Format normalization is deliberate**: the shared `IAudioSource` contract yields **float32 mono** at the device's native rate (the vendored Apple source's `desiredFormat` was never applied — it taps at hardware rate), and **`VoiceRecorder` resamples to the 16 kHz** the embedder needs (Android is already 16 kHz → no-op; Apple ~48 kHz → linear resample). `VoiceRecorder.RecordAsync(TimeSpan)` is the single seam the pages use: it owns the **MAUI `Permissions.Microphone`** request, capture lifetime, and PCM→float→resample. Needs `NSMicrophoneUsageDescription` (iOS) + `RECORD_AUDIO` (Android), both added. Still needs a real ECAPA `.onnx` bundled at `Sample/Resources/Raw/ecapa.onnx` (gitignored, supplied per build, same as `arcface.onnx`); missing model surfaces as a "model missing" message on the voice pages, not a crash.

### Not yet built (voice)

- **No benchmarks** for voice (face has `Recognize` latency vs gallery size).
- **No automated test for `VoiceEnrollmentView`** — the session underneath is well covered, but the control's loop, countdown and ceiling handling are verified by running the Sample on-device.

## Sample structure (feature folders)

The Sample is organized **by feature (vertical slices)**, mirroring `~/Desktop/dev/wonderland`, not by technical layer. Each `Features/<Domain>/` folder owns its pages+VMs (under `Pages/`) and a per-feature **`IMauiModule`** that registers its services:

- `Features/Face/` — `FaceModule` (`AddFaceIntelligence` + `AddTransient<FaceRecognitionAnalyzer>`), `Pages/` (Recognize uses `FaceRecognitionView`, Enroll uses the `FaceEnrollmentView` wizard, People + `PersonRow`). Namespace `Sample.Features.Face[.Pages]`. (`FaceDetectionExtensions.cs` was deleted with the camera `FaceAnalyzer` path.)
- `Features/Voice/` — `VoiceModule` (`AddVoiceIntelligence` + `AddAudioCapture`), `VoiceTuning` (measured `MaxDistance`/`RecordFor`), `Audio/` (vendored `IAudioSource`/`PipeStream`/`VoiceRecorder`/`AudioCaptureRegistration`/`NullAudioSource`), `Pages/` (VoiceRecognize/VoiceEnroll/Speakers + `SpeakerRow`). The fbank front end that used to live here (`KaldiFbank`, `FbankSpeakerEmbedder`) moved into `Shiny.VoiceIntelligence.Onnx`.
- `Features/Documents/` — `DocumentsModule` (`AddDocumentIntelligence`), `Pages/` (Scan).
- `Infrastructure/BundledAssets.cs` — `LoadBundledModel(...)` shared by the Face + Voice modules.

**Branding**: the app icon and splash are the Shiny mark, copied from `~/Desktop/dev/shiny/art/` (`appicon.png`, `appiconfg.png`, `splash.png` — all the same source image) and declared exactly as the Shiny sample does: `<MauiIcon Include="Resources\AppIcon\appicon.png" ForegroundFile="...appiconfg.png"/>` with **no `Color`** (the artwork carries its own background) and `<MauiSplashScreen ... Color="#0B0E17" BaseSize="200,200"/>`. The .NET template SVGs were removed. Note the resizetizer does **not** clean up after a changed `BaseSize` — a stale `splash_<hash>` set at the old size lingers in `bin/`, so `rm -rf Sample/bin/Debug/net10.0-ios Sample/obj/Debug/net10.0-ios` after changing icon/splash inputs if you want to confirm what actually ships.

`MauiProgram.cs` stays thin: `.UseShinyShell(...).UseShinyControls().UseShinyCamera().AddInfrastructureModules(new FaceModule(), new VoiceModule(), new DocumentsModule())`. `IMauiModule`/`AddInfrastructureModules` come from **`Shiny.Extensions.MauiHosting`** (5.1.2). VM↔Page maps still use `[ShellMap<TPage>("Route", registerRoute: false)]`; tab layout lives in `AppShell.xaml` with one `xmlns` alias per feature `.Pages` namespace. The `Sample.csproj` `<Import>`s **both** ONNX linker `.targets` (face + voice; the voice target name is `_Voice`-suffixed so they coexist — verified: both heads link clean).

## Document scanning (`Shiny.DocumentIntelligence`)

A single **modal** scanner contract — `IDocumentScanner.ScanAsync(...)` → `DocumentScanResult` (page images + optional PDF, or `IsCancelled`). It is **not** a frame analyzer; it takes over the screen, so it composes as a separate service, not another `CameraView` analyzer. Register with `services.AddDocumentIntelligence()` (registers `IDocumentScanner` → the per-TFM `DocumentScanner`). Tune via `DocumentScanRequest` (`PageLimit`, `AllowGalleryImport`, `Formats`).

**Extraction types**: `Receipt`, `Invoice` (OCR + heuristics), `DriversLicense` (AAMVA PDF417), `Passport` (ICAO 9303 MRZ), `CreditCard` (OCR + Luhn).

### OCR geometry is preserved, and rows are regrouped before parsing (settled — this was the main accuracy bug)

**Both OCR engines split text at large whitespace gaps, so a receipt's `TOTAL .......... 24.99` comes back as *two* observations** — the label and the amount are never in the same string. Every parser here is keyword-anchored ("the amount on the line that says total"), so before this they matched nothing and silently fell through to their fallbacks: `ReceiptParser` took `MaxMoney(text)`, the largest number anywhere on the page, which on a discounted receipt is the pre-discount subtotal. Total, tax, subtotal *and* line items were all effectively being guessed.

The fix is in the plumbing, not the parsers. `RecognizedLine` carries `TextBounds` (normalized 0..1, **top-left origin** — Vision's bottom-left rects and ML Kit's pixel rects both convert on the way in, so layout code has one coordinate space), and `RecognizedText.FromLines` — the factory every recognizer already used — runs `Internals/TextLayout.cs`: order into reading order, group fragments into visual rows, then compose `FullText` from the **rows**. So all four parsers improved with no parser change.

- `RecognizedText.Lines` is still the raw engine fragments; `Rows` is the grouped view; `FullText` is the rows joined. Parsers read `FullText`.
- Grouping rule: same row when vertical centres are within **0.6 × the *median* fragment height**. Median, not mean — a receipt's merchant name is several times the height of its line items and a mean would stretch the tolerance until adjacent rows merged. 0.6 sits comfortably below the ~1.2× spacing of consecutive rows, so a passport's two MRZ lines stay separate (`MrzLines_AreNotMergedIntoOne`) while a column of amounts still lands on its labels.
- **Missing geometry is a passthrough, not an error.** If *any* fragment lacks bounds the set is returned untouched, so the bare-net10.0 stub and any custom `ITextRecognizer` keep their exact old behaviour.
- Bonus fix: ML Kit's block order isn't reading order, and Vision was only sorted by Y with no X tiebreak — ordering now happens once, geometrically, for both.
- Covered by `TextLayoutTests` (10 tests through the public `FromLines` seam, including the end-to-end "parser finds the real total, not the biggest number").

Still open, in value order: **multi-candidate + checksum selection** (Vision's `TopCandidates(1)` discards alternatives you could validate with Luhn / MRZ check digits — the biggest remaining win for cards and passports); MRZ character-class repair (`MrzParser.Normalize` says it maps OCR confusions toward `<` but only uppercases and strips whitespace, and `FindFixedWidthRun` then *requires* a `<`); `TextRecognitionOptions.Document` leaving `MinimumTextHeight` at Vision's 1/32-of-image-height default, which drops small print; receipt arithmetic cross-validation (`subtotal + tax ≈ total`); `ExtractLineItems`' `LastIndexOf(amount.ToString("0.00"))` failing on grouped amounts like `1,234.56`; and `DocumentExtractor` concatenating all pages into one string, which makes "bottom-most total" cross page boundaries.

**Credit card scanning (`CreditCardParser` → `CreditCardData`).** The Luhn check digit is what makes this work: rather than guessing which digit run on a noisy card front is the PAN, every 13–19 digit candidate is tested and only a Luhn-valid one is accepted, which rejects dates, phone numbers and OCR noise without layout heuristics. Network comes from the IIN prefix; expiry prefers a `VALID THRU`-labelled line and otherwise takes the *latest* MM/YY found (cards also print `MEMBER SINCE` in the same shape); the cardholder is the bottom-most all-caps multi-word line that isn't card furniture. A result is returned even when Luhn fails, with `IsValid=false`, so a UI can show what was read instead of "nothing found".

**Two security choices in that type are deliberate — don't undo them:**
- **No CVV, ever.** There is no property and the parser never looks for one. PCI-DSS forbids storing it post-authorization and a scanning API that surfaces it invites exactly that. `CreditCardParserTests.NoCvvIsEverExposed` asserts this structurally so it can't be added by accident.
- **`ToString()` is overridden to mask the PAN.** A positional record's generated `ToString` prints every property, so `logger.LogInformation("{Card}", card)` would put a full card number in the logs. `MaskedNumber`/`Last4` are the display forms; `Number` holds the real value. The Sample's Scan page shows the **masked** number deliberately — a demo screen that renders a live PAN teaches the wrong habit. Scanning a PAN puts the app in PCI scope: keep it in memory, hand it to the processor, don't persist it.

**Multi-targeted, one `DocumentScanner` class name per TFM.** Shared abstractions live at the project root; platform code lives under `Platforms/{Net,Apple,MacOS,Android}/` and is opted in by `$(TargetPlatformIdentifier)` conditions in the csproj (`Compile Remove="Platforms/**"` then re-`Include` per TFM). Each platform defines its own `public class DocumentScanner : IDocumentScanner` in the `Shiny.DocumentIntelligence` namespace, so the shared `AddDocumentIntelligence` resolves the right one without `#if`.

| TFM | Impl | Notes |
|---|---|---|
| `net10.0-ios` / `-maccatalyst` | VisionKit `VNDocumentCameraViewController` | Present from the top view controller (found natively via `UIApplication.ConnectedScenes`), delegate → per-page `UIImage.AsPNG()`. `IsSupported` gates on OS 13+ (the native `+isSupported` isn't bound). |
| `net10.0-android` | ML Kit `GmsDocumentScanning` (`Xamarin.GooglePlayServices.MLKit.DocumentScanner`) | `GetStartScanIntent` → `IntentSender`, launched from a transparent **proxy `DocumentScannerActivity`** (sidesteps the AndroidX `ActivityResultLauncher` "register before RESUMED" rule). Can also emit a PDF. |
| `net10.0-macos` (AppKit) | Vision `VNDetectDocumentSegmentationRequest` + Core Image `CIPerspectiveCorrection` | AppKit has **no** document camera, so the user picks image(s) via `NSOpenPanel` and each is deskewed. Vision corners are normalized/bottom-left = CIImage space → **no Y-flip**. The filter is driven by KVC keys (`inputTopLeft`…) to avoid binding-name guessing. |
| bare `net10.0` (+ Windows) | throwing stub |

**`IDataDetector` — Apple's `NSDataDetector` behind a seam.** Finds dates, addresses, phone numbers and links in *already-recognized text*, so it runs after `ITextRecognizer` and composes with any OCR source. It's the engine behind tappable dates in Mail: locale-aware, resolves relative phrasing, and returns addresses pre-split into street/city/state/ZIP — all things the managed regex in `ParsingHelpers` won't do. Implemented in `Platforms/AppleShared/` (iOS/Catalyst/macOS); **inert on Android and bare net10.0** (`IsSupported => false`, returns empty) — Android's nearest equivalent is ML Kit Entity Extraction, which needs another Play Services dependency plus a runtime model download, so it isn't on by default.

Enrichment is strictly **additive**, which is what keeps cross-platform behaviour sane: it populates `ExtractedDocument.Entities` and fills a `Receipt.Date`/`Invoice.InvoiceDate` the type parser *missed*, but never overwrites a parsed value. So a platform without a detector produces a strict subset of the same result, not a different one. Preferring the detector's date outright would need measuring against real receipts first — the first date on a receipt isn't reliably the transaction date. Guarded by `DataDetectorEnrichmentTests`.

Note `Platforms/AppleVision/` was renamed to `Platforms/AppleShared/`: the folder is defined by its TFM set (the three Apple platforms), not by the framework it uses, and it now holds Foundation code alongside Vision code. `IsSupported => false`; `ScanAsync` throws `PlatformNotSupportedException`. |

**No MAUI dependency** (deliberate — `UseMauiEssentials` is resolved before the project body, so it can't be set per-TFM without polluting the shared `Directory.Build.props`; and macOS AppKit isn't a MAUI platform). Platform context is obtained natively: iOS walks `ConnectedScenes` for the top VC; **Android tracks the current `Activity` via `AndroidPlatform`**, bootstrapped by a zero-config `StartupContentProvider` (`[ContentProvider]` with a `${applicationId}`-scoped authority) that registers `IActivityLifecycleCallbacks` before the first activity — the same auto-init trick Essentials uses.

The Sample's **Scan** tab (`ScanViewModel`/`ScanPage`) drives it end-to-end. `BuildSections(...)` projects the result into `ParsedSection`/`ParsedField` rows (`Features/Documents/Pages/ParsedSection.cs`), so the screen shows **the typed result as fields** — each section headed by the actual .NET type (`ReceiptData`, `PassportData`, …) — rather than a formatted text blob. It renders **every** field of each payload, including the license `IssueDate` and the full remaining AAMVA `Elements` dictionary, and the passport `DocumentCode`/`IssuingCountry`/`PersonalNumber`. **If you add a field to a payload record, add it there too** — the projection is hand-written, not reflected, because the app is AOT-compatible.

Three display rules there are deliberate: a field renders **even when null** (as `—`), because what the parser *failed* to read is as informative as what it read and omitting empties makes a partial parse look complete; **missing and wrong are coloured differently** (grey `—` vs red for a failed Luhn/MRZ check digit or an expired card), because they mean different things; and the **raw OCR text is behind a toggle**, auto-opened only when nothing parsed — it's how you tell a parsing miss from an OCR miss, but it shouldn't be the headline. The captured pages are a horizontal thumbnail strip for the same reason: on this screen the parsed result is the subject and the images are the evidence.

The MAUI heads are android/ios, so the Sample exercises the VisionKit + ML Kit paths; the macOS AppKit path is library-only (MAUI targets Mac **Catalyst**, which uses the VisionKit path). iOS needs `NSCameraUsageDescription` in `Info.plist` (added).

## Tuning

- `FaceIntelligenceOptions.MaxDistance` (default `0.6` cosine distance ≈ `0.4` similarity). Lower = stricter (fewer false accepts).
- Enroll **several shots per person** (varied angle/lighting/expression). It's a gallery of templates; recognition takes the nearest across all of them, so more good shots improves recall. Quality over quantity — a blurry shot becomes a weak template.

## TODOs / known follow-ups

- **Identity is now an explicit `PersonIdentifier`, and the library stores no display name.** `Person.PersonIdentifier`/`Speaker.PersonIdentifier` is the identity key — an opaque caller-chosen string (user id, employee number, GUID). It's what `Enroll` takes, `Recognize` returns, and `Forget` deletes by; `IFaceStore`/`IVoiceStore` expose `RemoveByPersonIdentifier`. **The Sample passes the typed-in name as the identifier**, which is a legitimate choice for a demo and keeps its "one name per person" UX. Two related names that are easy to confuse: `Person.Id` is the *document* id (one stored shot, a fresh GUID per enroll), and `RecognitionResult.DocumentId` is that document id for the nearest hit — neither is an identity. What remains:
  - The **conflation risk didn't vanish, it moved to the caller.** Two different people enrolled under the same identifier are still silently merged, and one person under two identifiers still fragments. The library now makes that the app's explicit decision rather than an accident of a free-text name — an app that mints a stable key per person (as the Sample deliberately does not) is immune.
  - Gate enrollment with a recognition pass: before inserting, run `Recognize`; if it already matches someone within `MaxDistance`, prompt "looks like X — add as another shot of X, or enroll as new?". (The no-box `Enroll` overload already does this via `GateEnrollmentOnRecognition`; the box-based one used by the camera controls does not.)
  - Vote-based matching: require a majority of the top-k (`CandidateCount`) neighbors to agree before accepting a match, instead of trusting `hits[0]`.
  - **No migration was written.** The identifier is persisted in the JSON document blob under its property name, so records enrolled before the rename deserialize with an empty `PersonIdentifier` and will never match. Delete `faces.db`/`voices.db` (or re-enroll) rather than expecting old data to carry over.
- **Coordinate space**: `DetectedFace.Bounds` (normalized `0..1`, upright image space) and the captured `CameraPhoto` are paired by `DetectionCaptured`. The Sample now scales bounds by `photo.Width`/`photo.Height` (the normalized→pixel fix). Still verify on-device that the resulting box lands on the face given **front-camera mirroring/rotation** — if the still is mirrored/rotated vs. upright image space, the box may need flipping/rotating before cropping.
- **Alignment**: `FaceImaging.CropResize` does an expand-and-resize crop. For best ArcFace accuracy, add 5-point landmark alignment (the detector returns optional `Landmarks`).
