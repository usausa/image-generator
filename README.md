# Image Generator

Browser-based AI image generation tool powered by Azure OpenAI (gpt-image-2).

## Features

- **Text-to-image** — Generate images from a text prompt
- **Image editing** — Upload reference images and generate variations or edits
- **Drag & drop upload** — Drop image files directly onto the upload area
- **Batch generation** — Generate multiple images in one request; results appear incrementally
- **Parameter control** — Adjust size, quality, output format, compression, and number of generations
- **Output persistence** — Generated images are saved to disk and served as static files
- **Structured logging** — File-based daily rolling logs via Serilog

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Azure OpenAI resource with a `gpt-image-2` deployment

## Configuration

All settings are read from `appsettings.json` (or user secrets for local development).

| Key | Description | Default |
|-----|-------------|---------|
| `ImageGenerator:Endpoint` | Azure OpenAI endpoint URL | *(required)* |
| `ImageGenerator:DeploymentName` | Deployment name | `gpt-image-2` |
| `ImageGenerator:ApiKey` | Azure OpenAI API key | *(required)* |
| `ImageGenerator:OutputPath` | Directory for generated images | `../output` |
| `ImageGenerator:UploadPath` | Directory for uploaded reference images | `../upload` |
| `ImageGenerator:MaxRetries` | Maximum retry attempts on transient errors | `5` |

Paths are resolved relative to `AppContext.BaseDirectory` (the directory containing the executable).

### User secrets (local development)

```shell
dotnet user-secrets --project ImageGenerator set "ImageGenerator:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets --project ImageGenerator set "ImageGenerator:ApiKey" "<your-api-key>"
```

## Running

```shell
dotnet run --project ImageGenerator
```

Open a browser at `http://localhost:5000` (or the port shown in the console).

## Logging

Log files are written to `../log/ImageGenerator_<date>.log` relative to the executable directory.
The log level can be overridden in `appsettings.json` under the `Serilog` section.

## Directory layout at runtime

```
<install-dir>/
  ImageGenerator.exe   (or ImageGenerator on Linux)
  appsettings.json
../log/                 ← daily rolling log files
../output/              ← generated images (served as /output/*)
../upload/              ← uploaded reference images (served as /uploads/*)
```

## Installing as a service

### Windows (Windows Service)

```shell
sc create ImageGenerator binPath="<install-dir>\ImageGenerator.exe" start=auto
sc start ImageGenerator
```

### Linux (systemd)

Create `/etc/systemd/system/image-generator.service`:

```ini
[Unit]
Description=Image Generator

[Service]
WorkingDirectory=<install-dir>
ExecStart=<install-dir>/ImageGenerator
Restart=always

[Install]
WantedBy=multi-user.target
```

```shell
systemctl enable --now image-generator
```
