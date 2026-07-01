# Overview
Classes aren't going to be explicitly defined. They are going to be defined by the gear that exists in the game and the stat requirements for that gear. When players figure out the optimal builds and stat allocations, they will naturally sort themselves in the stereotypical mmorpg classes.

**The reason why it is defined this way is because I want to be able to define new classes simply by adding new gear to the game**. As in, if I want to define a new class (e.g. a monk), then I'll simply just define the exact stat distribution I want for that class (e.g. monk = equal parts wisdom + strength + dexterity) and I add new gear that requires that stat distribution.

This means that players will be able to re-spec their characters into different classes by redistributing their stats (we can probably make this extremely impractical though, for example we could make it so that all viable gear absolutely requires you to have a certain distribution of stats - as in, their stat distributions need to meet both a relative threshold and an absolute threshold) which may or may not be a good thing.

This also means that players who allocate their stats inefficiently will be able to play as narratively conflicting classes (e.g. if a player chooses to distribute their stats evenly, then they will be able to use any gear they'd like). I'm not sure if this should be allowed. The way players will be discouraged from doing this will be by making it so that allocating your stats inefficiently makes it so that you fuck up your progression. Or maybe we can make it so that the more a player uses a certain type of gear, the more "locked in" they become with their stat allocation. As in, if a player allocates their stats evenly but they only ever use healer gear, then the more they use their healer gear, the more their wisdom stat locks in. Stats that aren't locked in can be reallocated however the player wants.



# Stats
Right now, the stats are going to be:
- Dexterity
- ~~Vitality~~ I think Vitality should be rolled into strength.
- Strength
- Wisdom
I can't think of any other stats to add that would make meaningful sense. I don't like having a luck stat.

# Temperament
Players will also be able to allocate their skill points stats that're called Temperament stats. Temperament stats flavour what a character does with their normal stats. The temperament stats are:
- Damage dealer
- Supporter
- Artisan

Different stat distributions make different classes (idk if i want these classes to be explicitly defined in the game, I feel like it'd be cool to have players figure them out).
E.g. A tank is a strength supporter. A paladin is a strength + wisdom supporter. A healer is a wisdom supporter.

The artisan temperament is completely useless for combat but players who have characters that are fully specced into artisan will be valuable in the end game.



Classes
The stat distribution syntax is kinda self explanatory. If a stat isn't mentioned, then it can be anything. If a stat is mentioned, then it needs to be greater than 0. i.e. every time you see a ")" replace it with ">0)". The reason why I use "≳" instead of ">" is because ">" can't be used in filenames

|     | dex                                 | str                        | wis                         |
| --- | ----------------------------------- | -------------------------- | --------------------------- |
| dps | [[(dex) & (dps)]]            | [[warrior (str) & (dps)]]  | [[wizard (wis) & (dps)]]    |
| sup | [[thief (dex ≳ wis) & (sup ≳ dps)]] | [[tank (str) & (sup)]]     | [[healer (wis) & (sup)]]    |
| art | [[crafter (dex) & (str) & (art)]]   | [[gatherer (str) & (art)]] | [[enchanter (wis) & (art)]] |

Items and Abilities
Similar to RotMG, there is going to be an item slot for abilities, however, it is also going to be possible for armours, weapons, and accessories should also have toggles, passives, and abilities.



# Questions and FAQ
Q: Does this mean that classes that have similar stat allocations will be able to use each other's gear?
A: I don't know how I want to handle this. Some ways to handle this are:
- Yes, but they won't be able to use the most optimal gear. E.g. a [[thief (dex ≳ wis) & (sup ≳ dps)]] will be able to use low quality [[trickster (wis ≳ dex) & (sup)]] gear because the trickster stat distribution is a subset of the thief stat distribution.
- No, because class gear will be locked behind relative stat distribution. As in, a [[thief (dex ≳ wis) & (sup ≳ dps)]] won't be able to use [[trickster (wis ≳ dex) & (sup)]] gear because [[trickster (wis ≳ dex) & (sup)]] gear requires you to have no points in dps.

