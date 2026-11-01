using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using UnityEngine;
using M = BetterLoading.BetterLoadingMod;

namespace BetterLoading
{
    // Detecta a janela de carregamento (de um Import3DS até FimCargaSegundos sem nenhum), mexe no
    // FPS/câmeras durante ela e acumula os tempos para o relatório.
    internal static class Carga
    {
        static MelonLogger.Instance Log => Melon<M>.Logger;

        internal static bool Carregando;
        static long _inicio, _ultimoFim;
        static float _ultimoImportReal;

        // Estatísticas da carga atual (ticks do Stopwatch)
        internal static long TImport, TTexturas, TTexturasFora, TLeitores, TEsperas, TEsperaMax, Bytes3ds;
        internal static int N3ds, NTexHit, NTexMiss, NTexOriginal, NFalhas;
        internal static int ProfundidadeImport;
        static readonly List<(string arq, long t, long tex, long bytes)> _arquivos = new();

        // Estado salvo para restaurar
        static int _oVsync, _oFps;
        static bool _fpsMudado;
        static readonly List<(Camera cam, int mask)> _cameras = new();

        internal static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        internal static void InicioImport()
        {
            long agora = Stopwatch.GetTimestamp();
            if (!Carregando) Comecar(agora);
            else if (_ultimoFim != 0)
            {
                long espera = agora - _ultimoFim;
                TEsperas += espera;
                if (espera > TEsperaMax) TEsperaMax = espera;
            }
            _ultimoImportReal = Time.realtimeSinceStartup;
        }

        internal static void FimImport(string arquivo, long t, long tex, long bytes)
        {
            N3ds++; TImport += t; Bytes3ds += bytes;
            _arquivos.Add((arquivo, t, tex, bytes));
            _ultimoFim = Stopwatch.GetTimestamp();
            _ultimoImportReal = Time.realtimeSinceStartup;
            if (EsconderCenaLigado) EsconderCameras(); // câmeras criadas no meio da carga
        }

        static bool EsconderCenaLigado => M.Ativo.Value && M.EsconderCena.Value;

        static void Comecar(long agora)
        {
            Carregando = true;
            _inicio = agora; _ultimoFim = 0;
            TImport = TTexturas = TTexturasFora = TLeitores = TEsperas = TEsperaMax = Bytes3ds = 0;
            N3ds = NTexHit = NTexMiss = NTexOriginal = NFalhas = 0;
            _arquivos.Clear();
            if (M.Ativo.Value && M.DestravarFps.Value)
            {
                _oVsync = QualitySettings.vSyncCount; _oFps = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                _fpsMudado = true;
            }
            PatchImport3DS.NovaCarga();
            if (EsconderCenaLigado) EsconderCameras();
            Log.Msg("Carregamento começou");
        }

