using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;

namespace WmplWrap.Desktop;

/// <summary>
/// LiveCharts normally returns every stacked column at a shared X position for a tooltip.
/// This series limits the hit test to the actual colored segment under the pointer.
/// </summary>
internal sealed class ExactPointStackedColumnSeries : StackedColumnSeries<double>
{
    protected override IEnumerable<ChartPoint> FindPointsInPosition(
        Chart chart,
        LvcPoint pointerPosition,
        FindingStrategy strategy,
        FindPointFor findPointFor)
    {
        return Fetch(chart).Where(point =>
        {
            var area = (RectangleHoverArea?)point.Context.HoverArea;
            return area is not null &&
                   area.X <= pointerPosition.X && pointerPosition.X <= area.X + area.Width &&
                   area.Y <= pointerPosition.Y && pointerPosition.Y <= area.Y + area.Height;
        });
    }
}
