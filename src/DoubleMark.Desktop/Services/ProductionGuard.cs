using System.Diagnostics;

namespace DoubleMark.Desktop.Services;

public static class ProductionGuard
{
    public static bool CanUseProtectedFeature()
    {
#if DEBUG
        return true;
#else
        return !Debugger.IsAttached;
#endif
    }

    public static string ProtectedFeatureBlockedMessage =>
        "Произошла ошибка. Попробуйте ещё раз или обратитесь в поддержку.";
}
