# Energy data on Windows: findings for WattBar

Measured on a ThinkPad 21NU (Core Ultra 7 258V, Windows 11 26300) on 2026-09-29/30 while
hunting an 9–15 W idle drain that ended at 2–3 W. Everything below was verified on that
machine; numbers are from it and will differ elsewhere.

## 1. Where power numbers come from

| Source | What it gives | Resolution | Admin | Notes |
| --- | --- | --- | --- | --- |
| WinRT `Battery.AggregateBattery.GetReport()` | charge rate mW (signed), remaining / full mWh | gauge refresh, 30–60 s | no | what WattBar uses today |
| WMI `root\wmi` `BatteryStatus.DischargeRate` | same number, mW | same | no | identical gauge value |
| Perf counter `\Power Meter(power meter (0))\Power` | same number, mW | same | no | plain `PerformanceCounter`, no WinRT needed |
| Perf counter `\Energy Meter(rapl_package0_*)\Power` | CPU **package**, **cores** (pp0), **GPU** (pp1), **DRAM** power, mW | 1 s, truly instantaneous | no | Intel RAPL through the Windows EMI interface |
| Perf counter `\Energy Meter(rapl_package0_*)\Energy` | cumulative energy, **picowatt-hours** | 1 s | no | `Δ(pWh) × 3.6 / Δt(s) = mW`; `\Time` is in ms |
| ETW provider `Microsoft-Windows-Energy-Estimation-Engine` | **per-process** energy estimate, split CPU / SoC / display / disk / network / MBB / NPU / other | one dump every 60 020 ms | **yes** | the data behind Task Manager's "Power usage" column |
| `powercfg /srumutil /csv` | the same estimates aggregated **per hour**, per process, with on-battery / screen-on flags | 1 h | yes | history back ~7 days |
| `powercfg /batteryreport /xml` | capacity history, per-session drain in mWh with timestamps, design/full capacity, cycle count | per state change | no | best source for "true average W over a session" |
| `root\wmi` `MSAcpi_ThermalZoneTemperature` | ACPI thermal zone, tenths of K | on read | yes (returned nothing unelevated) | 84 °C at 2 % CPU was the tell for a hidden JVM load |
| `powercfg /getactivescheme` + registry `HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes` `ActiveOverlayDcPowerScheme` | active scheme and the Windows 11 power-mode overlay | on read | no | see §4 |
| `root\wmi` `WmiMonitorBrightness.CurrentBrightness` | panel brightness % | on read | no | the display was ~1.5 W at 50–60 % here |

## 2. The battery gauge is a slow, stepped signal

The fuel-gauge chip reports the Smart Battery *average* current, a rolling window of roughly
one minute, and refreshes it every 30–60 s. Consequences observed:

- Identical `DischargeRate` values for 20–40 s, then a step. Six 10-s samples in a row read
  7 969, 7 969, 7 969, 10 671, 10 671, 11 621 mW.
- A step change (brightness, closing an app, a burst) appears as a ramp spread over ~1 min. A
  short burst is smeared into a small bump. After closing Firefox the reading stayed at 6 W for
  a minute although the process was gone in seconds.
- While load rises the gauge lags the RAPL package counter: package 12 414 mW while the gauge
  still said 10 671 mW.
- `RemainingCapacity` moves in 10 mWh steps (values seen: 37 110, 35 460, 34 060, 33 610,
  51 140). Over a minute at 3 W that is 5 steps, so capacity deltas are only usable for
  averages over ≥ 5 min.

Implications for WattBar:

1. Sampling once a second and applying an EMA (α = 0.3) smooths a signal that is already a
   1-min average. The EMA mostly adds lag. Either label the tray number "1-min avg" and drop
   the EMA, or drive the tray digits from the RAPL package counter (see §3) and keep the gauge
   for the chart and time-left.
2. The sparkline and the 1-min chart window are mostly showing gauge steps, not power
   changes. The 10/30/60-min windows are the meaningful ones for the gauge.
