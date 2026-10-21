using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace NetSim.App.Services;

/// <summary>Схема уровня для отчёта: подпись и PNG.</summary>
public sealed record ReportImage(string Caption, byte[] Png, int WidthPx, int HeightPx);

/// <summary>
/// Простой построитель документа Word (.docx): заголовки, абзацы, таблицы, рисунки с подписями,
/// моноширинные блоки для расчётов. Рисунки и таблицы нумеруются автоматически.
/// </summary>
public sealed class WordReport : IDisposable
{
    private readonly WordprocessingDocument _doc;
    private readonly Body _body;
    private int _figure, _table;
    private uint _imageId = 1;

    public WordReport(string path)
    {
        _doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = _doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        _body = main.Document.Body!;
        AddStyles(main);
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        Style Heading(string id, string name, int size) => new(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "120" }, new OutlineLevel { Val = id == "Heading1" ? 0 : 1 }),
            new StyleRunProperties(new Bold(), new Color { Val = "1F3864" }, new FontSize { Val = (size * 2).ToString() }))
        { Type = StyleValues.Paragraph, StyleId = id };

        styles.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman", ComplexScript = "Times New Roman", EastAsia = "Times New Roman" },
                    new FontSize { Val = "28" }, new Languages { Val = "ru-RU" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "120", Line = "300", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            Heading("Heading1", "heading 1", 16),
            Heading("Heading2", "heading 2", 14),
            new Style(new StyleName { Val = "Caption" }, new BasedOn { Val = "Normal" },
                new StyleParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new StyleRunProperties(new Italic(), new FontSize { Val = "24" }))
            { Type = StyleValues.Paragraph, StyleId = "Caption" });
    }

    public void Title(string text) =>
        _body.Append(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "600", After = "240" }, new Justification { Val = JustificationValues.Center }),
            new Run(new RunProperties(new Bold(), new FontSize { Val = "36" }), new Text(text))));

    public void Centered(string text, bool bold = false) =>
        _body.Append(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(bold ? new RunProperties(new Bold()) : new RunProperties(), new Text(text) { Space = SpaceProcessingModeValues.Preserve })));

    public void Heading1(string text) => _body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text(text))));
    public void Heading2(string text) => _body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading2" }), new Run(new Text(text))));

    public void Paragraph(string text)
    {
        foreach (var line in text.Split('\n'))
            _body.Append(new Paragraph(
                new ParagraphProperties(new Indentation { FirstLine = "709" }, new Justification { Val = JustificationValues.Both }),
                new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
    }

    /// <summary>Блок расчёта моноширинным шрифтом (сохраняет выравнивание формул).</summary>
    public void Code(string text)
    {
        foreach (var line in text.Split('\n'))
            _body.Append(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                new Run(new RunProperties(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" }, new FontSize { Val = "20" }),
                    new Text(line.TrimEnd('\r')) { Space = SpaceProcessingModeValues.Preserve })));
        _body.Append(new Paragraph());
    }

    public void PageBreak() => _body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

    public void Table(ReportTable t, string captionPrefix)
    {
        _table++;
        _body.Append(new Paragraph(new ParagraphProperties(new KeepNext()),
            new Run(new Text($"{captionPrefix} {_table} — {t.Title}") { Space = SpaceProcessingModeValues.Preserve })));
        var table = new Table();
        var border = BorderValues.Single;
        table.Append(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = border, Size = 4 }, new LeftBorder { Val = border, Size = 4 },
                new BottomBorder { Val = border, Size = 4 }, new RightBorder { Val = border, Size = 4 },
                new InsideHorizontalBorder { Val = border, Size = 4 }, new InsideVerticalBorder { Val = border, Size = 4 }),
            new TableLayout { Type = TableLayoutValues.Autofit }));
        var tableGrid = new TableGrid();
        foreach (var _ in t.Headers) tableGrid.Append(new GridColumn());
        table.Append(tableGrid);

        TableCell Cell(string text, bool header) => new(
            new TableCellProperties(header ? new Shading { Val = ShadingPatternValues.Clear, Fill = "D9E2F3", Color = "auto" } : new Shading { Val = ShadingPatternValues.Nil }),
            new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                new Run(new RunProperties(header ? new Bold() : new Bold { Val = false }, new FontSize { Val = "20" }), new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve })));

        var head = new TableRow(new TableRowProperties(new TableHeader()));
        foreach (var h in t.Headers) head.Append(Cell(h, true));
        table.Append(head);
        foreach (var r in t.Rows)
        {
            var row = new TableRow();
            foreach (var c in r) row.Append(Cell(c, false));
            table.Append(row);
        }
        _body.Append(table);
        _body.Append(new Paragraph());
    }

    /// <summary>Рисунок шириной до 16 см с подписью «Рисунок N — ...» (пустой префикс — подпись как есть).</summary>
    public void Image(ReportImage img, string captionPrefix)
    {
        var main = _doc.MainDocumentPart!;
        var part = main.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(img.Png)) part.FeedData(ms);
        var relId = main.GetIdOfPart(part);

        const long emuPerCm = 360000;
        long maxW = 16 * emuPerCm;
        long w = img.WidthPx * 9525L, h = img.HeightPx * 9525L; // 96 dpi
        if (w > maxW) { h = h * maxW / w; w = maxW; }
        long maxH = 22 * emuPerCm;
        if (h > maxH) { w = w * maxH / h; h = maxH; }
        uint id = _imageId++;

        var drawing = new Drawing(new DW.Inline(
            new DW.Extent { Cx = w, Cy = h },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = id, Name = $"Picture {id}" },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = id, Name = $"image{id}.png" }, new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = w, Cy = h }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });

        _body.Append(new Paragraph(new ParagraphProperties(new KeepNext(), new Justification { Val = JustificationValues.Center }), new Run(drawing)));
        _figure++;
        var caption = captionPrefix.Length == 0 ? img.Caption : $"{captionPrefix} {_figure} — {img.Caption}";
        _body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Caption" }),
            new Run(new Text(caption) { Space = SpaceProcessingModeValues.Preserve })));
    }

    public void Dispose()
    {
        _body.Append(new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1134, Bottom = 1134, Left = 1701, Right = 850, Header = 709, Footer = 709 }));
        _doc.MainDocumentPart!.Document.Save();
        _doc.Dispose();
    }
}
