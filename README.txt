━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  DeepEffort v2 — ATAS Custom Indicator
  Build & Installation Guide
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━


━━  PREREQUISITES  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

1. .NET 8 SDK  (required for compilation)
   Download: https://dotnet.microsoft.com/download/dotnet/8.0
   Choose "SDK" (not just the runtime) for your Windows architecture (x64).

   Verify the install by opening a command prompt and running:
     dotnet --version
   You should see "8.x.x" (or higher).

2. Internet connection (first build only)
   The build downloads the OFT.Indicators NuGet package automatically.
   Subsequent builds use the local package cache.

3. ATAS platform
   ATAS must already be installed so you can copy the compiled DLL into it.
   Current ATAS builds target .NET 8; if you are on an older ATAS version
   that targets .NET 6, change <TargetFramework> in DeepEffortV2.csproj
   from "net8.0-windows" to "net6.0-windows" and rebuild.


━━  BUILDING  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  Option A — double-click build.bat in Windows Explorer.

  Option B — open a command prompt in this folder and run:
    dotnet build DeepEffortV2.csproj -c Release -o ./output

The compiled file will be:
    <this folder>\output\DeepEffortV2.dll


━━  TROUBLESHOOTING NuGet RESTORE  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

If the build fails with "Unable to find package OFT.Indicators":

  1. Confirm internet access and try again.

  2. If you are behind a proxy or offline, use the local-DLL fallback:
     a. Open DeepEffortV2.csproj in a text editor.
     b. Comment out the <PackageReference Include="OFT.Indicators" ...> line.
     c. Uncomment the "FALLBACK: local DLL references" block.
     d. Set the ATASDir property to your actual ATAS installation folder,
        e.g. C:\Program Files\ATAS  (the default ATAS install location).
     e. Run build.bat again.

  3. Check if ATAS provides a custom NuGet feed URL in their developer docs
     at https://atas.net (log in required).  If so, add a NuGet.config file
     in this folder containing that source URL.


━━  INSTALLING INTO ATAS  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

1. Locate the ATAS custom-indicators folder.
   Default path (Windows):
     %APPDATA%\ATAS\Indicators
   or open ATAS → menu Settings → Directories → Indicators to find the path.

2. Copy  output\DeepEffortV2.dll  into that folder.

3. Reload ATAS indicators:
   • Restart ATAS, OR
   • In ATAS go to Indicators panel → right-click → "Reload indicators"

4. Add the indicator to a chart:
   • Open the Indicators panel (or press Ctrl+I).
   • Search for "DeepEffort v2" under the "Order Flow" category.
   • Drag it onto a price chart (tick chart / range bar chart recommended).


━━  CONFIGURATION  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

All parameters are accessible from the indicator's settings panel in ATAS.

  Settings group
  ──────────────
  Lookback Period       (default 20)
    Number of closed candles used to build the percentile-rank window.
    Signals are suppressed until this many candles have formed.

  Effort Threshold      (default 0.75)
    Minimum composite effort score (0–1) to draw a box.
    Raise toward 1.0 for fewer, higher-conviction signals.
    Lower toward 0.5 for more frequent signals.

  Min Volume Multiplier (default 1.0)
    Candle volume must be ≥ (lookback average × this value).
    1.0 = above-average volume required; 0.5 = relaxed gate.

  Visuals group
  ─────────────
  Bull Box Colour       (default 40 % opaque green)
  Bear Box Colour       (default 40 % opaque red)
  Box Border Width      (default 2 px)
  Show Effort Label     (default on)
    Prints the numeric score (0.00–1.00) just above each box.


━━  SCORE COMPONENTS  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Each closed candle produces five 0–1 percentile scores:

  VolumeScore         — total volume vs lookback window
  DeltaRatioScore     — |delta| / volume vs lookback window
  SpeedScore          — volume / candle-duration vs lookback window
  RangeEfficiencyScore— volume / price-range vs lookback window
  CloseStrengthScore  — close position in range (0 = low, 1 = high)
                        Flipped (1 − position) for sell signals

Composite effort = (VolumeScore + DeltaRatioScore + SpeedScore +
                    RangeEfficiencyScore + CloseStrengthScore) / 5

A GREEN box fires when:  delta > 0  AND  BuyEffort ≥ threshold  AND  volume gate
A RED   box fires when:  delta < 0  AND  SellEffort ≥ threshold AND  volume gate


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
