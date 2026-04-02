using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace ErenshorRU
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class ErenshorRUPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.erenshor.ru";
        public const string NAME = "Erenshor Russian Translation";
        public const string VERSION = "1.4.0";

        internal static ManualLogSource Log;
        internal static TranslationDB T;
        internal static string PluginDir;

        private void Awake()
        {
            try
            {
                Log = Logger;
                PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                string translationDir = Path.Combine(PluginDir, "Translation");
                T = new TranslationDB(translationDir);
                Log.LogInfo($"[RU] {T.ExactCount} exact + {T.SubstringCount} substr loaded");

                var harmony = new Harmony(GUID);

                PatchSetter(harmony, typeof(TMP_Text), "text",
                    typeof(TextPatches), nameof(TextPatches.TMP_TextPrefix));
                PatchSetter(harmony, typeof(Text), "text",
                    typeof(TextPatches), nameof(TextPatches.LegacyTextPrefix));

                try { harmony.PatchAll(typeof(ChatPatches)); Log.LogInfo("[RU] ChatLogLine patch OK"); }
                catch (Exception e) { Log.LogError($"[RU] ChatLogLine FAIL: {e.Message}"); }

                try { ChatInputPatches.Apply(harmony); Log.LogInfo("[RU] Chat input patches OK"); }
                catch (Exception e) { Log.LogError($"[RU] Chat input patches FAIL: {e.Message}"); }

                try
                {
                    var m = typeof(FontEngine).GetMethod("LoadFontFace",
                        new[] { typeof(Font), typeof(int) });
                    if (m != null)
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(
                            typeof(FontEnginePatch), nameof(FontEnginePatch.Prefix)));
                        Log.LogInfo("[RU] FontEngine patch OK");
                    }
                }
                catch (Exception e) { Log.LogError($"[RU] FontEngine FAIL: {e.Message}"); }

                SceneManager.sceneLoaded += OnSceneLoaded;

                var go = new GameObject("ErenshorRU_Bootstrap");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<BootstrapScanner>();

                Log.LogInfo("[RU] Ready (Harmony setter mode)");
            }
            catch (Exception e) { Logger.LogError($"[RU] FATAL: {e}"); }
        }

        private static void PatchSetter(Harmony harmony, Type type, string prop,
            Type patchClass, string methodName)
        {
            try
            {
                var setter = AccessTools.PropertySetter(type, prop);
                if (setter != null)
                {
                    harmony.Patch(setter, prefix: new HarmonyMethod(patchClass, methodName));
                    Log.LogInfo($"[RU] {type.Name}.{prop} setter patch OK");
                }
            }
            catch (Exception e) { Log.LogError($"[RU] {type.Name}.{prop} FAIL: {e.Message}"); }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BootstrapScanner.RunScanDelayed();
        }
    }

    public class BootstrapScanner : MonoBehaviour
    {
        private static BootstrapScanner _instance;
        private float _nextScan;
        private int _scansLeft = 5;
        private bool _dumpDone;

        private void Awake() { _instance = this; }

        private void Start()
        {
            FontManager.Init();
            _nextScan = Time.time + 0.5f;
        }

        private void Update()
        {
            if (_scansLeft <= 0) return;
            if (Time.time < _nextScan) return;
            _scansLeft--;
            _nextScan = Time.time + 2f;
            RunOneScan();

            if (_scansLeft <= 0 && !_dumpDone)
            {
                _dumpDone = true;
                DumpUntranslated();
            }
        }

        public static void RunScanDelayed()
        {
            if (_instance != null)
            {
                _instance._scansLeft = Mathf.Max(_instance._scansLeft, 3);
                _instance._nextScan = Time.time + 0.3f;
                _instance._dumpDone = false;
            }
        }

        private static void RunOneScan()
        {
            var db = ErenshorRUPlugin.T;
            if (db == null) return;
            int tmpHit = 0, txtHit = 0;

            var allTmp = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (int i = 0; i < allTmp.Length; i++)
            {
                var c = allTmp[i];
                if (c == null) continue;
                try
                {
                    string s = c.text;
                    if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                    if (TranslationDB.IsNumericOrShort(s)) continue;
                    string tr = db.Translate(s);
                    if (tr != s)
                    {
                        FontManager.PatchTMPFont(c.font);
                        AutoSizer.ApplyTMP(c);
                        TextPatches.SetWithoutTranslation(c, tr);
                        tmpHit++;
                    }
                    else
                    {
                        db.RecordUntranslated(s, c.gameObject);
                    }
                }
                catch { }
            }

            var allTxt = Resources.FindObjectsOfTypeAll<Text>();
            for (int i = 0; i < allTxt.Length; i++)
            {
                var c = allTxt[i];
                if (c == null) continue;
                try
                {
                    string s = c.text;
                    if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                    if (TranslationDB.IsNumericOrShort(s)) continue;
                    string tr = db.Translate(s);
                    if (tr != s)
                    {
                        FontManager.PatchLegacyText(c);
                        AutoSizer.ApplyLegacy(c);
                        TextPatches.SetWithoutTranslation(c, tr);
                        txtHit++;
                    }
                    else
                    {
                        db.RecordUntranslated(s, c.gameObject);
                    }
                }
                catch { }
            }

            ErenshorRUPlugin.Log.LogInfo(
                $"[RU] Scan: TMP={allTmp.Length} ({tmpHit} tr), Text={allTxt.Length} ({txtHit} tr)");
        }

        private static void DumpUntranslated()
        {
            var db = ErenshorRUPlugin.T;
            if (db == null) return;
            string path = Path.Combine(ErenshorRUPlugin.PluginDir, "untranslated_dump.txt");
            db.DumpUntranslated(path);
        }
    }

    public static class AutoSizer
    {
        private static readonly HashSet<int> _done = new HashSet<int>();

        public static void ApplyTMP(TMP_Text c)
        {
            int id = c.GetInstanceID();
            if (_done.Contains(id)) return;
            _done.Add(id);
            if (c.enableAutoSizing) return;
            float cur = c.fontSize;
            if (cur < 1f) return;
            c.fontSizeMax = cur;
            c.fontSizeMin = Mathf.Max(cur * 0.5f, 6f);
            c.enableAutoSizing = true;
        }

        public static void ApplyLegacy(Text c)
        {
            int id = c.GetInstanceID();
            if (_done.Contains(id)) return;
            _done.Add(id);
            if (c.resizeTextForBestFit) return;
            int cur = c.fontSize;
            if (cur < 1) return;
            c.resizeTextMaxSize = cur;
            c.resizeTextMinSize = (int)Mathf.Max(cur * 0.5f, 6f);
            c.resizeTextForBestFit = true;
        }
    }

    public static class FontManager
    {
        private static Font _legacyFont;
        private static TMP_FontAsset _fallbackSemibold;
        private static TMP_FontAsset _fallbackBold;
        private static readonly HashSet<int> _patchedFontIds = new HashSet<int>();
        private static bool _metricsAdjusted;
        private static bool _initialized;

        private static readonly string FontDir =
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public static bool IsReady => _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                _legacyFont = Font.CreateDynamicFontFromOSFont("Segoe UI Semibold", 14);
                ErenshorRUPlugin.Log.LogInfo("[RU] Legacy Segoe UI Semibold OK");
            }
            catch (Exception e) { ErenshorRUPlugin.Log.LogError($"[RU] Legacy font err: {e.Message}"); }

            try
            {
                _fallbackSemibold = CreateFallback("Segoe UI Semibold", "seguisb.ttf");
                _fallbackBold = CreateFallback("Segoe UI Bold", "segoeuib.ttf");
            }
            catch (Exception e) { ErenshorRUPlugin.Log.LogError($"[RU] TMP font err: {e}"); }
        }

        private static TMP_FontAsset CreateFallback(string osName, string ttfFile)
        {
            string path = Path.Combine(FontDir, ttfFile);
            if (!File.Exists(path))
            {
                path = Path.Combine(@"C:\Windows\Fonts", ttfFile);
                if (!File.Exists(path)) return null;
            }
            var osFont = Font.CreateDynamicFontFromOSFont(osName, 44);
            FontEnginePatch.FontPathMap[osFont.name] = path;
            ErenshorRUPlugin.Log.LogInfo($"[RU] Mapped '{osFont.name}' => {ttfFile}");
            var fa = TMP_FontAsset.CreateFontAsset(osFont);
            if (fa != null)
            {
                fa.name = osName + " SDF";
                ErenshorRUPlugin.Log.LogInfo($"[RU] Created TMP fallback: {fa.name}");
            }
            return fa;
        }

        private static void AdjustFallbackMetrics(TMP_FontAsset fallback, TMP_FontAsset primary)
        {
            if (fallback == null || primary == null) return;
            var pfi = primary.faceInfo;
            var ffi = fallback.faceInfo;
            ffi.ascentLine = pfi.ascentLine;
            ffi.descentLine = pfi.descentLine;
            ffi.baseline = pfi.baseline;
            ffi.lineHeight = pfi.lineHeight;
            fallback.faceInfo = ffi;
        }

        private static TMP_FontAsset PickFallback(TMP_FontAsset gameFont)
        {
            if (gameFont == null) return _fallbackSemibold;
            string n = gameFont.name.ToLowerInvariant();
            if (n.Contains("bold") || n.Contains("agency") || n.Contains("broken"))
                return _fallbackBold ?? _fallbackSemibold;
            return _fallbackSemibold;
        }

        public static void PatchTMPFont(TMP_FontAsset font)
        {
            if (font == null) return;
            int id = font.GetInstanceID();
            if (_patchedFontIds.Contains(id)) return;
            _patchedFontIds.Add(id);
            var fb = PickFallback(font);
            if (fb == null) return;
            if (!_metricsAdjusted)
            {
                _metricsAdjusted = true;
                AdjustFallbackMetrics(_fallbackSemibold, font);
                AdjustFallbackMetrics(_fallbackBold, font);
            }
            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            font.fallbackFontAssetTable.Add(fb);
            ErenshorRUPlugin.Log.LogInfo($"[RU] '{font.name}' => fallback '{fb.name}'");
        }

        public static void PatchLegacyText(Text text)
        {
            if (_legacyFont != null) text.font = _legacyFont;
            text.alignByGeometry = true;
        }
    }

    public static class TextPatches
    {
        private static bool _translating;

        public static void TMP_TextPrefix(TMP_Text __instance, ref string value)
        {
            if (_translating) return;
            if (!FontManager.IsReady) return;
            if (ErenshorRUPlugin.T == null || string.IsNullOrEmpty(value) || value.Length < 2) return;
            if (TranslationDB.IsNumericOrShort(value)) return;
            _translating = true;
            try
            {
                string tr = ErenshorRUPlugin.T.Translate(value);
                if (tr != value)
                {
                    FontManager.PatchTMPFont(__instance.font);
                    AutoSizer.ApplyTMP(__instance);
                    value = tr;
                }
            }
            finally { _translating = false; }
        }

        public static void LegacyTextPrefix(Text __instance, ref string value)
        {
            if (_translating) return;
            if (!FontManager.IsReady) return;
            if (ErenshorRUPlugin.T == null || string.IsNullOrEmpty(value) || value.Length < 2) return;
            if (TranslationDB.IsNumericOrShort(value)) return;
            _translating = true;
            try
            {
                string tr = ErenshorRUPlugin.T.Translate(value);
                if (tr != value)
                {
                    FontManager.PatchLegacyText(__instance);
                    AutoSizer.ApplyLegacy(__instance);
                    value = tr;
                }
            }
            finally { _translating = false; }
        }

        public static void SetWithoutTranslation(TMP_Text c, string value)
        {
            _translating = true;
            try { c.text = value; }
            finally { _translating = false; }
        }

        public static void SetWithoutTranslation(Text c, string value)
        {
            _translating = true;
            try { c.text = value; }
            finally { _translating = false; }
        }
    }

    public static class FontEnginePatch
    {
        internal static readonly Dictionary<string, string> FontPathMap
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool Prefix(Font font, int pointSize, ref FontEngineError __result)
        {
            if (font == null) return true;
            if (FontPathMap.TryGetValue(font.name, out string path))
            {
                __result = FontEngine.LoadFontFace(path, pointSize);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class ChatPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChatLogLine), MethodType.Constructor,
            new Type[] { typeof(string), typeof(ChatLogLine.LogType), typeof(string) })]
        static void ChatLogLine_Ctor(ref string _msg)
        {
            if (ErenshorRUPlugin.T == null || string.IsNullOrEmpty(_msg)) return;
            _msg = ErenshorRUPlugin.T.Translate(_msg);
        }
    }

    public static class ChatInputMapper
    {
        private static readonly List<KeyValuePair<Regex, string>> _map
            = new List<KeyValuePair<Regex, string>>();

        static ChatInputMapper()
        {
            // Greetings
            Add("привет(ик)?", "hello");
            Add("здравствуй(те)?", "hello");
            Add("здоров(о)?", "hello");
            Add("салют", "hello");
            Add("хай", "hi");
            Add("хей", "hey");
            Add("йо+", "yo");
            Add("доброе утро", "morning");
            Add("добрый вечер", "evening");
            Add("добрый день", "good day");
            Add("как дела", "how are you");
            Add("алоха", "aloha");

            // Affirmations
            Add("да\\b", "yes");
            Add("ок\\b", "ok");
            Add("хорошо", "ok");
            Add("ладно", "ok");
            Add("конечно", "sure");
            Add("давай", "lets go");
            Add("ага", "yeah");
            Add("угу", "yeah");
            Add("иду", "coming");
            Add("понял(а)?", "got it");
            Add("отлично", "sweet");

            // Declinations
            Add("нет\\b", "no");
            Add("не могу", "can't");
            Add("занят(а)?", "busy");
            Add("потом", "later");
            Add("не хочу", "don't wanna");
            Add("пас\\b", "pass");
            Add("неа", "nah");
            Add("не сейчас", "maybe later");
            Add("в другой раз", "next time");

            // Gratitude
            Add("спасибо", "thanks");
            Add("спс", "thx");
            Add("благодарю", "thank you");

            // Apologies
            Add("извини(те)?", "sorry");
            Add("прости(те)?", "sorry");
            Add("ой\\b", "oops");
            Add("мой косяк", "my bad");
            Add("моя ошибка", "my mistake");

            // LFG / Group
            Add("ищу группу", "LFG");
            Add("группа", "group");
            Add("помо(гите|щь|ги)", "help");
            Add("опыт", "exp");
            Add("играть", "play");
            Add("хоч(ешь|у) в группу", "want to group?");
            Add("пойд[её]м", "lets go");
            Add("давай вместе", "lets go");

            // Level up
            Add("левел( ап)?", "level up");
            Add("уровень", "level up");
            Add("динг", "ding");

            // Goodnight / Bye
            Add("спокойной ночи", "goodnight");
            Add("пока\\b", "bye");
            Add("ночи\\b", "night");
            Add("спать", "sleep");
            Add("до свидания", "bye");

            // Info requests
            Add("где взя(ть|л)", "where did you get");
            Add("что падает", "what drops");
            Add("квест", "quest");
            Add("где найти", "where can I find");
            Add("как получить", "how do i get");
            Add("где достать", "where do i get");
            Add("откуда", "where did you get");

            // WhatsUp
            Add("чем заним", "whatcha doing");
            Add("что делаешь", "whatcha doing");

            // Location
            Add("где ты", "where are you");
            Add("ты где", "where are you");

            // Invis
            Add("инвиз", "invis");
            Add("невидимость", "invis");

            // Guild
            Add("вступай в (мою )?гильдию", "join my guild");
            Add("хочу в гильдию", "join your guild");
            Add("инвайт в гильдию", "guild invite");
            Add("приглаш(ение)? в гильдию", "guild invite");

            // Slot / item info
            Add("где (ты )?(взял|получил|нашёл|нашел)", "where did you get");
            Add("что за шмот", "what is that");
            Add("что это за", "what is that");

            // Level check
            Add("какой (у тебя )?уровень", "what level are you");
            Add("какой (у тебя )?левел", "what level are you");
        }

        private static void Add(string ruPattern, string enKeyword)
        {
            _map.Add(new KeyValuePair<Regex, string>(
                new Regex(ruPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                enKeyword));
        }

        public static string EnrichWithEnglish(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            bool hasCyrillic = false;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] >= '\u0400' && input[i] <= '\u04FF')
                { hasCyrillic = true; break; }
            }
            if (!hasCyrillic) return input;

            var matched = new List<string>();
            for (int i = 0; i < _map.Count; i++)
            {
                if (_map[i].Key.IsMatch(input))
                    matched.Add(_map[i].Value);
            }
            if (matched.Count == 0) return input;

            return input + " " + string.Join(" ", matched.ToArray());
        }
    }

    public static class ChatInputPatches
    {
        public static void Apply(Harmony harmony)
        {
            var parseSay = AccessTools.Method(typeof(SimPlayerShoutParse),
                "ParseSay", new[] { typeof(string), typeof(string), typeof(bool) });
            if (parseSay != null)
                harmony.Patch(parseSay, prefix: new HarmonyMethod(
                    typeof(ChatInputPatches), nameof(ParseSayPrefix)));

            var parseShout = AccessTools.Method(typeof(SimPlayerShoutParse),
                "ParseShout", new[] { typeof(string), typeof(string), typeof(bool) });
            if (parseShout != null)
                harmony.Patch(parseShout, prefix: new HarmonyMethod(
                    typeof(ChatInputPatches), nameof(ParseShoutPrefix)));

            var simReceive = AccessTools.Method(typeof(SimPlayerMngr),
                "SimReceiveMsg", new[] { typeof(string), typeof(string) });
            if (simReceive != null)
                harmony.Patch(simReceive, prefix: new HarmonyMethod(
                    typeof(ChatInputPatches), nameof(SimReceiveMsgPrefix)));

            var simSay = AccessTools.Method(typeof(SimPlayerMngr),
                "SimRespondToSay", new[] { typeof(string), typeof(SimPlayerTracking) });
            if (simSay != null)
                harmony.Patch(simSay, prefix: new HarmonyMethod(
                    typeof(ChatInputPatches), nameof(SimRespondToSayPrefix)));

            var parseText = AccessTools.Method(typeof(NPCDialogManager),
                "ParseText", new[] { typeof(string) });
            if (parseText != null)
                harmony.Patch(parseText, prefix: new HarmonyMethod(
                    typeof(ChatInputPatches), nameof(ParseTextPrefix)));
        }

        static void ParseSayPrefix(ref string _shout, bool _isPlayer)
        {
            if (_isPlayer) _shout = ChatInputMapper.EnrichWithEnglish(_shout);
        }

        static void ParseShoutPrefix(ref string _shout, bool _isPlayer)
        {
            if (_isPlayer) _shout = ChatInputMapper.EnrichWithEnglish(_shout);
        }

        static void SimReceiveMsgPrefix(ref string incomingMsg)
        {
            incomingMsg = ChatInputMapper.EnrichWithEnglish(incomingMsg);
        }

        static void SimRespondToSayPrefix(ref string incomingMsg)
        {
            incomingMsg = ChatInputMapper.EnrichWithEnglish(incomingMsg);
        }

        static void ParseTextPrefix(ref string _incoming)
        {
            _incoming = ChatInputMapper.EnrichWithEnglish(_incoming);
        }
    }

    public class TranslationDB
    {
        private readonly Dictionary<string, string> _exact =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, string>> _substrings =
            new List<KeyValuePair<string, string>>();
        private readonly List<KeyValuePair<string, string>> _prefixes =
            new List<KeyValuePair<string, string>>();
        private readonly Dictionary<string, string> _cache =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _untranslated = new HashSet<string>();
        private readonly Dictionary<string, string> _untranslatedPaths = new Dictionary<string, string>();
        private const int MaxCacheSize = 8192;
        private const int MinSubstringKeyLength = 4;

        public int ExactCount => _exact.Count;
        public int SubstringCount => _substrings.Count;

        public TranslationDB(string dir)
        {
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); return; }
            foreach (string file in Directory.GetFiles(dir, "*.txt"))
                LoadFile(file);
            _substrings.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            _prefixes.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        private void LoadFile(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            bool substringMode = false;
            int skipped = 0;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                if (line == "[SUBSTRING]") { substringMode = true; continue; }
                if (line == "[EXACT]") { substringMode = false; continue; }
                int sep = FindSeparator(line);
                if (sep <= 0) continue;
                string en = line.Substring(0, sep).Replace(@"\n", "\n");
                string ru = line.Substring(sep + 1).Replace(@"\n", "\n");
                if (substringMode)
                {
                    if (en.Trim().Length < MinSubstringKeyLength) { skipped++; continue; }
                    _substrings.Add(new KeyValuePair<string, string>(en, ru));
                }
                else
                {
                    _exact[en] = ru;
                    if (en.Length > 50 && en.EndsWith("...") && !string.IsNullOrEmpty(ru))
                        _prefixes.Add(new KeyValuePair<string, string>(
                            en.Substring(0, en.Length - 3), ru));
                }
            }
            if (skipped > 0)
                ErenshorRUPlugin.Log?.LogWarning(
                    $"[RU] {Path.GetFileName(path)}: skipped {skipped} short substr keys");
        }

        private static int FindSeparator(string line)
        {
            int depth = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '<') depth++;
                else if (c == '>' && depth > 0) depth--;
                else if (c == '=' && depth == 0) return i;
            }
            return -1;
        }

        public string Translate(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 2) return input;
            if (IsNumericOrShort(input)) return input;
            if (_cache.TryGetValue(input, out string cached)) return cached;
            string result = TranslateCore(input);
            if (_cache.Count >= MaxCacheSize) _cache.Clear();
            _cache[input] = result;
            return result;
        }

        private string TranslateCore(string input)
        {
            if (_exact.TryGetValue(input, out string val)) return val;

            string trimmed = input.Trim();
            if (trimmed != input && _exact.TryGetValue(trimmed, out val)) return val;

            string noNL = input.TrimEnd('\n', ' ');
            if (noNL != input && _exact.TryGetValue(noNL, out val)) return val;

            string stripped = StripRichTags(input);
            if (stripped != input && _exact.TryGetValue(stripped, out val))
                return input.Replace(stripped, val);

            for (int i = 0; i < _prefixes.Count; i++)
            {
                if (input.StartsWith(_prefixes[i].Key, StringComparison.Ordinal))
                    return _prefixes[i].Value;
            }

            string result = input;
            bool anyMatch = false;
            for (int i = 0; i < _substrings.Count; i++)
            {
                string en = _substrings[i].Key;
                if (result.IndexOf(en, StringComparison.Ordinal) >= 0)
                {
                    result = result.Replace(en, _substrings[i].Value);
                    anyMatch = true;
                }
            }
            if (!anyMatch) return input;

            int latin = 0, cyrillic = 0;
            for (int i = 0; i < result.Length; i++)
            {
                char c = result[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin++;
                else if (c >= '\u0400' && c <= '\u04FF') cyrillic++;
            }
            if (latin > 0 && cyrillic > 0 && latin > cyrillic * 3) return input;
            return result;
        }

        public void RecordUntranslated(string text, GameObject go)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2) return;
            if (HasCyrillic(text)) return;
            if (IsNumericOrShort(text)) return;
            bool hasLatin = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) { hasLatin = true; break; }
            }
            if (!hasLatin) return;
            string key = text.Length > 2000 ? text.Substring(0, 2000) + "..." : text;
            key = key.Replace("\n", @"\n").Replace("\r", "");
            if (_untranslated.Add(key) && go != null)
            {
                try { _untranslatedPaths[key] = GetPath(go); } catch { }
            }
        }

        public void DumpUntranslated(string path)
        {
            if (_untranslated.Count == 0) return;
            var sorted = _untranslated.OrderBy(s => s).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"# Untranslated strings: {sorted.Count}");
            sb.AppendLine($"# Dumped: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            foreach (var s in sorted)
            {
                _untranslatedPaths.TryGetValue(s, out string objPath);
                sb.AppendLine($"# GameObject: {objPath ?? "?"}");
                sb.AppendLine($"{s}=");
                sb.AppendLine();
            }
            try
            {
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                ErenshorRUPlugin.Log.LogInfo($"[RU] Dumped {sorted.Count} untranslated => {path}");
            }
            catch (Exception e)
            {
                ErenshorRUPlugin.Log.LogError($"[RU] Dump failed: {e.Message}");
            }
        }

        private static string GetPath(GameObject go)
        {
            var parts = new List<string>();
            var t = go.transform;
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool HasCyrillic(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '\u0400' && s[i] <= '\u04FF') return true;
            return false;
        }

        public static bool IsNumericOrShort(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != ' ' && c != '.' && c != ',' && c != '-' && c != '+'
                    && c != '/' && c != '%' && c != ':' && c != '(' && c != ')'
                    && (c < '0' || c > '9'))
                    return false;
            }
            return true;
        }

        private static string StripRichTags(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
