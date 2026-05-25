// DeepEffort v2 — ATAS Custom Indicator
// Detects high-conviction buyer / seller aggression via a composite normalised
// effort score and overlays coloured boxes on qualifying candles.
//
// Compile:  dotnet build DeepEffortV2.csproj -c Release
// Install:  copy bin\Release\net10.0-windows\DeepEffortV2.dll to your ATAS custom-indicators folder
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Drawing;
using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Settings;

namespace DeepEffortV2Indicator
{
    /// <summary>
    /// DeepEffort v2 — marks candles where buyers or sellers show unusually high
    /// conviction as measured by a five-component normalised effort score.
    ///
    /// Green box  = bullish aggression signal (delta > 0, BuyEffort >= threshold)
    /// Red   box  = bearish aggression signal (delta &lt; 0, SellEffort >= threshold)
    ///
    /// Signals only fire on fully closed candles — the indicator does not repaint.
    /// </summary>
    public class DeepEffortV2 : Indicator
    {
        // ─── Per-bar metric caches ──────────────────────────────────────────────
        // Stored by bar index so OnCalculate can rebuild the lookback window
        // correctly when the full history is recalculated on load / parameter change.
        private readonly Dictionary<int, decimal> _barVolumes     = new();
        private readonly Dictionary<int, decimal> _barDeltaRatios = new();
        private readonly Dictionary<int, decimal> _barSpeeds      = new();
        private readonly Dictionary<int, decimal> _barRangeEffs   = new();

        // ─── Detected signals ──────────────────────────────────────────────────
        // Keyed by bar index; populated only for closed bars that meet all criteria.
        private readonly Dictionary<int, SignalInfo> _signals = new();

        private struct SignalInfo
        {
            public bool    IsBull;
            public decimal Score;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Configurable parameters
        // ═══════════════════════════════════════════════════════════════════════

        private int     _lookbackPeriod      = 20;
        private decimal _effortThreshold     = 0.75m;
        private Color   _boxColorBull        = Color.FromArgb(102, 0, 200, 0);   // ~40 % green
        private Color   _boxColorBear        = Color.FromArgb(102, 200, 0, 0);   // ~40 % red
        private int     _boxBorderWidth      = 2;
        private bool    _showEffortLabel     = true;
        private decimal _minVolumeMultiplier = 1.0m;

        /// <summary>
        /// Number of closed candles used to build the percentile-rank lookback window.
        /// Signals are suppressed until this many candles have been seen.
        /// </summary>
        public int LookbackPeriod
        {
            get => _lookbackPeriod;
            set { _lookbackPeriod = value; RecalculateValues(); }
        }

        /// <summary>
        /// Minimum composite effort score (0–1) required to draw a box.
        /// Higher values produce fewer, higher-conviction signals.
        /// </summary>
        public decimal EffortThreshold
        {
            get => _effortThreshold;
            set { _effortThreshold = value; RecalculateValues(); }
        }

        /// <summary>
        /// Fill colour for bullish effort boxes.  Set the alpha channel for transparency.
        /// Default: 40 % opaque green — Color.FromArgb(102, 0, 200, 0).
        /// </summary>
        public Color BoxColorBull
        {
            get => _boxColorBull;
            set => _boxColorBull = value;
        }

        /// <summary>
        /// Fill colour for bearish effort boxes.  Set the alpha channel for transparency.
        /// Default: 40 % opaque red — Color.FromArgb(102, 200, 0, 0).
        /// </summary>
        public Color BoxColorBear
        {
            get => _boxColorBear;
            set => _boxColorBear = value;
        }

        /// <summary>Width in pixels of the rectangle border drawn around the candle.</summary>
        public int BoxBorderWidth
        {
            get => _boxBorderWidth;
            set => _boxBorderWidth = value;
        }

        /// <summary>When true, the effort score (0.00–1.00) is printed above each box.</summary>
        public bool ShowEffortLabel
        {
            get => _showEffortLabel;
            set => _showEffortLabel = value;
        }

