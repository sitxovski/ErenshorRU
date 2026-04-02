import os, re, glob

EXPORT = r"D:\SteamLibrary\steamapps\common\Erenshor\EXPORT\AssetRipper_export_20260402_103945\ExportedProject\Assets"
TRANS_DIR = r"D:\SteamLibrary\steamapps\common\Erenshor\BepInEx\plugins\ErenshorRU\Translation"

def load_translations():
    keys = set()
    for f in glob.glob(os.path.join(TRANS_DIR, "*.txt")):
        for line in open(f, encoding="utf-8"):
            line = line.strip()
            if not line or line.startswith("#") or line in ("[SUBSTRING]", "[EXACT]"):
                continue
            eq = line.find("=")
            if eq > 0:
                en = line[:eq].replace("\\n", "\n").strip()
                keys.add(en)
    return keys

PAT_MTEXT = re.compile(r"^\s+m_[Tt]ext:\s*(.+)$")
SKIP = re.compile(r"^[\d\s\.\-\+\*\(\)\[\]<>/\\|:;,!?@#$%^&=~`{}\"\']*$")
HAS_LATIN = re.compile(r"[A-Za-z]{2,}")

def extract_texts(filepath):
    texts = []
    try:
        with open(filepath, encoding="utf-8", errors="replace") as f:
            for line in f:
                m = PAT_MTEXT.match(line)
                if not m:
                    continue
                val = m.group(1).strip()
                if val.startswith("'") and val.endswith("'"):
                    val = val[1:-1]
                elif val.startswith('"') and val.endswith('"'):
                    val = val[1:-1]
                val = val.replace("\\n", "\n").strip()
                if len(val) < 2:
                    continue
                if SKIP.match(val):
                    continue
                if not HAS_LATIN.search(val):
                    continue
                texts.append(val)
    except:
        pass
    return texts

def main():
    print("Loading translations...")
    keys = load_translations()
    print(f"  {len(keys)} keys\n")

    prefab_files = glob.glob(os.path.join(EXPORT, "**", "*.prefab"), recursive=True)
    print(f"Found {len(prefab_files)} prefab files\n")

    all_missing = []
    by_file = {}

    for pf in sorted(prefab_files):
        texts = extract_texts(pf)
        rel = os.path.relpath(pf, EXPORT)
        for t in texts:
            clean = t.strip().rstrip("\n").strip()
            if clean in keys:
                continue
            noclean = t.rstrip("\n")
            if noclean in keys:
                continue
            if t in keys:
                continue
            all_missing.append((rel, t))
            by_file.setdefault(rel, []).append(t)

    print(f"Total prefab texts found: {sum(len(extract_texts(pf)) for pf in prefab_files)}")
    print(f"Missing translations: {len(all_missing)}\n")

    if not all_missing:
        print("All prefab texts are translated!")
        return

    seen = set()
    unique_missing = []
    for rel, t in all_missing:
        if t not in seen:
            seen.add(t)
            unique_missing.append((rel, t))

    print(f"Unique missing: {len(unique_missing)}\n")
    print("=" * 60)

    with open("missing_prefab_texts.txt", "w", encoding="utf-8") as out:
        out.write(f"# Missing prefab translations: {len(unique_missing)}\n\n")
        for rel, t in unique_missing:
            display = t.replace("\n", "\\n")
            out.write(f"# Source: {rel}\n")
            out.write(f"{display} =\n\n")
    print(f"Saved to missing_prefab_texts.txt ({len(unique_missing)} unique)")

if __name__ == "__main__":
    main()