3. A "true average" over the flyout window is best computed as
   `(RemainingCapacity(t0) − RemainingCapacity(t1)) / Δt`, not as the mean of gauge samples,
   for any window ≥ 5 min. The two disagree after load changes.
4. Do not treat repeated identical gauge values as independent samples when computing
   min/peak; the peak of the evening (17.7 W) was one gauge step, not a sustained level.

## 3. RAPL package power: the instantaneous number that is missing

Unelevated, one-second, and consistent with the energy deltas:

```
\Energy Meter(rapl_package0_pkg)\Power    → whole SoC, mW      (idle here 800–3 000, bursts 12 000+)
\Energy Meter(rapl_package0_pp0)\Power    → CPU cores, mW
\Energy Meter(rapl_package0_pp1)\Power    → integrated GPU, mW  (7–90 idle)
\Energy Meter(rapl_package0_dram)\Power   → on-package LPDDR5X, mW (120–210)
```

Battery ≈ package + panel backlight (≈1.5 W at 50–60 % here) + Wi-Fi/SSD/rest (≈0.3–0.8 W).
Showing "3.2 W battery · 0.9 W CPU" answers the question users actually ask: is it the
machine or is it something I run. The counters exist only on machines whose CPU driver
exposes RAPL through EMI (Intel since Skylake, most AMD laptops); probe `\Energy Meter(*)`
at start-up and hide the feature when absent.

```csharp
// System.Diagnostics.PerformanceCounter is in the Windows desktop framework; no package needed.
using var pkg = new PerformanceCounter("Energy Meter", "Power", "rapl_package0_pkg", readOnly: true);
using var bat = new PerformanceCounter("Power Meter", "Power", "power meter (0)", readOnly: true);
pkg.NextValue(); bat.NextValue();          // first call primes the counter
// ... once per second:
double packageW = pkg.NextValue() / 1000.0;
double batteryW = bat.NextValue() / 1000.0; // same gauge value as WinRT, in case WinRT is unavailable
```

`PerformanceCounterCategory.InstanceExists("rapl_package0_pkg", "Energy Meter")` is the probe.

## 4. Context that explains a reading

Three cheap reads turn a number into a diagnosis, and all three mattered on this machine:

- **Power scheme and overlay.** Windows 11 laptops should sit on Balanced
  (`381b4222-f694-41f0-9685-ff5bb260df2e`) with an overlay: `00000000-…` Balanced,
  `961cc777-2547-4f9d-8174-7d86181b8a7a` Best power efficiency,
  `ded574b5-45a0-4f42-8737-46345c09c238` Best performance. Something switched this machine to
  the hidden **High performance** scheme (`8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`) during the
  evening; the Settings "Power mode" dropdown went blank, the fan came on and the idle draw
  doubled. Windows does not log who does this. A one-line status "Balanced · efficiency" in the
  flyout, turning magenta when the scheme is not Balanced, would have caught it in a minute.
- **Brightness %** next to the wattage. The panel is the second largest consumer at idle and
  a jump from 61 % to 80 % was misread as a software problem.
- **AC/DC and Modern Standby transitions** from the battery report explain gaps in the chart.

Also worth exposing once per session, from `powercfg /batteryreport`: full-charge vs design
capacity (96.5 % here at 111 cycles) and the per-session drains in mWh, which are the only
honest "average W per session" numbers Windows keeps.

## 5. Per-process attribution: the Energy Estimation Engine (E3)

Task Manager's "Power usage" column and Settings › Battery usage both read Windows' Energy
Estimation Engine. It also publishes its estimates as an ETW provider, which is the only way
to get them live. Verified with `logman`:

```
logman start E3 -p Microsoft-Windows-Energy-Estimation-Engine 0xffffffffffffffff 5 -o e3.etl -ets
...wait ≥ 61 s...
logman stop E3 -ets
tracerpt e3.etl -o e3.csv -of CSV -y
```

