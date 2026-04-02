"""Scan all scenes + NPC prefabs for Dialog/Rejection strings and check coverage."""
import os, re, glob

EXPORT = r"D:\SteamLibrary\steamapps\common\Erenshor\EXPORT\AssetRipper_export_20260402_103945\ExportedProject\Assets"
TRANS_DIR = r"D:\SteamLibrary\steamapps\common\Erenshor\BepInEx\plugins\ErenshorRU\Translation"

def load_translations():
    keys = set()
    for f in glob.glob(os.path.join(TRANS_DIR, "*.txt")):
        with open(f, encoding="utf-8") as fh:
            for line in fh:
                line = line.rstrip("\r\n")
                if not line or line.startswith("#") or line in ("[SUBSTRING]", "[EXACT]"):
                    continue
                idx = line.find("=")
                if idx > 0:
                    en = line[:idx].replace("\\n", "\n").strip().strip("'")
                    if len(en) >= 2:
                        keys.add(en)
    return keys

def extract_dialogs(filepath):
    """Extract Dialog: and Rejection: fields."""
    strings = []
    try:
        with open(filepath, encoding="utf-8", errors="replace") as f:
            for line in f:
                for field in ("Dialog:", "Rejection:"):
                    m = re.match(rf'\s+{field}\s+(.+)', line)
                    if m:
                        val = m.group(1).strip().strip("'\"")
                        val = val.replace("\\n", "\n").replace("''", "'")
                        if len(val) >= 5 and any(c.isalpha() for c in val):
                            strings.append(val)
    except:
        pass
    return strings

def check(s, translated):
    if s in translated:
        return True
    s2 = s.strip().strip("'\"")
    if s2 in translated:
        return True
    # prefix
    for k in translated:
        if len(k) > 20 and (s.startswith(k) or k.startswith(s[:100])):
            return True
    return False

def main():
    print("Loading translations...")
    translated = load_translations()
    print(f"  {len(translated)} keys\n")

    # Collect all files to scan
    files = []
    for ext in ("*.unity", "*.prefab"):
        files.extend(glob.glob(os.path.join(EXPORT, "**", ext), recursive=True))

    total = 0
    found = 0
    missing = []

    for f in sorted(files):
        dialogs = extract_dialogs(f)
        if not dialogs:
            continue
        rel = os.path.relpath(f, EXPORT)
        for d in dialogs:
            total += 1
            if check(d, translated):
                found += 1
            else:
                missing.append((rel, d))

    print(f"Total dialog strings: {total}")
    print(f"Translated: {found}")
    print(f"Missing: {len(missing)}")
    if total > 0:
        print(f"Coverage: {found/total*100:.1f}%")

    if missing:
        print(f"\nMISSING DIALOGS ({len(missing)}):")
        for src, d in missing:
            disp = d.replace("\n", "\\n")
            if len(disp) > 120:
                disp = disp[:120] + "..."
            name = os.path.basename(src).replace(".prefab","").replace(".unity","")
            print(f"  [{name}] {disp}")

    # Write missing to file
    if missing:
        out = os.path.join(TRANS_DIR, "..", "missing_dialogs.txt")
        with open(out, "w", encoding="utf-8") as f:
            f.write(f"# Missing dialog translations: {len(missing)} / {total}\n\n")
            for src, d in missing:
                d_clean = d.replace("\n", "\\n")
                f.write(f"# Source: {src}\n{d_clean}=\n\n")
        print(f"\nSaved to: {out}")

if __name__ == "__main__":
    main()
