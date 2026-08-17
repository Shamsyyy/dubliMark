using DoubleMark.Core.Crpt;
using DoubleMark.Core.Print;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// CRPT label rendering from downloaded marking codes (spec §11.4, phase C).
/// Physical printer dispatch stays in <see cref="MarkPrintService"/>; this service renders labels.
/// </summary>
public interface ICrptPrintService
{
    MarkRenderResult RenderLabel(CrptMarkingCodeItem code, PrintTemplate template, int dpi = 300);

    IReadOnlyList<MarkRenderResult> RenderBatch(
        IReadOnlyList<CrptMarkingCodeItem> codes,
        PrintTemplate template,
        int dpi = 300);
}
