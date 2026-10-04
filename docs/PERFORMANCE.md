# Performance

Budget for the always-running tracker (from the project brief):

| Metric | Budget | Measured (phase 2) | Status |
|---|---|---|---|
| Private working set | ≤ 15 MB | **4.2 MB** (worker 2.45 MB + supervisor 1.77 MB) | ✅ |
| CPU when the mouse is idle | ≈ 0 % | **0.000 %** (no wake-ups at all) | ✅ |
| CPU, 1000 Hz mouse | - | **0.34 %** of one core | ✅ |
| CPU, 8000 Hz mouse in a game | < 0.5 % of one core | **0.116 %** with the author's real ~6000 Hz mouse (2 min); 0.61 % with synthetic input | ✅ |
| Managed allocations on the input path | 0 | **0 bytes** (unit test + live counter) | ✅ |
| Statistics window: time to first window | < 1.5 s | **~0.6 s** (published, ReadyToRun; 3 runs: 616, 582, 585 ms) | ✅ |
| Data lost on a crash | ≤ 60 s | ≤ 60 s (flush every 60 s, on sleep, lock, logoff, exit) | ✅ |

## Test system

- AMD Ryzen 7 9800X3D (4.7 GHz nominal), 32 GB RAM
- Windows 11 Home 25H2, build 26200
- .NET 10.0.12, tracker published with NativeAOT (`OptimizationPreference=Size`)
- Measured on 2026-10-02

## How it is measured

```bash
dotnet build -c Release
dotnet publish src/AimOdometer.Tracker -c Release -o artifacts/tracker
powershell -ExecutionPolicy Bypass -File tools/Run-Benchmarks.ps1 -Seconds 20
```

- **Input:** `tools/InputSimulator` injects relative mouse moves with `SendInput`. Above 1000 Hz it sends several
  reports per call every millisecond (a single thread cannot call `SendInput` 8000 times a second; it tops out around
  3500). The cursor moves in small closed loops, so it ends where it started.
- **CPU:** `tools/PerfProbe` reads `QueryProcessCycleTime` of the tracker worker and divides by the nominal clock.
  This is precise; `GetProcessTimes` is not, because Windows charges CPU time in 15.6 ms ticks and the tracker runs in
  short bursts (the coarse numbers swung between 0 and 3 %).
- **Memory:** private working set from `GetProcessMemoryInfo` (`PROCESS_MEMORY_COUNTERS_EX2`).
- **Events, wake-ups, allocations:** read from the tracker itself over its named pipe (`GetStatus`).
- A throw-away data folder is used, so benchmark runs never touch real statistics.

## Results (default settings, 20 s per scenario)

| Scenario | CPU % of one core (cycles) | Private WS max MB | Events/s received | Wake-ups/s | Alloc bytes |
|---|---|---|---|---|---|
| idle | 0.000 | 2.52 | 0 | 0 | 7,904 ¹ |
| 1000 Hz | 0.340 | 2.59 | 950 | 59 | 8,160 ¹ |
| 8000 Hz (synthetic) | 0.609 | 2.63 | 2,760 ² | 61 | 0 |
| 8000 Hz + EcoQoS | 0.614 | 2.53 | 2,756 ² | 62 | 16,000 ¹ |

¹ Allocations come from answering the probe's own pipe requests, not from input processing (the 8000 Hz row, which
had no extra requests in its window, shows 0).
² The simulator sent ~7,500 reports/s; Windows 11 coalesced them into ~2,760 deliveries/s for the background tracker.

## Where the CPU goes

Splitting the worker's time into user and kernel mode at 8000 Hz showed **more than 85 % kernel time**: the cost is
Windows delivering raw input to the process (`GetRawInputData` / `GetRawInputBuffer`, message queue), not our code.
Our own processing (user mode, under 15 % of the total) is allocation-free.

So the lever is how often the tracker wakes up. The first version woke up for every `WM_INPUT` message:

| Version | Wake-ups/s at ~3500 reports/s | CPU % |
|---|---|---|
| One wake-up per report | 3,492 | 7.6 (coarse measurement) |
| Batched, 8 ms | 123 | 0.84 |
| **Batched, 16 ms (default)** | **61** | **0.61** |
| Batched, 25 ms | 38 | 0.52 |

