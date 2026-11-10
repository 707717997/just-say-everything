using System;
using System.Speech.Synthesis;

namespace SayAllVoice
{
    /// <summary>说话回应。本机语音，没有网络也能用，按语音键时立刻闭嘴。</summary>
    public static class Speaker
    {
        private static SpeechSynthesizer _s;
        public static bool Enabled = true;
        public static string Last = "";

        private static SpeechSynthesizer Get()
        {
            if (_s != null) return _s;
            SpeechSynthesizer s = new SpeechSynthesizer();
            s.SetOutputToDefaultAudioDevice();
            foreach (InstalledVoice v in s.GetInstalledVoices())
            {
                if (v.VoiceInfo.Culture.Name == "zh-CN" && v.VoiceInfo.Name.IndexOf("Huihui", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    s.SelectVoice(v.VoiceInfo.Name);
                    break;
                }
            }
            s.Rate = 2;
            s.Volume = 100;
            _s = s;
            return s;
        }

        public static void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Last = text;
            if (!Enabled) return;
            try
            {
                SpeechSynthesizer s = Get();
                s.SpeakAsyncCancelAll();
                s.SpeakAsync(text);
            }
            catch (Exception ex) { Log.Warn("speak: " + ex.Message); }
        }

        public static void Stop()
        {
            try { if (_s != null) _s.SpeakAsyncCancelAll(); } catch { }
        }
    }
}
