using System;
using System.Collections;
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
using UnityEngine.Networking;
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
        public const string VERSION = "2.7.2";

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
                NPCDialogPatches.LoadKeywords(translationDir);

                var harmony = new Harmony(GUID);

                PatchSetter(harmony, typeof(TMP_Text), "text",
                    typeof(TextPatches), nameof(TextPatches.TMP_TextPrefix));
                PatchSetter(harmony, typeof(Text), "text",
                    typeof(TextPatches), nameof(TextPatches.LegacyTextPrefix));

                try { harmony.PatchAll(typeof(ChatPatches)); Log.LogInfo("[RU] ChatLogLine patch OK"); }
                catch (Exception e) { Log.LogError($"[RU] ChatLogLine FAIL: {e.Message}"); }

                try { harmony.PatchAll(typeof(NPCDialogPatches)); Log.LogInfo("[RU] NPCDialog patch OK"); }
                catch (Exception e) { Log.LogError($"[RU] NPCDialog FAIL: {e.Message}"); }

                try { harmony.PatchAll(typeof(QuestLogPatches)); Log.LogInfo("[RU] QuestLog patch OK"); }
                catch (Exception e) { Log.LogError($"[RU] QuestLog FAIL: {e.Message}"); }

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
                go.AddComponent<UpdateChecker>();

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
                try { FontManager.ReplaceTMPFont(c); } catch { }
                try
                {
                    string s = c.text;
                    if (string.IsNullOrEmpty(s) || s.Length < 2) continue;
                    if (TranslationDB.IsNumericOrShort(s)) continue;
                    string tr = db.Translate(s);
                    if (tr != s)
                    {
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
        private static readonly Dictionary<int, float> _origSizes = new Dictionary<int, float>();

        public static void ApplyTMP(TMP_Text c)
        {
            if (!c.enableAutoSizing) return;
            int id = c.GetInstanceID();
            if (!_origSizes.ContainsKey(id))
                _origSizes[id] = c.fontSizeMax;
            float orig = _origSizes[id];
            if (orig > 0 && c.fontSizeMax > orig)
                c.fontSizeMax = orig;
        }

        public static void ApplyLegacy(Text c)
        {
            if (!c.resizeTextForBestFit) return;
            int id = c.GetInstanceID();
            if (!_origSizes.ContainsKey(id))
                _origSizes[id] = c.resizeTextMaxSize;
            float orig = _origSizes[id];
            if (orig > 0 && c.resizeTextMaxSize > (int)orig)
                c.resizeTextMaxSize = (int)orig;
        }
    }

    public static class FontManager
    {
        private static readonly HashSet<int> _patchedFontIds = new HashSet<int>();
        private static bool _initialized;

        private static readonly Dictionary<string, TMP_FontAsset> _tmpReplace =
            new Dictionary<string, TMP_FontAsset>();
        private static readonly Dictionary<string, Font> _legacyReplace =
            new Dictionary<string, Font>();
        private static readonly Dictionary<string, string> _fontPaths =
            new Dictionary<string, string>();
        private static readonly Dictionary<string, TMP_FontAsset> _adaptedCache =
            new Dictionary<string, TMP_FontAsset>();
        private static readonly HashSet<int> _ourFontIds = new HashSet<int>();
        private static readonly HashSet<int> _ourLegacyFontIds = new HashSet<int>();
        private static readonly Dictionary<int, TMP_FontAsset> _fontReplacementMap =
            new Dictionary<int, TMP_FontAsset>();

        private const string CyrillicChars =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";

        public static bool IsReady => _initialized;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            string modFontsDir = Path.Combine(ErenshorRUPlugin.PluginDir, "Fonts");

            try
            {
                LoadReplacement(modFontsDir, "Montserrat-Regular", "Montserrat-Regular.ttf");
                LoadReplacement(modFontsDir, "Montserrat-Medium", "Montserrat-Medium.ttf");
                LoadReplacement(modFontsDir, "Montserrat-SemiBold", "Montserrat-SemiBold.ttf");
                LoadReplacement(modFontsDir, "Montserrat-Bold", "Montserrat-Bold.ttf");
                LoadReplacement(modFontsDir, "Montserrat-ExtraBold", "Montserrat-ExtraBold.ttf");
                LoadReplacement(modFontsDir, "Montserrat-Black", "Montserrat-Black.ttf");
                LoadReplacement(modFontsDir, "Montserrat-Italic", "Montserrat-Italic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-MediumItalic", "Montserrat-MediumItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-SemiBoldItalic", "Montserrat-SemiBoldItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-BoldItalic", "Montserrat-BoldItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-ExtraBoldItalic", "Montserrat-ExtraBoldItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-BlackItalic", "Montserrat-BlackItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-Light", "Montserrat-Light.ttf");
                LoadReplacement(modFontsDir, "Montserrat-LightItalic", "Montserrat-LightItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-ExtraLight", "Montserrat-ExtraLight.ttf");
                LoadReplacement(modFontsDir, "Montserrat-ExtraLightItalic", "Montserrat-ExtraLightItalic.ttf");
                LoadReplacement(modFontsDir, "Montserrat-Thin", "Montserrat-Thin.ttf");
                LoadReplacement(modFontsDir, "Montserrat-ThinItalic", "Montserrat-ThinItalic.ttf");
                LoadReplacement(modFontsDir, "PxPlus-VGA9", "PxPlus IBM VGA9.ttf");
            }
            catch (Exception e) { ErenshorRUPlugin.Log.LogError($"[RU] Replacement font err: {e}"); }
        }

        private static void LoadReplacement(string dir, string key, string filename)
        {
            string path = Path.Combine(dir, filename);
            if (!File.Exists(path))
            {
                ErenshorRUPlugin.Log.LogWarning($"[RU] Font not found: {path}");
                return;
            }
            _fontPaths[key] = path;
            FontEnginePatch.FontPathMap[key] = path;

            var osFont = Font.CreateDynamicFontFromOSFont(key, 44);
            FontEnginePatch.FontPathMap[osFont.name] = path;
            var fa = TMP_FontAsset.CreateFontAsset(osFont);
            if (fa != null)
            {
                fa.name = key + " SDF";
                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;

                try
                {
                    fa.TryAddCharacters(CyrillicChars, out _);
                }
                catch (Exception ex)
                {
                    ErenshorRUPlugin.Log.LogWarning($"[RU] Cyrillic pre-populate for {key}: {ex.Message}");
                }

                var fi = fa.faceInfo;
                fi.ascentLine *= 0.75f;
                fi.lineHeight = fi.ascentLine - fi.descentLine;
                if (IsSemiBoldOrHeavier(key))
                    fi.scale = 0.9f;
                fa.faceInfo = fi;

                if (!IsLightFont(key))
                {
                    fa.material.SetFloat("_OutlineWidth", 0.25f);
                    fa.material.SetColor("_OutlineColor", new Color(0, 0, 0, 1));
                    fa.material.EnableKeyword("OUTLINE_ON");
                }
                _tmpReplace[key] = fa;
                _ourFontIds.Add(fa.GetInstanceID());
                ErenshorRUPlugin.Log.LogInfo(
                    $"[RU] Loaded: {fa.name} (pt={fi.pointSize}, asc={fi.ascentLine:F1}, desc={fi.descentLine:F1}, scale={fi.scale:F2})");
            }

            var legFont = Font.CreateDynamicFontFromOSFont(key, 14);
            FontEnginePatch.FontPathMap[legFont.name] = path;
            _legacyReplace[key] = legFont;
            _ourLegacyFontIds.Add(legFont.GetInstanceID());
        }

        private static TMP_FontAsset GetAdaptedFallback(string key, int targetPt)
        {
            string cacheKey = key + "@" + targetPt;
            if (_adaptedCache.TryGetValue(cacheKey, out var cached))
                return cached;

            if (!_fontPaths.TryGetValue(key, out string path))
                return DGet(_tmpReplace, key);

            var osFont = Font.CreateDynamicFontFromOSFont(key + "_" + targetPt, 44);
            FontEnginePatch.FontPathMap[osFont.name] = path;
            var fa = TMP_FontAsset.CreateFontAsset(osFont);
            if (fa == null)
                return DGet(_tmpReplace, key);

            fa.name = key + " SDF [" + targetPt + "]";
            fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            try { fa.TryAddCharacters(CyrillicChars, out _); }
            catch { }

            var fi = fa.faceInfo;
            fi.pointSize = targetPt;
            float scale = 0.85f;
            if (key.Contains("Bold") || key.Contains("ExtraBold"))
                scale *= 0.9f;
            fi.scale = scale;
            fa.faceInfo = fi;

            _adaptedCache[cacheKey] = fa;
            return fa;
        }

        private static readonly HashSet<int> _outlinedFbMats = new HashSet<int>();
        private static readonly HashSet<int> _sizeAdjusted = new HashSet<int>();

        private static void EnableOutlineOnFallbackMaterial(Material mat)
        {
            if (mat == null) return;
            int id = mat.GetInstanceID();
            if (_outlinedFbMats.Contains(id)) return;
            _outlinedFbMats.Add(id);
            try
            {
                mat.SetFloat("_OutlineWidth", 0.25f);
                mat.SetColor("_OutlineColor", new Color(0, 0, 0, 1));
                mat.EnableKeyword("OUTLINE_ON");
            }
            catch { }
        }

        private static bool IsLightFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;
            string n = fontName.ToLowerInvariant();
            if (n.Contains("light") || n.Contains("thin")) return true;
            if (n.Contains("italic") && !n.Contains("bold") && !n.Contains("semi") &&
                !n.Contains("medium") && !n.Contains("black"))
                return true;
            return false;
        }

        private static bool IsSemiBoldOrHeavier(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;
            string n = fontName.ToLowerInvariant();
            return n.Contains("semibold") || n.Contains("bold") ||
                   n.Contains("extrabold") || n.Contains("black");
        }

        public static void ReplaceTMPFont(TMP_Text comp)
        {
            if (comp == null || comp.font == null) return;

            var origFont = comp.font;
            int origId = origFont.GetInstanceID();

            if (_ourFontIds.Contains(origId))
            {
                ApplyOutlineToComponent(comp);
                return;
            }

            if (!_fontReplacementMap.TryGetValue(origId, out var replacement))
            {
                replacement = FindTMPReplacement(origFont.name);
                _fontReplacementMap[origId] = replacement;

                if (replacement != null)
                    ErenshorRUPlugin.Log.LogInfo(
                        $"[RU] Font map: '{origFont.name}' => '{replacement.name}'");
                else
                    PatchTMPFont(origFont);
            }

            if (replacement != null)
            {
                float origLS = comp.lineSpacing;
                var origFI = origFont.faceInfo;
                var newFI = replacement.faceInfo;

                try
                {
                    Traverse.Create(comp).Field("m_fontMaterial").SetValue(null);
                }
                catch { }

                comp.font = replacement;
                comp.fontSharedMaterial = replacement.material;

                if (origFI.pointSize > 0 && newFI.pointSize > 0 &&
                    newFI.lineHeight > 0.1f)
                {
                    float origPerEm = origFI.lineHeight / (float)origFI.pointSize;
                    float newPerEm  = newFI.lineHeight  / (float)newFI.pointSize;
                    if (newPerEm > 0.01f)
                    {
                        float ratio = origPerEm / newPerEm * 1.08f;
                        comp.lineSpacing =
                            (ratio * (1f + origLS / 100f) - 1f) * 100f;
                    }
                }

                try { comp.ForceMeshUpdate(true); } catch { }
            }

            ApplyOutlineToComponent(comp);
        }

        public static void ApplyOutlineToComponent(TMP_Text comp)
        {
            if (comp == null) return;
            try
            {
                var font = comp.font;
                if (font == null) return;

                int compId = comp.GetInstanceID();
                if (!_sizeAdjusted.Contains(compId) && comp.fontSize >= 36f)
                {
                    _sizeAdjusted.Add(compId);
                    comp.fontSize *= 0.75f;
                }

                if (IsLightFont(font.name))
                {
                    if (comp.outlineWidth > 0f)
                    {
                        comp.outlineWidth = 0f;
                        comp.outlineColor = new Color32(0, 0, 0, 0);
                    }
                    return;
                }

                if (comp.outlineWidth < 0.2f)
                {
                    comp.outlineWidth = 0.25f;
                    comp.outlineColor = new Color32(0, 0, 0, 255);
                }

                if (font.fallbackFontAssetTable != null)
                {
                    for (int i = 0; i < font.fallbackFontAssetTable.Count; i++)
                    {
                        var fb = font.fallbackFontAssetTable[i];
                        if (fb != null && fb.material != null)
                            EnableOutlineOnFallbackMaterial(fb.material);
                    }
                }
            }
            catch { }
        }

        private static T DGet<T>(Dictionary<string, T> d, string k) where T : class
        {
            return d.TryGetValue(k, out T v) ? v : null;
        }

        private static T MatchWeightToMontserrat<T>(string n, Dictionary<string, T> dict) where T : class
        {
            if (n.Contains("extrabold") && n.Contains("italic"))
                return DGet(dict, "Montserrat-BoldItalic") ?? DGet(dict, "Montserrat-Bold");
            if (n.Contains("extrabold"))
                return DGet(dict, "Montserrat-Bold");
            if (n.Contains("semibold") && n.Contains("italic"))
                return DGet(dict, "Montserrat-SemiBoldItalic");
            if (n.Contains("semibold"))
                return DGet(dict, "Montserrat-SemiBold");
            if (n.Contains("bold") && n.Contains("italic"))
                return DGet(dict, "Montserrat-BoldItalic");
            if (n.Contains("bold"))
                return DGet(dict, "Montserrat-Bold");
            if (n.Contains("medium") && n.Contains("italic"))
                return DGet(dict, "Montserrat-MediumItalic");
            if (n.Contains("medium"))
                return DGet(dict, "Montserrat-Medium");
            if (n.Contains("extralight") && n.Contains("italic"))
                return DGet(dict, "Montserrat-ExtraLightItalic");
            if (n.Contains("extralight"))
                return DGet(dict, "Montserrat-ExtraLight");
            if (n.Contains("light") && n.Contains("italic"))
                return DGet(dict, "Montserrat-LightItalic");
            if (n.Contains("light"))
                return DGet(dict, "Montserrat-Light");
            if (n.Contains("thin") && n.Contains("italic"))
                return DGet(dict, "Montserrat-ThinItalic");
            if (n.Contains("thin"))
                return DGet(dict, "Montserrat-Thin");
            if (n.Contains("black") && n.Contains("italic"))
                return DGet(dict, "Montserrat-BlackItalic");
            if (n.Contains("black"))
                return DGet(dict, "Montserrat-Black");
            if (n.Contains("italic"))
                return DGet(dict, "Montserrat-Italic");
            return DGet(dict, "Montserrat-Regular");
        }

        private static TMP_FontAsset FindTMPReplacement(string fontName)
        {
            string n = fontName.ToLowerInvariant();

            if (n.Contains("perfectdos"))
                return DGet(_tmpReplace, "PxPlus-VGA9");

            if (n.Contains("awesome") || n.Contains("icon") || n.Contains("symbol") ||
                n.Contains("libertinus") || n.Contains("math") || n.Contains("glyph") ||
                n.Contains("emoji"))
                return null;

            return MatchWeightToMontserrat(n, _tmpReplace);
        }

        private static Font FindLegacyReplacement(string fontName)
        {
            string n = fontName.ToLowerInvariant();

            if (n.Contains("perfectdos"))
                return DGet(_legacyReplace, "PxPlus-VGA9");

            if (n.Contains("awesome") || n.Contains("icon") || n.Contains("symbol") ||
                n.Contains("libertinus") || n.Contains("math") || n.Contains("glyph") ||
                n.Contains("emoji"))
                return null;

            return MatchWeightToMontserrat(n, _legacyReplace);
        }

        private static TMP_FontAsset PickBestFallback(TMP_FontAsset gameFont)
        {
            var direct = FindTMPReplacement(gameFont != null ? gameFont.name : "");
            if (direct != null) return direct;
            if (gameFont == null)
                return DGet(_tmpReplace, "Montserrat-Regular");
            string n = gameFont.name.ToLowerInvariant();
            if (n.Contains("bold"))
                return DGet(_tmpReplace, "Montserrat-Bold")
                    ?? DGet(_tmpReplace, "Montserrat-SemiBold")
                    ?? DGet(_tmpReplace, "Montserrat-Regular");
            if (n.Contains("semibold") || n.Contains("medium") || n.Contains("demi"))
                return DGet(_tmpReplace, "Montserrat-SemiBold")
                    ?? DGet(_tmpReplace, "Montserrat-Medium")
                    ?? DGet(_tmpReplace, "Montserrat-Regular");
            return DGet(_tmpReplace, "Montserrat-Regular");
        }

        public static void PatchTMPFont(TMP_FontAsset font)
        {
            if (font == null) return;
            int id = font.GetInstanceID();
            if (_patchedFontIds.Contains(id)) return;
            _patchedFontIds.Add(id);

            var fb = PickBestFallback(font);
            if (fb == null) return;

            string fbKey = null;
            foreach (var kv in _tmpReplace)
            {
                if (kv.Value == fb) { fbKey = kv.Key; break; }
            }

            int primaryPt = font.faceInfo.pointSize;
            int fbPt = fb.faceInfo.pointSize;
            TMP_FontAsset toAdd;

            if (fbKey != null && primaryPt != fbPt)
            {
                toAdd = GetAdaptedFallback(fbKey, primaryPt);
            }
            else
            {
                toAdd = fb;
            }

            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(toAdd))
                font.fallbackFontAssetTable.Add(toAdd);

            ErenshorRUPlugin.Log.LogInfo(
                $"[RU] '{font.name}' (pt={primaryPt}) => fallback '{toAdd.name}' (pt={toAdd.faceInfo.pointSize}, s={toAdd.faceInfo.scale:F2})");
        }

        public static void PatchLegacyText(Text text)
        {
            if (text.font != null && _ourLegacyFontIds.Contains(text.font.GetInstanceID()))
                return;
            string origName = text.font != null ? text.font.name : "";
            var replacement = FindLegacyReplacement(origName);
            if (replacement != null)
                text.font = replacement;
            else
            {
                var fallback = DGet(_legacyReplace, "Montserrat-Regular");
                if (fallback != null)
                    text.font = fallback;
            }
        }
    }

    public static class TextPatches
    {
        private static bool _translating;

        public static void TMP_TextPrefix(TMP_Text __instance, ref string value)
        {
            if (_translating) return;
            if (!FontManager.IsReady) return;
            if (ErenshorRUPlugin.T == null) return;

            FontManager.ReplaceTMPFont(__instance);

            if (string.IsNullOrEmpty(value) || value.Length < 2) return;
            if (TranslationDB.IsNumericOrShort(value)) return;
            _translating = true;
            try
            {
                string tr = ErenshorRUPlugin.T.Translate(value);
                if (tr != value)
                {
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
        private static readonly string[] EnSeparators =
            { " shouts: ", " says: ", " tells the guild: ", " tells the group: ", " tells you: " };
        private static readonly string[] RuSeparators =
            { " кричит: ", " говорит: ", " говорит гильдии: ", " говорит группе: " };

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChatLogLine), MethodType.Constructor,
            new Type[] { typeof(string), typeof(ChatLogLine.LogType), typeof(string) })]
        static void ChatLogLine_Ctor(ref string _msg)
        {
            if (ErenshorRUPlugin.T == null || string.IsNullOrEmpty(_msg)) return;

            if (TrySplitAndTranslate(ref _msg, EnSeparators, true))
                return;

            if (TrySplitAndTranslate(ref _msg, RuSeparators, false))
                return;

            string full = ErenshorRUPlugin.T.Translate(_msg);
            if (full != _msg) _msg = full;
        }

        private static bool TrySplitAndTranslate(ref string msg, string[] seps, bool translateSep)
        {
            for (int i = 0; i < seps.Length; i++)
            {
                int idx = msg.IndexOf(seps[i], StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                string name = msg.Substring(0, idx);
                string sep = seps[i];
                string content = msg.Substring(idx + sep.Length);

                string trSep = sep;
                if (translateSep)
                {
                    string trFull = ErenshorRUPlugin.T.Translate(sep);
                    if (trFull != sep)
                        trSep = trFull;
                }

                string trContent = ErenshorRUPlugin.T.Translate(content);
                if (trContent == content)
                {
                    string lower = ToMixedCase(content);
                    if (lower != content)
                    {
                        string trLower = ErenshorRUPlugin.T.Translate(lower);
                        if (trLower != lower)
                            trContent = trLower;
                    }
                }
                if (trContent == content)
                    trContent = ErenshorRUPlugin.T.TranslateDirect(content);

                if (trContent == content)
                    trContent = TranslateSentences(content);

                msg = name + trSep + trContent;
                return true;
            }
            return false;
        }

        private static string TranslateSentences(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 6) return content;

            var parts = new List<string>();
            int start = 0;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if ((c == '.' || c == '!' || c == '?') && i + 1 < content.Length && content[i + 1] == ' ')
                {
                    parts.Add(content.Substring(start, i - start + 1));
                    start = i + 2;
                }
            }
            if (start < content.Length)
                parts.Add(content.Substring(start));

            if (parts.Count < 2) return content;

            bool anyTranslated = false;
            for (int i = 0; i < parts.Count; i++)
            {
                string p = parts[i].Trim();
                if (p.Length < 4) continue;
                string tr = ErenshorRUPlugin.T.TranslateDirect(p);
                if (tr != p) { parts[i] = tr; anyTranslated = true; continue; }
                string lower = ToMixedCase(p);
                if (lower != p)
                {
                    tr = ErenshorRUPlugin.T.TranslateDirect(lower);
                    if (tr != lower) { parts[i] = tr; anyTranslated = true; continue; }
                }
                tr = ErenshorRUPlugin.T.Translate(p);
                if (tr != p) { parts[i] = tr; anyTranslated = true; }
            }

            return anyTranslated ? string.Join(" ", parts) : content;
        }

        private static string ToMixedCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            bool allUpper = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 'a' && c <= 'z') { allUpper = false; break; }
            }
            if (!allUpper) return s;

            var sb = new StringBuilder(s.Length);
            bool nextUpper = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?')
                {
                    sb.Append(c);
                    nextUpper = true;
                }
                else if (c == ' ' || c == ',')
                {
                    sb.Append(c);
                }
                else if (nextUpper)
                {
                    sb.Append(char.ToUpper(c));
                    nextUpper = false;
                }
                else
                {
                    sb.Append(char.ToLower(c));
                }
            }
            return sb.ToString();
        }
    }

    [HarmonyPatch]
    public static class NPCDialogPatches
    {
        internal static readonly Dictionary<string, string> _reverseKeywords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _keywordStems =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _lastOriginalDialog;

        public static void LoadKeywords(string dir)
        {
            string path = Path.Combine(dir, "npc_keywords.txt");
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int sep = line.IndexOf('=');
                if (sep <= 0) continue;
                string en = line.Substring(0, sep);
                string ru = line.Substring(sep + 1);
                if (!string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(ru))
                    _keywordStems[en] = ru;
            }
            ErenshorRUPlugin.Log?.LogInfo($"[RU] Loaded {_keywordStems.Count} keyword stems");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NPCDialog), "GetDialog")]
        static void GetDialog_Post(ref string __result)
        {
            if (ErenshorRUPlugin.T == null || string.IsNullOrEmpty(__result)) return;
            _lastOriginalDialog = __result;
            string tr = ErenshorRUPlugin.T.Translate(__result);
            if (tr != __result) __result = tr;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NPCDialogManager), "GenericHail")]
        static void GenericHail_Post(NPCDialogManager __instance)
        {
            WrapTranslatedKeywords(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NPCDialogManager), "ParseText")]
        static void ParseText_Post(NPCDialogManager __instance)
        {
            WrapTranslatedKeywords(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NPCDialogManager), "SearchForKeyword")]
        static bool SearchForKeyword_Pre(NPCDialogManager __instance, string _word, ref bool __result)
        {
            if (!_reverseKeywords.TryGetValue(_word, out string englishKw))
                return true;

            var dialogs = __instance.MyDialogOptions;
            if (dialogs != null)
            {
                for (int i = 0; i < dialogs.Length; i++)
                {
                    if (dialogs[i].KeywordToActivate.Contains(englishKw))
                    {
                        __result = true;
                        return false;
                    }
                }
            }
            return true;
        }

        internal static bool TryReverseKeyword(string word, out string englishKw)
        {
            return _reverseKeywords.TryGetValue(word, out englishKw);
        }

        private static void WrapTranslatedKeywords(NPCDialogManager __instance)
        {
            if (ErenshorRUPlugin.T == null) return;
            var t = Traverse.Create(__instance);
            string returnStr = t.Field("ReturnString").GetValue<string>();
            if (string.IsNullOrEmpty(returnStr)) return;

            var keywords = t.Field("Keywords").GetValue<List<string>>();
            if (keywords == null || keywords.Count == 0) return;

            bool changed = false;
            for (int k = 0; k < keywords.Count; k++)
            {
                string keyword = keywords[k];

                if (returnStr.Contains("<color=#16EC00>[" + keyword + "]</color>"))
                    continue;

                if (returnStr.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                {
                    returnStr = returnStr.Replace(keyword,
                        "<color=#16EC00>[" + keyword + "]</color>");
                    changed = true;
                    continue;
                }

                string ruWord = FindTranslatedKeyword(returnStr, keyword);
                if (ruWord != null)
                {
                    int idx = returnStr.IndexOf(ruWord, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        returnStr = returnStr.Substring(0, idx)
                            + "<color=#16EC00>[" + ruWord + "]</color>"
                            + returnStr.Substring(idx + ruWord.Length);
                        _reverseKeywords[ruWord] = keyword;
                        changed = true;
                    }
                }
            }

            if (changed)
                t.Field("ReturnString").SetValue(returnStr);
        }

        private static string FindTranslatedKeyword(string translatedText, string keyword)
        {
            if (_keywordStems.TryGetValue(keyword, out string stem) ||
                _keywordStems.TryGetValue(keyword.ToLower(), out stem))
            {
                string found = FindStemInText(translatedText, stem);
                if (found != null) return found;
            }

            string tr = ErenshorRUPlugin.T.TranslateDirect(keyword);
            if (tr != keyword && translatedText.Contains(tr))
                return tr;

            string lower = keyword.ToLower();
            if (lower != keyword)
            {
                tr = ErenshorRUPlugin.T.TranslateDirect(lower);
                if (tr != lower && translatedText.Contains(tr))
                    return tr;
            }

            if (_lastOriginalDialog != null &&
                _lastOriginalDialog.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string found = FindByPosition(_lastOriginalDialog, translatedText, keyword);
                if (found != null) return found;
            }

            return null;
        }

        private static string FindStemInText(string text, string stem)
        {
            if (string.IsNullOrEmpty(stem) || stem.Length < 2) return null;
            int idx = text.IndexOf(stem, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int start = idx;
            while (start > 0 && !IsWordBoundary(text[start - 1]))
                start--;

            int end = idx + stem.Length;
            while (end < text.Length && !IsWordBoundary(text[end]))
                end++;

            if (end <= start) return null;
            string word = text.Substring(start, end - start);
            return word.Length >= 2 ? word : null;
        }

        private static string FindByPosition(string original, string translated, string keyword)
        {
            int kwIdx = original.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (kwIdx < 0) return null;

            float relPos = (float)(kwIdx + keyword.Length / 2) / original.Length;
            int approxPos = (int)(relPos * translated.Length);
            approxPos = Math.Max(0, Math.Min(approxPos, translated.Length - 1));

            int start = approxPos;
            while (start > 0 && !IsWordBoundary(translated[start - 1]))
                start--;

            int end = approxPos;
            while (end < translated.Length && !IsWordBoundary(translated[end]))
                end++;

            if (end <= start) return null;
            string word = translated.Substring(start, end - start);

            if (word.Length < 3) return null;
            bool hasCyrillic = false;
            for (int i = 0; i < word.Length; i++)
                if (word[i] >= '\u0400' && word[i] <= '\u04FF') { hasCyrillic = true; break; }
            return hasCyrillic ? word : null;
        }

        private static bool IsWordBoundary(char c)
        {
            return char.IsWhiteSpace(c) || c == ',' || c == '.' || c == '!'
                || c == '?' || c == ':' || c == ';' || c == '"' || c == '\''
                || c == '[' || c == ']' || c == '<' || c == '>' || c == '('
                || c == ')' || c == '\u2014' || c == '\u2013';
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

    [HarmonyPatch]
    public static class QuestLogPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuestLog), "DisplayQuestInfo")]
        static bool DisplayQuestInfo_Prefix(QuestLog __instance, string _buttonNum)
        {
            try
            {
                int index = int.Parse(_buttonNum);
                var t = Traverse.Create(__instance);
                int start = t.Field("start").GetValue<int>();
                bool viewActive = t.Field("viewActive").GetValue<bool>();
                bool viewComplete = t.Field("viewComplete").GetValue<bool>();
                bool viewRepeat = t.Field("viewRepeat").GetValue<bool>();

                int questIndex = start + index;
                Quest quest = null;

                if (viewActive)
                {
                    if (questIndex < GameData.HasQuest.Count)
                        quest = GameData.QuestDB.GetQuestByName(GameData.HasQuest[questIndex]);
                }
                else
                {
                    var filtered = new List<string>();
                    for (int i = 0; i < GameData.CompletedQuests.Count; i++)
                    {
                        var q = GameData.QuestDB.GetQuestByName(GameData.CompletedQuests[i]);
                        if (q == null) continue;
                        if (viewRepeat && q.repeatable)
                            filtered.Add(GameData.CompletedQuests[i]);
                        else if (viewComplete && !q.repeatable)
                            filtered.Add(GameData.CompletedQuests[i]);
                    }
                    if (questIndex < filtered.Count)
                        quest = GameData.QuestDB.GetQuestByName(filtered[questIndex]);
                }

                if (quest != null)
                    __instance.Desc.text = quest.QuestDesc;
                else
                    __instance.Desc.text = "No quest selected.";
            }
            catch (Exception e)
            {
                ErenshorRUPlugin.Log.LogError($"[RU] QuestLog patch error: {e.Message}");
            }
            return false;
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
        }

        static void SimRespondToSayPrefix(ref string incomingMsg)
        {
            incomingMsg = ChatInputMapper.EnrichWithEnglish(incomingMsg);
        }

        static void ParseTextPrefix(ref string _incoming)
        {
            if (NPCDialogPatches.TryReverseKeyword(_incoming, out string englishKw))
            {
                _incoming = englishKw;
                return;
            }
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
        private const int MinSubstringKeyLength = 3;

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
                if (c == '<' && i + 1 < line.Length &&
                    (char.IsLetter(line[i + 1]) || line[i + 1] == '/'))
                    depth++;
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

        public string TranslateDirect(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (_exact.TryGetValue(input, out string val)) return val;
            string trimmed = input.Trim();
            if (trimmed != input && _exact.TryGetValue(trimmed, out val)) return val;
            string noTrail = input.TrimEnd('\n', '\r', ' ', '.');
            if (noTrail != input && _exact.TryGetValue(noTrail, out val)) return val;
            return input;
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

    public class UpdateChecker : MonoBehaviour
    {
        private const string ReleasesAPI =
            "https://api.github.com/repos/sitxovski/ErenshorRU/releases/latest";
        private const string ReleasesURL =
            "https://github.com/sitxovski/ErenshorRU/releases/latest";

        private enum State { Checking, UpToDate, Outdated, Error }
        private State _state = State.Checking;
        private string _remoteVersion = "";
        private string _releaseBody = "";
        private float _showUntil;
        private float _fadeStart;
        private bool _dismissed;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _closeBtnStyle;
        private GUIStyle _changelogStyle;
        private bool _stylesReady;

        private void Start()
        {
            StartCoroutine(CheckVersion());
        }

        private IEnumerator CheckVersion()
        {
            yield return new WaitForSeconds(3f);

            var req = UnityWebRequest.Get(ReleasesAPI);
            req.SetRequestHeader("User-Agent", "ErenshorRU-UpdateChecker");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.ProtocolError)
            {
                ErenshorRUPlugin.Log?.LogWarning($"[RU] Update check failed: {req.error}");
                _state = State.Error;
                _showUntil = Time.time + 5f;
                _fadeStart = Time.time + 4f;
                yield break;
            }

            string body = req.downloadHandler.text;
            _remoteVersion = ParseTagName(body);
            _releaseBody = ParseReleaseBody(body);

            if (string.IsNullOrEmpty(_remoteVersion))
            {
                _state = State.Error;
                _showUntil = Time.time + 5f;
                _fadeStart = Time.time + 4f;
                yield break;
            }

            string local = NormalizeVersion(ErenshorRUPlugin.VERSION);
            string remote = NormalizeVersion(_remoteVersion);

            ErenshorRUPlugin.Log?.LogInfo($"[RU] Version check: local={local} remote={remote}");

            if (CompareVersions(remote, local) > 0)
            {
                _state = State.Outdated;
            }
            else
            {
                _state = State.UpToDate;
                _showUntil = Time.time + 8f;
                _fadeStart = Time.time + 6f;
            }
        }

        private static string ParseReleaseBody(string json)
        {
            int idx = json.IndexOf("\"body\"", StringComparison.Ordinal);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";

            var sb = new StringBuilder();
            for (int i = q1 + 1; i < json.Length; i++)
            {
                if (json[i] == '"' && json[i - 1] != '\\') break;
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == 'r') { i++; continue; }
                    if (next == '"') { sb.Append('"'); i++; continue; }
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(json[i]);
            }
            string raw = sb.ToString().Trim();
            if (raw.Length > 400) raw = raw.Substring(0, 400) + "...";
            return raw;
        }

        private static string ParseTagName(string json)
        {
            int idx = json.IndexOf("\"tag_name\"", StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static string NormalizeVersion(string v)
        {
            if (v == null) return "0.0.0";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("V", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            return v;
        }

        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
                int vb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        private void BuildStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.12f, 0.92f));
            bgTex.Apply();

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = bgTex;
            _boxStyle.border = new RectOffset(0, 0, 0, 0);
            _boxStyle.padding = new RectOffset(16, 16, 12, 12);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 14;
            _labelStyle.wordWrap = true;
            _labelStyle.richText = true;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 14;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.padding = new RectOffset(16, 16, 6, 6);

            _closeBtnStyle = new GUIStyle(GUI.skin.button);
            _closeBtnStyle.fontSize = 12;
            _closeBtnStyle.padding = new RectOffset(4, 4, 2, 2);

            _changelogStyle = new GUIStyle(GUI.skin.label);
            _changelogStyle.fontSize = 12;
            _changelogStyle.wordWrap = true;
            _changelogStyle.richText = true;
            _changelogStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        }

        private void OnGUI()
        {
            if (_dismissed) return;
            if (_state == State.Checking) return;

            BuildStyles();

            if (_state == State.UpToDate || _state == State.Error)
            {
                if (Time.time > _showUntil) return;

                float alpha = 1f;
                if (Time.time > _fadeStart)
                    alpha = Mathf.Clamp01((_showUntil - Time.time) / (_showUntil - _fadeStart));

                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                DrawUpToDate();
                GUI.color = prev;
            }
            else if (_state == State.Outdated)
            {
                DrawOutdated();
            }
        }

        private void DrawUpToDate()
        {
            float w = 340f, h = 50f;
            float x = Screen.width - w - 20f;
            float y = Screen.height - h - 20f;
            var rect = new Rect(x, y, w, h);

            GUI.Box(rect, GUIContent.none, _boxStyle);

            var accent = _state == State.Error
                ? "<color=#FFaa44>"
                : "<color=#44FF88>";
            string msg = _state == State.Error
                ? $"{accent}ErenshorRU</color>  Не удалось проверить обновление"
                : $"{accent}ErenshorRU v{ErenshorRUPlugin.VERSION}</color>  Перевод актуален ✓";

            _labelStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x + 8, y + 4, w - 16, h - 8), msg, _labelStyle);
        }

        private void DrawOutdated()
        {
            bool hasChangelog = !string.IsNullOrEmpty(_releaseBody);
            float w = 400f;
            float changelogH = hasChangelog ? 100f : 0f;
            float h = 120f + changelogH;
            float x = Screen.width - w - 20f;
            float y = Screen.height - h - 20f;
            var rect = new Rect(x, y, w, h);

            GUI.Box(rect, GUIContent.none, _boxStyle);

            string msg =
                $"<color=#FFCC44><b>ErenshorRU — доступна новая версия!</b></color>\n" +
                $"Установлена: <color=#FF8866>v{ErenshorRUPlugin.VERSION}</color>  →  " +
                $"Новая: <color=#44FF88>{_remoteVersion}</color>";

            _labelStyle.alignment = TextAnchor.UpperCenter;
            GUI.Label(new Rect(x + 8, y + 10, w - 16, 50), msg, _labelStyle);

            if (hasChangelog)
            {
                string clHeader = "<color=#88BBFF><b>Что изменено:</b></color>";
                string clBody = FormatChangelog(_releaseBody);
                GUI.Label(new Rect(x + 14, y + 58, w - 28, 18), clHeader, _changelogStyle);
                GUI.Label(new Rect(x + 14, y + 76, w - 28, changelogH - 18),
                    clBody, _changelogStyle);
            }

            float btnW = 140f, btnH = 30f;
            float btnY = y + h - btnH - 12f;

            if (GUI.Button(new Rect(x + w / 2f - btnW - 8, btnY, btnW, btnH),
                "Обновить", _btnStyle))
            {
                Application.OpenURL(ReleasesURL);
                Application.Quit();
            }

            if (GUI.Button(new Rect(x + w / 2f + 8, btnY, btnW, btnH),
                "Позже", _btnStyle))
            {
                _dismissed = true;
            }

            if (GUI.Button(new Rect(x + w - 28, y + 4, 22, 22), "×", _closeBtnStyle))
            {
                _dismissed = true;
            }
        }

        private static string FormatChangelog(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder();
            string[] lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.Length == 0) continue;
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    sb.Append("<color=#CCDDAA>  ●</color> ");
                    sb.AppendLine(line.Substring(2).Trim());
                }
                else if (line.StartsWith("# ") || line.StartsWith("## "))
                {
                    string hdr = line.TrimStart('#', ' ');
                    sb.Append("<color=#FFDD88><b>");
                    sb.Append(hdr);
                    sb.AppendLine("</b></color>");
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
