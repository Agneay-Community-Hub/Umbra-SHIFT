using UnityEngine;

namespace UmbraShift
{
    /// <summary>All audio is synthesized at startup: no audio files needed.</summary>
    public class Synth : MonoBehaviour
    {
        const int SR = 22050;
        AudioSource sfx, music;
        AudioClip step, turn, land, shade, death, win, spark, blocked, click;
        public float MusicVolume = 0.35f;

        void Awake()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.volume = MusicVolume;

            step = Make("step", 0.07f, (t, i) => Sine(620 - 2000 * t, t) * Env(t, 0.002f, 0.07f) * 0.25f);
            land = Make("land", 0.16f, (t, i) => (Sine(95 * (1 - t * 2), t) * 0.8f + Noise() * 0.15f) * Env(t, 0.002f, 0.16f) * 0.5f);
            blocked = Make("blocked", 0.09f, (t, i) => Square(140, t) * Env(t, 0.002f, 0.09f) * 0.12f);
            click = Make("click", 0.05f, (t, i) => Sine(1200, t) * Env(t, 0.001f, 0.05f) * 0.2f);
            float lp = 0;
            turn = Make("turn", 0.42f, (t, i) =>
            {
                float cutoff = Mathf.Lerp(0.03f, 0.35f, Mathf.Sin(t / 0.42f * Mathf.PI));
                lp += (Noise() - lp) * cutoff;
                return (lp * 0.9f + Sine(180 + 260 * t, t) * 0.12f) * Env(t, 0.06f, 0.42f) * 0.6f;
            });
            shade = Make("shade", 0.5f, (t, i) => (Sine(73.4f, t) + 0.5f * Sine(110f, t) + 0.25f * Sine(146.8f * (1 + 0.01f * Mathf.Sin(t * 30)), t)) * Env(t, 0.03f, 0.5f) * 0.22f);
            death = Make("death", 0.6f, (t, i) => (Square(300 * Mathf.Pow(0.15f, t / 0.6f), t) * 0.5f + Noise() * 0.4f * (1 - t / 0.6f)) * Env(t, 0.001f, 0.6f) * 0.25f);
            spark = Make("spark", 0.6f, (t, i) => (Sine(1568, t) + 0.6f * Sine(2349, t) * Env(t - 0.08f, 0.001f, 0.5f)) * Env(t, 0.001f, 0.6f) * 0.18f);
            float[] arp = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
            win = Make("win", 1.6f, (t, i) =>
            {
                float s = 0;
                for (int n = 0; n < arp.Length; n++)
                {
                    float tt = t - n * 0.11f;
                    if (tt > 0) s += (Sine(arp[n], tt) + 0.3f * Sine(arp[n] * 2.01f, tt)) * Env(tt, 0.004f, 1.2f);
                }
                return s * 0.12f;
            });

            music.clip = BuildMusic();
            music.Play();
        }

        public void Play(string name, float vol = 1f, float pitch = 1f)
        {
            AudioClip c = name switch
            {
                "step" => step, "turn" => turn, "land" => land, "shade" => shade, "death" => death,
                "win" => win, "spark" => spark, "blocked" => blocked, "click" => click, _ => null
            };
            if (c == null) return;
            sfx.pitch = pitch;
            sfx.PlayOneShot(c, vol);
            if (recording) log.Add((Time.time - recStart, c, vol, pitch));
        }

        // ---------- offline mixdown (used by the trailer recorder) ----------
        bool recording;
        float recStart;
        readonly System.Collections.Generic.List<(float t, AudioClip c, float vol, float pitch)> log =
            new System.Collections.Generic.List<(float, AudioClip, float, float)>();

        public void BeginRecording() { recording = true; recStart = Time.time; log.Clear(); }