        /// <summary>
        /// Candle total volume must be >= (average volume * MinVolumeMultiplier).
        /// Default 1.0 means "candle volume must exceed the lookback average."
        /// </summary>
        public decimal MinVolumeMultiplier
        {
            get => _minVolumeMultiplier;
            set { _minVolumeMultiplier = value; RecalculateValues(); }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Constructor
        // ═══════════════════════════════════════════════════════════════════════

        public DeepEffortV2()
        {
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Calculation logic  (called once per bar, oldest -> newest)
        // ═══════════════════════════════════════════════════════════════════════

        protected override void OnCalculate(int bar, decimal value)
        {
            // Purge any previously stored signal so a parameter-triggered
            // full recalculation doesn't leave stale entries.
            _signals.Remove(bar);

            // ── Gate: only process fully closed candles ─────────────────────
            // CurrentBar is the live (in-progress) bar index.
            // Bars with index < CurrentBar are confirmed closed.
            if (bar >= CurrentBar)
                return;

            var candle = GetCandle(bar);

            decimal totalVolume = candle.Volume;
            decimal delta       = candle.Delta;         // ask vol − bid vol
            decimal candleRange = candle.High - candle.Low;  // in price units / ticks

            // ── Candle duration (seconds) ───────────────────────────────────
            // For tick / range bars the open-time of bar+1 ≈ close-time of bar.
            decimal durationSeconds = 1m; // fallback prevents division-by-zero
            {
                var dt = (GetCandle(bar + 1).Time - candle.Time).TotalSeconds;
                if (dt > 0) durationSeconds = (decimal)dt;
            }

            // ── Close position within the high-low range ────────────────────
            // 0.0 = close at the low, 1.0 = close at the high.
            decimal closePosition = 0.5m; // neutral default for doji candles
            if (candleRange > 0)
                closePosition = (candle.Close - candle.Low) / candleRange;

            // ── Raw per-candle metrics ──────────────────────────────────────
            decimal deltaRatio = totalVolume > 0 ? Math.Abs(delta) / totalVolume : 0m;
            decimal speed      = totalVolume / durationSeconds;          // volume per second
            decimal rangeEff   = candleRange > 0 ? totalVolume / candleRange : 0m; // vol per tick

            // Store for later lookback window construction
            _barVolumes[bar]     = totalVolume;
            _barDeltaRatios[bar] = deltaRatio;
            _barSpeeds[bar]      = speed;
            _barRangeEffs[bar]   = rangeEff;   // 0 when range == 0 (handled below)

            // ── Wait for a full lookback window ─────────────────────────────
            if (bar < LookbackPeriod)
                return;

            // ── Build lookback window: bars [bar-N ... bar-1] ───────────────
            int windowStart = bar - LookbackPeriod;
            var volWindow   = new List<decimal>(LookbackPeriod);
            var drWindow    = new List<decimal>(LookbackPeriod);
            var spWindow    = new List<decimal>(LookbackPeriod);
            var reWindow    = new List<decimal>(LookbackPeriod);

            for (int i = windowStart; i < bar; i++)
            {
                if (_barVolumes.TryGetValue(i, out var v))             volWindow.Add(v);
                if (_barDeltaRatios.TryGetValue(i, out var d))         drWindow.Add(d);
                if (_barSpeeds.TryGetValue(i, out var s))              spWindow.Add(s);
                // Only include non-zero range-efficiency values so zero-range
                // candles don't dilute the distribution.
                if (_barRangeEffs.TryGetValue(i, out var r) && r > 0) reWindow.Add(r);
            }

            if (volWindow.Count == 0) return; // not enough data yet

            // ── Percentile-rank normalisation (0 = bottom of window, 1 = top) ─
            // Score = fraction of window values strictly below this candle's value.
            decimal volumeScore   = PercentileRank(volWindow, totalVolume);
            decimal deltaRatScore = PercentileRank(drWindow,  deltaRatio);
            // Fall back to 0.5 (neutral) when the sub-window is empty.
            decimal speedScore    = spWindow.Count > 0
                                        ? PercentileRank(spWindow, speed)
                                        : 0.5m;
            decimal rangeEffScore = (reWindow.Count > 0 && candleRange > 0)
                                        ? PercentileRank(reWindow, rangeEff)
                                        : 0.5m;

            // ── Average volume gate ─────────────────────────────────────────
            decimal volSum = 0m;
            foreach (var v in volWindow) volSum += v;
            decimal avgVolume = volSum / volWindow.Count;
            if (totalVolume < avgVolume * MinVolumeMultiplier)
                return;

            // ── Composite effort scores ─────────────────────────────────────
            // BuyEffort  — only on positive-delta candles (net buying aggression).
            // SellEffort — only on negative-delta candles (net selling aggression).
            // CloseStrength reflects where price closed within the range.
            if (delta > 0)
            {
                decimal closeStrength = closePosition;  // high close reinforces bull effort
                decimal buyEffort = (volumeScore + deltaRatScore + speedScore + rangeEffScore + closeStrength) / 5m;

                if (buyEffort >= EffortThreshold)
                    _signals[bar] = new SignalInfo { IsBull = true, Score = buyEffort };
            }
            else if (delta < 0)
            {
                decimal closeStrength = 1m - closePosition; // low close reinforces bear effort
                decimal sellEffort = (volumeScore + deltaRatScore + speedScore + rangeEffScore + closeStrength) / 5m;

                if (sellEffort >= EffortThreshold)
                    _signals[bar] = new SignalInfo { IsBull = false, Score = sellEffort };
            }
        }

        /// <summary>
        /// Returns the fraction of <paramref name="window"/> values strictly less than
        /// <paramref name="value"/> — a standard percentile rank in [0, 1).
        /// </summary>
        private static decimal PercentileRank(List<decimal> window, decimal value)
        {
            if (window.Count == 0) return 0m;
            int below = 0;
            foreach (var v in window)
                if (v < value) below++;
            return (decimal)below / window.Count;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Custom rendering  (called every chart frame for all visible bars)
        // ═══════════════════════════════════════════════════════════════════════

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final) return;
            if (ChartInfo is null) return;

            // ── Derive fully-opaque border colours from the (possibly semi-transparent) fills ─
            var bullBorder = Color.FromArgb(255, _boxColorBull.R, _boxColorBull.G, _boxColorBull.B);
            var bearBorder = Color.FromArgb(255, _boxColorBear.R, _boxColorBear.G, _boxColorBear.B);

            // ── Create OFT rendering resources for this frame ───────────────
            var bullPen   = new OFT.Rendering.Tools.RenderPen(bullBorder, _boxBorderWidth);
            var bearPen   = new OFT.Rendering.Tools.RenderPen(bearBorder, _boxBorderWidth);
            var labelFont = new OFT.Rendering.Tools.RenderFont("Arial", 8f);

            // Iterate all calculated bars; GetXByBar returns off-screen coords
            // for bars outside the visible area so nothing extra is drawn.
            for (int bar = 0; bar < CurrentBar; bar++)
            {
                if (!_signals.TryGetValue(bar, out var signal)) continue;

                var candle = GetCandle(bar);

                // ── Convert price levels to screen pixels ───────────────────
                // Y increases downward on screen, so higher prices = smaller Y.
                int xLeft  = ChartInfo.GetXByBar(bar);
                int xRight = ChartInfo.GetXByBar(bar + 1); // bar is always closed so bar+1 exists
                int yTop   = ChartInfo.GetYByPrice((decimal)candle.High);
                int yBot   = ChartInfo.GetYByPrice((decimal)candle.Low);

                // Guard against inverted / degenerate coordinates
                if (yTop > yBot) (yTop, yBot) = (yBot, yTop);
                int w = xRight - xLeft;
                int h = yBot   - yTop;
                if (w <= 0 || h <= 0) continue;

                var rect = new Rectangle(xLeft, yTop, w, h);

                // ── Draw filled box + border ─────────────────────────────────
                if (signal.IsBull)
                {
                    context.FillRectangle(_boxColorBull, rect);
                    context.DrawRectangle(bullPen, rect);
                }
                else
                {
                    context.FillRectangle(_boxColorBear, rect);
                    context.DrawRectangle(bearPen, rect);
                }

                // ── Optional effort-score label above the box ────────────────
                if (_showEffortLabel)
                {
                    string label      = signal.Score.ToString("F2");
                    Color  labelColor = signal.IsBull ? bullBorder : bearBorder;
                    var    labelRect  = new Rectangle(xLeft, yTop - 14, Math.Max(w, 32), 14);
                    context.DrawString(label, labelFont, labelColor, labelRect);
                }
            }
        }
    }
}
