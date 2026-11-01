using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BetterLoading.BetterLoadingMod), "BetterLoading", "0.1.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]

namespace BetterLoading
{
    // Acelera o carregamento de mapas/ônibus mod em .3ds:
    //  - o jogo renderiza um frame inteiro entre um .3ds e outro; durante o carregamento o FPS fica
    //    destravado e (opcional) as câmeras só desenham UI, então cada espera custa ~1 ms;
    //  - o arquivo é lido inteiro para a memória e vértices/faces/UVs são lidos em bloco;
    //  - texturas decodificadas (e comprimidas, se o jogo comprimir) vão para um cache em disco;
    //  - relatório de tempos em UserData/BetterLoading_relatorio.txt ao fim de cada carga.
    public class BetterLoadingMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> Ativo, LeituraRapida, CacheTexturas, DestravarFps, EsconderCena, PreAquecer;
        internal static MelonPreferences_Entry<float> FimCargaSegundos;

        public override void OnLateInitializeMelon() => UiIntegracao.Registrar();


        public override void OnInitializeMelon()
        {
            var c = MelonPreferences.CreateCategory("BetterLoading", "BetterLoading");
            Ativo = c.CreateEntry("Ativo", true, "Liga o mod");
            LeituraRapida = c.CreateEntry("LeituraRapida3DS", true, "Lê o .3ds inteiro na memória e vértices/faces/UVs em bloco");
            CacheTexturas = c.CreateEntry("CacheTexturas", true, "Guarda as texturas já decodificadas/comprimidas em UserData/BetterLoading/texturas");
            DestravarFps = c.CreateEntry("DestravarFpsAoCarregar", true, "Tira vsync e limite de FPS enquanto carrega (o jogo espera um frame por arquivo)");
            EsconderCena = c.CreateEntry("EsconderCenaAoCarregar", true, "Durante o carregamento as câmeras só desenham a UI (a tela de carregamento cobre tudo mesmo)");
            PreAquecer = c.CreateEntry("PreAquecerArquivos", true, "Lê os arquivos da pasta em segundo plano para o Windows deixá-los em cache antes do jogo pedir");
            FimCargaSegundos = c.CreateEntry("FimCargaSegundos", 2f, "Segundos sem carregar nada para considerar que a carga terminou");

            CacheTextura.Iniciar();
        }

        public override void OnUpdate() => Carga.Atualizar();

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => Carga.CenaMudou();
    }
}
