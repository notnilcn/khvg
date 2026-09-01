// mcp-call.mjs — call the open-godot-mcp editor bridge directly from the CLI.
//
// The MCP tools (godot_game, godot_input, ...) are just JSON-RPC 2.0 messages
// sent to a WebSocket server the Godot editor plugin hosts on ws://127.0.0.1:6970.
// This script lets you send those same tool_invoke messages yourself, no MCP
// client needed. The Godot editor must be running (the bridge lives in-editor).
//
// Usage:
//   node mcp-call.mjs <tool> <action> [params-json]
// Example:
//   node mcp-call.mjs godot_game play '{"scene":"res://Scenes/main_menu.tscn"}'
//
// Override the port with GODOT_MCP_PORT if the bridge landed on another one.

const [tool, action, paramsJson] = process.argv.slice(2);

if (!tool || !action) {
  console.error("usage: node mcp-call.mjs <tool> <action> [params-json]");
  process.exit(2);
}

let params = {};
if (paramsJson) {
  try {
    params = JSON.parse(paramsJson);
  } catch (e) {
    console.error("invalid params JSON: " + e.message);
    process.exit(2);
  }
}

const port = process.env.GODOT_MCP_PORT || 6970;
const id = 1;
const ws = new WebSocket(`ws://127.0.0.1:${port}`);

const timer = setTimeout(() => {
  console.error("timeout: no response from bridge within 20s");
  process.exit(1);
}, 20000);

ws.onopen = () => {
  ws.send(JSON.stringify({
    jsonrpc: "2.0",
    id,
    method: "tool_invoke",
    params: { tool, action, params },
  }));
};

ws.onmessage = (ev) => {
  const msg = JSON.parse(ev.data);
  if (msg.id !== id) return; // ignore heartbeats/events
  clearTimeout(timer);
  console.log(JSON.stringify(msg.result ?? msg.error, null, 2));
  ws.close();
  process.exit(msg.error ? 1 : 0);
};

ws.onerror = () => {
  clearTimeout(timer);
  console.error(`cannot connect to ws://127.0.0.1:${port} — is the Godot editor running?`);
  process.exit(1);
};
