# Plotting curves and maps: plan

Status: implemented 2026-10-17 (see "Implementation status" at the end).

**Decisions:**
- The 3D surface view is wanted.
- Graphs are **read-only**: no editing in the graph. Clicking a point only selects its cell in the grid.
- Graphs start **off**. A Settings option ("Show graphs when opening tables") turns them on by default.

## What gets plotted

Terms follow how RaiderLite classifies tables by shape (see `TableShape`):

| Your term | RaiderLite shape | RomRaider type | Plot |
|---|---|---|---|
| 1D table | **Array**: one axis plus a row of values (a curve) | 2D (or 1D array) | Line graph, value against axis value |
| 2D table | **Map**: X axis × Y axis grid | 3D | Family of lines (one per row) and, later, an optional surface view |
| — | Scalar, Switch | 1D / Switch | No graph |

Data is small. In the Lotus T4e and T6 definitions the longest curve has 64 points and the
largest maps are 32×32 (1,024 values). Rendering speed is therefore about *smoothness and
latency while dragging or rotating*, not throughput.

## Recommended UX

### Where the graph lives
In the table's own window, as a pane that you toggle, not a separate window.
- **Curves:** the graph sits **below** the grid. Curves are drawn as a wide strip, so the
  graph uses the same width.
- **Maps:** the graph sits to the **right** of the grid. Maps are roughly square, so
  side by side wastes the least space.
- **Toggling:** View → **Graph** on the ribbon, or **Ctrl+G** in the table window. The
  choice is remembered per table, like window positions.
- **Sizing:** fit-to-table (double-click the title) includes the graph. When space is short,
  the grid keeps priority, the same rule the layout already applies to everything else.

This matches the common tuning-tool pattern (EcuFlash, WinOLS, TunerPro), where the table and
its graph are one view. You see a change in both at once, and selection is shared.

