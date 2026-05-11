These notes are for stupid people who don't know anything about programming.

# Delegates and events
`{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` is generated from the [module bindings](<g/SpacetimeDB/module_bindings/Tables/PlayerPosition.g.cs>). The PlayerPositionHandle has the same outputs as the `{c#}_OnInsert` function, so C# knows to pass the outputs to the `{c#}_OnInsert` function whenever the `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` function fires. The `{c#}SpacetimeDBManager.Instance.Conn.Db.PlayerPosition.OnInsert` is constructed in the spacetimedb sdk. I'm too lazy to go into anymore details on this.


# Instance = this;
Laconic: A shortcut for referring to the object of that class. Only works for singletons. ^Instance-this

A common thing you'll see in a lot of `_Ready()` functions is `Instance = this;`. Before we explain it, it'd be good to go through an example to understand the order of operations.
#### Example [`StdbPlayerSync._Ready()`](<StdbPlayerSync.cs#`_Ready()`>):
1. [`StdbPlayerSync`](<StdbPlayerSync.cs`>) is in the [`project.godot`](<project.godot>) autoload, so it is automatically loaded when you open the [`project.godot`](<project.godot>) file.
	- For the smart stupid people, "automatically loaded" means that [`project.godot`](<project.godot>) literally does something like `{c#}StdbPlayerSync myObject = new StdbPlayerSync()`. However, this is done under the hood and the reasons for that is because it has pancreatic lung cancer.
2. [`StdbPlayerSync._Ready()`](<StdbPlayerSync.cs#`_Ready()`>) runs and it sets `{c#}StdbPlayerSync.Instance = this;`
	- The `{c#}StdbPlayerSync.Instance` variable is accessible to every script that has access to the [`StdbPlayerSync`](<StdbPlayerSync.cs>) class because [it is a public static variable](<StdbPlayerSync.cs#^Instance>).
	- The `this` thing is a special keyword in C# and it refers to the specific object that belongs to that class.
		- Other scripts are technically able to access the object by referring to the object's path in the scene tree: `{c#}GetNode<StdbPlayerSync>("/root/StdbPlayerSync").RemoteNodes` but doing it that way gives you melanoma (skin cancer) so people don't do it that way.

The purpose of setting `{c#}StdbPlayerSync.Instance` is so that other scripts can have access to the `{c#}StdbPlayerSync myObject = new StdbPlayerSync()` object without having to referring to that object via its path in the scene tree. This only works for singleton classes since the `{c#}Instance = this;` thing breaks when there are multiple objects assigning it, since the `{c#}Instance = this` thing for older objects is overwritten by newer objects. If you're curious on how individual objects of non-singleton classes are referenced, then it's usually done via an objectId thing like [`EnemyID`](<Enemy.cs#^EnemyID>).

Stupid stupid notes:
- The reason why we want to have access to the object even though other scripts have access to the class is because the object is what stores the spacetimedb connection data. If you zapped the object out of existence (including the object that's in the scene tree), then every other script would have to create a new connection to the spacetimedb database every time those scripts wanted to interact with the database.

# Callbacks
Callbacks are a programming technique where you pass a function into another function and do cool things with it.
```js
console.log("Start");

setTimeout(() => {
  console.log("This is the callback running after 2 seconds.");
}, 2000);

console.log("End");

// Output Order:
// Start
// End
// (2 second pause)
// This is the callback running after 2 seconds.
```

```python
# A lambda function is used here as a callback
fruits = ["apple", "Banana", "cherry"]
fruits.sort(key=lambda s: s.lower()) 

print(fruits) # Output: ['apple', 'Banana', 'cherry']
```
# Lerp
Linear interpolation.
![An In-Depth look at Lerp, Smoothstep, and Shaping Functions](<https://www.youtube.com/watch?v=YJB1QnEmlTs>)

# Difference between assigning, declaring, and initializing a variable
## Assigning a variable
Setting a variable equal to a thing. E.g. "var myVar = 5".
## Declaring a variable
Defining a variable's type. "var myVar: int"
## Initializing a variable
Similar to assigning a variable, except you expect its value to change later on. Usually done for arrays with a pre-defined size.