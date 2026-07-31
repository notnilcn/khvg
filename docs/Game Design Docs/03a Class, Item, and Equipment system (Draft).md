# About this draft
This is a merged rewrite of [[03 Class system, Item system, and Equipment system]] and the "Possible abilities" section of [[Inspirations, research, and new things to add]]. Most of the ability brainstorm from the inspirations doc had already been folded into 03 piecemeal as CriticMarkup insertions — this draft just accepts all of that, reorganizes it into one coherent read, and adds a new "fast vs heavy weapon" idea (the hammer thing) that I want to apply across every melee-adjacent class, not just warriors.

Nothing here is final. Treat it the same as 03 — ideas, not spec.

# Class system overview
Classes aren't going to be explicitly defined. They're defined by the gear that exists in the game and the stat requirements for that gear. Once players figure out optimal builds and stat allocations, they'll naturally sort themselves into the stereotypical MMORPG classes anyway.

The reason for doing it this way: I want to be able to add a new class just by adding new gear. If I want a monk, I define the stat distribution I want for it (say, equal parts wisdom + strength + dexterity) and add gear that requires that distribution. No new class system code, just new items.

~~This means players could re-spec into different classes just by redistributing stats (we could make this impractical though — e.g. require gear to meet both a relative *and* an absolute stat threshold), which may or may not be a good thing.~~

~~It also means players who spread their stats evenly could play narratively conflicting classes (mixing healer and warrior gear, say). Not sure if that should be allowed. Possible deterrents: inefficient allocation tanks your progression, or gear usage "locks in" your stats over time — the more you use healer gear, the more your wisdom locks in, while unused stats stay reallocatable.~~

We can figure out the details later.

# Stats
- Dexterity
- Strength
- Wisdom
I can't think of another stat that would make meaningful sense. I don't like the idea of a luck stat.
# Temperament
Temperament stats work exactly like normal stats — the only difference is conceptual, not mechanical. They flavour *what* a character does with their normal stats:
- Damage dealer
- Supporter
- Artisan

Different stat + temperament distributions produce different classes. E.g. a tank is a strength supporter, a warrior is a strength damage dealer, a paladin is a strength+wisdom supporter, a healer is a wisdom supporter, a wizard is a wisdom damage dealer.

Artisan is useless for combat, but a character fully specced into it should be valuable in the endgame crafting/economy loop. I want leveling an artisan character to be intentionally gruelling — I want other players to go "what the fuck?" when they see someone who actually did it.

# Item and equipment system overview
- Items are designed around RotMG's swap-out meta, but without requiring the player to actually swap out.
	- Weapons can have toggles that change shot pattern.
	- Characters have n=6 ability slots and can equip multiple abilities. Abilities are tied to items, and some ability items take up multiple slots (shields take 6, spells take 2, tomes/scriptures take 3).
		- If order doesn't matter, then the total number of combinations/permutations that fill up n slots can be calculated with the integer partition function.
			```
			def partitions(n, max_size=None):
				"""Yield partitions of n as non-increasing tuples, each part <= max_size."""
				if max_size is None:
					max_size = n
				if n == 0:
					yield ()
					return
				for k in range(min(max_size, n), 0, -1):
					for rest in partitions(n - k, k):
						yield (k,) + rest
usage: len(list(partitions(6)))=11
			```
		- If order matters, then the total number of combinations/permutations that fill up n slots can be calculated with the integer composition function
			```
			def compositions(n, max_size=None):
			    """Yield ordered slot arrangements of n."""
			    if n == 0:
			        yield ()
			        return
			    for k in range(1, min(max_size or n, n) + 1):
			        for rest in compositions(n - k, max_size):
			            yield (k,) + rest
usage: len(list(compositions(6)))=32
			```
		- I think I want the order to matter, since the integer composition function grows quickly ($2^{n-1}$). You could make the order matter by making the first ability stronger than the second ability and so on.
- Items have enchantments and modifiers, similar to the Borderlands item system.

