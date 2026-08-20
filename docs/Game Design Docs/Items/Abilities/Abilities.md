# DPS
## Bullet master (dex > 0) & (dps > 0)
- toggle between charge/shotgun/piercing/etc. shot patterns
- Big charge shot: self-slows, fires a single shot that gets stronger the longer it's charged
	- Hold-to-charge
- Big volley shot: self-slows, fires a large spread that gets stronger the longer it's charged — especially strong against low-defense enemies
	- Hold-to-charge
- Spawn a dummy on self that allies can shoot; the archer absorbs the damage that the dummy takes and releases it as a big charge/volley shot
- Mark an enemy to take increased damage
- Quick slash that destroys enemy bullets, if the slash is stronger than the bullet
- Grapple hook toward a point on the ground (still takes damage while grappling)
## Warrior (str > 0) & (dps > 0)
- mark an enemy and buff self to convert all incoming damage from that enemy over the next n seconds into dot damage. the dot damage can be purged.
	- Dot pool, sheddable, reduced. Incoming hits add to a damage pool instead of HP loss; the pool drains as damage ticks over ~5s. Only 70% of the original damage is applied to the pool bleed conversion is partly mitigated. Also, the ability no longer converts all incoming damage into DoT damage - you now have to mark a specific enemy and all damage coming from that enemy gets converted to DoT damage. I want there to be multiple conditions for purging the damage pool and I want you to create an ability for each condition. The conditions are: 1. Avoid taking any damage for ~2s. 2. Stay within a radius of the marked enemy for ~5s. 3. Dealing x damage (for now just use 1 damage) to the marked enemy. 4. The marked enemy dies.
- mark an enemy and buff self to store all the damage dealt by self within the next n seconds and double it if a condition is met or negates it to 0 if a different condition is met.
	- I want there to be multiple conditions for the damage doubling/negation and I want you to create an ability for each condition. The conditions are: 1. No-hit wager (double if the player takes no hits during the window; released at normal if hit 1-2 times; lost entirely if hit 5+ times). 2. Stay within a radius of the marked enemy (doubled if staying within radius r1; normal if staying within r2; negated if you leave r2). 3. Dealing x damage (doubled if reaching x damage; normal if damage is between y and z; negated if below z).
