According to chatgpt, godot has the following 9 basic things:
- Scenes
	- Kinda similar to the english meaning of the word "scene". It's a particular sequence of things and actions that happen, usually set in a particular place. It's a little more abstract though, for example a starting screen or a lobby can also be defined as a scene (the buttons are the things, clicking on them is the actions, and when you click on the buttons you may go to a new screen i.e. a new place). A scene can even be the player character (the character model is the things, moving it around is the actions, and the fact that your actions only effect your player character is the place). Scenes are also known as node trees.
- Nodes
	- Nodes are kind of like the things that move in a scene, except it's more abstract than that. If we're going with the movie analogy, a node is kind of like a soul. Your soul has a body, but your soul is incapable of performing certain actions (e.g. if you're the type of person who'd never kill an innocent, then that is your soul. You have a righteous soul). Nodes, like your soul, determines what type of actions you can do and the types of things you'd have. Other people describe then as agents, but in my head an agent is a person and a person has a specific body, which isn't really what a node is. A node can have a TYPE of body, but it doesn't have a SPECIFIC body (similar to how a human soul will always be reincarnated into a human, but it won't always be the same human). Nodes are practically the fundamental building blocks of a game (you can be pedantic and go lower but you'd be gay if you did that).
- Resources
	- Assets, texture, sound files. Continuing with the soul analogy, resources are like the specific body you inhabit.
- Signals
	- Nodes emit signals when events occur. It is how nodes interract with each other.

Similar to how scenes are node trees, your entire game is a scene tree. When you enter the main menu and navigate to play, you are passing through different scenes. When you enter the next level in a dungeon, you are entering a new scene.
