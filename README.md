# Shiny Face & Document Intelligence

On-device intelligence for .NET (MAUI + native), built on the Shiny stack. Two independent stacks:

### Face intelligence — enrollment + recognition
- **Capture + detect** — `Shiny.Maui.Controls.Camera` + `Shiny.Maui.Controls.Camera.Face` (live preview, on-device detection via Apple Vision / Android ML Kit). Detection gives face **bounds**, not embeddings.
- **Embed** — `Shiny.FaceIntelligence.Onnx`: crop the detected face → **ArcFace ONNX** (`Microsoft.ML.OnnxRuntime`) → a 512-d, L2-normalized vector.
- **Store + match** — `Shiny.FaceIntelligence.DocumentDb.Sqlite` over `Shiny.DocumentDb.Sqlite` **vector search** (`NearestVectors`, backed by sqlite-vec / `vec0`).

"Training" here is **enrollment**: store several embeddings per person; recognition is a nearest-neighbor lookup with a cosine-distance threshold. No model is trained on-device. The pipeline is split into a core package plus swappable embedder/store packages, composed via a registration builder — pull only what you use (native embedder without ONNX, Postgres without sqlite, etc.).

### Document intelligence — native modal scanner
`Shiny.DocumentIntelligence` puts one `IDocumentScanner.ScanAsync(...)` over each platform's first-party scanner: **VisionKit** (iOS/Mac Catalyst), **ML Kit** (Android, optional PDF), and **Vision document segmentation** on macOS AppKit (which has no document camera — it deskews picked images). No MAUI dependency.

| Package | TFM | Role |
|---|---|---|
| `Shiny.FaceIntelligence` | `net10.0` | Core: contracts (`IFaceEmbedder`, `IFaceStore`, `IFaceIntelligence`), `FaceIntelligenceManager`, imaging, builder. SkiaSharp only. |
| `Shiny.FaceIntelligence.Onnx` | `net10.0` | ONNX ArcFace embedder (`UseOnnxEmbedder`) + iOS linker fix. |
| `Shiny.FaceIntelligence.DocumentDb` | `net10.0` | Provider-agnostic Shiny.DocumentDb store (`UseDocumentDbStore`). |
| `Shiny.FaceIntelligence.DocumentDb.Sqlite` | `net10.0` | Turnkey sqlite-vec store (`UseSqliteStore`). |
| `Shiny.DocumentIntelligence` | `net10.0;-android;-ios;-maccatalyst;-macos` | Native document scanner (`IDocumentScanner`, `AddDocumentIntelligence`). |
| `Sample` (root) | `net10.0-android;net10.0-ios` | MAUI app (Shiny.Maui.Shell MVVM): Recognize / Enroll / People / Scan tabs. |

## Quick start

```bash
dotnet build Sample/Sample.csproj -f net10.0-android
dotnet build Sample/Sample.csproj -f net10.0-ios
```

Compose the pipeline at startup:

```csharp
services.AddFaceIntelligence(face =>
{
    face.Options.MaxDistance = 0.6f;
    face.UseOnnxEmbedder(o => o.ModelBytesProvider = () => LoadBundledModel("arcface.onnx"));
    face.UseSqliteStore(o => { o.ConnectionString = "Data Source=faces.db"; o.VectorExtensionPath = "vec0"; });
});
```

Add the document scanner alongside it and call it from anywhere:

```csharp
services.AddDocumentIntelligence(); // registers IDocumentScanner

// later, in a ViewModel/service
var result = await scanner.ScanAsync(new DocumentScanRequest { PageLimit = 10 });
if (!result.IsCancelled)
    foreach (var page in result.Pages) { /* page.ImageData (PNG/JPEG) */ }
```

Two assets are **not** in the repo and required at runtime: an **ArcFace ONNX model** and the **sqlite-vec native binary** (`vec0.dylib`/`vec0.so`). Drop the model at `Sample/Resources/Raw/arcface.onnx` (a compact 112×112 ArcFace like MobileFaceNet keeps app size down) — it's loaded from the app package as bytes. The app launches without these; enroll/recognize report "model missing". See **[CLAUDE.md](CLAUDE.md)** for details.

## Benchmarks

Recognition latency (embed + sqlite-vec `NearestVectors` + threshold) as the enrolled gallery grows, at the real 512-d ArcFace width. A deterministic fake embedder isolates the vector-store cost; no ONNX model involved. Reproduce with `dotnet run --project tests/Shiny.FaceIntelligence.Benchmarks -c Release -- --filter '*'`.

BenchmarkDotNet v0.15.8 · Apple M5 Pro · .NET 10.0.8 (Arm64) · macOS 26.5

| Gallery size | Mean | Allocated |
|---:|---:|---:|
| 100 | 259.0 µs | 91 KB |
| 1,000 | 487.8 µs | 91 KB |
| 10,000 | 4,758.3 µs | 91 KB |

Brute-force exact search scales roughly linearly with gallery size (`VectorIndexKind.None`); ~4.8 ms at 10k enrolled shots is comfortable for on-device use. Managed allocations are constant (the result set), independent of gallery size. For much larger galleries, switch to an ANN index.

## Development

Architecture, build/pack details, the ONNX Runtime iOS linker fix, tuning, and the open TODOs (identity hardening, coordinate space, landmark alignment) are documented in **[CLAUDE.md](CLAUDE.md)** — the canonical reference for working in this repo.
