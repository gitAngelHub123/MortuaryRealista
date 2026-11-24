// © Angel — Idioma del menú del mod: el mismo del juego (español → español; cualquier otro → inglés)
using System;
using BepInEx.Configuration;
using UnityEngine;

namespace MortuaryRealista
{
    internal static class Idioma
    {
        internal static ConfigEntry<string> Elegido;
        static bool? actual;

        public static void Configurar(ConfigFile cfg)
        {
            Elegido = cfg.Bind("General", "Idioma", "auto", "Idioma del menú del mod: auto (el del juego), es o en");
        }

        static bool Detectar()
        {
            string v = (Elegido?.Value ?? "auto").Trim().ToLowerInvariant();
            if (v == "es") return true;
            if (v == "en") return false;
            try
            {
                var d = StaticData.data;
                if (d != null) return d.language == Language.Spanish;
            }
            catch { }
            return Application.systemLanguage == SystemLanguage.Spanish;
        }

        internal static bool Es { get { if (actual == null) actual = Detectar(); return actual.Value; } }

        // true si el idioma cambió desde la última vez (para rearmar el menú)
        internal static bool Cambio()
        {
            bool ahora = Detectar();
            if (actual == null) { actual = ahora; return false; }
            if (ahora == actual.Value) return false;
            actual = ahora;
            Plugin.Log.LogInfo("Idioma del menú: " + (ahora ? "español" : "inglés"));
            return true;
        }
    }
}