        static void EsconderCameras()
        {
            const int camadaUI = 1 << 5;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || c.targetTexture != null) continue;
                bool ja = false;
                foreach (var (cam, _) in _cameras) if (cam != null && cam.Pointer == c.Pointer) { ja = true; break; }
                if (ja) continue;
                _cameras.Add((c, c.cullingMask));
                c.cullingMask &= camadaUI;
            }
        }

        static void Restaurar()
        {
            // Só volta o que nós mudamos e que o jogo não mexeu depois.
            if (_fpsMudado)
            {
                _fpsMudado = false;
                if (QualitySettings.vSyncCount == 0) QualitySettings.vSyncCount = _oVsync;
                if (Application.targetFrameRate == -1) Application.targetFrameRate = _oFps;
            }
            foreach (var (cam, mask) in _cameras)
                if (cam != null) cam.cullingMask = mask;
            _cameras.Clear();
        }

        internal static void Atualizar()
        {
            if (!Carregando) return;
            if (ProfundidadeImport > 0) return;
            if (Time.realtimeSinceStartup - _ultimoImportReal < M.FimCargaSegundos.Value) return;
            Terminar();
        }

        internal static void CenaMudou()
        {
            // Câmeras da cena anterior somem; restaura o que der e mantém a contagem.
            if (!Carregando) return;
            foreach (var (cam, mask) in _cameras) if (cam != null) cam.cullingMask = mask;
            _cameras.Clear();
            if (EsconderCenaLigado) EsconderCameras();
        }

        static void Terminar()
        {
            Carregando = false;
            Restaurar();
            long total = _ultimoFim - _inicio;
            long so3ds = TImport - TTexturas - TLeitores;
            Log.Msg($"Carregamento: {N3ds} .3ds ({Bytes3ds / 1048576.0:F1} MB) em {Ms(total) / 1000:F1}s | dentro do Import3DS {Ms(TImport) / 1000:F1}s " +
                    $"(texturas {Ms(TTexturas) / 1000:F1}s, vértices/faces {Ms(TLeitores) / 1000:F1}s, resto {Ms(so3ds) / 1000:F1}s) | esperando frame {Ms(TEsperas) / 1000:F1}s | " +
                    $"texturas: {NTexHit} do cache, {NTexMiss} novas, {NTexOriginal} pelo jogo");
            try { GravarRelatorio(total, so3ds); } catch (Exception e) { Log.Warning("Relatório: " + e.Message); }
        }

        static void GravarRelatorio(long total, long so3ds)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== BetterLoading {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==");
            sb.AppendLine($"Opções: leitura rápida {M.LeituraRapida.Value}, cache texturas {M.CacheTexturas.Value}, FPS destravado {M.DestravarFps.Value}, esconder cena {M.EsconderCena.Value}, pré-aquecer {M.PreAquecer.Value}");
            sb.AppendLine($"Compressão de texturas do jogo: {SafeComprimir()}");
            sb.AppendLine();
            sb.AppendLine($"Do primeiro ao último .3ds:  {Ms(total) / 1000,8:F2} s");
            sb.AppendLine($"  dentro do Import3DS:       {Ms(TImport) / 1000,8:F2} s  ({N3ds} arquivos, {Bytes3ds / 1048576.0:F1} MB)");
            sb.AppendLine($"    texturas (CarregaTextura){Ms(TTexturas) / 1000,8:F2} s  (cache {NTexHit}, novas {NTexMiss}, pelo jogo {NTexOriginal})");
            sb.AppendLine($"    vértices/faces/UVs:      {Ms(TLeitores) / 1000,8:F2} s");
            sb.AppendLine($"    resto (chunks, materiais, normais, objetos): {Ms(so3ds) / 1000:F2} s");
            sb.AppendLine($"  entre arquivos (frame + o que o jogo faz no meio): {Ms(TEsperas) / 1000:F2} s, média {(N3ds > 1 ? Ms(TEsperas) / (N3ds - 1) : 0):F1} ms, pior {Ms(TEsperaMax):F1} ms");
            sb.AppendLine($"Texturas carregadas fora do Import3DS: {Ms(TTexturasFora) / 1000:F2} s");
            if (NFalhas > 0) sb.AppendLine($"Falhas no caminho rápido (caiu no original): {NFalhas}");
            sb.AppendLine();
            sb.AppendLine("Arquivos mais lentos (total ms | texturas ms | MB | arquivo):");
            foreach (var a in _arquivos.OrderByDescending(a => a.t).Take(25))
                sb.AppendLine($"{Ms(a.t),9:F1} | {Ms(a.tex),9:F1} | {a.bytes / 1048576.0,6:F2} | {a.arq}");
            string caminho = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "BetterLoading_relatorio.txt");
            File.WriteAllText(caminho, sb.ToString(), Encoding.UTF8);
            Log.Msg("Relatório gravado em " + caminho);
        }

        static string SafeComprimir()
        {
            try { return Il2Cpp.MEPCanvasStart.comprimirTexturasRuntime ? "ligada" : "desligada"; } catch { return "?"; }
        }
    }
}
