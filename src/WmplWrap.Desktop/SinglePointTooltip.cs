using LiveChartsCore;
using LiveChartsCore.Drawing.Layouts;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView.Drawing;
using LiveChartsCore.SkiaSharpView.SKCharts;

namespace WmplWrap.Desktop;

/// <summary>Shows the hovered point only, even when multiple chart series overlap.</summary>
public sealed class SinglePointTooltip : SKDefaultTooltip
{
    protected override Layout<SkiaSharpDrawingContext> GetLayout(IEnumerable<ChartPoint> foundPoints, Chart chart) =>
        base.GetLayout(foundPoints.Take(1), chart);
}