In RotMG, one of the main ways people maximize DPS is swapping gear sets mid-fight — usually because when an enemy is staggered or armor-broken, a set with a higher bullet count suddenly out-DPSes a set with fewer, harder-hitting bullets (higher defense disproportionately punishes low-bullet-count builds). That swap-out meta was clunky, and I don't want it to be as central here.

Instead: weapons get toggles, tied to enchantments, that flip the shot pattern between low-count/high-damage and high-count/low-damage. Same tactical decision, no inventory shuffling.

RotMG's Staff of Extreme Prejudice is the reference case — it fires 10 bullets in a circle and does the highest DPS in the game, but only if you're standing directly on top of the enemy, so it's only good against staggered targets. I want that shot pattern to exist here as a modifier/enchantment: useless early, game-breaking late. Off-meta builds with a huge late-game payoff is a classic RPG fantasy and I want it in this game.

All the buffs and status conditions in [[02 Gameplay#Buffs, debuffs, and damage calculation nuances]] should be able to show up as enchantments/modifiers. If the enchantments are good enough, a lower-tier weapon should sometimes beat a higher-tier one.

Enchantment slots should be player-modifiable, plus some innate enchantments that can't be changed but also don't eat a slot. A weapon born with the Extreme-Prejudice-style toggle as an *innate* enchantment should be extremely valuable to minmaxers.

Abilities are tied to equipment (n=6 ability slots, some abilities eating multiple). Some ability items passively modify your kit just by being equipped (paladin buffs), others require you to actively swap to them to use them (wizard spells).

# Weapon philosophy: many bullets vs. few
One thing I want to formalize across every class, not just the warrior: most weapon categories should offer a **fast** option and a **heavy** option, and the two should feel mechanically opposed the same way the shot-pattern toggle does for ranged weapons.

- **Many bullets** — high fire rate, extremely high raw pre-mitigated damage, weak against enemies that have high defence.
- **Few bullets** — low fire rate, effective against enemies that have a high defence stat, relatively lower pre-mitigation damage.

The hammer is the clearest example: a heavy counterpart to the warrior/paladin/tank sword. Slow, telegraphed swing, big knockback, and it should hit *hardest* against staggered/exposed enemies — mechanically it's the melee version of the "useless in the early game, busted in the late game" off-meta fantasy the Extreme Prejudice staff already represents for ranged. It also gives the strength classes an actual reason to care about enemy stagger/armor-break state, the same way bullet-count builds already do.

Once the hammer exists for the strength classes, the same fast/heavy split is worth applying elsewhere so it isn't just a melee gimmick:
- **Gunner** — undeveloped.
- **Wizard** — staves = many bullets, wands = few bullets
- **Healer** — staves = many bullets, wands = few bullets
- **Trickster** — undeveloped.

None of this needs to be symmetrical or forced onto every class — it's a design lens to reach for when a class's kit feels one-note, not a checklist every class must satisfy.

# Classes
Stat distribution syntax: if a stat isn't mentioned, it can be anything; if it is mentioned, it needs to be greater than 0 (i.e. every time you see a class tagged with a stat, mentally append ">0"). Class items are locked behind these stat thresholds.

## DPS
### Gunner (dex, dps)
Similar to the archer in RotMG, though "archer" doesn't make a ton of sense in a magic-heavy world — these might just be gunslingers instead.

**Weapons:** Bow, guns?

**Abilities:**
- (innate?) toggle between charge/shotgun/piercing/etc. shot patterns
- Big charge shot: self-slows, fires a single shot that gets stronger the longer it's charged
- Big volley shot: self-slows, fires a large spread that gets stronger the longer it's charged — especially strong against low-defense enemies
- Spawn a dummy/blackhole that allies can shoot; the archer can absorb the accumulated damage and release it as a big charge/volley shot
- Mark an enemy to take increased damage
- Quick slash that destroys enemy bullets, if the slash is stronger than the bullet
- Grapple hook toward a point on the ground (still takes damage while grappling)

### Warrior (str, dps)
**Weapons:** Sword (pixel-quest-style — look at Roblox Pixel Quest's strike animation, and Terraria for sword variety: slash projectiles, etc.), plus the **hammer** as the heavy alternative described above.

**Ability: magic tattoos.** Think Witch Hat Atelier rather than RotMG's warrior helm (which is a boring ability item). Higher-grade tattoos come from higher-grade ink — monster blood, god ichor, special tree sap, etc. The in-fiction reason warriors can swap abilities is that each tattoo needs prep time before use; equipping one should probably consume some kind of ink/reagent item. Tattoos should visibly glow when activated.
- (innate?) passive dodge: teleport short distances to avoid bullets, no windup — this could just be the warrior's baseline kit
- A stronger, windup version of the above: short teleport with a telegraphed activation, similar to the Kensei ability from RotMG. Meant to let a skilled player dodge through entire boss phases solo.
- Convert all incoming damage, for n seconds, into bleed (with some way to shed the bleed if you're clever about it)
- Convert all damage dealt *and* received into bleed
- Buff sword damage for n seconds — could add burn, or a "stores all damage dealt over n seconds and doubles it" high-risk/high-reward version (with a fail condition that zeroes the payout if broken)
- Store all damage dealt/received over n seconds and negate/multiply it if some condition is met — same high-risk shape as the sword-storing idea

### Wizard (wis, dps)
**Weapons:** Staffs/staves and wands, RotMG-flavored but not copied 1:1.
- Staffs — see the [S.T.A.F.F. showcase](https://youtu.be/m0COVnAtCxA&t=107) and tiered-staff [PPE example](https://youtu.be/MKtTq4U3hMU?si=ReRPrelHkF1btWOA&t=468) for reference, not for direct copying
- Wands — Lumiaire and tiered wands, same deal
- Tome/grimoire as the heavy alternative described above

**Ability: spell scrolls**
- Delayed beam of light that deals damage ([reference](https://www.youtube.com/watch?v=dU0andwLZmk))
- Bullets that converge on/around the player's cursor ([penetrating blast reference](https://www.youtube.com/watch?v=-aCETL8lteA), [tablet vs. para spell comparison](https://www.youtube.com/watch?v=4W4metbXxsc))
- Windup spell that forces you to stop shooting, then sends out a fireball
- Summon a spire that damages surrounding enemies (the offensive mirror of the healer's mushroom-tome spire below)
- Meteor that breaks into shrapnel on landing
- Straight-up laser beam

## SUP
### Healer (wis, sup)
**Weapons:** Wands — copy RotMG's Lumiaire and tiered wands. Censer/ritual bowl as the heavy alternative described above.

**Ability: tomes/books/scriptures**
- Mushroom tome: spawns a spire that heals everyone in a radius. Back when RotMG had a bigger population, raid leaders used this for group coordination — "push up, you heal more near the front because that's where the mushroom tomes are."
- AoE heal
- Targeted heal (nearest-to-cursor, or lowest-HP player within a radius of the cursor)
- Debuff removal
- Witch-doctor-flavored ground circles — could all just be mushroom tome variants:
  - A circle that stops allies inside it from dropping below 1 HP
  - A circle that heals allies over time
  - A circle that burst-heals allies after n seconds
  - A circle that boosts ally damage
  - A circle that damages enemies over time
  - A circle that slows enemies
  - A circle that increases damage taken by enemies inside it
  - A circle that evenly distributes all damage taken by players standing in it — group-coordination tech, the damage-side mirror of the mushroom-tome push-up trick

### Paladin (str + wis, sup)
Similar to the paladin in RotMG. Lower defense than the tank, higher health pool — weak against enemies that fire lots of small low-damage bullets, or that spawn in groups/summon adds. Might not want to call it "paladin" specifically since I don't want organized religion in the game's lore.

**Weapons:** Sword (pixel-quest-style, Terraria for variety), plus the hammer as the heavy alternative.

**Abilities:** RotMG uses seals for this, which is boring — probably reuses the tattoo item concept instead.
- Temporary full damage immunity (self-buff)
- Immunity to the *next single hit* within n seconds — cheaper, single-target version of full invuln
- Temporary increased health regen for nearby allies
- Buff nearby allies' damage
- Convert all damage into bleed
- Buff allies to store damage dealt/received over n seconds and negate/multiply it on a condition — same shape as the warrior tattoo idea
- Redirect all party damage over the next n seconds to self — a "protect the healer" panic button
- Store all pre-mitigated damage received over n seconds and release it as a smite — turns a big incoming hit into burst damage instead of just eating it

### Tank (str, sup)
Similar to the knight in RotMG. Higher defense than the paladin, lower health pool (shouldn't matter much with a good healer). Some endgame bosses should only be beatable by leaning on the tank's stagger/armor-break debuffs specifically.

**Weapons:** Sword (pixel-quest-style, Terraria for variety), plus the hammer as the heavy alternative — arguably the tank is the class the hammer was designed around, given how much the kit already leans on stagger/armor-break.

**Ability: shield**
- Shield bash → contributes to a stagger bar (a softer version of RotMG's knight stun)
- Shield bash applies armor-break or expose (like Ogmur/Samurai in RotMG — expose adds a flat +10% damage taken that ignores enemy defense, pairing well with high-bullet-count weapons; armor-break zeroes enemy defense outright)
- Temporary full damage immunity (self-buff)
- Immunity to the *next single hit* within n seconds — cheaper version of the above
- Reduce all incoming damage by a percentage for n seconds
- Store pre-mitigated damage received over n seconds, release it as a shield bash — same store-then-release shape as the paladin's smite
- Shield wall that absorbs any bullets that hit it

### Trickster (wis ≳ dex, sup)
Merges RotMG's assassin, rogue, and trickster into one class.

**Weapons:** Undecided. Crossbow, maybe? Not a fan of how daggers work in RotMG and haven't found a version I like better — Terraria has magic throwing knives but that reads a bit off. Dual daggers could work as the fast option against whichever heavier weapon we land on (see the fast/heavy note above).

**Ability: implement all of these.** Might make this class's "magic" tied to physical items rather than scrolls, the way the wizard's is.
- Invisibility (cloak)
- Poisons/debuffs (vials of liquid? reads a bit too "consumable" though)
- Teleport (magic rocks/orbs?)
- Decoy (magic rocks/orbs?) — summons a mirror image that takes aggro
  - Could split this into two items: a low-cooldown single decoy for normal fights, and a separate longer-cooldown "summon multiple decoys" version for boss mechanics
- Party-wide panic button: teleports the whole party to a pocket dimension, briefly invisible and immune to all damage — needs a long cooldown or it trivializes mechanics

# Old notes
Subclasses:
- Periodic invulnerability (str + wis spec)
- High defense (str + vit spec)
- High health (vit + str spec)

My content brain wants to do this via gear; my monetization brain wants subclasses, since character slots are the monetization hook.

High defense is good against lots of small bullets (flat damage reduction, more bullets = more total reduction). High health is good against a small number of high-damage bullets, and against armor-break situations. Periodic invulnerability is skill-based and needs party coordination to maximize — its *only* advantage over the other trees should be more damage. A 1 DPS + 1 tank + 1 support party should roughly match a 1 support + 2 periodic-invuln-knight party in output, but only with the right gear, and only in dungeons designed around it. That setup should demand extreme danger and coordination — mistime the tanking and the boss aggros the support, who should die near-instantly from low mobility and low defense.

Could have an endgame boss that needs both tank archetypes at once: a low volume of targeted, high-damage, armor-piercing shots (needs the high-HP tank) plus waves of minions firing weak bullets (needs the high-defense tank).

Some dungeons should require scouts to optimize — like RotMG's Shatters (destroy the statues) or Lost Halls (find the boss room / destroy the pots). Scouts should be able to solo some dungeons, but group play should still be more optimal.

There should be endgame builds that let tricksters match DPS output under the right conditions (e.g. a properly debuffed enemy). This class might break the game's balance, but I like a class that can make or break a dungeon run — similar to how tricksters enable full-skip void runs in RotMG.