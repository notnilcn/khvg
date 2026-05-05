# Overview
Godot is a game engine.

# Things in godot
## Scenes
- Scenes are collections of nodes, also known as node trees.
- Kinda similar to the english meaning of the word "scene" (as in, a movie scene). It's a particular sequence of things and actions that happen, usually set in a particular place. It's a little more abstract though, for example a starting screen or a lobby can also be defined as a scene (the buttons are the things, clicking on them is the actions, and when you click on the buttons you may go to a new screen i.e. a new place). A scene can even be the player character (the player's character model + inventory + etc are the "things", moving the character model + dropping + etc is the actions, and the fact that your actions only effect your player character is the place). Scenes are also known as node trees.
## Nodes
- Nodes are the basic building blocks of Godot. Nodes are where most of your code will be. 
- Continuing on with the movie analogy for scenes, nodes are the essence of the things in the movie scene. And by essence, I mean the non-physical properties of the thing that identify that thing as that thing. E.g. if a dog was in a movie scene, then the node equivalent would be "A dog-shaped thing that can bark and has 4 legs and ears".
## Resources
- Assets, texture, sound files.
- Continuing with the movie analogy, resources are like the specific attributes of the things in the movie scene. E.g. if it's a movie remake, then the two different actors will be 2 different resources.
## Signals
- Signals are how nodes interact with each other. Nodes emit signals when events occur, and code in other nodes.
- Continuing with the movie analogy, signals are like specific story-beats or scene-occurrences that trigger another thing. Like in that one unscripted scene from avengers civil war where iron man (tony) first meets with spiderman (peter) and tony tells peter "I'm going to sit here so you move the leg" and then peter moves his leg. Tony saying that is a signal. insert soyjack.
- Signals aren't multiplayer things. It's expected that you'll use signals even in a single player game. Signals are literally just how nodes interact with each other.

## 
Similar to how scenes are node trees, your entire game is a scene tree. When you enter the main menu and navigate to play, you are passing through different scenes. When you enter the next level in a dungeon, you are entering a new scene.