- enter a windup animation to do a short teleport.
## Wizard (wis > 0) & (dps > 0)
- Spell that spawns a beam of light that deals damage
- Spell that summons bullets that converge on/from/around the player's cursor
- Spell that forces the player to stop shooting to enter a wind up animation that sends a fireball or some shit
- Spell that summons a spire that damages surrounding enemies (mirror image of the healer's mushroom tome spire idea below, but offensive instead of healing)
- Spell that calls down a meteor that breaks into shrapnel after it lands
- Spell that shoots out a laser
# SUP

## Witch Doctor (wis > 0) & (sup > 0)
- a circle that makes players immune to debuffs
- a circle that reduces all damage that players take
- a circle that players can't die within ("can't drop below n hp" zone)
- a circle that heals allies over time
- a circle that burst heals allies after n seconds
- a circle that makes allies deal more damage
- a circle that damages enemies over time
- a circle that deals a burst of damage to enemies after n seconds.
- a circle that slows enemies
- a circle that makes enemies receive more damage
- a circle that distributes all damage received within it evenly across all players standing in it
## Paladin
- buff nearby allies to have increased health regen
- mark an enemy and buff players in a radius around self to convert all incoming damage from that enemy over the next n seconds into dot damage. the dot damage can be purged.
	- Dot pool, sheddable, reduced. Incoming hits add to a damage pool instead of HP loss; the pool drains as damage ticks over ~5s. Only 70% of the original damage is applied to the pool bleed conversion is partly mitigated. Also, the ability no longer converts all incoming damage into DoT damage - you now have to mark a specific enemy and all damage coming from that enemy gets converted to DoT damage. I want there to be multiple conditions for purging the damage pool and I want you to create an ability for each condition. The conditions are: 1. Avoid taking any damage for ~2s. 2. Stay within a radius of the marked enemy for ~5s. 3. Dealing x damage (for now just use 1 damage) to the marked enemy. 4. The marked enemy dies.
- mark an enemy and buff players in a radius around self to store all the damage dealt by self within the next n seconds and double it if a condition is met or negates it to 0 if a different condition is met.
	- I want there to be multiple conditions for the damage doubling/negation and I want you to create an ability for each condition. The conditions are: 1. No-hit wager (double if the player takes no hits during the window; released at normal if hit 1-2 times; lost entirely if hit 5+ times). 2. Stay within a radius of the marked enemy (doubled if staying within radius r1; normal if staying within r2; negated if you leave r2). 3. Dealing x damage (doubled if reaching x damage; normal if damage is between y and z; negated if below z).
- self buff that grants immunity to all damage for the next n seconds
- self buff that grants immunity to the next single instance of damage received within n seconds (weaker/cheaper version of the full invuln buff above)
- redirect all damage that party members receive over the next n seconds to self
- store all pre-mitigated damage received over the next n seconds and release it as a smite
## Tank (str > 0) & (sup > 0)
- shield bash contributes to an enemy's stagger bar
- shield bash applies armour break debuff and reduces enemy defence to 0
	- Bar + stun, as well as bar + stun + break (create two ability items). Per-enemy stagger bar (new runtime field); bash adds stagger, full bar stuns the enemy for a few seconds, then resets. I want the stun behavior to be dynamic. Some enemies pause their attacks + movement when stunned, some get slowed, others get enraged.
- self buff that grants immunity to all damage for the next n seconds
- self buff that reduce all incoming damage by a percentage for the next n seconds
- store pre-mitigated damage received for the next n seconds and release it as a shield bash
- shield wall that absorbs all bullets that hit it
## Trickster (wis > dex > 0) & (sup > 0)
Maybe make this class's magic be tied to physical items rather than magic scrolls like the wizard.
- invisibility
- poisons
- debuffs
- teleport
- decoy — summons a mirror image that enemies can target
- buff nearby allies to be immune to all damage for the next n seconds


# Previous session prompts questions and outputs

Read @server/spacetimedb/src/item/AGENTS.md and then go through @"docs/Game Design Docs/Items/Abilities/Abilities.md" and write a plan that details how to implement each ability. Some of the abilities in that @"docs/Game Design Docs/Items/Abilities/Abilities.md" file are ambiguous, so ask me questions about them to help you clarify. I want any duplicated buffs or debuffs to share the same function in the code - e.g. the warrior's self buff to convert damage into bleed damage should act the same as the paladin's aoe buff that does the same thing. If there are any open ended design decisions, then feel free to pitch in any ideas as well.

Q  The doc lists ~45 abilities across 6 classes, requiring ~5 new subsystems (status effects, enemy debuffs, ground zones, spell damage, targeting changes). How should the plan structure implementation?
→  Phased by subsystem (Recommended)
Q  'Convert damage into bleed damage' (warrior self-buff, paladin AoE buff — same shared function) is undefined. What should bleed mean mechanically?
→  Dot pool, sheddable, reduced. Incoming hits add to a damage pool instead of HP loss; the pool drains as damage ticks over ~5s. Only 70% of the original damage is applied to the pool - bleed conversion is partly mitigated. Also, the ability no longer converts all incoming damage into DoT damage - you now have to mark a specific enemy and all damage coming from that enemy gets converted to DoT damage. I want there to be multiple conditions for purging the damage pool and I want you to create an ability for each condition. The conditions are: 1. Avoid taking any damage for ~2s. 2. Stay within a radius of the marked enemy for ~5s. 3. Dealing x damage (for now just use 1 damage) to the marked enemy. 4. The marked enemy dies.
Q  Warrior self-buff and paladin AoE buff: 'store all damage dealt for n seconds, DOUBLED if a condition is met, NEGATED to 0 if a different condition is met'. The doc never says the conditions. Which wager do you want?
→  I have decided to change the specifics of those buffs. I instead want them to mark and enemy first and then apply those buffs. The stored damage doubling/negation only applies to the marked enemy. I want there to be multiple conditions for the damage doubling/negation and I want you to create an ability for each condition. The conditions are: 1. No-hit wager (double if the player takes no hits during the window; released at normal if hit 1-2 times; lost entirely if hit 5+ times). 2. Stay within a radius of the marked enemy (doubled if staying within radius r1; normal if staying within r2; negated if you leave r2). 3. Dealing x damage (doubled if reaching x damage; normal if damage is between y and z; negated if below z).
Q  Paladin 'buff nearby allies', paladin 'redirect party damage', trickster 'buff nearby allies': no party/squad system exists in the game. How should AoE ally effects pick targets?
→  Radius around caster (Recommended)
Q  Bullet master's charge/volley shots, wizard's windup fireball, and warrior's windup teleport all imply a cast that isn't instant. Abilities today are single-press instant casts. What input model do you want?
→  Hold-to-charge (Recommended)
Q  Archer's shootable dummy, trickster's decoy, and invisibility all require enemies to target things that aren't players (or stop targeting players). Enemy targeting today only considers logged-in players' position rows. Include this overhaul?
→  Defer these three
Q  Tank shield bash: 'contributes to an enemy's stagger bar' — no stagger system exists. How far should it go?
→  Bar + stun, as well as bar + stun + break (create two ability items). Per-enemy stagger bar (new runtime field); bash adds stagger, full bar stuns the enemy for a few seconds, then resets. I want the stun behavior to be dynamic. Some enemies pause their attacks + movement when stunned, some get slowed, others get enraged.
Q  Trickster's list is vague: 'invisibility, poisons, debuffs, teleport, decoy, AoE invuln buff'. Teleport/decoy/invuln are covered by other answers. What should 'poisons' and 'debuffs' concretely be?
→  Reuse shared debuffs (Recommended)