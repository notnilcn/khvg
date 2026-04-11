Every player runs a copy of the game, including the server. Everyone has access to the same functions.

# peer_id
When players connect to a server, they're assigned a peer_id via some internal websocket function that you don't need to know about. The host server has a peer_id of 1 and every other player has a peer_id greater than 

1.

# rpc(mode,transfer_mode,call_local)
## mode: "authority" or "any_peer"

Players communicate to the server via @rpc, whose first argument can be either "any_peer" or "authority". This argument specifies who determines what is true.
### "authority"
Practically speaking, "authority" means "the machine that controls the source of truth is the authority". Programmatically speaking, it means that the only machine that can call that function is the authority. This is usually used for important functions need to be validated by a server (e.g. you don't want damage calculations being calculated by clients. Even though every player should be running the same copy of the game and the damage calculations should be the same, cheaters still exist & they may send rpc calls that break the game).
The authority can be set using node.set_multiplayer_authority(id) and it usually makes sense to set this to the server (the game code doesn't do this which we will elaborate on after explaining any_peer).
### "any_peer"
Practically speaking, "any_peer" means "the machine that controls the source of truth is anyone". Programatically speaking, it means that any machine can call this function on your current machine through the server (as in, the server acts as a relay between each machine. it does not mean that different clients send packets directly to each other. kinda inane to make that distinction but i needed it when i first learned about this). This is usually done for things like player movement and player aiming.
### when to use authority vs any_peer + exceptions & special cases

Use "authority" when you want the server to determine the source of truth (authoritative networking). Use "any_peer" when you want the server to determine the source of truth (kinda like client side prediction but not really because player movement + aiming is kinda supposed to be decided by the client). If you want things to be hybrid (which is what your game is), use "any_peer" but have a validating function that validates the outputs.
## transfer_mode: "reliable", "unreliable", "unreliable_ordered"

unreliable: Packets sent with this mode are not guaranteed to arrive and may be received out of order. This is ideal for high-frequency updates like player movement or position data, where occasional packet loss is acceptable and resending every packet would cause network congestion. 

unreliable_ordered: Packets are not guaranteed to arrive, but if they do, they are processed in the order they were sent. This mode is useful when the sequence of events matters, but occasional loss is tolerable. Incorrect use can lead to packet loss if later packets are ignored due to ordering constraints. 

reliable: Packets are guaranteed to be delivered and processed in order. Godot will resend packets until acknowledged by the recipient. This mode is essential for critical game events, such as scoring, game state changes, or triggering end-of-game events, where missing a packet could desynchronize clients. 

## call_local: "call_local", "call_remote"
If `call_local`, then function is also executed locally when called via `rpc()`.