#!/usr/bin/env python3
"""Sync selected markdown files to Discord forum posts.

Each configured file gets one forum post (thread), split across as many
messages as needed to stay under Discord's 2000-character message limit.
Splitting happens on line boundaries only: a line is never cut in half,
it's just pushed into the next message if it would overflow the current
one. Re-running after an edit reuses the existing thread and edits
messages in place (adding/removing trailing messages as the file grows
or shrinks) instead of posting new threads every time.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request

CONFIG_PATH = ".discord-sync/config.json"
STATE_PATH = ".discord-sync/state.json"
MESSAGE_LIMIT = 2000
THREAD_NAME_LIMIT = 100


def load_json(path, default):
    if not os.path.exists(path):
        return default
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def chunk_by_lines(text, limit=MESSAGE_LIMIT):
    lines = text.splitlines(keepends=True)
    chunks = []
    current = ""
    for line in lines:
        # A single line longer than the limit has no valid split point to
        # borrow from, so it gets hard-split as a last resort.
        while len(line) > limit:
            if current:
                chunks.append(current)
                current = ""
            chunks.append(line[:limit])
            line = line[limit:]
        if len(current) + len(line) > limit:
            chunks.append(current)
            current = line
        else:
            current += line
    if current:
        chunks.append(current)
    return chunks or [""]


def discord_request(method, url, body=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    req.add_header("User-Agent", "DiscordSyncBot (https://github.com/Notnilc-N/ggdd, 1.0)")
    while True:
        try:
            with urllib.request.urlopen(req) as resp:
                raw = resp.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            raw = e.read()
            if e.code == 429:
                retry_after = 1.0
                try:
                    retry_after = json.loads(raw).get("retry_after", 1.0)
                except Exception:
                    pass
                time.sleep(float(retry_after) + 0.25)
                continue
            print(f"Discord API error {e.code} for {method} {url}: {raw!r}", file=sys.stderr)
            raise


def derive_title(path, text):
    return os.path.splitext(os.path.basename(path))[0][:THREAD_NAME_LIMIT]


def sync_file(path, webhook_url, state):
    with open(path, "r", encoding="utf-8") as f:
        text = f.read()
    if not text.strip():
        print(f"Skipping {path}: file is empty", file=sys.stderr)
        return
    chunks = chunk_by_lines(text)

    entry = state.get(path, {"thread_id": None, "messages": []})
    old_messages = entry["messages"]
    new_messages = []
    thread_id = entry["thread_id"]

    for i, chunk in enumerate(chunks):
        if thread_id is None:
            # First chunk of a file we haven't posted before: this call
            # creates the forum post itself.
            title = derive_title(path, text)
            resp = discord_request("POST", f"{webhook_url}?wait=true", {
                "content": chunk,
                "thread_name": title,
            })
            thread_id = resp["channel_id"]
            new_messages.append({"id": resp["id"], "content": chunk})
            continue

        if i < len(old_messages) and old_messages[i]["content"] == chunk:
            new_messages.append(old_messages[i])
            continue

        if i < len(old_messages):
            discord_request(
                "PATCH",
                f"{webhook_url}/messages/{old_messages[i]['id']}?thread_id={thread_id}",
                {"content": chunk},
            )
            new_messages.append({"id": old_messages[i]["id"], "content": chunk})
        else:
            resp = discord_request(
                "POST",
                f"{webhook_url}?wait=true&thread_id={thread_id}",
                {"content": chunk},
            )
            new_messages.append({"id": resp["id"], "content": chunk})

    # File got shorter than last sync: drop the now-unused trailing messages.
    for stale in old_messages[len(new_messages):]:
        discord_request(
            "DELETE",
            f"{webhook_url}/messages/{stale['id']}?thread_id={thread_id}",
        )

    state[path] = {"thread_id": thread_id, "messages": new_messages}


def main():
    webhook_url = os.environ["DISCORD_WEBHOOK_URL"]
    config = load_json(CONFIG_PATH, {"files": []})
    state = load_json(STATE_PATH, {})

    for path in config["files"]:
        if not os.path.exists(path):
            print(f"Skipping {path}: file not found", file=sys.stderr)
            continue
        print(f"Syncing {path}...")
        sync_file(path, webhook_url, state)
        # Save after every file so a failure partway through doesn't lose
        # progress already made on earlier files.
        save_json(STATE_PATH, state)


if __name__ == "__main__":
    main()