- Needs an elevated process (`Access is denied` otherwise). A real-time session from C# is
  possible with the `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet package
  (`TraceEventSession` + `EnableProvider("Microsoft-Windows-Energy-Estimation-Engine")`),
  again only when elevated. Since WattBar is not elevated by default this should be an
  opt-in "Who is using it" panel that relaunches elevated, or a hint pointing at Task Manager.
- Cadence: one batch per **60 020 ms** (`UpdateStatsStoreTimerEvent` → `ReadyForQuery`),
  ~150 `EnergyEstimate` events per batch on this machine.
- `EnergyEstimate` user-data field order, as exported by `tracerpt`:
  `AppId, <type>, CPU, SoC, Display, Disk, Network, MBB, NPU, Other, EMI, <loss?>, IntervalMs (60020), Flags ("DC |MonitorOn |Foreground "), <n>, InteractivityState ("NotUnique"/"Minimized"/"Visible"/"Focus"), …`
  This is the same order as the `powercfg /srumutil` CSV columns.
- `AppId` is a device path (`\Device\HarddiskVolume4\…\x.exe`), a packaged-app name
  (`Mozilla.Firefox_156.0.0.0_x64__n80bbvh6b1yt2`), a service tag
  (`svchost.exe [netsvcs] [Winmgmt]`), `System`, `System Interrupts`, `MemCompression`,
  `vmmemWSL`, or one of the hardware meters `EMI_RAPL_Package0_{PKG,PP0,PP1,DRAM}`.
- **The same process appears several times per batch**, once per state combination
  (foreground/background, screen on/off). Merge by name before ranking.
- **Display energy is charged to the foreground application.** The front window is always the
  "top consumer". Rank by the CPU column, or show Display as its own line, or the panel is
  misleading (WattBar itself topped the list at 40 % while its flyout was open).
- The unit is undocumented and did not reconcile with mWh (totals were ~8× the battery
  report's mWh for the same day). Show shares, not absolute values. Reconciling the
  `EMI_RAPL_Package0_PKG` row against the `\Energy Meter` delta over the same minute would
  pin the unit; not done yet.
- Hourly history for a "last 24 h" view: `powercfg /srumutil /output x.csv /csv` (elevated),
  filter `OnBattery == TRUE`, drop `EMI_RAPL*` and `Unknown` rows, group by app.
  Reference implementations: `C:\Users\Public\e3live.ps1` (per-minute, live) and
  `C:\Users\Public\srumtop.ps1` (hourly) on the dev machine.

## 6. Things Task Manager does not show that cost watts

- **Timer resolution holders.** JVMs, IntelliJ and Firefox each held a 1 ms system timer
  (`powercfg /energy` → "Platform Timer Resolution: Outstanding Timer Request"). That blocks
  deep package idle at near-zero CPU %. `powercfg /energy /duration 60` (elevated, 60 s) is the
  only stock tool that lists them; WattBar could offer to run it when idle draw stays high.
- **Kernel time.** Privileged time was 10 % of the machine against 3 % attributable to
  processes: drivers, hypervisor (WSL2 + memory integrity), interrupts. E3 books it to
  `System` / `System Interrupts`.
- **PCIe ASPM Off** in the power scheme keeps the NVMe SSD and Wi-Fi card from sleeping.
  Readable with `powercfg /q <scheme> 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5`;
  a good "check your plan" item next to the scheme status.

## 7. Suggested WattBar changes, in order

1. Read `\Energy Meter(rapl_package0_pkg)\Power` each second; show package W under the
   battery W in the flyout (and optionally as the tray digits, since it is the only
   instantaneous number). Hide when the counter set is absent.
2. Label the battery figure "1-min avg", drop or weaken the EMA, and compute the window
   average from `RemainingCapacity` deltas for windows ≥ 5 min.
3. Flyout status row: scheme/overlay name, brightness %, AC/DC. Highlight when the active
   scheme is not Balanced.
4. Optional elevated "Who is using it" panel from the E3 provider, merged per process, ranked
   by CPU energy, with Display listed separately. Refresh every 60 s.
5. CSV log (roadmap item 2): log battery mW, package mW, brightness, scheme, per second; that
   file is what would have found the High-performance switch in one plot.
