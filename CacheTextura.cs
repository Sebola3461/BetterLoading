using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using M = BetterLoading.BetterLoadingMod;

namespace BetterLoading
{
    // MepUtils.CarregaTextura decodifica PNG/JPG com LoadImage (gera mipmaps) e, se a opção do jogo
    // estiver ligada, roda Compress(true) — tudo na thread principal, toda vez que o mapa carrega.
    // Aqui o resultado final (dados crus com mipmaps, já comprimidos ou não) vai para um arquivo
    // e nas próximas cargas a textura é montada direto com LoadRawTextureData.
    //
    // Fluxo do original que é reproduzido no acerto do cache:
    //   arquivo não existe -> null | arquivo+".override" substitui | dicTex[caminho] reaproveita |
    //   arquivo+".config.txt" usa outro carregador (não mexemos) | maior que maxTextureSize tem
    //   aviso próprio (deixamos o original tratar) | ehPraCachear -> dicTex.Add(caminho, tex)
    [HarmonyPatch(typeof(Il2Cpp.MepUtils), nameof(Il2Cpp.MepUtils.CarregaTextura))]
    internal static class CacheTextura
    {
        static MelonLogger.Instance Log => Melon<M>.Logger;
        const int Magico = 0x31545243; // "CRT1"
        const int Versao = 1;
        const int TamCabecalho = 4 + 4 + 8 + 8 + 4 * 5;
        static string _pasta;

        internal static void Iniciar()
        {
            _pasta = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "BetterLoading", "texturas");
            try { Directory.CreateDirectory(_pasta); } catch (Exception e) { Log.Warning("Pasta do cache: " + e.Message); }
        }

        class Estado { internal long t0; internal string arqCache; internal long tam, data; }

        static bool Prefix(string __0, bool __1, ref Texture2D __result, out Estado __state)
        {
            __state = new Estado { t0 = Stopwatch.GetTimestamp() };
            if (!M.Ativo.Value || !M.CacheTexturas.Value || _pasta == null) return Original();
            try
            {
                string path = __0;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Original();
                if (File.Exists(path + ".config.txt")) return Original();
                string p = File.Exists(path + ".override") ? path + ".override" : path;

                var dic = Il2Cpp.DicCacheMods.instance?.dicTex;
                if (__1 && (dic == null || dic.ContainsKey(p))) return Original();
                if (Il2Cpp.AvisoLowMemory.vaiFecharPqFaltaRAM) return Original();

                bool comprimir = Il2Cpp.MEPCanvasStart.comprimirTexturasRuntime;
                var fi = new FileInfo(p);
                __state.tam = fi.Length; __state.data = fi.LastWriteTimeUtc.Ticks;
                __state.arqCache = Path.Combine(_pasta, Chave(p, comprimir) + ".tex");

                var tex = Ler(__state.arqCache, __state.tam, __state.data);
                if (tex == null) { Carga.NTexMiss++; return true; } // o Postfix grava

                int max = SystemInfo.maxTextureSize;
                if (tex.width > max || tex.height > max)
                {
                    UnityEngine.Object.Destroy(tex);
                    __state.arqCache = null;
                    return Original();
                }
                if (__1) dic.Add(p, tex);
                __result = tex;
                __state.arqCache = null;
                Carga.NTexHit++;
                return false;
            }
            catch (Exception e)
            {
                Carga.NFalhas++;
                Log.Warning($"Cache de textura ({Path.GetFileName(__0)}): {e.Message}");
                __state.arqCache = null;
                return Original();
            }
        }

        static bool Original() { Carga.NTexOriginal++; return true; }

        static void Postfix(Texture2D __result, Estado __state)
        {
            long t = Stopwatch.GetTimestamp() - __state.t0;
            if (Carga.ProfundidadeImport > 0) { Carga.TTexturas += t; PatchImport3DS.TexturasArquivoAtual += t; }
            else Carga.TTexturasFora += t;

            if (__state.arqCache == null || __result == null) return;
            try { Gravar(__result, __state.arqCache, __state.tam, __state.data); }
            catch (Exception e) { Log.Warning("Gravando cache de textura: " + e.Message); }
        }

        static string Chave(string caminho, bool comprimir)
        {
            string s = Path.GetFullPath(caminho).ToLowerInvariant() + (comprimir ? "|c" : "|n");
            var h = SHA1.HashData(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(h, 0, 12);
        }

        static unsafe Texture2D Ler(string arq, long tam, long data)
        {
            if (!File.Exists(arq)) return null;
            byte[] b;
            try { b = File.ReadAllBytes(arq); } catch { return null; }
            if (b.Length < TamCabecalho) return null;
            fixed (byte* p = b)
            {
                int* i = (int*)p;
                if (i[0] != Magico || i[1] != Versao) return null;
                if (*(long*)(p + 8) != tam || *(long*)(p + 16) != data) return null; // imagem mudou
                int* h = (int*)(p + 24);
                int w = h[0], alt = h[1], fmt = h[2], mips = h[3], len = h[4];
                if (w <= 0 || alt <= 0 || len <= 0 || TamCabecalho + len > b.Length) return null;
                var tex = new Texture2D(w, alt, (TextureFormat)fmt, mips > 1, false);
                if (tex.mipmapCount != mips || !tex.LoadRawTextureDataImpl((IntPtr)(p + TamCabecalho), len))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.Apply(false, false);
                return tex;
            }
        }

        static unsafe void Gravar(Texture2D tex, string arq, long tam, long data)
        {
            var raw = tex.GetRawTextureData();
            if (raw == null || raw.Length == 0) return;
            int len = raw.Length;
            var b = new byte[TamCabecalho + len];
            fixed (byte* p = b)
            {
                int* i = (int*)p;
                i[0] = Magico; i[1] = Versao;
                *(long*)(p + 8) = tam; *(long*)(p + 16) = data;
                int* h = (int*)(p + 24);
                h[0] = tex.width; h[1] = tex.height; h[2] = (int)tex.format; h[3] = tex.mipmapCount; h[4] = len;
                Buffer.MemoryCopy(Bloco.Dados(raw), p + TamCabecalho, len, len);
            }
            // Escrita fora da thread principal; nome temporário para nunca deixar arquivo pela metade.
            Task.Run(() =>
            {
                try
                {
                    string tmp = arq + ".tmp" + Environment.CurrentManagedThreadId;
                    File.WriteAllBytes(tmp, b);
                    File.Move(tmp, arq, true);
                }
                catch { }
            });
        }
    }
}
