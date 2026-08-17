using DoubleMark.Core.Crpt;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Services.Crpt;

public sealed class CrptPrintService : ICrptPrintService
{
    private readonly MarkRenderService _renderService;
    private readonly Gs1Parser _parser;

    public CrptPrintService(MarkRenderService? renderService = null, Gs1Parser? parser = null)
    {
        _renderService = renderService ?? new MarkRenderService();
        _parser = parser ?? new Gs1Parser();
    }

    public MarkRenderResult RenderLabel(CrptMarkingCodeItem code, PrintTemplate template, int dpi = 300)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(template);

        var parseResult = _parser.Parse(code.RawPayload);
        if (!parseResult.IsValid || parseResult.Code is null)
            throw new InvalidOperationException("Only valid marking codes can be rendered for CRPT print.");

        var render = _renderService.Render(new MarkRenderRequest
        {
            RawPayload = code.RawPayload,
            ParseResult = parseResult,
            Template = template,
            Source = "CRPT",
            Dpi = dpi,
        });

        var inputGs = Gs1BarcodeEncoding.CountGs(code.RawPayload);
        if (render.GsCount != inputGs)
        {
            throw new InvalidOperationException(
                $"GS preservation failed for code #{code.Id}: expected {inputGs}, render has {render.GsCount}.");
        }

        return render;
    }

    public IReadOnlyList<MarkRenderResult> RenderBatch(
        IReadOnlyList<CrptMarkingCodeItem> codes,
        PrintTemplate template,
        int dpi = 300)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(template);

        var results = new List<MarkRenderResult>(codes.Count);
        foreach (var code in codes)
            results.Add(RenderLabel(code, template, dpi));

        return results;
    }
}
