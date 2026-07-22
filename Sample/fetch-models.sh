#!/usr/bin/env bash
# Fetch the ONNX models the Sample needs at runtime. They are gitignored
# (Sample/Resources/Raw/*.onnx), so a fresh clone has to pull them down.
#
#   ./Sample/fetch-models.sh
#
# ecapa.onnx (the speaker embedder) is NOT fetched here — supply it yourself.
set -euo pipefail

raw="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/Resources/Raw"

# ArcFace embedder — InsightFace buffalo_s recognition (w600k_mbf, MobileFaceNet).
# ~13 MB, input [N,3,112,112], output [1,512]. Kept compact deliberately: w600k_r50 is ~166 MB.
arcface="https://huggingface.co/immich-app/buffalo_s/resolve/main/recognition/model.onnx"

# Face detector — UltraFace version-RFB-320 from the ONNX model zoo.
# ~1.3 MB, input [1,3,240,320], outputs scores [1,4420,2] + boxes [1,4420,4]
# — exactly what OnnxDetectorOptions defaults to.
detector="https://media.githubusercontent.com/media/onnx/models/main/validated/vision/body_analysis/ultraface/models/version-RFB-320.onnx"

fetch() {
    local url="$1" dest="$2"
    if [ -f "$dest" ]; then
        echo "skip  $(basename "$dest") (already present)"
        return
    fi
    echo "fetch $(basename "$dest")"
    curl -fL --progress-bar -o "$dest" "$url"
}

fetch "$arcface" "$raw/arcface.onnx"
fetch "$detector" "$raw/face_detector.onnx"

if [ ! -f "$raw/ecapa.onnx" ]; then
    echo
    echo "NOTE: $raw/ecapa.onnx is missing — supply an ECAPA-TDNN speaker"
    echo "      embedder export (192-d) yourself. The voice pages report"
    echo "      'model missing' until it is there."
fi