Batching means: after a wake-up the tracker sleeps until 16 ms have passed since the previous batch, then takes
everything that queued up with one `GetRawInputBuffer` loop. Windows buffers raw input in the meantime and delivers the
game's own input independently, so this delays only our bookkeeping (8 ms on average), never the game.

**Why 16 ms and not 25 ms:** the flick-speed window is 50 ms; with 16 ms batches it always contains at least three
batches. 25 ms would save another 0.1 % of a core in the synthetic test but make the peak-speed measurement coarse.
The interval can be changed with `--batch-ms` for experiments.

## EcoQoS

`SetProcessInformation(ProcessPowerThrottling)` made no measurable difference (0.609 % vs 0.614 %) and is therefore
**off by default** (`--ecoqos` or the `ecoqos` setting turns it on). With batching the tracker already spends almost
all of its time asleep, which is what EcoQoS would otherwise help with.

## Accuracy

| Test | Sent | Recorded | Error |
|---|---|---|---|
| 1000 Hz, straight line, 2 s | 8,000 counts | 8,000 | 0 |
| 8000 Hz, straight line, 5 s | 80,000 counts | 80,000 | 0 |
| 8000 Hz, straight line, 15 s | 240,000 counts | 240,000 | 0 |
| ~7500 Hz, tight circles (radius 30 counts ≈ 1 mm at 800 DPI), 2 runs | 975,867 counts total | 967,886 | **−0.82 %** |

No reports are lost, including while the tracker sleeps between batches.

The only error source is **Windows 11 coalescing for background listeners** (risk R1 in the plan): several reports are
merged into one with the summed `dx, dy`. On a straight line that changes nothing; on a curve the merged segment is a
chord instead of an arc. For an arc of length *L* on a circle of radius *R* the relative loss is about *(L/R)² / 24*:

- 1 mm circles at ~7500 Hz (the synthetic worst case above): measured 0.82 %.
- Typical aim movement (radius ≥ 2 cm, 50 cm/s, ~8 ms merges): about 0.2 % or less.

### Real mouse (2026-10-02)

Measured with `tools/InputProbe` (a focused window reading raw input at the full rate, compared with the background
tracker over the same period), author's wireless mouse, 800 DPI, vigorous aim-like circles and flicks for 62 s:

| Reader | Reports | Path |
|---|---|---|
| Foreground, full rate | 372,307 (5,986/s) | 1,023,375 counts |
| Background tracker (coalesced by Windows) | ~125/s while moving | 999,622 counts |
| **Difference** | | **−2.32 %** |

This is far more than the 0.2 % estimated above: fast, tight micro-corrections bend the path a lot within 8 ms.
The ruler test (straight strokes) is unaffected: 4 strokes of 57 cm were recorded as 247 cm, with about 19 cm of
extra positioning moves in the same period.

**Status:** accepted as a known limitation (the author considers up to ~2 % acceptable). Background raw input on
Windows 11 has no documented opt-out. A research spike on Microsoft GameInput as an alternative input source is
planned for phase 10.

The same 2-minute run measured the tracker at **0.116 % of one core** and 2.96 MB private working set:
Windows delivered on average 70 reports/s (125/s while moving) instead of ~6000.

## Crash recovery

`RegisterApplicationRestart` was the original plan but **does not work for this app**: NativeAOT ends an unhandled
exception with a fast-fail (`0xC0000409`), and Windows Error Reporting does not restart fast-failed processes
(verified: the tracker crashed and stayed down). The tracker therefore runs as two processes:

- **Supervisor** (1.8 MB, no window, no database, waits in `WaitForExit`, 0 % CPU) starts the worker and restarts it
  after 2 s if it exits with a non-zero code, at most 5 times in 10 minutes. It does not restart during logoff or
  shutdown, and exits when the worker exits normally.
- **Worker** - the actual tracker.

Verified with the `--crash-after 10` test hook: the worker fast-failed, was restarted within 2 s without the hook, and
`--stop` then ended both processes.

## Open items

- ~~Real high-rate mouse~~: confirmed, hardware input is coalesced to ~125/s for background listeners (0.116 % CPU).
- Accuracy loss from coalescing during intense aim (−2.3 % measured): GameInput spike.
- Windows 10 has no background coalescing; an 8000 Hz mouse there would deliver 8000 reports/s. Estimated cost from
  the kernel-dominated profile: roughly 1-1.5 % of one core. Needs a Windows 10 machine to confirm.
