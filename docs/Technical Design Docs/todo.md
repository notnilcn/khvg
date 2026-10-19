# bullets and abilities optimization

  • P1: player movement/inventory/abilities/stats submodule carve-out; single chunk source of truth; static-vs-runtime inventory slot split; item/seeds.rs dead-tunable triage.
  • P2: bullet pooling + typed interop wrappers; oversized-component extraction (TerrainComponent, LocalPlayer, ItemSidebar); binder row-args signals starting with the bullet path; singletons out of hot paths; dirty-only overlay redraw.

# Disconnect vs delayed nexus

Just discourage people who abuse disconnecting cosmetically. Disconnecting should have the same function as instant nexus but make it so that it like makes the player's character name and items a different colour or something.

# Friday Facts Factorio

## FFF-176
#### Belts optimization

After initiation process and path of trials with multithreading ([more](https://www.factorio.com/blog/post/fff-151)), a solution was achieved which I described briefly on the [forum](https://forums.factorio.com/viewtopic.php?f=5&t=39893&start=60#p238247) a few days ago. The next challenge is optimizing the very heart of Factorio - the transport belts.

Many of us know that if you are going to build a huge factory, you better put as many underground belts as possible - it just gives more UPS when you start dropping below 60. That happens due to cache coherency and other issues, but it gave us idea of treating sequences of adjacent belts as one single piece, so performance-wise they behave like the underground belt's underground part. kovarex mentioned this [here](https://www.factorio.com/blog/post/fff-148).

![](https://cdn.factorio.com/assets/img/blog/fff-148-transport-belt-segments.png)

However, this is not everything that can be done to belts. For example look at this thing:

![](https://cdn.factorio.com/assets/img/blog/fff-176-simple-movement.gif)

Currently we move every item on every belt that can move. So if you have 20,000 items on belts in your giga-factory - each of those items will consume CPU to advance its position forward. The thing to notice here is that topology of items on belts does not change all that often - i.e. when a transport belt is not blocked by something, items easily flow without changing relative position to each other, and when that belt gets blocked, that property is still true for items located before that blocked part.

So in addition to uniting belts into several consecutive pieces sharing the same array of items, we decided to rethink the way we store their coordinates. We no longer store absolute coordinates of items, instead we store the distance between items. Look at this visualization:

![](https://cdn.factorio.com/assets/img/blog/fff-176-movement-no-red.gif)

Here blue lines represent the distance between items, and yellow lines represent the initial and final gap to extremes of the cumulative transport line. Most of the time only those yellow things change which asks for an uber-optimization, where for every transport line of like 20-100 belt segments you only increment/decrement those terminal gap sizes, and do not touch items at all! Which technically this means incrementing/decrementing two integers instead of incrementing the position of all 200 items on those belts. The only place you waste performance is rethrowing an item from one sequence to another - that's why we want to keep transport line sequences as long as possible. There is another issue though - it's when belt gets blocked:

![](https://cdn.factorio.com/assets/img/blog/fff-176-movement-with-red.gif)

When this happens you don't decrement that yellow part anymore, it's already zero. Instead you decrement size of that last non-zero gap shown with red arrow. It may seem that each time the belt is blocked, you will have to scan that sequence of items to find that last gap (which may be very far away and kill all performance at smelting lines), but here goes the obvious/unobvious kung-fu... **whenever a belt compresses - it will stay that way forever**. It means that once two items are stuck close together - away from inserters they will stay stuck close forever. This property allows us to cache the index of the last positive gap location, and update it on the fly because **that index can never increase, only decrease**. So essentially this algorithm becomes amortized constant time with respect to the number of items produced by your factory, multiplied by number of transport lines that the item has to travel.

This method however has its implications. You can no longer tell the item position from its index in the transport-line array, you have to iterate all of them first to get there with the sum of all the inter-item distances. The good thing - we never actually used this property, neither did we do any binary searches. The only performance-critical thing affected by such a representation is inserters, but since they need this item-tracking information sequentially with every tick, inserters can easily track what happens inside a transport line (what was moved and what was not last tick), and update the absolute position of a tracked item still at O(1).

Also there are dynamic merging/unmerging routines which keep transport lines under inserters or side-loading at some limited range, maybe 9 tiles, while inserter-free lines can be 100 tiles long. These numbers are still to be calibrated. So far overall performance gains on items movement are at x50-100 with that O(1) optimization. Though it's not everything regarding belts, so actual gains are expected to be around x5-x10. Curved and straight belts are all merged together already, the next step will be embedding underground belts as part of a single very long line.

In the end only splitters should be the legitimate entities to break transport lines apart, in addition to some big 100-tile long limitation of how long transport line can be for the sake of a player picking/dropping items, rendering and other things to consider. So far factories performing at 25 ups start growing to 35-40 ups just because of that belts optimization, and belts are not everything these factories contain.

With 0.15 you will never ever have to build underground belts for the sake of performance :) Factorio's heart is beating nicely now and will not distract from blueprinting the truly important things.
