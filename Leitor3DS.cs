using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using M = BetterLoading.BetterLoadingMod;
using R = Il2Cpp._3DSReader;
using BinaryReader = Il2CppSystem.IO.BinaryReader;

namespace BetterLoading
{
    // O _3DSReader original abre o arquivo com File.OpenRead (e nunca fecha) e lê cada float/ushort
    // com uma chamada virtual de BinaryReader. Aqui o arquivo vem inteiro para um MemoryStream e
    // os três blocos grandes (vértices, faces, UVs) são lidos de uma vez e convertidos por ponteiro.
    // O ReadMain original continua fazendo todo o resto (materiais, texturas, objetos).
    [HarmonyPatch(typeof(R), nameof(R.Import3DS))]
    internal static class PatchImport3DS
    {
        static MelonLogger.Instance Log => Melon<M>.Logger;
        static readonly HashSet<string> _pastasAquecidas = new(StringComparer.OrdinalIgnoreCase);

        internal static long TexturasArquivoAtual;

        static bool Prefix(string __0, R.tipoCarregamento __1, ref GameObject __result, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            Carga.InicioImport();
            Carga.ProfundidadeImport++;
            TexturasArquivoAtual = 0;
            if (!M.Ativo.Value) return true;
            if (M.PreAquecer.Value) Aquecer(__0);
            if (!M.LeituraRapida.Value) return true;

            Il2CppStructArray<byte> dados;
            try { dados = Il2CppSystem.IO.File.ReadAllBytes(__0); }
            catch { return true; } // o original lança o mesmo erro

            var go = new GameObject();
            go.name = __0;
            var br = new BinaryReader(new Il2CppSystem.IO.MemoryStream(dados));
            try
            {
                R.ReadMain(br, go, __1);
                __result = go;
            }
            catch (Exception e)
            {
                // Mesmo comportamento do original: o objeto parcial fica e quem chamou recebe nada.
                Log.Warning($"Falha lendo {Path.GetFileName(__0)}: {e.Message}");
                __result = null;
            }
            finally { br.Close(); }
            return false;
        }

        static void Postfix(string __0, long __state)
        {
            Carga.ProfundidadeImport--;
            long t = Stopwatch.GetTimestamp() - __state;
            long bytes = 0;
            try { bytes = new FileInfo(__0).Length; } catch { }
            Carga.FimImport(__0, t, TexturasArquivoAtual, bytes);
        }

        // Lê a pasta do .3ds em segundo plano (uma vez por carga) para os próximos arquivos e
        // texturas já estarem no cache de disco do Windows quando o jogo pedir.
        static void Aquecer(string arquivo)
        {
            string pasta;
            try { pasta = Path.GetDirectoryName(Path.GetFullPath(arquivo)); } catch { return; }
            if (string.IsNullOrEmpty(pasta)) return;
            lock (_pastasAquecidas) if (!_pastasAquecidas.Add(pasta)) return;
            var t = new Thread(() =>
            {
                try
                {
                    var buf = new byte[1 << 20];
                    long lido = 0, limite = 2L << 30;
                    var arquivos = Directory.GetFiles(pasta);
                    Array.Sort(arquivos, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in arquivos)
                    {
                        using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0) lido += n;
                        if (lido > limite) break;
                    }
                }
                catch { }
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "BetterLoading.Aquecer" };
            t.Start();
        }

        internal static void NovaCarga() { lock (_pastasAquecidas) _pastasAquecidas.Clear(); }
    }

    internal static unsafe class Bloco
    {
        // Elementos de um array IL2CPP começam depois de klass, monitor, bounds e max_length.
        internal static byte* Dados(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase arr) => (byte*)arr.Pointer + 4 * IntPtr.Size;
    }

    [HarmonyPatch(typeof(R), nameof(R.ReadVertices))]
    internal static class PatchReadVertices
    {
        static unsafe bool Prefix(Mesh __0, BinaryReader __1, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            if (!M.Ativo.Value || !M.LeituraRapida.Value || __0 == null || __1 == null) return true;
            int n = __1.ReadUInt16();
            var bytes = __1.ReadBytes(n * 12);
            int got = bytes.Length / 12;
            var arr = new Il2CppStructArray<Vector3>(n);
            float* src = (float*)Bloco.Dados(bytes);
            Vector3* dst = (Vector3*)Bloco.Dados(arr);
            if (R.Reorientate)
                for (int i = 0; i < got; i++, src += 3) dst[i] = new Vector3(src[0], src[2], src[1]);
            else
                Buffer.MemoryCopy(src, dst, (long)n * 12, (long)got * 12);
            __0.vertices = arr;
            return false;
        }

        static void Postfix(long __state) => Carga.TLeitores += Stopwatch.GetTimestamp() - __state;
    }

    [HarmonyPatch(typeof(R), nameof(R.ReadIndices))]
    internal static class PatchReadIndices
    {
        static unsafe bool Prefix(Mesh __0, BinaryReader __1, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            if (!M.Ativo.Value || !M.LeituraRapida.Value || __0 == null || __1 == null) return true;
            int n = __1.ReadUInt16();
            var bytes = __1.ReadBytes(n * 8);
            int got = bytes.Length / 8;
            var arr = new Il2CppStructArray<int>(n * 3);
            ushort* src = (ushort*)Bloco.Dados(bytes);
            int* dst = (int*)Bloco.Dados(arr);
            // Face no arquivo: a, b, c, flags. O original grava c, b, a (inverte a ordem de giro).
            for (int i = 0; i < got; i++, src += 4, dst += 3)
            {
                dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0];
            }
            __0.triangles = arr;
            return false;
        }

        static void Postfix(long __state) => Carga.TLeitores += Stopwatch.GetTimestamp() - __state;
    }

    [HarmonyPatch(typeof(R), nameof(R.ReadUVS))]
    internal static class PatchReadUVS
    {
        static unsafe bool Prefix(Mesh __0, BinaryReader __1, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            if (!M.Ativo.Value || !M.LeituraRapida.Value || __0 == null || __1 == null) return true;
            int n = __1.ReadUInt16();
            var bytes = __1.ReadBytes(n * 8);
            int got = bytes.Length / 8;
            var arr = new Il2CppStructArray<Vector2>(n);
            Buffer.MemoryCopy(Bloco.Dados(bytes), Bloco.Dados(arr), (long)n * 8, (long)got * 8);
            __0.uv = arr;
            return false;
        }

        static void Postfix(long __state) => Carga.TLeitores += Stopwatch.GetTimestamp() - __state;
    }
}
