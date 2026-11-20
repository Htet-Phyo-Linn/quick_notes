using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace QuickNote.Avalonia.Views
{
    /// <summary>
    /// A staggered masonry panel that distributes items into dynamic columns (e.g. 2 for Keep grid, 1 for list).
    /// </summary>
    public class StaggeredGridPanel : Panel
    {
        public static readonly StyledProperty<int> ColumnsProperty =
            AvaloniaProperty.Register<StaggeredGridPanel, int>(nameof(Columns), defaultValue: 2);

        public static readonly StyledProperty<double> SpacingProperty =
            AvaloniaProperty.Register<StaggeredGridPanel, double>(nameof(Spacing), defaultValue: 10.0);

        public int Columns
        {
            get => GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public double Spacing
        {
            get => GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        static StaggeredGridPanel()
        {
            AffectsMeasure<StaggeredGridPanel>(ColumnsProperty, SpacingProperty);
            AffectsArrange<StaggeredGridPanel>(ColumnsProperty, SpacingProperty);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            try
            {
                var children = Children;
                int count = children.Count;
                if (count == 0) return new Size(0, 0);

                int cols = Math.Max(1, Columns);
                double spacing = Spacing;
                double availWidth = double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width;

                if (cols <= 1)
                {
                    double totalHeight = 0;
                    bool first = true;
                    for (int i = 0; i < count; i++)
                    {
                        if (i >= children.Count) break;
                        var child = children[i];
                        if (child == null || !child.IsVisible) continue;
                        child.Measure(new Size(availWidth, double.PositiveInfinity));
                        if (!first) totalHeight += spacing;
                        totalHeight += child.DesiredSize.Height;
                        first = false;
                    }
                    return new Size(availWidth, totalHeight);
                }

                double totalSpacing = spacing * (cols - 1);
                double colWidth = Math.Max(0, (availWidth - totalSpacing) / cols);
                Span<double> colHeights = cols <= 16 ? stackalloc double[cols] : new double[cols];

                for (int i = 0; i < count; i++)
                {
                    if (i >= children.Count) break;
                    var child = children[i];
                    if (child == null || !child.IsVisible) continue;
                    child.Measure(new Size(colWidth, double.PositiveInfinity));

                    // Find column with min height
                    int targetCol = 0;
                    double minH = colHeights[0];
                    for (int c = 1; c < cols; c++)
                    {
                        if (colHeights[c] < minH)
                        {
                            minH = colHeights[c];
                            targetCol = c;
                        }
                    }

                    if (colHeights[targetCol] > 0)
                    {
                        colHeights[targetCol] += spacing;
                    }
                    colHeights[targetCol] += child.DesiredSize.Height;
                }

                double maxHeight = 0;
                for (int c = 0; c < cols; c++)
                {
                    if (colHeights[c] > maxHeight) maxHeight = colHeights[c];
                }
                return new Size(availWidth, maxHeight);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StaggeredGridPanel] MeasureOverride error: {ex.Message}");
                return new Size(double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width, 0);
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            try
            {
                var children = Children;
                int count = children.Count;
                if (count == 0) return finalSize;

                int cols = Math.Max(1, Columns);
                double spacing = Spacing;
                double width = finalSize.Width;

                if (cols <= 1)
                {
                    double y = 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (i >= children.Count) break;
                        var child = children[i];
                        if (child == null || !child.IsVisible) continue;
                        child.Arrange(new Rect(0, y, width, child.DesiredSize.Height));
                        y += child.DesiredSize.Height + spacing;
                    }
                    return finalSize;
                }

                double totalSpacing = spacing * (cols - 1);
                double colWidth = Math.Max(0, (width - totalSpacing) / cols);
                Span<double> colY = cols <= 16 ? stackalloc double[cols] : new double[cols];

                for (int i = 0; i < count; i++)
                {
                    if (i >= children.Count) break;
                    var child = children[i];
                    if (child == null || !child.IsVisible) continue;

                    int targetCol = 0;
                    double minY = colY[0];
                    for (int c = 1; c < cols; c++)
                    {
                        if (colY[c] < minY)
                        {
                            minY = colY[c];
                            targetCol = c;
                        }
                    }

                    double x = targetCol * (colWidth + spacing);
                    double y = colY[targetCol];

                    child.Arrange(new Rect(x, y, colWidth, child.DesiredSize.Height));
                    colY[targetCol] += child.DesiredSize.Height + spacing;
                }

                return finalSize;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StaggeredGridPanel] ArrangeOverride error: {ex.Message}");
                return finalSize;
            }
        }
    }
}
