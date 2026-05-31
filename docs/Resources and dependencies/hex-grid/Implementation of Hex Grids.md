# Implementation of Hex Grids

 from [Red Blob Games](https://www.redblobgames.com/)

- [Home](https://www.redblobgames.com/)
- [Blog](https://www.redblobgames.com/blog/)
- [Links](https://pinboard.in/u:amitp/t:gamedev/)
- [Bluesky](https://bsky.app/profile/redblobgames.com)
- [About](http://www-cs-students.stanford.edu/~amitp/)

6 May 2015

## Table of Contents
```table-of-contents
```

Note: this article is a companion guide to my [guide to hex grids](https://www.redblobgames.com/grids/hexagons/). The data structures and functions here implement the math and algorithms described on that page.

The [main page](https://www.redblobgames.com/grids/hexagons/) covers the _theory_ for hex grid algorithms and math. Now let’s write a library to handle hex grids. The first thing to think about is what the core concepts will be.

- Since most of the algorithms work with cube coordinates, I’ll need a data structure for cube coordinates, along with algorithms that work with them. I’ll call this the **Hex** class.
- For some games I want to show coordinates to the player, and those will probably _not_ be cube, but instead axial or offset, so I’ll need a data structure for the player-visible coordinate system, as well as functions for converting back and forth. _Cube and axial are basically the same_ so I’m not going to bother implementing a separate axial system, and I’ll reuse **Hex**. For offset coordinates, I’ll make a separate data structure **Offset**.
- A grid map will likely need additional storage for terrain, objects, units, etc. A 2D array can be used but it’s not always straightforward, so I’ll create a **Map** class for this.
- To draw hexes on the screen, I need a way to convert hex coordinates into screen space. I’ll call this the **Layout** class. The main article doesn’t cover some of the additional features I want:
    - Support y-axis pointing down (common in 2D libraries) as well as y-axis pointing up (common in 3D libraries). The main article only covers y-axis pointing down.
    - Support stretched or squashed hexes, which are common with pixel graphics. The main article only supports equilateral hexes.
    - Support the 0,0 hex being located on the screen anywhere. The main article always places the 0,0 hex at x=0, y=0.
- I also need a way to convert mouse clicks and other pixel coordinates back into hex coordinates. I will put this into the **Layout** class. The same things I need to deal with for hex to screen (y-axis direction, stretch/squash, origin) have to be dealt with for screen to hex, so it makes sense to put them together.
- The main article doesn’t distinguish hexes that have integer coordinates from those with fractional coordinates. I’ll define a second class **FractionalHex** for the two algorithms where I want to have floating point coordinates: linear interpolation and rounding.
- Once I have coordinates and the `neighbors` function implemented I can use all _graph algorithms_ including movement range and pathfinding. I cover pathfinding for graphs [on another page](https://www.redblobgames.com/pathfinding/a-star/introduction.html) and won’t duplicate that code here.

I’m going to use C++ for the code samples.

You can [download the code samples](https://www.redblobgames.com/grids/hexagons/implementation.html#code) in C++, Python, C#, Haxe, Java, JavaScript, TypeScript, Lua. **These samples are a “recipe”** to use and adapt, as opposed to a ready-to-use library. I also maintain a list of [libraries](https://www.redblobgames.com/grids/hexagons/implementation.html#third-party) based on these recipes.

## 1 Hex coordinates

On the main page, I treat Cube and Axial systems separately. Cube coordinates derived from x,y,z cartesian coordinates, and use three axes q,r,s 120° apart, where q+r+s = 0. Axial coordinates have two axes q,r that are 120° apart.
/*
``` */
struct Hex { // Cube storage, cube constructor
    const int q, r, s;
    Hex(int q_, int r_, int s_): q(q_), r(r_), s(s_) {
        assert (q + r + s == 0);
    }
};
/*
```
*/
Pretty simple. Here’s a class that stores axial coordinates internally, but uses cube coordinates for the interface:
/*
``` */
struct Hex { // Axial storage, cube constructor
    const int q_, r_;
    Hex(int q, int r, int s): q_(q), r_(r) {
        assert (q + r + s == 0);
    }

    inline int q() { return q_; }
    inline int r() { return r_; }
    inline int s() { return - q_ - r_; }
};
/*
```
*/
These two classes are effectively equivalent. The first one stores `s` explicitly and the second one uses accessors and calculates `s` when needed. **Cube and Axial are essentially the same system**, so I’m not going to write a separate class for each. However **the labels on this page are different**. On the main page, the axial/cube relationship is q→x, r→z, but here I am making q→q, r→r. And that means on the main page cube coordinates are (q, -q-r, r) but on this page cube coordinates are (q, r, -q-r). _This makes my two pages inconsistent_ and I plan to update the main page to match this page.

Yet another style is to calculate `s` in the constructor instead of passing it in:
/*
``` */
struct Hex { // Cube storage, axial constructor
    const int q, r, s;
    Hex(int q_, int r_): q(q_), r(r_), s(-q_ - r_) {}
};
/*
```
*/
An advantage of the axial constructor style is that more than half the time, you’re doing this anyway at the call site. You’ll have `q` and `r` and not `s`, so you’ll pass in `-q-r` for the third parameter. You can also combine this with the second style (axial storage), and store only `q` and `r`, and calculate `s` in an accessor.

Yet another style is to use an array instead of named fields:
/*
``` */
struct Hex { // Vector storage, cube constructor
    const int v[3];
    Hex(int q, int r, int s): v{q, r, s} {
        assert (q + r + s == 0);
    }

    inline int q() { return v[0]; }
    inline int r() { return v[1]; }
    inline int s() { return v[2]; }
};
/*
```
*/
An advantage of this style is that you start seeing patterns where `q`, `r`, `s` are all treated the same way. You can write loops to handle them uniformly instead of duplicating code. You might use SIMD instructions on CPU. You might use `vec3` on the GPU. When you read the equality, `hex_add`, `hex_subtract`, `hex_scale`, `hex_length`, `hex_round`, and `hex_lerp` functions below, you’ll see how it might be useful to treat the coordinates uniformly. When you read `hex_to_pixel` and `pixel_to_hex` you’ll see that vector and matrix operations (CPU or GPU) can be used with hex coordinates when expressed this way.

Programming is full of tradeoffs. For this page, I want to focus on simplicity and readability, not performance or abstraction, so I’m going to use the first style: cube storage, cube constructor. I find it easiest to understand the algorithms in this style. However, I like all of these styles, and wouldn’t hesitate to choose any of them, as long as things are consistent in the project. In a language with multiple constructors, I’d include _both_ the axial and cube constructors for convenience. In C++, the `int` could instead be a template parameter. In C or C++11, the `int v[]` style and the `int q, r, s` style can be [merged with a union](https://www.reedbeta.com/blog/on-vector-math-libraries/). A template parameter `w` can also be used to distinguish between positions and vectors. Putting all of these together:
/*
``` */
template <typename Number, int w>
struct _Hex { // Both storage types, both constructors
    union {
        const Number v[3];
        struct { const Number q, r, s; };
    };

    Hex(Number q_, Number r_): v{q_, r_, -q_ - r_} {}
    Hex(Number q_, Number r_, Number s_): v{q_, r_, s_} {}
};
typedef _Hex<int, 1> Hex;
typedef _Hex<int, 0> HexDifference;
typedef _Hex<double, 1> FractionalHex;
typedef _Hex<double, 0> FractionalHexDifference;
/*
```
*/
I didn’t use this C++-specific style on this page because I want to make translation to other languages straightforward.

Another design alternative is to use the `x`, `y`, `z` names so that you can make hex coordinates and cartesian coordinates reuse the same data structures. If you’re already using a vector library for geometry, you can reuse that for hex coordinates, and then use a matrix library for representing hex-to-pixel and pixel-to-hex operations.

###  1.1 Equality

Equality and inequality is straightforward: two hexes are equal if their coordinates are equal. In C++, use operator ==; in Python, define a method `__eq__`; in Java, define a method `equals()`. Use the language’s standard style if possible.
/*
``` */
bool operator == (Hex a, Hex b) {
    return a.q == b.q && a.r == b.r && a.s == b.s;
}

bool operator != (Hex a, Hex b) {
    return !(a == b);
}
/*
```
*/
###  1.2 Coordinate arithmetic

Since cube coordinates come from 3D cartesian coordinates, I automatically get things like addition, subtraction, multiplication, and division. For example, you can have `Hex(2, 0, -2)` that represents two steps northeast, and add that to location `Hex(3, -5, 2)` the obvious way: `Hex(2 + 3, 0 + -5, -2 + -2)`. With other coordinate systems like offset coordinates, you can’t do that and get what you want. These operations are what you’d implement with 3D cartesian vectors, but I am using `q`, `r`, `s` names in this class instead of `x`, `y`, `z`:
/*
``` */
Hex hex_add(Hex a, Hex b) {
    return Hex(a.q + b.q, a.r + b.r, a.s + b.s);
}

Hex hex_subtract(Hex a, Hex b) {
    return Hex(a.q - b.q, a.r - b.r, a.s - b.s);
}

Hex hex_multiply(Hex a, int k) {
    return Hex(a.q * k, a.r * k, a.s * k);
}
/*
```
*/
An alternate design is to reuse an existing vec3 class from your geometry library to represent axial/cube coordinates, and in that case you won’t have to write these functions.

###  1.3 Distance

The distance between two hexes is the length of the line between them. Both the distance and length operations can come in handy. It looks like [the distance function from the main article](https://www.redblobgames.com/grids/hexagons/#distances):
/*
``` */
int hex_length(Hex hex) {
    return int((abs(hex.q) + abs(hex.r) + abs(hex.s)) / 2);
}

int hex_distance(Hex a, Hex b) {
    return hex_length(hex_subtract(a, b));
}
/*
```
*/
#### 1.3.1. Neighbors

With distance, I defined two functions: length works on one argument and distance works with two. The same is true with neighbors. The direction function is with one argument and the neighbor function is with two. It looks like [the neighbors function from the main article](https://www.redblobgames.com/grids/hexagons/#neighbors):
/*
``` */
const vector<Hex> hex_directions = {
    Hex(1, 0, -1), Hex(1, -1, 0), Hex(0, -1, 1), 
    Hex(-1, 0, 1), Hex(-1, 1, 0), Hex(0, 1, -1)
};

Hex hex_direction(int direction /* 0 to 5 */) {
    assert (0 <= direction && direction < 6);
    return hex_directions[direction];
}

Hex hex_neighbor(Hex hex, int direction) {
    return hex_add(hex, hex_direction(direction));
}
/*
```
*/
To make directions outside the range 0..5 work, use `hex_directions[(6 + (direction % 6)) % 6]`. Yeah, it’s ugly, but it will work with negative directions too. (Side note: it would’ve been nice to have a [modulo operator](https://stackoverflow.com/questions/4003232/how-to-code-a-modulo-operator-in-c-c-obj-c-that-handles-negative-numbers) in C++.)

## 2 Layout

The next major piece of functionality I need is a way to convert between hex coordinates and screen coordinates. There’s a _pointy top_ layout and a _flat top_ hex layout. The conversion uses a matrix as well as the inverse of the matrix, so I need a way to store those. Also, for drawing the corners, pointy top starts at 30° and flat top starts at 0°, so I need a place to store that too.

I’m going to define an **Orientation** helper class to store these: the 2×2 forward matrix, the 2×2 inverse matrix, and the starting angle:
/*
``` */
struct Orientation {
    const double f0, f1, f2, f3;
    const double b0, b1, b2, b3;
    const double start_angle; // in multiples of 60°
    Orientation(double f0_, double f1_, double f2_, double f3_,
                double b0_, double b1_, double b2_, double b3_,
                double start_angle_)
    : f0(f0_), f1(f1_), f2(f2_), f3(f3_),
      b0(b0_), b1(b1_), b2(b2_), b3(b3_),
      start_angle(start_angle_) {}
};
/*
```
*/
There are only two orientations, so I’m going to make constants for them:
/*
``` */
const Orientation layout_pointy
  = Orientation(sqrt(3.0), sqrt(3.0) / 2.0, 0.0, 3.0 / 2.0,
                sqrt(3.0) / 3.0, -1.0 / 3.0, 0.0, 2.0 / 3.0,
                0.5);
const Orientation layout_flat
  = Orientation(3.0 / 2.0, 0.0, sqrt(3.0) / 2.0, sqrt(3.0),
                2.0 / 3.0, 0.0, -1.0 / 3.0, sqrt(3.0) / 3.0,
                0.0);
/*
```
*/
Now I will use them for the layout class:
/*
``` */
struct Layout {
    const Orientation orientation;
    const Point size;
    const Point origin;
    Layout(Orientation orientation_, Point size_, Point origin_)
    : orientation(orientation_), size(size_), origin(origin_) {}
};
/*
```
*/
Oh, hm, I guess I need a minimal **Point** class. If your graphics/geometry library already provides one, use that.
/*
``` */
struct Point {
    const double x, y;
    Point(double x_, double y_): x(x_), y(y_) {}
};
/*
```
*/
Side note: observe how many of these are arrays of numbers underneath. Hex is int[3]. Orientation is an angle, a double, and two matrices, each double[4] or double[2][2]. Point is double[2]. Layout is an Orientation and two Points. Later on the page, FractionalHex is double[3], and OffsetCoord is int[2]. I use structs instead of arrays of numbers because giving a _name_ to things helps me understand them, and also helps with type checking. However, an alternate design choice is to reuse a standard vector library for all of these types, and then use standard matrix multiply for the layout. You can then use your library’s matrix inverse to calculate the inverse matrix. Using arrays of numbers (or a numeric array class) instead of separate structs with names will allow you to reuse more code. I chose not to do that but I think it’s a reasonable choice.

Ok, now I’m ready to write the layout algorithms.

###  2.1 Hex to screen

The main article has [two versions of axial hex-to-pixel](https://www.redblobgames.com/grids/hexagons/#hex-to-pixel), one for each orientation. The code is essentially the same except the numbers are different, so for this implementation I’ve put the numbers from the matrix into the Orientation class, as `f0` through `f3`:
/*
``` */
Point hex_to_pixel(Layout layout, Hex h) {
    const Orientation& M = layout.orientation;
    double x = (M.f0 * h.q + M.f1 * h.r) * layout.size.x;
    double y = (M.f2 * h.q + M.f3 * h.r) * layout.size.y;
    return Point(x + layout.origin.x, y + layout.origin.y);
}
/*
```
*/
The main article has two optional steps:

- [Non-zero origin](https://www.redblobgames.com/grids/hexagons/#hex-to-pixel-mod-origin) representing the _center_ of the q=0,r=0 hexagon. I store this in `layout.origin`. It’s a _translate_ transformation. If you don’t need this, set it to `Point(0, 0)`.
- [Non-uniform scaling](https://www.redblobgames.com/grids/hexagons/#hex-to-pixel-mod-pixelsize), especially for matching pixel sprite sizes. I store this in `layout.size`. It’s a _scale_ transform. If you need uniform scaling, set it to `Point(size, size)`.

I’ll show some uses of these in the [2.4](https://www.redblobgames.com/grids/hexagons/implementation.html#layout-examples) section below.

###  2.2 Screen to hex

The main article has [two versions of axial pixel-to-hex](https://www.redblobgames.com/grids/hexagons/#pixel-to-hex), one for each orientation. Again, the code is the same except for the numbers, which are the inverse of the matrix. I put the matrix inverse into the Orientation class, as `b0` through `b3`, and used it here. In the forward direction, to go from hex coordinates to screen coordinates I _first_ multiply by the matrix, _then_ multiply by the size, _then_ add the origin. To go in the reverse direction, I have to undo these. _First_ undo the origin by subtracting it, _then_ undo the size by dividing by it, _then_ undo the matrix multiply by multiplying by the inverse:
/*
``` */
FractionalHex pixel_to_hex_fractional(Layout layout, Point p) {
    const Orientation& M = layout.orientation;
    Point pt = Point((p.x - layout.origin.x) / layout.size.x, 
                     (p.y - layout.origin.y) / layout.size.y);
    double q = M.b0 * pt.x + M.b1 * pt.y;
    double r = M.b2 * pt.x + M.b3 * pt.y;
    return FractionalHex(q, r, -q - r);
}
/*
```
*/
There’s a complication here: I start with integer hex coordinates to go to screen coordinates, but when going in reverse, I have no guarantee that the screen location will be exactly at a hexagon center. Instead of getting back an integer hex coordinate, I get back a floating point value (type `double`), which means I return a **FractionalHex** instead of a **Hex**. To get back to the integer, I need to [round](https://www.redblobgames.com/grids/hexagons/#rounding) it to the nearest hex. I’ll implement that in a bit.

###  2.3 Drawing a hex

To draw a hex, I need to know where each corner is relative to the center of the hex. With the flat top orientation, the corners are at 0°, 60°, 120°, 180°, 240°, 300°. With pointy top, they’re at 30°, 90°, 150°, 210°, 270°, 330°. I encode that in the Orientation class’s `start_angle` value, either 0.0 for 0° or 0.5 for 30°.

Once I know where the corners are relative to the center, I can calculate the corners in screen locations by adding the center to each corner, and putting the coordinates into an array.
/*
``` */
Point hex_corner_offset(Layout layout, int corner) {
    Point size = layout.size;
    double angle = 2.0 * M_PI *
             (layout.orientation.start_angle + corner) / 6;
    return Point(size.x * cos(angle), size.y * sin(angle));
}

vector<Point> polygon_corners(Layout layout, Hex h) {
    vector<Point> corners = {};
    Point center = hex_to_pixel(layout, h);
    for (int i = 0; i < 6; i++) {
        Point offset = hex_corner_offset(layout, i);
        corners.push_back(Point(center.x + offset.x,
                                center.y + offset.y));
    }
    return corners;
}
/*
```
*/
###  2.4 Layout examples

Ok, let’s try it out! I have written Hex, Orientation, Layout, and Point and the functions that go with each. That’s enough for me to draw hexes. I’m going to use the Javascript version of these functions to draw some hexes in the browser.

Let’s try the two orientations, layout_pointy and layout_flat:

Let’s try uniform scaling of `size`: Point(10, 10), Point(25, 25), and Point(50, 50). When the two values are the same, we get _regular_ hexagons. The `size` value is half the height for pointy-top hexagons and half the width for flat-top hexagons.

But sometimes we want to stretch the hexagons to **fit sprite assets**, so my `size` has a separate `x` and `y` scaling. From [the main page](https://www.redblobgames.com/grids/hexagons/#basics), we can use these calculations:

- for _flat top_ art sprites `W✕H`, set `size` to Point(W/2, H/sqrt(3)). The example fits 100✕100 sprites that are slightly taller.
- for _pointy top_ art sprites `W✕H`, set `size` to Point(W/sqrt(3), H/2). The example fits 100✕100 sprites that are slightly wider.

Another thing we can do with `size` is to **flip the r axis**. Compare `size` set to Point(25, 25) and set to Point(25, -25). This is also useful if your y-axis grows upwards, as you can choose whether to make the `r` coordinate grow upwards with `y` (positive `size.y`) or downwards opposite of `y` (negative `size.y`).

The `origin` is occasionally useful too. I usually set it to Point(0, 0). That puts the _center_ of the q=0,r=0 hexagon at x=0,y=0. But if we want the **top left** of that hexagon to be at x=0,y=0 then:

- for _flat top_ hexes, set `origin` to Point(size.x, size.y * sqrt(3)/2):

- for _pointy top_ hexes, set `origin` to Point(size.x * sqrt(3)/2, size.y):

I think the above diagrams are a reasonable set of tests for the `orientation`, `size`, and `origin`. It shows that the `Layout` class can handle a wide variety of needs, without having to make different variants of the **Hex** class.

An alternate (simpler) implementation would be to always set `origin` to 0,0 and set `size` to 1,1. Then _chain_ simpler transforms together by using vector operations on the cartesian coordinates:

hex→pixel

first hex→cartesian, then _scale_ the cartesian coordinate by multiplying by the desired scale, and then _translate_ it to the desired origin.

pixel→hex

first _undo_ the translate by subtracting the origin, then _undo_ the scale by dividing by the scale, then run cartesian→hex.

## 3 Fractional Hex

For pixel-to-hex I need fractional hex coordinates. It looks like the **Hex** class, but uses `double` instead of `int`:
/*
``` */
struct FractionalHex {
    const double q, r, s;
    FractionalHex(double q_, double r_, double s_)
    : q(q_), r(r_), s(s_) {}
};
/*
```
*/
###  3.1 Hex rounding

Rounding turns a fractional hex coordinate into the nearest integer hex coordinate. The algorithm is straight out of the [main article](https://www.redblobgames.com/grids/hexagons/#rounding):
/*
``` */
Hex hex_round(FractionalHex h) {
    int q = int(round(h.q));
    int r = int(round(h.r));
    int s = int(round(h.s));
    double q_diff = abs(q - h.q);
    double r_diff = abs(r - h.r);
    double s_diff = abs(s - h.s);
    if (q_diff > r_diff and q_diff > s_diff) {
        q = -r - s;
    } else if (r_diff > s_diff) {
        r = -q - s;
    } else {
        s = -q - r;
    }
    return Hex(q, r, s);
}
/*
```
*/
In the [Screen to hex](https://www.redblobgames.com/grids/hexagons/implementation.html#pixel-to-hex) section I wrote a function that turns a pixel coordinate into a _fractional_ hex coordinate. Rounding turns that into a regular hex coordinate:

Hex pixel_to_hex_rounded(Layout layout, Point p) {
    return hex_round(pixel_to_hex_fractional(layout, p));
}

###  3.2 Line drawing

To draw a line, I linearly interpolate between two hexes, and then round it to the nearest hex. To linearly interpolate between hex coordinates I linearly interpolate each of the components (`q`, `r`, `s`) independently:
/*
``` */
float lerp(double a, double b, double t) {
    return a * (1-t) + b * t;
    /* better for floating point precision than
       a + (b - a) * t, which is what I usually write */
}

FractionalHex hex_lerp(Hex a, Hex b, double t) {
    return FractionalHex(lerp(a.q, b.q, t),
                         lerp(a.r, b.r, t),
                         lerp(a.s, b.s, t));
}
/*
```
*/
Line drawing is not too bad once I have linear interpolation:
/*
``` */
vector<Hex> hex_linedraw(Hex a, Hex b) {
    int N = hex_distance(a, b);
    vector<Hex> results = {};
    double step = 1.0 / max(N, 1);
    for (int i = 0; i <= N; i++) {
        results.push_back(hex_round(hex_lerp(a, b, step * i)));
    }
    return results;
}
/*
```
*/
I needed to stick that `max(N, 1)` bit in there to handle lines with length 0 (when A == B).

Sometimes the `hex_lerp` will output a point that’s _on an edge_. On some systems, the rounding code will push that to one side or the other, somewhat unpredictably and inconsistently. To make it always push these points in the same direction, add an “epsilon” value to `a`. This will “nudge” things in the same direction when it’s on an edge, and leave other points unaffected.
/*
``` */
vector<Hex> hex_linedraw(Hex a, Hex b) {
    int N = hex_distance(a, b);
    FractionalHex a_nudge(a.q + 1e-6, a.r + 1e-6, a.s - 2e-6);
    FractionalHex b_nudge(b.q + 1e-6, b.r + 1e-6, b.s - 2e-6);
    vector<Hex> results = {};
    double step = 1.0 / max(N, 1);
    for (int i = 0; i <= N; i++) {
        results.push_back(
            hex_round(hex_lerp(a_nudge, b_nudge, step * i)));
    }
    return results;
}
/*
```
*/
The nudge is not always needed. You might try without it first.

For a different approach, see [chad-autry’s zig-zag library](https://github.com/chad-autry/zig-zag/blob/master/index.js).

## 4 Map

There are _two_ related problems to solve: how to **generate a shape** and how to **store map data**. Let’s start with storing map data.

###  4.1 Map storage

The simplest way to store a map is to use a hash table. In C++, in order to use `unordered_map<Hex,_>` or `unordered_set<Hex>` I need to define a hash function for `Hex`. It would’ve been nice if C++ made it easier to define this, but it’s not too bad. I hash the `q` and `r` fields (I can skip `s` because it’s redundant), and combine them using the algorithm from Boost’s `hash_combine`:
/*
``` */
namespace std {
    template <> struct hash<Hex> {
        size_t operator()(const Hex& h) const {
            hash<int> int_hash;
            size_t hq = int_hash(h.q);
            size_t hr = int_hash(h.r);
            return hq ^ (hr + 0x9e3779b9 + (hq << 6) + (hq >> 2));
        }
    };
}
/*
```
*/
Here’s an example of making a map with a `float` height at each hex:

unordered_map<Hex, float> heights;
heights[Hex(1, -2, 3)] = 4.3;
cout << heights[Hex(1, -2, 3)];

The hash table by itself isn’t that useful. I need to combine it with something that creates a map shape. In graph terms, I need something that creates the nodes.

###  4.2 Map shapes

In this section I write some loops that will produce various shapes of maps. You can use these loops to make a set of hex coordinates for your map, or fill in a map data structure, or iterate over the locations in the map. I’ll write sample code that fills in a set of hex coordinates.

#### 4.2.1. Parallelograms

With axial/cube coordinates, a straightforward loop over coordinates will produce a parallelogram map instead of a rectangular one.
/*
``` */
unordered_set<Hex> map;
for (int q = q1; q <= q2; q++) {
    for (int r = r1; r <= r2; r++) {
        map.insert(Hex(q, r, -q-r)));
    }
}
/*
```
*/
There are three coordinates, and the loop requires you choose any two of them: (q,r), (s,q), or (r,s) lead to these pointy top maps, respectively:

And these flat top maps:

#### 4.2.2. Triangles

There are two directions for triangles to face, and the loop depends on which direction you use. Assuming the y axis points down, with pointy top these triangles face south/northwest/northeast, and with flat top these triangles face east/northwest/southwest.
/*
``` */
unordered_set<Hex> map;
for (int q = 0; q <= map_size; q++) {
    for (int r = 0; r <= map_size - q; r++) {
        map.insert(Hex(q, r, -q-r));
    }
}
/*
```
*/
With pointy top these triangles face north/southwest/southeast and with flat top these triangles face west/northeast/southeast:
/*
``` */
unordered_set<Hex> map;
for (int q = 0; q <= map_size; q++) {
    for (int r = map_size - q; r <= map_size; r++) {
        map.insert(Hex(q, r, -q-r));
    }
}
/*
```
*/
If your flip your y-axis, then it’ll switch north and south here, as you might expect.

#### 4.2.3. Hexagons

Generating a hexagonal shape map is described [on the main page](https://www.redblobgames.com/grids/hexagons/#range).
/*
``` */
unordered_set<Hex> map;
for (int q = -N; q <= N; q++) {
    int r1 = max(-N, -q - N);
    int r2 = min( N, -q + N);
    for (int r = r1; r <= r2; r++) {
        map.insert(Hex(q, r, -q-r));
    }
}
/*
```
*/
Here’s what I get for pointy top and flat top orientations:

#### 4.2.4. Rectangles

With axial/cube coordinates, getting rectangular maps is a little trickier! The [main article](https://www.redblobgames.com/grids/hexagons/#map-storage) gives a clue but I don’t actually show the code. The code depends on whether using flat top or pointy top hexes. The trick is to loop over offset coordinates and then convert those to axial. Let’s start with **pointy top hexes**:
/*
``` */
unordered_set<Hex> map;
for (int r = top; r <= bottom; r++) { // pointy top
    int r_offset = floor(r/2.0); // or r>>1
    for (int q = left - r_offset; q <= right - r_offset; q++) {
        map.insert(Hex(q, r, -q-r));
    }
}
/*
```
*/
That loop can produce grids like these:

  
`left=0 right=6 top=0 bottom=4`

  
`left=-3 right=+3 top=-2 bottom=+2`

The left/right/top/bottom are essentially _offset coordinates_, as offset coordinates are a more natural fit for rectangular maps.

How about **flat top hexes**?
/*
``` */
unordered_set<Hex> map;
for (int q = left; q <= right; q++) { // flat top
    int q_offset = floor(q/2.0); // or q>>1
    for (int r = top - q_offset; r <= bottom - q_offset; r++) {
        map.insert(Hex(q, r, -q-r));
    }
}

  
`left=0 right=6 top=0 bottom=4`

  
`left=-3 right=+3 top=-2 bottom=+2`
/*
```
*/
You might also need to experiment to get exactly the map you want. Try setting the offset to `floor((q+1)/2.0)` or `floor((q-1)/2.0)` instead of `floor(q/2.0)` for example, and the boundary will change slightly.

###  4.3 Optimized storage

The hash table approach is pretty generic and works with any shape of map, including weird shapes and shapes with holes. You can view it as a type of node-and-edge graph structure, storing the nodes but explicitly but calculating the edges on the fly with the `hex_neighbor` function.

A different way to store the node-and-edge graph structure is to calculate all the edges ahead of time and store them explicitly. Give each node an integer id and then use an array of arrays to store neighbors. Or make each node an object and use a field to store a list of neighbors. These graph structures are also generic and work with any shape of map. You can also use any graph algorithm on them, such as movement range, distance map, or pathfinding. Storing the edges implicitly works well when the map is regular or is being edited; storing them explicitly can work well when the map is irregularly shaped (boundary, walls, holes) and isn’t changing frequently.

Some map shapes also allow a compact 2D or 1D array. The [main article](https://www.redblobgames.com/grids/hexagons/#map-storage) gives a visual explanation. Here, I’ll give an explanation based on code. The main idea is that for all the map shapes, there is a nested loop of the form
/*
``` */
for (int a = a1; a < a2; a++) {
    for (int b = b1; b < b2; b++) {
        ...
    }
}
/*
```
*/
For compact map storage, I’ll make an array of arrays, and index it with `array[a-a1][b-b1]`. I _subtract where the loop starts_ so that the first index will be 0. For example, here’s the code for a rectangular shape **with pointy top hexes**: (for flat top hexes, the loop is different)
/*
``` */
for (int r = top; r <= bottom; r++) {
    int r_offset = floor(r/2.0);
    for (int q = left - r_offset; q <= right - r_offset; q++) {
        map.insert(Hex(q, r, -q-r));
    }
}
/*
```
*/
For pointy top hexes, variable `a` is `r`, and `b` is `q`. Value `a1` (where the `r` loop starts) is `top` and `b1` (where the `q` loop starts) is `left - floor(r/2.0)`. That means the array will be indexed `array[r-top][q-(left-floor(r/2.0))]` which simplifies to `array[r-top][q-left+floor(r/2.0)]`. Note that `floor(r/2.0)` can be written `r>>1`.

The second thing I need to know is the _size_ of the arrays. I need `a2-a1` arrays, and the size of each should be `b2-b1`. Be sure to check for off-by-1 errors: if the loop is written a <= a2 then you’ll want `a2-a1+1` arrays, and similarly for b <= b2. I can build these arrays using C++ vectors using this pattern:
/*
``` */
vector<vector<T>> map(a2-a1);
for (int a = a1; a < a2; a++) {
    map.emplace_back(b2-b1);
}
/*
```
*/
For the rectangle example, `a2-a1` becomes `bottom-top+1` and `b2-b1` becomes `right-left+1`:
/*
``` */
int height = bottom - top + 1;
vector<vector<T>> map(height);
for (int r = 0; r < height; r++) {
    int width = right - left + 1;
    map.emplace_back(width);
}
/*
```
*/
I can encapsulate all of this into a Map class:
/*
``` */
template<class T> class RectangularPointyTopMap {
    vector<vector<T>> map;

    int left_, top_;
  public:
    RectangularPointyTopMap(int left, int top, int right, int bottom)
                 : left_(left), top_(top) 
    {
        int height = bottom - top + 1;
        map.resize(height);
        for (int r = 0; r < height; r++) {
            int width = right - left + 1;
            map.emplace_back(width);
        }
    }

    inline T& at(int q, int r) {
        return map[r - top_][q - left_ + (r >> 1)];
    }
};
/*
```
*/
For the other map shapes, it’s only slightly more complicated, but the same pattern applies: I have to _study the loop that created the map_ in order to figure out the _size_ and _array access_ for the map.

1D arrays are trickier and I won’t try to tackle them here. In practice, **I rarely use array storage** for hex maps, except when the maps are large, and my code is written in C++. Although it’s more compact, it almost never makes a difference in practice in my projects. For most of my projects, I use a hash table and/or graph representation. It gives me the most flexibility and reusability. I only need the more compact storage when storage size matters.

## 5 Rotation

There are two one-step rotation functions, but which is “left” and which is “right” depends on your map orientation. You may have to swap these.
/*
``` */
Hex hex_rotate_left(Hex a)
{
    return Hex(-a.s, -a.q, -a.r);
}

Hex hex_rotate_right(Hex a)
{
    return Hex(-a.r, -a.s, -a.q);
}
/*
```
*/
Note that these are slightly different from the [main page](https://www.redblobgames.com/grids/hexagons/#rotation) because q,r,s don’t quite line up with x,y,z.

If you think of the coordinates _v_ in vector format, these operations are 3x3 matrix multiplies, M times _v_, where M = [0 0 -1; -1 0 0; 0 -1 0]. The matrix inverse M-1 = [0 -1 0; 0 0 -1; -1 0 0] rotates in the opposite direction. Raising the matrix to a power M_k_ rotates _k_ times. You can precomputate all the rotation matrices, or combine the matrix with other operations such as translate, scale, etc.

## 6 Offset coordinates

I use the names `q` and `r` for cube/axial coordinates, and `col` and `row` for offset coordinates:
/*
``` */
struct OffsetCoord {
    const int col, row;
    OffsetCoord(int col_, int row_): col(col_), row(row_) {}
};
/*
```
*/
I’m expecting that I’ll use the cube/axial **Hex** class everywhere, except for displaying to the player. That’s where offset coordinates will be useful. That means the only operations I need are converting **Hex** to **OffsetCoord** and back.

There are four offset types: odd-r, even-r, odd-q, even-q. The “r” types are used with with pointy top hexagons and the “q” types are used with flat top. Whether it’s even or odd can be encoded as an offset direction **+1** or **-1**. For pointy top, the offset direction tells us whether to slide alternate rows right or left. For flat top, the offset direction tells us whether to slide alternate columns up or down.
/*
``` */
const int EVEN = +1;
const int ODD = -1;

OffsetCoord qoffset_from_cube(int offset, Hex h) {
    assert(offset == EVEN || offset == ODD);
    int col = h.q;
    int row = h.r + int((h.q + offset * (h.q & 1)) / 2);
    return OffsetCoord(col, row);
}

Hex qoffset_to_cube(int offset, OffsetCoord h) {
    assert(offset == EVEN || offset == ODD);
    int q = h.col;
    int r = h.row - int((h.col + offset * (h.col & 1)) / 2);
    int s = -q - r;
    return Hex(q, r, s);
}

OffsetCoord roffset_from_cube(int offset, Hex h) {
    assert(offset == EVEN || offset == ODD);
    int col = h.q + int((h.r + offset * (h.r & 1)) / 2);
    int row = h.r;
    return OffsetCoord(col, row);
}

Hex roffset_to_cube(int offset, OffsetCoord h) {
    assert(offset == EVEN || offset == ODD);
    int q = h.col - int((h.row + offset * (h.row & 1)) / 2);
    int r = h.row;
    int s = -q - r;
    return Hex(q, r, s);
}
/*
```
*/
If you’re only using even or odd, you can hard-code the value of `offset` into the code, making it simpler and faster. Alternatively, `offset` can be a template parameter so that the compiler can inline and optimize it.

For offset coordinates I need to know if a row/col is even or odd. I use `a&1` ([bitwise and](https://en.wikipedia.org/wiki/Bitwise_operation#AND)) instead of `a%2` return 0 or +1. Why?

- On systems using [two’s complement](https://en.wikipedia.org/wiki/Two's_complement) representation, which is just about every system out there, `a&1` returns 0 for even `a` and 1 for odd `a`. This is what I want. It’s not strictly portable, but [should work everywhere in practice](https://www.open-std.org/jtc1/sc22/wg21/docs/papers/2018/p0907r0.html).
- The `%` remainder operator has [multiple variants](https://en.wikipedia.org/wiki/Modulo#Variants_of_the_definition): floored, euclidean, truncated, rounded, and ceiling.
    - With floored or euclidean, `(-1) % 2` is +1
    - With truncated, `(-1) % 2` is -1. _This will cause the algorithms on this page to break for negative coordinates_.
- If you know that your coordinate `a` will never be negative, you can safely use `a%2`.
- If you know your language uses floored or euclidean, you can safely use `a%2`. [Wikipedia’s list](https://en.wikipedia.org/wiki/Modulo#In_programming_languages) shows:
    - Use floored `%` in Python, Lua 5, Raku.
    - Do not use truncated `%` in C, C++, C#, Objective C, Java, Javascript, Rust, Scala, Swift, Zig, Godot, Haxe, Perl, Ruby.
    - Some languages offer both remainder and modulo. Use `mod` in Ada, Common Lisp, COBOL, CSS, Elixir, Elm, Haskell, Julia, Kotlin, Matlab, Prolog, Ruby, Standard ML, Clojure. Sometimes this function has a different name: Godot (`posmod`), Scheme (`remainder`), Rust (`rem_euclid`).
- If you don’t have `a&1` available:
    - either define an evenodd function: `abs(a) % 2` works only for 2.
    - or define a modulo function: `(a % b + b) % b` works for any positive `b`, including 2.

Also, in many (all?) languages, `&` has lower precedence than `+` so be sure to parenthesize `a&1`.

## 7 Notes

- In languages that don’t support `a>>1`, you can use `floor(a/2)` instead.
- Most of the functions are small and should be inlined in languages that support it.
- Operator overloading is sometimes abused, but might be nice for the arithmetic Hex operations `hex_add`, `hex_subtract`, `hex_scale`. I didn’t use it here.
- I wrote this code in module style, but you might prefer to write it as class style, where the functions are static or class methods. In some languages, class style is the only choice. Some of the methods might be better as instance methods.
- In languages that support more than one constructor, or optional arguments, it might be handy to have both the two-argument axial constructor and the three-argument cube constructor.

###  7.1 Cube vs Axial

Cube coordinates are three numbers, but one can be computed from the others. Whether you want to store the third one as a field or compute it in an accessor is primarily a code style decision. If performance is the main concern, the cost of the accessor vs the cost of the computation will matter most. In languages like C++ where accessors are inlined away, save the memory (accessing RAM is expensive) and use an accessor. In languages like Python where accessors are expensive, save the function call (function calls are expensive) and store the third coordinate in a field.

Also take a look at the paper [Analyzing Performance Differences between Multiple Code Versions](https://web.archive.org/web/20170924024146/http://web.cs.ucla.edu/~tianyi.zhang/perfdiff.pdf) (Kalbarczyk et al) which found axial and cube to be faster than offset for line of sight, distance, and other algorithms, but slower than offset for displaying offset coordinates (as expected). I can’t find their code though.

If performance matters, the best thing to do is to _actually measure it_.

###  7.2 C++

- These are all value types, cheap to copy and pass around. For a bit more compactness, if your maps are small you can use an int16 or int8 for the Hex and Offset class. If you’re computing `s` in an accessor, storing `q` and `r` (or `col` and `row`) as int16 will let you fit the entire coordinate into 32 bits.
- As written, these classes have a non-default constructor, so they won’t count as a POD trivial type, although I think they count as a POD standard-layout type. Switch to a default constructor and use struct initialization if you’d like them to be a POD trivial type.
- I could have written a template class `Hex<>` and instantiated it as `Hex<int>` and `Hex<double>`. I decided not to because I expect that many of the readers will be translating the code to another language.

###  7.3 Python, Javascript

- Python and other dynamically typed languages don’t need Hex and FractionalHex to be separate. You can write the FractionalHex functions to work with Hex instead, and skip the FractionalHex class.

## 8 Source Code

###  8.1 Code from this page

I have some unoptimized incomplete code in several languages, with some unit tests too, but no documentation or examples. Feel free to use these as a starting point writing your hex grid library:

- [C++](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.cpp)
- [Python](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.py)
- [C#](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.cs)
- [Haxe](https://www.redblobgames.com/grids/hexagons/codegen/output/Tests.hx)
- [Java](https://www.redblobgames.com/grids/hexagons/codegen/output/Tests.java)
- Javascript [top-level functions](https://www.redblobgames.com/grids/hexagons/codegen/output/lib-functions.js) or [with classes](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.js) or [with es6 modules](https://www.redblobgames.com/grids/hexagons/codegen/output/lib-module.js)
- [Typescript](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.ts)
- [Lua](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.lua) 5.2; see [source](https://www.redblobgames.com/grids/hexagons/codegen/OutputLua.hx) for notes about 5.1 and 5.3
- [Rust](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.rs) ; not entirely idiomatic Rust

Caveat: this is _procedurally generated code_ ([yes, really!](https://simblob.blogspot.com/2015/03/hex-grids-code-generation.html)) and doesn’t follow the best style and idiom recommendations for each language. It’d be cool to add C, Objective C, Racket, Ruby, Haskell, Swift, and others, but I don’t know when I might have time to do that.

My procedural code generator is kinda awful but if you want to take a look at it, it’s [codegen.zip](https://www.redblobgames.com/grids/hexagons/codegen/codegen.zip).

[Changed 2016-07-20] I changed the winding direction for `hex_corner_offset` to match that of `hex_neighbor`; this should not matter in theory but it’s nice for them to match.

[Changed 2018-03-10] I changed the Java, C#, and Typescript output to use instance methods instead of static methods. I added a precondition invariant check to make sure q+r+s == 0 when you call the Hex constructor. This should help catch bugs sooner.

###  8.2 Other libraries

It’s worth looking at these libraries, some of which include source code:

- 
    - [GameLogic Grids](http://gamelogic.co.za/grids/) - Unity - includes hexagons but also many other grid types
    - [Sylves](https://www.boristhebrave.com/docs/sylves/1/articles/grids/) - C# - includes hexagons but also many other grid types
    - [Hex-Grid Utilities](https://github.com/pgeerkens/HexGridUtilitiesForGames) - C# - includes field of view, pathfinding, WinForms
    - [akhra/HexCoord](https://github.com/akhra/HexCoord) - C# / Unity
    - [DigitalMachinist/HexGrid](https://github.com/DigitalMachinist/HexGrid) - C#
    - [Amaranthos/UnityHexGrid](https://github.com/Amaranthos/UnityHexGrid/) - C# / Unity
    - [svejdo1/HexGrid](https://github.com/svejdo1/HexGrid) - C#
    - [Banbury/UnityHexGrid](https://github.com/Banbury/UnityHexGrid) - C# / Unity
    - [aurelwu.github.io](https://aurelwu.github.io/) - Unity
    - [owenmoore/unity-hexagonal-grids](https://github.com/owenmoore/cube-coordinates) - Unity
    - [imurashka/HexagonalLib](https://github.com/imurashka/HexagonalLib) - C# / .NET
    - [Clpsplug/hexagonal_map](https://github.com/Clpsplug/hexagonal_map) - C# - cube/axial coordinates
    - [juna8001/HexEngine](https://github.com/juna8001/HexEngine) - C# / Unity
- 
    - [Hexworks/mixite](https://github.com/Hexworks/mixite) - Java
    - [timgilbert/scala-hexmap](https://github.com/timgilbert/scala-hexmap) - Scala
    - [mraad/grid-hex](https://github.com/mraad/grid-hex) - Scala
    - [dmccabe/khexgrid](https://github.com/dmccabe/khexgrid) - Kotlin
    - [OfflineBrain/khexagon](https://github.com/OfflineBrain/khexagon) - Kotlin
    - [DM-UK/HexTriCompositeGrid](https://github.com/DM-UK/HexTriCompositeGrid) - Java, hex + triangle grid
- 
    - [denizztret/ObjectiveHexagon](https://github.com/denizztret/ObjectiveHexagon) - Objective C
    - [pkclsoft/HexLib](https://github.com/pkclsoft/HexLib) - Objective C
    - [denizztret/ObjectiveHexagon](https://github.com/denizztret/ObjectiveHexagon) - Objective C
- 
    - [MadGeorge/AmitsHexGridLibrarySwift](https://github.com/MadGeorge/AmitsHexGridLibrarySwift) - Swift
    - [fananek/hex-grid](https://github.com/fananek/hex-grid) - Swift
- 
    - [flauwekeul/honeycomb](https://github.com/flauwekeul/honeycomb) - JavaScript + TypeScript, includes map shapes, line traversal, serialization
    - [mpalmerlee/HexagonTools](https://github.com/mpalmerlee/HexagonTools) - JavaScript + Canvas
    - [RobertBrewitz/axial-hexagonal-grid](https://github.com/RobertBrewitz/axial-hexagonal-grid) - JavaScript
    - [bodinaren/BHex.js](https://github.com/bodinaren/BHex.js) - JavaScript
    - [Hellenic/react-hexgrid](https://github.com/Hellenic/react-hexgrid/) - JavaScript / React
    - [vonWolfehaus/von-grid](https://github.com/vonWolfehaus/von-grid/) - JavaScript / Three.js
    - [othree/hexagons](https://github.com/othree/hexagons) - JavaScript - odd-r coordinates
    - [cefleet/hexAPI](https://github.com/cefleet/hexAPI) - JavaScript
    - [njlr/solid-hex](https://github.com/njlr/solid-hex) - JavaScript + pipeline operator
    - [aahdee/p5grid](https://github.com/aahdee/p5grid) - JavaScript / P5.js
    - [joshuabowers/hexagonally](https://github.com/joshuabowers/hexagonally) - TypeScript
    - [scrapcupcake/hexs6](https://github.com/scrapcupcake/hexs6) - JavaScript
    - [euoia/hex-grid.js](https://github.com/euoia/hex-grid.js) - JavaScript
- 
    - [RedFT/Hexy](https://github.com/RedFT/Hexy) - Python
    - [stephanh42/hexutil](https://github.com/stephanh42/hexutil) - Python - doubled coordinates
    - [BorisTheBrave/grids](https://github.com/BorisTheBrave/grids) - Python - hexagon, square, and triangle grids
    - [kompoth/geks](https://github.com/kompoth/geks) - Python - with map generation, pathfinding
- 
    - [czuger/rhex](https://github.com/czuger/rhex) - Ruby
    - [SpeciesFileGroup/waxy](https://github.com/SpeciesFileGroup/waxy) - Ruby
- 
    - [Voronchuck/hexagons](https://package.elm-lang.org/packages/Voronchuk/hexagons/2.0.0) - Elm
    - [danneu/elm-hex-grid](https://github.com/danneu/elm-hex-grid) - Elm
    - [etague/elm-hexagons](https://github.com/etaque/elm-hexagons) - Elm
- 
    - [alkavan/hexagon-tiles](https://github.com/alkavan/hexagon-tiles) - Rust
    - [dpc/hex2d-rs](https://github.com/dpc/hex2d-rs) - Rust
    - [leftiness/hex_math](https://github.com/leftiness/hex_math) - Rust
    - [ozkriff/zemeroth](https://github.com/ozkriff/zemeroth) - game written in Rust; hex code not separated out into its own library
    - [iancormac84/hexae](https://github.com/iancormac84/hexae) - Rust
    - [hankruiger/bestagon](https://gitlab.com/hankruiger/bestagon) - Rust
    - [cmarcbs7/chickenwire](https://github.com/cmarcbs7/chickenwire) - Rust
    - [ManevilleF/hexx](https://github.com/ManevilleF/hexx) - Rust
    - [CoCoSol007/hexing](https://github.com/CoCoSol007/hexing) - Rust
    - [algodiva/gridava](https://github.com/algodiva/gridava) - Rust, with tile, edge, and vertex coordinates
    - [lucidBrot/hexgridspiral](https://github.com/lucidBrot/hexgridspiral) - Rust, with spiral and cube coordinates, rotation, wedges, movement range, reflection
- 
    - [icrawler/Hexamoon](https://github.com/icrawler/HexaMoon) - Lua
- 
    - [droxpopuli/HexMap](https://github.com/droxpopuli/HexMap) - GDscript / Godot
    - [DDoop/HexTesting](https://github.com/DDoop/HexTesting) - GDscript / Godot#
    - [romlock/godot-gdhexgrid](https://github.com/romlok/godot-gdhexgrid) - GDscript / Godot 3
    - [HugoEnzo/HexGrid](https://github.com/HugoEnzo/HexGrid_Godot_4.0) - GDscript / Godot 4
- 
    - [nanosch/hexopia](https://github.com/nanosch/hexopia) - PHP
    - [Astatroth/php-hexgrid](https://github.com/Astatroth/php-hexgrid) - PHP
- 
    - [mhwombat/grid](https://github.com/mhwombat/grid/wiki) - Haskell - includes square, triangle, hexagonal, octagonal grids
    - [RyanMcNamara86/Hex](https://github.com/RyanMcNamara86/Hex) - Haskell
    - [andeemarks/clj-hex-grid](https://github.com/andeemarks/clj-hex-grid) - Clojure
    - [rayalex/hexgrid](https://github.com/rayalex/hexgrid) - Elixir
    - [zacharycarter’s gist](https://gist.github.com/zacharycarter/c5565930ba57af5554bb8180d566f067) - Nim
    - [pmcxs/hexgrid](https://github.com/pmcxs/hexgrid) - Go
    - [hautenessa/hexagolang](https://github.com/hautenessa/hexagolang) - Go
    - [GiovineItalia/Hexagons.jl](https://github.com/GiovineItalia/Hexagons.jl) - Julia
    - [nohkumado/hexagonal_grid](https://gitlab.com/nohkumado/hexagonal_grid) - Dart / Flutter

Also for Unity take a look at [CatlikeCoding’s tutorial](https://catlikecoding.com/unity/tutorials/hex-map/part-1/).

Email me [redblobgames@gmail.com](mailto:redblobgames@gmail.com), or comment here:

Copyright © 2026 [Red Blob Games](https://www.redblobgames.com/)  
 [RSS Feed](https://www.redblobgames.com/blog/posts.xml)

Citation:

Patel, Amit J., "Implementation of Hex Grids", 
  Red Blob Games, 2015,
  https://www.redblobgames.com/grids/hexagons/implementation.html

@online{Patel-2015,
  author       = {Patel, Amit J.}, 
  title        = {Implementation of Hex Grids},
  organization = {Red Blob Games},
  year         = {2015},
  url          = {https://www.redblobgames.com/grids/hexagons/implementation.html}
  urldate      = {2026-05-03}
}

Created 6 May 2015 with [Emacs Org-mode](https://orgmode.org/), from [implementation.org](https://www.redblobgames.com/grids/hexagons/implementation.org) and the helper Javascript file [implementation.js](https://www.redblobgames.com/grids/hexagons/implementation.js). Last modified: 28 Apr 2026