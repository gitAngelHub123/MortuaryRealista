// © Angel — Más árboles afuera de la funeraria: copias de los abedules del propio juego (Birch2–Birch7) repartidas al azar
// sobre el pasto/tierra (malla pPlane2), con giro y tamaño distintos. Siempre en los mismos lugares (semilla fija) para que
// el lugar no cambie entre partidas. Solo se ponen donde hay espacio libre: nada de muros, rejas, puertas ni otros árboles cerca.
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortuaryRealista
{
    internal static class Arboles
    {
        internal static ConfigEntry<int> Cantidad;
        static readonly List<GameObject> puestos = new List<GameObject>();
        static int firmaHecha = int.MinValue, cantidadHecha = -1;
        static float proxima;

        public static void Configurar(ConfigFile cfg)
        {
            Cantidad = cfg.Bind("Materiales", "ArbolesExtra", 35, "Árboles extra afuera de la funeraria (copias de los abedules del juego; 0 = como el juego)");
        }

        public static void Tick(bool on)
        {
            if (Time.unscaledTime < proxima) return;
            proxima = Time.unscaledTime + 3f;
            int quiero = on ? Math.Max(0, Cantidad.Value) : 0;
            var esc = SceneManager.GetActiveScene();
            int firma = SceneManager.sceneCount * 7919 + esc.handle;
            puestos.RemoveAll(g => g == null);
            if (firma == firmaHecha && quiero == cantidadHecha) return;
            foreach (var g in puestos) UnityEngine.Object.Destroy(g);
            puestos.Clear();
            cantidadHecha = quiero;
            if (quiero == 0) { firmaHecha = firma; return; }
            try { if (Plantar(quiero)) firmaHecha = firma; }
            catch (Exception e) { Plugin.Log.LogWarning("Árboles: " + e.Message); firmaHecha = firma; }
        }

        static bool Plantar(int cuantos)
        {
            Renderer suelo = null;
            var modelos = new List<GameObject>();
            var existentes = new List<Vector3>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.scene.IsValid() || !t.gameObject.activeInHierarchy) continue;
                if (t.name == "pPlane2" && suelo == null) { var r = t.GetComponent<Renderer>(); if (r != null && r.sharedMaterial != null) suelo = r; }
                // raíz de cada árbol: "Birch3", "Birch6 (4)"... (la malla hija se llama igual que el modelo)
                if (t.name.StartsWith("Birch") && t.parent != null && !t.parent.name.StartsWith("Birch") && t.GetComponentInChildren<Renderer>() != null)
                {
                    existentes.Add(t.position);
                    string baseNom = t.name.Split(' ')[0];
                    if (baseNom != "Birch2" && !modelos.Exists(m => m.name.Split(' ')[0] == baseNom)) modelos.Add(t.gameObject);
                }
            }
            if (suelo == null || modelos.Count == 0) { cantidadHecha = -1; return false; }   // escena sin patio: se reintenta
            var b = suelo.bounds;
            // superficies planas encima del pasto (calle, vereda, estacionamiento, caminos): muchas no tienen colisión
            var pavimento = new List<Bounds>();
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (r == null || r == suelo || !r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy || !(r is MeshRenderer)) continue;
                var rb = r.bounds;
                if (rb.size.y < 0.8f && rb.size.x * rb.size.z > 3f && rb.max.y > b.center.y - 0.1f && rb.min.y < b.center.y + 0.6f) pavimento.Add(rb);
            }
            var rng = new System.Random(1944);
            int puestosOk = 0, intentos = 0;
            while (puestosOk < cuantos && intentos < cuantos * 200)
            {
                intentos++;
                var p = new Vector3((float)(b.min.x + rng.NextDouble() * b.size.x), b.max.y + 30f, (float)(b.min.z + rng.NextDouble() * b.size.z));
                // solo sobre la TIERRA (malla pPlane2): la calle y las veredas están casi a la misma altura, así que se miran
                // todos los choques del rayo y se descarta el punto si cualquier otra cosa está a la altura del suelo o encima
                var golpes = Physics.RaycastAll(p, Vector3.down, 80f);
                RaycastHit hit = default; bool hayTierra = false;
                foreach (var g in golpes) if (g.collider != null && (g.collider.name == suelo.name || g.collider.GetComponent<Renderer>() == suelo)) { hit = g; hayTierra = true; break; }
                if (!hayTierra) continue;
                bool tapada = false;
                foreach (var g in golpes)
                    if (g.collider != null && g.collider != hit.collider && !g.collider.isTrigger && g.point.y > hit.point.y - 0.25f) { tapada = true; break; }
                if (tapada) continue;
                if (Math.Abs(hit.point.y - b.center.y) > 1.5f) continue;
                bool sobrePavimento = false;
                foreach (var pb in pavimento)
                    if (hit.point.x > pb.min.x - 1f && hit.point.x < pb.max.x + 1f && hit.point.z > pb.min.z - 1f && hit.point.z < pb.max.z + 1f) { sobrePavimento = true; break; }
                if (sobrePavimento) continue;
                bool cerca = false;
                foreach (var e in existentes) if ((new Vector2(e.x - hit.point.x, e.z - hit.point.z)).sqrMagnitude < 4.5f * 4.5f) { cerca = true; break; }
                if (cerca) continue;
                // espacio libre: ningún muro, reja, puerta ni objeto a menos de 2,5 m (a la altura del tronco)
                bool ocupado = false;
                foreach (var col in Physics.OverlapCapsule(hit.point + Vector3.up * 0.6f, hit.point + Vector3.up * 3f, 2.5f))
                    if (col != null && col != hit.collider && !col.isTrigger) { ocupado = true; break; }
                if (ocupado) continue;
                var modelo = modelos[rng.Next(modelos.Count)];
                var copia = UnityEngine.Object.Instantiate(modelo, hit.point, Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f));
                copia.name = modelo.name.Split(' ')[0] + " (Mortuary Realista)";
                float s = 0.8f + (float)rng.NextDouble() * 0.45f;
                copia.transform.localScale = modelo.transform.lossyScale * s;
                puestos.Add(copia); existentes.Add(hit.point); puestosOk++;
            }
            Plugin.Log.LogInfo($"Árboles: {puestosOk} abedules nuevos en el patio ({modelos.Count} modelos, {pavimento.Count} zonas pavimentadas evitadas, {intentos} intentos)");
            return true;
        }
    }
}
