using System;
using MelonLoader;

[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]

namespace BetterLoading
{
    // Coloca o mod no menu da PBSLoaderCommonInterface (ações sem atalho de teclado e sliders com faixa). Ela é base do PBSLoader,
    // sempre instalada junto com a PBSLoaderCommonUtils.
    internal static class UiIntegracao
    {
        internal static void Registrar()
        {
            try { Fazer(); }
            catch (Exception e) { MelonLogger.Warning("Integração com a PBSLoaderCommonInterface falhou: " + e.Message); }
        }

        static void Fazer()
        {
            const string C = "BetterLoading";
            PBSLoaderCommonInterface.PainelMods.Nome(C, "BetterLoading");
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "FimCargaSegundos", 0.5f, 10f, 0.5f);
        }
    }
}
