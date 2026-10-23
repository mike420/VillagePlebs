using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VillagePlebs
{
    internal static class Marks
    {
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Glow = new Color(0.45f, 0.85f, 0.75f);

        private class Mark
        {
            public GameObject Go;
            public Func<bool> Active;
            public string Text;
            public float Until, NextLine, Height;
            public bool Spot;
        }

        private static readonly List<Mark> All = new List<Mark>();

        public static void Put(Component target, Humanoid who, string doing, Func<bool> active)
        {
            if (target == null || who == null) return;
            GameObject go = target.gameObject;
            foreach (Mark old in All.Where(m => m.Go == go).ToList()) Clear(old);
            var mark = new Mark { Go = go, Active = active, Text = doing != null ? $"{Companion.NameOf(who)}: {doing}" : null, Until = Time.time + 900f, Height = HeightOf(go) };
            All.Add(mark);
        }

        public static void PutSpot(Vector3 at, Humanoid who, string doing, Func<bool> active)
        {
            if (who == null) return;
            foreach (Mark old in All.Where(m => m.Spot && m.Go != null && Vector3.Distance(m.Go.transform.position, at) < 1.5f).ToList()) Clear(old);
            var go = new GameObject("VillagePlebs_Spot");
            go.transform.position = at + Vector3.up * 0.3f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Glow;
            light.range = 3f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            All.Add(new Mark { Go = go, Active = active, Text = $"{Companion.NameOf(who)}: {doing}", Until = Time.time + 600f, Height = 1.2f, Spot = true });
        }

        public static void Tick()
        {
            if (All.Count == 0) return;
            float pulse = 0.25f + 0.15f * Mathf.Sin(Time.time * 4f);
            foreach (Mark m in All.ToList())
            {
                bool on = false;
                try { on = m.Go != null && Time.time < m.Until && m.Active(); } catch { }
                if (!on) { Clear(m); continue; }
                if (m.Spot) { Light l = m.Go.GetComponent<Light>(); if (l != null) l.intensity = 1.5f + 6f * (pulse - 0.1f); }
                else MaterialMan.instance?.SetValue(m.Go, Emission, Glow * pulse);
                if (m.Text != null && Time.time >= m.NextLine && Chat.instance != null)
                {
                    m.NextLine = Time.time + 5f;
                    Chat.instance.SetNpcText(m.Go, Vector3.up * m.Height, Pointing.Reach, 6f, "", Words.Show(m.Text), false);
                }
            }
        }

        private static void Clear(Mark m)
        {
            All.Remove(m);
            if (m.Go == null) return;
            Chat.instance?.ClearNpcText(m.Go);
            if (m.Spot) { UnityEngine.Object.Destroy(m.Go); return; }
            MaterialMan.instance?.ResetValue(m.Go, Emission);
        }

        public static void Forget() { foreach (Mark m in All.ToList()) Clear(m); }

        private static float HeightOf(GameObject go)
        {
            float top = 1.5f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) top = Mathf.Max(top, r.bounds.max.y - go.transform.position.y);
            return Mathf.Clamp(top + 0.4f, 1.2f, 3.2f);
        }
    }
}