        /// <summary>Mix music + every logged sound effect into a 16-bit mono WAV of the given length.</summary>
        public void SaveRecording(string path, float seconds, float musicFadeOut = 3f)
        {
            recording = false;
            int n = Mathf.CeilToInt(seconds * SR);
            var mix = new float[n];
            var m = new float[music.clip.samples];
            music.clip.GetData(m, 0);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float env = Mathf.Clamp01(t / 1.5f) * Mathf.Clamp01((seconds - t) / musicFadeOut);
                mix[i] = m[i % m.Length] * MusicVolume * 1.6f * env;
            }
            foreach (var (t, c, vol, pitch) in log)
            {
                var d = new float[c.samples];
                c.GetData(d, 0);
                int s0 = Mathf.RoundToInt(t * SR);
                for (int j = 0; ; j++)
                {
                    float src = j * pitch;
                    int si = (int)src;
                    if (si + 1 >= d.Length || s0 + j >= n) break;
                    if (s0 + j < 0) continue;
                    float f = src - si;
                    mix[s0 + j] += (d[si] * (1 - f) + d[si + 1] * f) * vol;
                }
            }
            float peak = 0.0001f;
            foreach (var v in mix) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = Mathf.Min(1.6f, 0.92f / peak);
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++) w.Write((short)Mathf.Clamp(mix[i] * gain * 32767f, -32768, 32767));
            }
        }

        public void SetMusic(bool on) { music.mute = !on; }
        public bool MusicOn => !music.mute;

        // ---------- helpers ----------
        static System.Random rng = new System.Random(7);
        static float Noise() => (float)(rng.NextDouble() * 2 - 1);
        static float Sine(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t);
        static float Square(float f, float t) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t));
        static float Env(float t, float a, float len)
        {
            if (t < 0 || t > len) return 0;
            if (t < a) return t / a;
            float x = (t - a) / (len - a);
            return (1 - x) * (1 - x);
        }

        static AudioClip Make(string name, float len, System.Func<float, int, float> f)
        {
            int n = Mathf.CeilToInt(len * SR);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)SR, i), -1, 1);
            var c = AudioClip.Create(name, n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>A slow, dark ambient loop: 4 chords of detuned pads plus a sparse pluck arpeggio.</summary>
        static AudioClip BuildMusic()
        {
            float bar = 6f;
            float[][] chords =
            {
                new[] { 110.00f, 164.81f, 220.00f, 261.63f },  // Am
                new[] { 87.31f, 130.81f, 174.61f, 261.63f },   // F
                new[] { 98.00f, 146.83f, 196.00f, 246.94f },   // G
                new[] { 82.41f, 123.47f, 164.81f, 246.94f },   // Em
            };
            float len = bar * chords.Length;
            int n = Mathf.CeilToInt(len * SR);
            var d = new float[n];
            // pads
            for (int c = 0; c < chords.Length; c++)
            {
                int s0 = (int)(c * bar * SR);
                int span = (int)((bar + 1.5f) * SR);
                foreach (var f in chords[c])
                {
                    for (int i = 0; i < span; i++)
                    {
                        int idx = (s0 + i) % n;
                        float t = i / (float)SR;
                        float env = Mathf.Clamp01(t / 1.8f) * Mathf.Clamp01((bar + 1.5f - t) / 2.2f);
                        float v = Mathf.Sin(2 * Mathf.PI * f * t) + Mathf.Sin(2 * Mathf.PI * f * 1.004f * t) * 0.7f
                                  + Mathf.Sin(2 * Mathf.PI * f * 0.5f * t) * 0.3f;
                        d[idx] += v * env * 0.035f;
                    }
                }
            }
            // plucks
            var r = new System.Random(3);
            float stepLen = bar / 8f;
            for (int c = 0; c < chords.Length; c++)
            {
                for (int s = 0; s < 8; s++)
                {
                    if (r.NextDouble() < 0.35) continue;
                    float f = chords[c][r.Next(chords[c].Length)] * (r.NextDouble() < 0.5 ? 2 : 4);
                    int s0 = (int)((c * bar + s * stepLen) * SR);
                    int span = (int)(1.6f * SR);
                    for (int i = 0; i < span; i++)
                    {
                        int idx = (s0 + i) % n;
                        float t = i / (float)SR;
                        float v = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 3.2f);
                        d[idx] += v * 0.05f;
                    }
                }
            }
            // simple echo
            int delay = (int)(stepLen * 1.5f * SR);
            for (int i = 0; i < n; i++) d[i] += d[(i - delay + n) % n] * 0.3f;
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i], -1, 1);
            var clip = AudioClip.Create("music", n, 1, SR, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
