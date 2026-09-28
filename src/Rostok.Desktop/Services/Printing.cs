using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Rostok.Controls;

namespace Rostok.Desktop.Services;

// Печать и PDF: системный диалог печати (принтер «Microsoft Print to PDF» сохраняет PDF).
// Документ собирается из блоков экрана и раскладывается по страницам A4 — разрыв между блоками, а не посреди строки.
public static class Printing
{
    private const double Margin = 56; // ≈ 1,5 см

    public static void PrintText(string title, string text)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var doc = new FlowDocument
        {
            FontFamily = Theme.UiFont, FontSize = 13, Foreground = Theme.Ink, PagePadding = new Thickness(Margin),
            PageWidth = dlg.PrintableAreaWidth, PageHeight = dlg.PrintableAreaHeight, ColumnWidth = double.PositiveInfinity, LineHeight = 20,
        };
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0), MinOrphanLines = 1 });
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
    }

    // build(ширина) должен вернуть свежие элементы (не те, что уже стоят на экране).
    public static void PrintBlocks(string title, Func<double, IEnumerable<FrameworkElement>> build)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        var pageW = dlg.PrintableAreaWidth;
        var pageH = dlg.PrintableAreaHeight;
        var doc = Compose(pageW, pageH, build(pageW - Margin * 2));
        dlg.PrintDocument(doc.DocumentPaginator, title);
    }

    public static FixedDocument Compose(double pageW, double pageH, IEnumerable<FrameworkElement> blocks)
    {
        var contentW = pageW - Margin * 2;
        var contentH = pageH - Margin * 2;
        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageW, pageH);
        StackPanel? current = null;
        double used = 0;

        void NewPage()
        {
            current = new StackPanel { Width = contentW };
            var page = new FixedPage { Width = pageW, Height = pageH, Background = Brushes.White };
            FixedPage.SetLeft(current, Margin);
            FixedPage.SetTop(current, Margin);
            page.Children.Add(current);
            doc.Pages.Add(new PageContent { Child = page });
            used = 0;
        }

        foreach (var block in blocks)
        {
            block.Measure(new Size(contentW, double.PositiveInfinity));
            var h = block.DesiredSize.Height;
            if (current is null || (used + h > contentH && used > 0)) NewPage();
            if (h <= contentH)
            {
                current!.Children.Add(block);
                used += h;
                continue;
            }
            // блок выше страницы — режем его изображение на полосы
            block.Arrange(new Rect(0, 0, contentW, h));
            for (double y = 0; y < h; y += contentH)
            {
                if (y > 0) NewPage();
                var slice = Math.Min(contentH, h - y);
                current!.Children.Add(new Rectangle
                {
                    Width = contentW, Height = slice,
                    Fill = new VisualBrush(block) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, y, contentW, slice) },
                });
                used = slice;
            }
        }
        if (doc.Pages.Count == 0) NewPage();
        return doc;
    }
}
