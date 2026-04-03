import os

TRANS_DIR = r'Translation'
files = ['ui_system.txt', 'misc.txt', 'ui_extra.txt', 'items_lore.txt',
         'npc_dialogs.txt', 'tutorials.txt', 'quests.txt', 'skills_spells.txt',
         'simplayer_chat.txt']

all_keys = set()
all_subs = set()
for fn in files:
    p = os.path.join(TRANS_DIR, fn)
    if not os.path.exists(p):
        continue
    sub_mode = False
    with open(p, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith('#'):
                continue
            if line == '[SUBSTRING]':
                sub_mode = True
                continue
            if line == '[EXACT]':
                sub_mode = False
                continue
            eq = line.find('=')
            if eq > 0:
                k = line[:eq]
                if sub_mode:
                    all_subs.add(k)
                else:
                    all_keys.add(k)

audit_strings = [
    "You cannot fight underwater!",
    "You must be facing your target!",
    "You score a CRIPPLING BLOW!",
    "You score a critical hit!",
    "Equipped ",
    "Unequipped ",
    "You cannot equip two of the same RELIC ITEMS.",
    "Second hand must be empty to equip a 2-handed weapon!",
    "Two handed weapons must be equipped in the primary slot with an empty secondary hand.",
    "You must buy this item before you can use it that way!",
    "Cannot store or rearrange items on corpses.",
    "Cannot hotkey items from bank.",
    "This spell can only be cast on group members, yourself, or a willing target",
    "This target is invulnerable.",
    "You've summoned a companion!",
    "You cannot have two summoned companions!",
    "Your spell resonates and casts again!",
    "You feel a surge of returning mana.",
    "Your spell did not take hold...",
    "You land a critical blast!",
    "You execute a FINALE!",
    "Your bracer begins to glow!",
    "The glow fizzes... (invalid target)",
    "The Spirits of Erenshor have chosen Balance...",
    "A ROARING ECHO courses through the air",
    "You have died and lost some experience.",
    "New respawn point set!",
    "You have escaped from your bonds!",
    "You missed the water.",
    "You feel a nibble - catch chance increased!",
    "You did not catch anything.",
    "Not even a nibble...",
    "It's a good cast!",
    "You cast your line...",
    "You need a fishing pole in your hand!",
    "You can't fish while in the water!",
    "Your inventory is full, you must re-cast your line manually.",
    "You have moved, you must re-cast your line manually.",
    "Cannot use more than one template item at a time.",
    "Invalid fuel source...",
    "Invalid template item...",
    "Invalid recipe... Check items and quantities",
    "item successfully forged!",
    "This Diamond can only be used to remove a blessing...",
    "This Diamond can only be used on a blessed item.",
    "It begins to rain.",
    "The rain stops.",
    "This skill requires a shield to be equipped!",
    "This skill requires a two-handed weapon to be equipped!",
    "This skill requires a bow to be equipped!",
    "This skill requires a weapon to be equipped!",
    "You are not experienced enough to learn this spell yet...",
    "You are not experienced enough to learn this skill yet...",
    "Price must be a round number.",
    "Price cannot be negative.",
    "You need more gold to buy this item.",
    "Welcome to Level ",
    "You have a class proficiency point to spend! Open your inventory to assign it.",
    "A sensation of free will washes over you...",
    "Character Load Success!",
    "Cooldown period...",
    "New Character",
    "Choose a name!",
    "Choose a class!",
    "Command not recognized.",
    "You are not currently in a group.",
    "You are not currently in a guild.",
    "Getting main assist's target...",
    "Main assist currently has no target",
    "You are the main assist...",
    "No Quest Assigned",
    "No quest selected.",
    "Sablehearts Curse grows stronger...",
    "Astra begins to inhale again...",
    "Can't do that while dead...",
    "Can't use skills while dead!",
    "Can't cast while stunned...",
    "Can't invite party members while dead...",
    " hours left...",
    "DPS Recording Started",
    "Ring 1", "Ring 2", "Bracer 1", "Bracer 2",
    "Ascension XP: ",
    " drops from ",
    " comes from ",
    " drops that.",
    "This reciple can only upgrade one BLUE SPARKLING item",
    "This mold can only remove a blessing from one item",
    "You you you scored a... hey wait, nevermind.",
    "You did not learn anything from this trivial opponent.",
    "YOU have taken ",
    "Just hanging out solo. Hit me up if you wanna group!",
    "You left me hanging last time you invited me...",
    "You're too far away to do that!",
    "Loading Data...",
    "Loading Server Data...",
    "No Spell Selected.",
    "No Skill Selected.",
    "No mining tool in inventory",
]

missing = []
for s in audit_strings:
    in_exact = s in all_keys
    in_sub = any(sub in s for sub in all_subs if len(sub) >= 4)
    if not in_exact and not in_sub:
        missing.append(s)

print(f'Missing from audit: {len(missing)} strings')
for s in sorted(set(missing)):
    print(f'  {s}')
