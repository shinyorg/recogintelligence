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
| `src/Shiny.VoiceIntelligence` | `net10.0` | **Core** (voice): contracts (`ISpeakerEmbedder`, `IVoiceStore`, `IVoiceIntelligence`), `Speaker`, `RecognitionResult`, `VoiceMatch`, `VoiceIntelligenceManager`, `VoiceIntelligenceRegistrationBuilder` + `AddVoiceIntelligence`. Deps: **DI.Abstractions only** (no SkiaSharp — audio has no image stage). **No ONNX, no DocumentDb, no audio capture.** |
| `src/Shiny.VoiceIntelligence.Onnx` | `net10.0` | `OnnxEcapaEmbedder` + `UseOnnxEmbedder`. Deps: voice core + Microsoft.ML.OnnxRuntime. **Ships the iOS linker targets** (target name suffixed `_Voice` so it coexists with the face `.Onnx` targets — see below). |
| `src/Shiny.VoiceIntelligence.DocumentDb` | `net10.0` | `DocumentDbVoiceStore` + `UseDocumentDbStore(providerFactory)`. Provider-agnostic; deps: voice core + Shiny.DocumentDb. |
| `src/Shiny.VoiceIntelligence.DocumentDb.Sqlite` | `net10.0` | Turnkey `UseSqliteStore` (sqlite-vec). Deps: voice `.DocumentDb` + Shiny.DocumentDb.Sqlite(.VectorSupport). |
| `src/Shiny.DocumentIntelligence` | `net10.0;net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-macos` | **Native document scanner**: `IDocumentScanner` + `AddDocumentIntelligence`. VisionKit (iOS/Catalyst), ML Kit (Android), Vision segmentation (macOS AppKit), throwing stub (bare net10.0). Dep: DI.Abstractions (+ ML Kit binding on Android only). See [Document scanning](#document-scanning-shinydocumentintelligence). |
| `Sample` (repo root) | `net10.0-android;net10.0-ios` (`maccatalyst` commented out; `windows` only on a Windows host) | MAUI app, **MVVM via Shiny.Maui.Shell**, organized **by feature** (`Features/{Face,Voice,Documents}/`), each with a per-feature `IMauiModule` (`Shiny.Extensions.MauiHosting` → `AddInfrastructureModules(...)`). Tabs: **Recognize/Enroll/People** (face) · **Voice ID/Voice Enroll/Speakers** (voice) · **Scan** (page↔ViewModel maps, `ShinyShell`). Refs face+voice `.Onnx` + `.Sqlite` + `.DocumentIntelligence` (under `..\src\`) via `ProjectReference`. See [Sample structure](#sample-structure-feature-folders). |

**Repo layout**: shippable packages in `src/`, the demo app in `Sample/` at the root, tests/benchmarks in `tests/`. **Central Package Management** is on — all versions live in `Directory.Packages.props` (CPM), so `<PackageReference>` elements carry **no `Version=`**; add/bump versions there. `Directory.Build.props` hoists the shared `ImplicitUsings`/`Nullable` (per-project settings like `TargetFramework`, `IsAotCompatible`, `IsPackable` stay in each csproj). Note: with two feeds + CPM, restore emits `NU1507` (package-source-mapping advisory) — benign.

**Two version pins are deliberate (don't "update" them blindly)** — both are documented inline in `Directory.Packages.props`:
- **`Microsoft.ML.OnnxRuntime` is held at `1.20.1`.** ORT `1.27.0` (and other newer revs) break the **Android** manifest merge with `AMM0000` "namespace used in multiple modules" (duplicate `com.google.*` namespaces) on the .NET Android 36.x manifest merger — the app won't build. iOS/tests are fine; the block is Android-only. Revisit when ORT ships an Android AAR compatible with the current merger.
- **`Microsoft.Maui.Controls` is held at `10.0.71`** to match the prerelease **Shiny camera betas** (`Camera`/`Camera.Face` 0141, `Controls` 0121). Bumping MAUI (e.g. 10.0.80) and/or the camera betas (0142) shifts transitive AndroidX/play-services/firebase versions and re-triggers the same `AMM0000` Android breakage. Bump MAUI and the camera package **together**, and only after verifying the Android head. Everything else in CPM tracks latest stable (xunit v3 4.0 / BenchmarkDotNet 0.16 are prerelease-only, so those stay on latest stable too).

## Build & run

```bash
# Everything
dotnet build Shiny.FaceIntelligence.slnx

# Pack all packages (the .Onnx package ships the iOS linker targets — see below)
dotnet pack Shiny.FaceIntelligence.slnx -c Release -o ./artifacts

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
- `Shiny.VoiceIntelligence.TestKit` / `Shiny.VoiceIntelligence.Tests` — the voice twins. Same shape (`AddVoiceIntelligence(voice => { voice.UseEmbedder(new FakeSpeakerEmbedder(dim)); voice.UseSqliteStore(...); })` against the real sqlite-vec store), and they **share the same committed `tests/runtimes/.../vec0` binary**. 10 tests, all passing: match/no-match, empty store, cosine-distance geometry, threshold enforcement, multi-utterance nearest-neighbor, `Forget`/`GetAll`, and a 192-d dimension round-trip. **No fake-image trick needed** (see below) — the voice manager does no decode, so `FakeSpeakerEmbedder` reads the sample buffer directly as the vector and `TestVoices.Utterance(...)` just hands a vector in.

Key design points when extending:
- **The fake-image trick**: `FaceIntelligenceManager.Enroll` decodes the image with SkiaSharp for its thumbnail, independent of the embedder. So `TestFaces.Image(...)` produces a *real* PNG with the embedding appended as a trailing payload — Skia decodes the PNG, `FakeEmbedder` reads the trailing block. Don't pass raw float buffers as "images"; enrollment will throw on decode.
- **vec0 is required at runtime**; tests `Assert.Skip` when `Vec0Locator.Find()` returns null (except `Vec0Binary_IsAvailable_OnDeveloperMachine`, which asserts presence to document the dependency). `Vec0Locator` searches next to the assembly and walks up to the committed `runtimes/` folder so BenchmarkDotNet's generated subprocess finds it.
- These need **no ONNX model** — the model only matters for the real embedder, which isn't exercised here.

`nuget.config` clears sources and adds the public **dnceng `dotnet10` feed** alongside nuget.org — it serves the `Microsoft.Maui.Controls 10.0.71` build that the prerelease Shiny camera controls target. The Sample pins `10.0.71` to avoid an NU1605 downgrade. Don't remove the feed or bump MAUI independently of the camera package.

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

**Detection: two paths (deliberate).** ArcFace is an **embedder**, not a detector — it always returns a vector for whatever `FaceBox` it's handed and never reports "bad face." So a face box must come from somewhere. There are two ways, both supported:
- **In-library ONNX detector (`IFaceDetector`, used by enrollment).** `Shiny.FaceIntelligence.IFaceDetector` (Infrastructure seam) → `Onnx.OnnxUltraFaceDetector` (UltraFace RFB-320: resize → `(px-127)/128` → NCHW → scores`[1,N,2]`+boxes`[1,N,4]` → threshold + NMS → pixel `FaceBox`es with confidence). Registered via `UseOnnxDetector(...)`/`UseDetector(...)` — **optional**; the manager takes `IEnumerable<IFaceDetector>` and only the **no-box** overloads use it. `FaceIntelligenceManager.Enroll(name, imageData, allowDuplicate=false)` and `Recognize(imageData)` run the detector, then apply the gates in `FaceIntelligenceOptions` (`MinDetectionConfidence`, `MinFaceSizeFraction`, `RejectMultipleFaces`, `GateEnrollmentOnRecognition`) and throw `FaceDetectionException{Reason: NoFace|LowConfidence|MultipleFaces|TooSmall}` or `FaceEnrollmentConflictException(match)` (the duplicate/mismatch gate — re-call with `allowDuplicate:true` to force). This is what the Sample's **Enroll** page now uses: one still capture on tap → ONNX detects + gates → embed. **No camera frame analyzer.** (Core's `DetectedFace{Box,Confidence}` is distinct from the camera's `Shiny.Maui.Controls.Camera.Face.DetectedFace`; the Sample aliases the camera one as `CameraFace` in `FaceDetectionExtensions.cs`.)
- **Camera frame analyzer (still used by the Recognize page).** `Shiny.Maui.Controls.Camera.Face`'s `FaceAnalyzer` raises `FacesDetected(FacesDetectedEventArgs)` per frame; the Sample (`FaceDetectionExtensions.cs`) picks the `Largest()` camera face and maps its `Bounds` to a Core `FaceBox`, then calls the **box-based** `Recognize(imageData, box)`. The box-based `Enroll`/`Recognize` overloads need no detector and are unchanged.

**iOS capture gotcha (camera beta `1.0.1-beta-0121`):** the declarative `CaptureOnDetection="True"` → `CameraView.DetectionCaptured` path **never fires on iOS** in this beta (Vision detection itself works — `FacesDetected` fires and the preview is live). So the **Recognize** page drives capture itself: subscribe to `FaceAnalyzer.FacesDetected`, and continuously call `CameraView.CapturePhotoAsync()` and pass `photo.Data` + the scaled box to the VM. (The **Enroll** page no longer uses `FacesDetected` at all — it captures one still on the button tap and lets the in-library ONNX detector find + gate the face; see the two-path detection note above.) A page-level `capturing` bool guards against the per-frame event re-entering while a capture/enroll is in flight. If a future camera build fixes `DetectionCaptured`, the declarative path is simpler — but verify on-device before switching back. **`DetectedFace.Bounds` are normalized `0..1`** (upright image space), but `FaceBox` is in **pixels** — so `ToFaceBox(photo.Width, photo.Height)` scales by the captured photo's pixel dimensions. (Passing the normalized values straight through crops a sub-pixel sliver and the face is effectively never found.) The library's contract is "give me the photo bytes + a pixel `FaceBox`."

**JSON / AOT**: all packages are `IsAotCompatible`. `FacesJsonContext` (in **core**) is the source-generated `JsonSerializerContext` for `Person`; `UseDocumentDbStore` feeds `FacesJsonContext.Default.Options` to the store for serialization + the LINQ expression visitor. Do **not** add another `[JsonSerializerContext]` — it's inherited. New persisted document types must be added as `[JsonSerializable]` here.

## Runtime assets you must supply (not in the repo)

The app launches without these; enroll/recognize surface a "model missing" message rather than crashing.
1. **ArcFace ONNX model** (112×112 in, 512-d out). Drop it at `Sample/Resources/Raw/arcface.onnx` (gitignored — supply per build); the `Resources\Raw\**` glob bundles it. The Sample loads it **as bytes** and configures `face.UseOnnxEmbedder(o => o.ModelBytesProvider = () => LoadBundledModel("arcface.onnx"))` (reads via `FileSystem.OpenAppPackageFileAsync`). Bundled assets aren't real file paths on iOS/Android, so use `ModelBytesProvider`/`ModelBytes` rather than `ModelPath` — `OnnxEmbedderOptions` supports all three (priority: provider → bytes → path), and `OnnxArcFaceEmbedder` has matching `byte[]` (bundled/server-stream) and `string` (file/server) constructors. The provider runs lazily on first enroll/recognize, so a missing model surfaces there (pages catch `FileNotFoundException`), not at startup. Model size dominates app size — prefer a compact ArcFace (MobileFaceNet/`w600k_mbf`, single-digit MB) over `w600k_r50` (~166 MB) for on-device; download-on-first-run is the alternative for large models.
2. **UltraFace detector ONNX model** (for the no-box enrollment path). Drop it at `Sample/Resources/Raw/face_detector.onnx` (gitignored — supply per build). Same lazy-bytes flow as ArcFace: `face.UseOnnxDetector(o => o.ModelBytesProvider = () => LoadBundledModel("face_detector.onnx"))`, loaded on first enroll (missing → `FileNotFoundException`, caught by the page). Defaults target **UltraFace version-RFB-320 / slim-320** (input `1×3×240×320`, scores`[1,N,2]`+boxes`[1,N,4]` normalized); tune `OnnxDetectorOptions.InputWidth/Height/Mean/Std/ScoreThreshold/IouThreshold`. A detector with a different output layout (SCRFD/RetinaFace/YuNet) needs its own `IFaceDetector` via `UseDetector(...)`. Tiny (~1 MB), so negligible next to ArcFace. **Only the Enroll page needs it** — Recognize still uses the camera analyzer.
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

**Parallel to face, one-to-one.** Same builder + validation (`AddVoiceIntelligence` throws unless an `ISpeakerEmbedder` and an `IVoiceStore` are registered, naming `UseOnnxEmbedder`/`UseSqliteStore`), same lazy-model and lazy-store deferral, same "vector dimension read from the embedder" (`MapVectorProperty<Speaker>(s => s.Embedding, embedder.Dimensions, VectorDistance.Cosine)`), same `[JsonIgnore]` embedding living only in the vec0 sidecar, same "one document = one utterance, keyed by free-text `Name`" model (inherits the **same identity-vs-name TODO** as face). `VoicesJsonContext` is the source-gen `JsonSerializerContext` for `Speaker`. Registration reads identically:

```csharp
services.AddVoiceIntelligence(voice =>
{
    voice.Options.MaxDistance = 0.7f;                                            // see tuning caveat below
    voice.UseOnnxEmbedder(o => o.ModelBytesProvider = () => LoadBundled("ecapa.onnx"));
    voice.UseSqliteStore(o => o.ConnectionString = "Data Source=voices.db");
});
```

**Key differences from face (all deliberate):**
- **Capture-agnostic core.** Face core takes `byte[] imageData` + `FaceBox`; voice core takes a `float[]` **sample buffer** (mono PCM, [-1,1], at `ISpeakerEmbedder.SampleRate`, default 16 kHz). The library **never touches audio hardware** — capturing (mic/file/stream) is the app's job, exactly as the camera is for face. So voice core has **no SkiaSharp** and no image/thumbnail stage; `Speaker` has no thumbnail.
- **Embedder input is the raw waveform.** `OnnxEcapaEmbedder` feeds `[1, samples]` (the common ECAPA export) → `Run` → L2-normalize. A model that expects **features (fbank/MFCC)** needs a feature-extraction step added before `Run` — swap in your own `ISpeakerEmbedder` via `UseEmbedder(...)` for that. Dimension hint defaults to **192** (ECAPA-TDNN); ArcFace's 512 does not apply.
- **The ArcFace model does NOT transfer** — different modality (image→vector vs audio→vector). What transferred is the **ONNX plumbing**: the options/lazy-provider pattern, `UseOnnxEmbedder`, the bundled-asset flow, and the iOS linker `.targets`. You still supply an ECAPA `.onnx`.

**Shared iOS linker `.targets` — the one real cross-package gotcha.** Both `.Onnx` packages auto-import their `build/<PackageId>.targets`, and an app that references **both** (this repo's Sample will) would hit a **duplicate MSBuild target name** error. So `Shiny.VoiceIntelligence.Onnx.targets` is a copy of the face one with the `Target Name` suffixed **`_DropOnnxRegisterCustomOpsForcedSymbol_Voice`**. The `DisableOnnxRegisterCustomOpsWorkaround` property and the `-Wl,-U,_RegisterCustomOps` flag are intentionally **identical** across both (one toggle governs both; the linker de-dupes the repeated `-U`). Everything in [the ONNX linker section](#critical-gotcha-onnx-runtime-iosmaccatalyst-linker-fix) applies verbatim.

**Threshold tuning is unfinished and important.** `VoiceIntelligenceOptions.MaxDistance` defaults to **`0.7`** (permissive) purely as a placeholder. Speaker-embedding score distributions vary far more by model/channel than face does — this **must** be tuned against your actual ECAPA export with measured FAR/FRR before it means anything. Don't ship the default.

### Anti-spoofing (findings — not yet built in either repo)

Plain voiceprint matching is **defeated by a recording or a voice clone** — treat it as a convenience factor, not a security gate. A `grep` confirms **no liveness/anti-spoofing (PAD) exists anywhere in this repo yet**. Roadmap, cheapest-and-strongest first:
1. **Challenge–response (biggest win, no new model).** Generate a random prompt (random digits) per attempt; require the user to say *that*, and gate on (a) an STT transcript matching the prompt **and** (b) the speaker embedding matching. A recording/stockpiled voicebank can't answer an unknown prompt — kills the whole replay class. Needs a speech-to-text service (the speech repo's `ISpeechToTextService`, or platform STT).
2. **A countermeasure (CM) model** — an `ISpoofDetector` seam (bonafide-vs-spoof classifier: AASIST / RawNet2 / LCNN from ASVspoof, ONNX, same plumbing as the embedder). Passive net for synthetic audio; generalizes poorly to unseen attacks, so a filter not a wall.
3. **Multimodal liveness** — this repo's real edge: combine **face liveness + the voice challenge** ("look at the camera and say 7-4-1-9"). Strong on-device gate that neither modality gives alone; an argument for keeping face + voice in one repo.

### Voice in the Sample (built)

Voice now has three Sample tabs — **Voice ID** (`VoiceRecognizePage`), **Voice Enroll** (`VoiceEnrollPage`), **Speakers** (`SpeakersPage`) — under `Sample/Features/Voice/` (see [Sample structure](#sample-structure-feature-folders)). They mirror the face pages but are **button-driven, not continuous** (you can't passively sample a voice), and record through mic capture instead of the camera.

**Mic capture is vendored, not a package.** `Shiny.Audio` is *not* published to NuGet, so its capture impls were copied into the Sample rather than referenced: `Sample/Platforms/iOS/AppleAudioSource.cs` (`AVAudioEngine` tap) + `Sample/Platforms/Android/AndroidAudioSource.cs` (`AudioRecord`), both `public class … : IAudioSource` in namespace `Sample.Features.Voice.Audio` so `AddAudioCapture()` picks the right one per-TFM via `#if IOS/ANDROID` (a `NullAudioSource` stub covers MacCatalyst/Windows). **Format normalization is deliberate**: the shared `IAudioSource` contract yields **float32 mono** at the device's native rate (the vendored Apple source's `desiredFormat` was never applied — it taps at hardware rate), and **`VoiceRecorder` resamples to the 16 kHz** the embedder needs (Android is already 16 kHz → no-op; Apple ~48 kHz → linear resample). `VoiceRecorder.RecordAsync(TimeSpan)` is the single seam the pages use: it owns the **MAUI `Permissions.Microphone`** request, capture lifetime, and PCM→float→resample. Needs `NSMicrophoneUsageDescription` (iOS) + `RECORD_AUDIO` (Android), both added. Still needs a real ECAPA `.onnx` bundled at `Sample/Resources/Raw/ecapa.onnx` (gitignored, supplied per build, same as `arcface.onnx`); missing model surfaces as a "model missing" message on the voice pages, not a crash.

### Not yet built (voice)

- **No benchmarks** for voice (face has `Recognize` latency vs gallery size).

## Sample structure (feature folders)

The Sample is organized **by feature (vertical slices)**, mirroring `~/Desktop/dev/wonderland`, not by technical layer. Each `Features/<Domain>/` folder owns its pages+VMs (under `Pages/`) and a per-feature **`IMauiModule`** that registers its services:

- `Features/Face/` — `FaceModule` (`AddFaceIntelligence`), `FaceDetectionExtensions`, `Pages/` (Recognize/Enroll/People + `PersonRow`). Namespace `Sample.Features.Face[.Pages]`.
- `Features/Voice/` — `VoiceModule` (`AddVoiceIntelligence` + `AddAudioCapture`), `Audio/` (vendored `IAudioSource`/`PipeStream`/`VoiceRecorder`/`AudioCaptureRegistration`/`NullAudioSource`), `Pages/` (VoiceRecognize/VoiceEnroll/Speakers + `SpeakerRow`).
- `Features/Documents/` — `DocumentsModule` (`AddDocumentIntelligence`), `Pages/` (Scan).
- `Infrastructure/BundledAssets.cs` — `LoadBundledModel(...)` shared by the Face + Voice modules.

`MauiProgram.cs` stays thin: `.UseShinyShell(...).UseShinyControls().UseShinyCamera().AddInfrastructureModules(new FaceModule(), new VoiceModule(), new DocumentsModule())`. `IMauiModule`/`AddInfrastructureModules` come from **`Shiny.Extensions.MauiHosting`** (5.1.2). VM↔Page maps still use `[ShellMap<TPage>("Route", registerRoute: false)]`; tab layout lives in `AppShell.xaml` with one `xmlns` alias per feature `.Pages` namespace. The `Sample.csproj` `<Import>`s **both** ONNX linker `.targets` (face + voice; the voice target name is `_Voice`-suffixed so they coexist — verified: both heads link clean).

## Document scanning (`Shiny.DocumentIntelligence`)

A single **modal** scanner contract — `IDocumentScanner.ScanAsync(...)` → `DocumentScanResult` (page images + optional PDF, or `IsCancelled`). It is **not** a frame analyzer; it takes over the screen, so it composes as a separate service, not another `CameraView` analyzer. Register with `services.AddDocumentIntelligence()` (registers `IDocumentScanner` → the per-TFM `DocumentScanner`). Tune via `DocumentScanRequest` (`PageLimit`, `AllowGalleryImport`, `Formats`).

**Multi-targeted, one `DocumentScanner` class name per TFM.** Shared abstractions live at the project root; platform code lives under `Platforms/{Net,Apple,MacOS,Android}/` and is opted in by `$(TargetPlatformIdentifier)` conditions in the csproj (`Compile Remove="Platforms/**"` then re-`Include` per TFM). Each platform defines its own `public class DocumentScanner : IDocumentScanner` in the `Shiny.DocumentIntelligence` namespace, so the shared `AddDocumentIntelligence` resolves the right one without `#if`.

| TFM | Impl | Notes |
|---|---|---|
| `net10.0-ios` / `-maccatalyst` | VisionKit `VNDocumentCameraViewController` | Present from the top view controller (found natively via `UIApplication.ConnectedScenes`), delegate → per-page `UIImage.AsPNG()`. `IsSupported` gates on OS 13+ (the native `+isSupported` isn't bound). |
| `net10.0-android` | ML Kit `GmsDocumentScanning` (`Xamarin.GooglePlayServices.MLKit.DocumentScanner`) | `GetStartScanIntent` → `IntentSender`, launched from a transparent **proxy `DocumentScannerActivity`** (sidesteps the AndroidX `ActivityResultLauncher` "register before RESUMED" rule). Can also emit a PDF. |
| `net10.0-macos` (AppKit) | Vision `VNDetectDocumentSegmentationRequest` + Core Image `CIPerspectiveCorrection` | AppKit has **no** document camera, so the user picks image(s) via `NSOpenPanel` and each is deskewed. Vision corners are normalized/bottom-left = CIImage space → **no Y-flip**. The filter is driven by KVC keys (`inputTopLeft`…) to avoid binding-name guessing. |
| bare `net10.0` (+ Windows) | throwing stub | `IsSupported => false`; `ScanAsync` throws `PlatformNotSupportedException`. |

**No MAUI dependency** (deliberate — `UseMauiEssentials` is resolved before the project body, so it can't be set per-TFM without polluting the shared `Directory.Build.props`; and macOS AppKit isn't a MAUI platform). Platform context is obtained natively: iOS walks `ConnectedScenes` for the top VC; **Android tracks the current `Activity` via `AndroidPlatform`**, bootstrapped by a zero-config `StartupContentProvider` (`[ContentProvider]` with a `${applicationId}`-scoped authority) that registers `IActivityLifecycleCallbacks` before the first activity — the same auto-init trick Essentials uses.

The Sample's **Scan** tab (`ScanViewModel`/`ScanPage`) drives it end-to-end. The MAUI heads are android/ios, so the Sample exercises the VisionKit + ML Kit paths; the macOS AppKit path is library-only (MAUI targets Mac **Catalyst**, which uses the VisionKit path). iOS needs `NSCameraUsageDescription` in `Info.plist` (added).

## Tuning

- `FaceIntelligenceOptions.MaxDistance` (default `0.6` cosine distance ≈ `0.4` similarity). Lower = stricter (fewer false accepts).
- Enroll **several shots per person** (varied angle/lighting/expression). It's a gallery of templates; recognition takes the nearest across all of them, so more good shots improves recall. Quality over quantity — a blurry shot becomes a weak template.

## TODOs / known follow-ups

- **Identity is keyed solely by the free-text `Person.Name`** — the main fragility. Two different people enrolled under the same name are silently conflated (a match to either returns that one name); one person under two names fragments into competing identities whose returned label varies run-to-run by pose/lighting. Enrolling never corrupts another person's data (independent GUID inserts), but:
  - **(highest value)** Decouple identity from display name: add a stable `PersonKey`/id to `Person` and group documents by it instead of by `Name`.
  - Gate enrollment with a recognition pass: before inserting, run `Recognize`; if it already matches someone within `MaxDistance`, prompt "looks like X — add as another photo of X, or enroll as new?".
  - Vote-based matching: require a majority of the top-k (`CandidateCount`) neighbors to agree before accepting a match, instead of trusting `hits[0]`.
- **Coordinate space**: `DetectedFace.Bounds` (normalized `0..1`, upright image space) and the captured `CameraPhoto` are paired by `DetectionCaptured`. The Sample now scales bounds by `photo.Width`/`photo.Height` (the normalized→pixel fix). Still verify on-device that the resulting box lands on the face given **front-camera mirroring/rotation** — if the still is mirrored/rotated vs. upright image space, the box may need flipping/rotating before cropping.
- **Alignment**: `FaceImaging.CropResize` does an expand-and-resize crop. For best ArcFace accuracy, add 5-point landmark alignment (the detector returns optional `Landmarks`).