```
┌ sensor: maf scaling ───────────────────────────────────────────────┐
│ Table Edit Compare View                                            │
│                       signal (v) ▾                                 │
│ │0.16│0.31│0.47│0.63│0.78│ ... │4.84│5.00│        (grid, unchanged) │
│ │ 0.0│ 0.0│ 0.1│ 0.4│ 0.7│ ... │ 512│ 540│                          │
│ Values: g/s ▾                                                      │
│ ┌──────────────────────────────────────────────────────────────┐   │
│ │ 540┤                                                     ●   │   │
│ │    │                                              ●●●●●●     │   │
│ │ 270┤                                    ●●●●●●●●            │   │
│ │    │                  ●●●●●●●●●●●●●●●●                      │   │
│ │   0┼●●●●●●●●●●●●●●●●────┬──────────┬──────────┬──────────┬  │   │
│ │    0.0               1.25        2.5        3.75        5.0 V│   │
│ └──────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

### Curves: what the graph does
- **Plotted against real axis values.** X spacing follows the actual axis values, so uneven
  breakpoints show the true shape. A static (text) axis falls back to evenly spaced labels.
- **Units follow the scalings.** The graph uses the current scalings of the values and the
  axis, so the per-axis Raw / Metric toggles apply.
- **Linked selection, both ways.** Cells selected in the grid are highlighted points.
  Clicking or drag-selecting points selects the cells. Hovering shows a crosshair with
  "X: 2.50 V   Y: 270 g/s   raw 108".
- **Drag points to edit.** Drag vertically to change a value. It snaps to whole raw steps,
  so what you see is what gets stored. Dragging one of several selected points moves them
  all together, the same as an increment. Each drag is one undo step. Locked and
  user-level-restricted tables don't drag.
- **Changes are visible.** When values differ from their originals, the original curve is
  drawn faintly behind the current one. This matches the grid's red/blue changed-cell
  borders.
- **Compare overlay.** When the table is compared with another image (Compare menu), that
  image's curve is drawn dashed.

### Maps: what the graph does
- **Lines view (default):** one line per Y-axis row, plotted against the X axis, colored
  with the same min→max palette as the grid. This is the most useful view for tuning, since
  it shows how, for example, each load row varies across RPM. Further behavior:
  - A small legend shows the Y value of each line.
  - **Transpose** swaps to one line per X column.
  - Hovering a line highlights its row in the grid, and the selected cell is ringed on its line.
  - Dragging a point edits that cell, with the same snapping and undo as curves.
- **Surface view (optional, later phase):** a heat-colored 3D surface you rotate with the mouse,
  with the selection outlined. It's for seeing the overall shape, so it's view-only.
  Precise edits stay in the grid or the Lines view.

You said earlier that 3D graphs weren't needed, so the surface view is the last phase and
can be dropped without affecting the rest.

## Technology options (measured)

All sizes are what each option adds to a framework-dependent x64 publish. Today's app is
**0.44 MB** (exe plus dlls).

| Option | Adds | GPU | Fit for this UX | Notes |
|---|---|---|---|---|
| ScottPlot 5 (SkiaSharp + OpenTK) | **+20.6 MB** | OpenGL through OpenTK 3.1 | Good for curves and line families; no 3D surface; drag editing needs custom code | OpenTK 3.1 and the SkiaSharp WinForms views restore as .NET Framework builds (NU1701 warnings on .NET 10): a compatibility risk. 47× the app's size. |
| LiveCharts2 (SkiaSharp) | **+20.5 MB** | OpenGL | Animated dashboards rather than editing | WinForms package is pre-release for .NET 10. Same native weight as ScottPlot. |
| OxyPlot | +0.9 MB | **None** (GDI+ renderer on WinForms) | Good 2D charts; custom code for drag editing; no 3D | Mature but slow-moving. Doesn't meet the GPU goal. |
| Helix Toolkit (SharpDX) | large | Direct3D 11 | 3D only; needs a WPF host | SharpDX is unmaintained. Rejected. |
| **From scratch on Direct2D + DirectWrite via Vortice** | **+1.3 to 1.6 MB** | **Direct2D on the GPU, automatic software (WARP) fallback** | **Full control**: linked selection, drag editing, overlays that match existing grid behavior, 2.5D surface | Vortice is MIT-licensed and actively maintained. Direct2D and Direct3D are part of Windows, so there are no native binaries to ship. |
| From scratch on Direct2D via CsWin32 (generated interop) | ≈ +0.1 MB (estimate) | same as above | same | Smallest possible, but much more hand-written COM interop. Kept as a later size optimization. |
| From scratch on GDI+ | 0 MB | None | Full control | Fast enough (below), but no GPU. Useful as a fallback renderer. |

### Measured rendering cost
Spike on this machine (AMD Radeon 840M): 32 lines × 32 points, anti-aliased, with point
markers. This is the worst case in your definitions.

| Renderer | Time per frame |
|---|---|
| Direct2D on the GPU | **1.6 ms** |
| Direct2D software fallback (WARP) | 3.0 ms |
| GDI+ | 4.1 ms |

All three fit easily within a 60 fps frame (16.7 ms). Direct2D on the GPU is about 2.5× faster
than GDI+, stays crisp at high DPI, and keeps CPU free while dragging or rotating. The spike
also confirmed that Direct2D renders correctly from a WinForms window on .NET 10. It can
render offscreen into a bitmap as well, which the tests and copy-graph-as-image will use.

### Recommendation
**Build it from scratch on Direct2D and DirectWrite using Vortice**, behind a small renderer
interface with a GDI+ fallback.
- **GPU:** it uses the GPU when one is available, and Direct2D drops to software on its own
  (Remote Desktop, no GPU). Nothing extra to write.
- **Size:** about 1.5 MB instead of 20 MB, with no native DLLs to ship.
- **UX:** the editing-focused design above (linked selection, drag to edit, original and
  compare overlays) needs custom interaction code with any chart library. Owning the
  drawing makes that simpler, not harder.
- **Swap later if needed:** the renderer interface makes it possible to replace Vortice with
  CsWin32-generated interop later, if the last 1.5 MB matters.

## Architecture

### Core (no UI, unit-tested) — `RaiderLite.Core/Plotting/`
- **`PlotSeries` extraction:** builds the series from a table. Each point carries X and Y real
  values (current scalings), its `DataCell`, selected/changed flags, the original Y, and the
  compared Y. Maps produce one series per row (or per column when transposed).
- **`AxisTicks`:** "nice number" tick generation and labels formatted with the scaling's format.
- **`Viewport`:** data↔pixel mapping, nearest-point hit testing, and drag-to-value conversion
  with snapping to raw steps (`Scale.ToBin` plus rounding, as typed values already use).
- **`SurfaceProjection`** (surface phase): orbit camera, projection, and back-to-front
  ordering of the grid's quads.

### UI — `RaiderLite/Plotting/`
- **`IPlotRenderer`:** lines, polylines, markers, filled polygons, rectangles, text and clipping.
  - `D2DRenderer`: Vortice `ID2D1HwndRenderTarget` with type Default, so it uses the GPU and
    falls back to WARP. Text via DirectWrite. Brushes and geometry are cached and rebuilt only
    when data or size changes. It handles `D2DERR_RECREATE_TARGET` (device loss after a driver
    reset, sleep or a remote session) by rebuilding the target, and sets the DPI from
    `DeviceDpi`.
  - `GdiRenderer`: used if Direct2D can't be created at all. It's also simple to use for tests.
- **`PlotControl : Control`:**
  - Draws only on `WM_PAINT` and invalidates on the table's existing (already coalesced)
    `DataChanged`. There's no render loop, so it uses no power when idle.
  - Owns mouse interaction: hover, select, drag, and orbit for the surface.
- **`CurvePlotView`, `MapLinesView`, `SurfaceView`:** turn series into draw calls.
- **Integration:**
  - `TableForm.LayoutContent` places the pane below or to the right of the grid.
    `SizeToContent` and `RequiredClientHeight` account for it.
  - The ribbon gets a Graph toggle (chart icon from the icon font) and Ctrl+G.
  - Selection syncs both ways through the existing `DataCell.IsSelected`. `TableGrid` needs
    a small hook to select cells from code.
  - Edits go through `Table`/`DataCell` operations inside `Rom.History.Begin("Drag")`, so
    undo, aliases, checksums and tree bolding keep working unchanged.

## Phases

| Phase | Work | Rough size |
|---|---|---|
| 0 | Spike: Direct2D in WinForms on .NET 10, timings, offscreen render | **done** (this document) |
| 1 | Core plotting math plus unit tests: series extraction, ticks, viewport, hit test, snapping | small |
| 2 | `PlotControl`, Direct2D and GDI+ renderers, device-loss and DPI handling; curve graph (read-only) in table windows with the ribbon toggle; linked selection; hover readout | medium |
| 3 | Drag-to-edit: snapping, multi-point drag, undo, locked/user-level rules; original and compare overlays | medium |
| 4 | Map Lines view: legend, transpose, row highlight, drag edit | small to medium |
| 5 (optional) | 2.5D surface view drawn with Direct2D (depth-sorted quads, no shaders), orbit, selection outline; copy graph as image | medium |

Each phase ships on its own; after phase 2, curves are already useful to look at.

## Testing
- **Core math:** unit tests for ticks, viewport mapping, hit testing and snapping, including
  axes that decrease, all-equal values, and a single point.
- **Rendering:** render offscreen through Direct2D's DC render target and GDI+ into bitmaps,
  checked with the existing harness approach. This works even when the desktop is locked.
- **Performance:** a benchmark harness for the largest map, Lines and Surface, on GPU and WARP.
  Target: under 4 ms per frame on WARP.
- **Leaks:** extend the existing leak harness to open and close graph panes repeatedly. Direct2D
  and DirectWrite objects are COM resources and must be disposed with the pane.

## Risks and how they're handled
- **GPU device loss** (driver update, sleep, Remote Desktop reconnect): recreate the render
  target on `D2DERR_RECREATE_TARGET`. This is a standard pattern, and it will be tested by
  forcing a WARP target.
- **Machines without Direct2D hardware support:** WARP is automatic. If Direct2D creation
  fails outright, the GDI+ renderer takes over.
- **High DPI and moving between monitors:** the render target DPI follows `DeviceDpi`, and
  geometry is rebuilt on DPI change.
- **Vortice upkeep:** it's a thin, MIT-licensed binding over stable Windows APIs. The
  renderer interface contains the dependency if it ever needs replacing (CsWin32 option).

## Open questions
1. Is the map surface view (phase 5) wanted, or are the Lines view and the grid enough?
2. Should dragging snap to whole raw steps only (recommended), or allow free dragging that
   rounds on release?
3. Default for new tables: graph off (current behavior) or on for curves?

## Implementation status (2026-10-17)

All planned phases except drag editing, which was dropped by decision, are built:

- **Core** (`RaiderLite.Core/Plotting`): `GraphData` (curve/map extraction, originals,
  compared values, transposed maps), `AxisTicks`, `SurfaceCamera`. Covered by `PlottingTests`.
- **Rendering** (`RaiderLite/Plotting`):
  - `IPlotRenderer`, with `D2DRenderer` (Direct2D/DirectWrite via Vortice 3.8.3) and `GdiRenderer` (fallback).
  - `PlotControl`: window render target with automatic software fallback, device-loss
    recovery, DPI change handling, and Copy Image via a DC render target.
  - `LinesView` and `SurfaceView`.
  - `GraphPane`: Lines / Surface / Transpose / Reset View for maps, and a right-click Copy Image.
- **UI:**
  - Ctrl+G and View → Graph in table windows, and a Graph button in the ribbon's View group.
  - Settings → "Show graphs when opening tables" (off by default).
  - The last map style (Lines or Surface) is remembered.
- **Placement:** curves go below the grid. Maps go right or below, whichever gives the graph
  more room; when neither has 300 px, the graph goes right and the grid is limited to 55% of
  the width, scrolling with frozen axes.

Measured on the AMD Radeon 840M with a 32×32 map, drawing work per frame (excluding the vsync wait):

| View | Average | Worst |
|---|---|---|
| Lines | 2.8 ms | 10.4 ms |
| Surface | 5.4 ms | 15.1 ms |

On screen, repaints are vsync-paced at 16.7 ms (60 Hz). Outlining surface quads with plain
lines instead of stroked geometry cut the surface cost from 14 ms to 5.4 ms.

Other results:
- **Leaks:** 400 table windows opened and closed with graphs showing (maps in surface view):
  0 survive, and handle counts are flat.
- **Size:** the published app grew from 0.44 MB to 1.8 MB (Vortice adds about 1.4 MB). No native DLLs.
