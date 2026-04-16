# Media Bitrate Viewer

A cross-platform desktop app for inspecting how the bitrate of a video file behaves over time. Drop in a file, pick a stream, and get an interactive graph of frame-level bitrate with statistics for whatever range you're looking at.

Useful for checking encoder output, spotting bitrate spikes, comparing CBR vs. VBR behavior, or just understanding where the bits in a file actually go.

## Requirements

- Windows, macOS, or Linux
- [.NET 10 runtime](https://dotnet.microsoft.com/)
- **ffprobe** (from [FFmpeg](https://ffmpeg.org/)) on your `PATH` — the app uses it to read frame metadata. You'll see a clear error message if it's missing.

## Running

From the repo root:

```bash
dotnet run --project src/MediaBitrateViewer.App
```

## Loading a file

- **Drag and drop** a video file onto the window
- **File → Open File…** or **Ctrl/Cmd+O**
- **File → Open Recent** (up to 10 recent files)
- **Reload** the current file with **Ctrl/Cmd+R**

Each file opens in its own window. Dropping onto a window that already has a file loaded opens a new window.

The app analyzes the video frame-by-frame via ffprobe. Results are cached in your OS temp directory, keyed by a fingerprint of the file — so reopening the same file later is instant. Analysis can be cancelled any time with **Escape** or the Cancel button; partial data stays visible and is clearly labelled.

## The graph

The central graph plots bitrate over time in Mbps. You can switch between four modes from the toolbar:

- **Per-second** — bitrate aggregated into 1-second bins (default)
- **Per-frame** — every individual frame
- **Rolling average** — windowed average over a configurable window
- **Peak envelope** — min/max bitrate within a rolling window

For rolling average and peak envelope, pick a window size between 500 ms and 10 s.

### Navigation

- **Scroll wheel** — zoom in/out around the cursor (X-axis)
- **Click and drag** — pan horizontally
- **Double-click** or **Reset Zoom** / **Ctrl/Cmd+0** — fit everything back in view
- **Hover** — a red guide line tracks your cursor and updates the readout in real time

## Side panels

The right sidebar gives you three live readouts:

**Cursor readout** — time and bitrate at your mouse position, plus the current graph mode.

**Stream metadata** — codec, profile, resolution, pixel format, frame rate, duration, declared bitrate, and container info for the active stream.

**Visible range statistics** — min, max, average, 95th percentile, sample count, and time range for whatever part of the graph is currently in view. These update as you zoom and pan, which makes it easy to compare different sections of a file.

## Multiple video streams

If a file has more than one video stream, a stream selector appears in the toolbar. Each stream is analyzed and cached independently.

## Theming

**View → Theme** lets you pick **System**, **Light**, or **Dark**. The choice persists between sessions and the graph colours update live.

## Cache management

The **Cache** dropdown in the toolbar has two actions:

- **Clear cache for current stream** — forces re-analysis of just the selected stream
- **Clear cache for current file** — clears everything cached for the current file

Useful if you've re-encoded a file with the same name or suspect a stale result.

## What's remembered between sessions

- Window position and size
- Selected theme
- Graph mode and rolling window size
- Recent files list (up to 10)

## Keyboard shortcuts

| Action          | Windows / Linux | macOS   |
| --------------- | --------------- | ------- |
| Open file       | Ctrl+O          | Cmd+O   |
| Reload          | Ctrl+R          | Cmd+R   |
| Reset zoom      | Ctrl+0          | Cmd+0   |
| Cancel analysis | Esc             | Esc     |

## Known limitations

- Video streams only — audio streams are not analyzed
- No export yet (CSV, image) — the graph is read-only
- Requires ffprobe on `PATH`; no bundled binary or custom path picker

## License

Released under the [MIT License](LICENSE).
