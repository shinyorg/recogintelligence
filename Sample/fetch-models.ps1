$ErrorActionPreference = 'Stop'

$raw = Join-Path $PSScriptRoot 'Resources\Raw'
New-Item -ItemType Directory -Path $raw -Force | Out-Null

$models = @(
	@{
		Uri = 'https://huggingface.co/immich-app/buffalo_s/resolve/main/recognition/model.onnx'
		Name = 'arcface.onnx'
	},
	@{
		Uri = 'https://media.githubusercontent.com/media/onnx/models/main/validated/vision/body_analysis/ultraface/models/version-RFB-320.onnx'
		Name = 'face_detector.onnx'
	}
)

foreach ($model in $models) {
	$destination = Join-Path $raw $model.Name
	if (Test-Path $destination) {
		Write-Host "skip  $($model.Name) (already present)"
		continue
	}

	$temporary = "$destination.$([guid]::NewGuid().ToString('N')).download"
	try {
		Write-Host "fetch $($model.Name)"
		Invoke-WebRequest -Uri $model.Uri -OutFile $temporary
		if (-not (Test-Path $temporary) -or (Get-Item $temporary).Length -eq 0) {
			throw "Download of $($model.Name) produced an empty file."
		}
		Move-Item -Path $temporary -Destination $destination
	}
	finally {
		if (Test-Path $temporary) {
			Remove-Item -Path $temporary -Force
		}
	}
}

$ecapa = Join-Path $raw 'ecapa.onnx'
if (-not (Test-Path $ecapa)) {
	Write-Host ''
	Write-Host "NOTE: $ecapa is missing; supply an ECAPA-TDNN speaker embedder model yourself."
	Write-Host "      The voice pages report 'model missing' until it is there."
}
