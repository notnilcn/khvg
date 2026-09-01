// Throwaway guard-test harness for the P0 server validation layer. Run 2.
// argv: farEnemyId chestItemId nearEnemyId weakStepDefId
// or: --flag <victimIdentityHex>  (join, flag the victim once, exit)
using SpacetimeDB;
using SpacetimeDB.Types;

if (args.Length >= 2 && args[0] == "--flag")
{
    var flagConn = DbConnection.Builder()
        .WithUri("http://127.0.0.1:3000")
        .WithDatabaseName("bullethell")
        .OnConnect((c, id, token) => Console.WriteLine($"CONNECTED {id}"))
        .Build();
    var flagRunning = true;
    var flagPump = new Thread(() => { while (flagRunning) { try { flagConn.FrameTick(); } catch { } Thread.Sleep(10); } });
    flagPump.IsBackground = true; flagPump.Start();
    Thread.Sleep(3000);
    flagConn.SubscriptionBuilder().Subscribe(new[] { "SELECT * FROM local_player_profiles" });
    Thread.Sleep(1500);
    flagConn.Reducers.SetUsername("flagbot");
    Thread.Sleep(500);
    var flagName = "FB" + Guid.NewGuid().ToString()[..6];
    flagConn.Reducers.CreateProfile(flagName, "Knight");
    Thread.Sleep(800);
    var fp = flagConn.Db.LocalPlayerProfiles.Iter().First(p => p.ProfileName == flagName);
    flagConn.Reducers.JoinWorld(fp.ProfileId);
    Thread.Sleep(1200);
    flagConn.Reducers.FlagPlayerHit(Identity.FromHexString(args[1]));
    Thread.Sleep(1000);
    Console.WriteLine("FLAGGED " + args[1]);
    flagRunning = false;
    flagConn.Disconnect();
    return;
}

if (args[0] == "--swap3")
{
    // 3-swap cooldown laundry test: ability -> backpack A, plain swap A<->B, backpack B -> ability.
    var conn3 = DbConnection.Builder()
        .WithUri("http://127.0.0.1:3000")
        .WithDatabaseName("bullethell")
        .OnConnect((c, id, token) => Console.WriteLine($"CONNECTED {id}"))
        .Build();
    conn3.OnUnhandledReducerError += (ctx, ex) => Console.WriteLine("REDUCER ERROR: " + ex.Message);
    var running3 = true;
    var pump3 = new Thread(() => { while (running3) { try { conn3.FrameTick(); } catch { } Thread.Sleep(10); } });
    pump3.IsBackground = true; pump3.Start();
    Thread.Sleep(3000);
    conn3.SubscriptionBuilder().Subscribe(new[] { "SELECT * FROM local_player_profiles", "SELECT * FROM local_player_inventory" });
    Thread.Sleep(1500);
    conn3.Reducers.SetUsername("swapbot");
    Thread.Sleep(500);
    var name3 = "SB" + Guid.NewGuid().ToString()[..6];
    conn3.Reducers.CreateProfile(name3, "Knight");
    Thread.Sleep(800);
    var p3 = conn3.Db.LocalPlayerProfiles.Iter().First(p => p.ProfileName == name3);
    conn3.Reducers.JoinWorld(p3.ProfileId);
    Thread.Sleep(1200);
    Console.WriteLine($"WAITING: give_item '\"swapbot\"' '\"{name3}\"' '\"Delete Orb\"'");
    var dl = DateTime.UtcNow + TimeSpan.FromSeconds(120);
    while (DateTime.UtcNow < dl && !conn3.Db.LocalPlayerInventory.Iter().Any(s => s.ItemId == "Delete Orb")) Thread.Sleep(1000);
    List<PlayerInventorySlot> Slots3() => conn3.Db.LocalPlayerInventory.Iter().ToList();
    uint Free3(uint lo, uint hi) => Slots3().Where(s => s.SlotIndex >= lo && s.SlotIndex <= hi && s.ItemId == null && s.OccupiedBy == null).Select(s => s.SlotIndex).DefaultIfEmpty(uint.MaxValue).Min();
    var orb3 = Slots3().FirstOrDefault(s => s.ItemId == "Delete Orb");
    if (orb3 == null) { Console.WriteLine("FAIL: orb never arrived"); return; }
    var abil3 = Free3(32, 37); var g1 = Free3(7, 30);
    Console.WriteLine($"orb at {orb3.SlotIndex}, ability {abil3}, g1 {g1}");
    conn3.Reducers.SwapSlots(orb3.SlotIndex, abil3); Thread.Sleep(700);
    conn3.Reducers.ActivateAbility(abil3, 0f, 0f); Thread.Sleep(700);
    var cdSet = Slots3().FirstOrDefault(s => s.SlotIndex == abil3)?.CooldownUntil;
    Console.WriteLine($"cooldown after activate: {(object?)cdSet?.ToString() ?? "null"}");
    conn3.Reducers.SwapSlots(abil3, g1); Thread.Sleep(700);           // region -> backpack g1
    var g2 = Slots3().Where(s => s.SlotIndex >= 7 && s.SlotIndex <= 30 && s.SlotIndex != g1 && s.ItemId == null && s.OccupiedBy == null).Select(s => s.SlotIndex).DefaultIfEmpty(uint.MaxValue).Min();
    conn3.Reducers.SwapSlots(g1, g2); Thread.Sleep(700);               // plain backpack swap (the hole)
    var cdG1 = Slots3().FirstOrDefault(s => s.SlotIndex == g1)?.CooldownUntil;
    var cdG2 = Slots3().FirstOrDefault(s => s.SlotIndex == g2)?.CooldownUntil;
    var orbAt = Slots3().FirstOrDefault(s => s.ItemId == "Delete Orb")?.SlotIndex;
    Console.WriteLine($"after plain swap {g1}<->{g2}: orb at {orbAt}, g1 cd={(object?)cdG1?.ToString() ?? "null"}, g2 cd={(object?)cdG2?.ToString() ?? "null"}");
    conn3.Reducers.SwapSlots(g2, abil3); Thread.Sleep(700);            // backpack g2 -> ability
    var cdFinal = Slots3().FirstOrDefault(s => s.SlotIndex == abil3)?.CooldownUntil;
    var ok = cdSet != null && cdG2 != null && cdG1 == null && cdFinal != null && cdFinal.Equals(cdSet);
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} [swap3-laundry] cooldown after 3-swap round trip: {(object?)cdFinal?.ToString() ?? "null"} (expected {(object?)cdSet?.ToString() ?? "null"})");
    running3 = false;
    conn3.Disconnect();
    return;
}

var farEnemyId = ulong.Parse(args[0]);
var chestItemId = ulong.Parse(args[1]);
var nearEnemyId = ulong.Parse(args[2]);
var weakStepId = ulong.Parse(args[3]);

var errors = new List<string>();
var errLock = new object();
int errMark = 0;
void MarkErrors() { lock (errLock) errMark = errors.Count; }
List<string> NewErrors() { lock (errLock) return errors.Skip(errMark).ToList(); }

var connected = new ManualResetEventSlim();
var conn = DbConnection.Builder()
    .WithUri("http://127.0.0.1:3000")
    .WithDatabaseName("bullethell")
    .OnConnect((c, id, token) => { Console.WriteLine($"CONNECTED {id}"); connected.Set(); })
    .OnConnectError(e => Console.WriteLine("CONNECT ERROR: " + e.Message))
    .Build();

conn.OnUnhandledReducerError += (ctx, ex) =>
{
    lock (errLock) errors.Add(ex.Message);
    Console.WriteLine("REDUCER ERROR: " + ex.Message);
};

var running = true;
var pump = new Thread(() => { while (running) { try { conn.FrameTick(); } catch { } Thread.Sleep(10); } });
pump.IsBackground = true;
pump.Start();

if (!connected.Wait(TimeSpan.FromSeconds(10))) { Console.WriteLine("FAIL: no connection"); return; }

conn.SubscriptionBuilder().Subscribe(new[]
{
    "SELECT * FROM local_player_profiles",
    "SELECT * FROM local_player_position",
    "SELECT * FROM local_player_inventory",
    "SELECT * FROM local_player_data",
    "SELECT * FROM nearby_loot_drops",
    "SELECT * FROM nearby_enemies",
});
Thread.Sleep(1500);

conn.Reducers.SetUsername("guardbot");
Thread.Sleep(500);
var runId = Guid.NewGuid().ToString()[..6];
conn.Reducers.CreateProfile("GB" + runId, "Knight");
Thread.Sleep(800);
var profile = conn.Db.LocalPlayerProfiles.Iter().FirstOrDefault(p => p.ProfileName == "GB" + runId);
if (profile == null) { Console.WriteLine("FAIL: profile not created"); return; }
conn.Reducers.JoinWorld(profile.ProfileId);
Thread.Sleep(1200);

PlayerPosition? Pos() => conn.Db.LocalPlayerPosition.Iter().FirstOrDefault();
var pos0 = Pos();
if (pos0 == null) { Console.WriteLine("FAIL: no position row after join"); return; }
Console.WriteLine($"JOINED at ({pos0.X},{pos0.Y}) profile={profile.ProfileId}");

Console.WriteLine($"WAITING for items: give_item '\"guardbot\"' '\"GB{runId}\"' '\"Bow\"' / '\"Delete Orb\"'");
var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
while (DateTime.UtcNow < deadline)
{
    var inv = conn.Db.LocalPlayerInventory.Iter().Select(s => s.ItemId).ToList();
    if (inv.Contains("Bow") && inv.Contains("Delete Orb")) break;
    Thread.Sleep(1000);
}
var rows = conn.Db.LocalPlayerInventory.Iter().Where(s => s.ItemId != null).ToList();
Console.WriteLine("SLOTS: " + string.Join(", ", rows.Select(s => $"{s.SlotIndex}:{s.ItemId}")));
if (!rows.Any(s => s.ItemId == "Bow") || !rows.Any(s => s.ItemId == "Delete Orb")) { Console.WriteLine("FAIL: items never arrived"); return; }

var bowSlot = rows.First(s => s.ItemId == "Bow");
if (bowSlot.SlotIndex != 0)
{
    conn.Reducers.SwapSlots(bowSlot.SlotIndex, 0);
    Thread.Sleep(600);
}

void Report(string name, bool pass, string detail) =>
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} [{name}] {detail}");

// ---- T1: movement displacement clamp ----
var start = Pos()!;
conn.Reducers.ReportMovement(10000f, 10000f, 0.785f, 100f);
Thread.Sleep(500);
var after = Pos()!;
var jump = Math.Sqrt((after.X - start.X) * (after.X - start.X) + (after.Y - start.Y) * (after.Y - start.Y));
Report("clamp-teleport", jump < 200.0, $"jumped {jump:F1} units, landed ({after.X:F1},{after.Y:F1})");
conn.Reducers.ReportMovement(after.X + 20, after.Y, 0f, 100f);
Thread.Sleep(500);
var after2 = Pos()!;
Report("clamp-normal", Math.Abs(after2.X - (after.X + 20)) < 1.0 && Math.Abs(after2.Y - after.Y) < 1.0,
    $"small move landed ({after2.X:F1},{after2.Y:F1})");

// ---- T2: report_enemy_hit range check ----
MarkErrors();
conn.Reducers.ReportEnemyHit(farEnemyId);
Thread.Sleep(500);
Report("enemy-hit-range", NewErrors().Any(m => m.Contains("not near")), "errors: " + string.Join(" | ", NewErrors()));

// ---- T3: report_enemy_hit rate limit (near enemy so calls SUCCEED and count) ----
MarkErrors();
for (int i = 0; i < 60; i++) { conn.Reducers.ReportEnemyHit(nearEnemyId); Thread.Sleep(10); }
Thread.Sleep(1000);
var e3 = NewErrors();
var enemyHp = conn.Db.NearbyEnemies.Iter().FirstOrDefault(e => e.EnemyId == nearEnemyId)?.Hp;
Report("enemy-hit-rate", e3.Count(m => m.Contains("rate exceeded")) >= 10,
    $"{e3.Count(m => m.Contains("rate exceeded"))} rate rejections of 60 calls, other errors: {e3.Where(m => !m.Contains("rate exceeded")).Count()}, enemy hp now {enemyHp}");
Thread.Sleep(1500);

// ---- T7: cooldown survives backpack round-trip ----
List<PlayerInventorySlot> Slots() => conn.Db.LocalPlayerInventory.Iter().ToList();
uint FreeSlot(uint lo, uint hi) => Slots().Where(s => s.SlotIndex >= lo && s.SlotIndex <= hi && s.ItemId == null && s.OccupiedBy == null).Select(s => s.SlotIndex).DefaultIfEmpty(uint.MaxValue).Min();
var orbSlot = Slots().First(s => s.ItemId == "Delete Orb").SlotIndex;
var abil = FreeSlot(32, 37);
var gen = FreeSlot(7, 30);
Console.WriteLine($"orb at {orbSlot}, free ability slot {abil}, free general slot {gen}");
MarkErrors();
conn.Reducers.SwapSlots(orbSlot, abil);
Thread.Sleep(700);
conn.Reducers.ActivateAbility(abil, 0f, 0f);
Thread.Sleep(700);
var cdBefore = Slots().FirstOrDefault(s => s.SlotIndex == abil)?.CooldownUntil;
Console.WriteLine($"after activate: slot{abil} cooldown_until={(object?)cdBefore?.ToString() ?? "null"}");
conn.Reducers.SwapSlots(abil, gen);
Thread.Sleep(700);
var cdMid = Slots().FirstOrDefault(s => s.SlotIndex == gen)?.CooldownUntil;
conn.Reducers.SwapSlots(gen, abil);
Thread.Sleep(700);
var cdAfter = Slots().FirstOrDefault(s => s.SlotIndex == abil)?.CooldownUntil;
Console.WriteLine($"round-trip: out={(object?)cdMid?.ToString() ?? "null"} back={(object?)cdAfter?.ToString() ?? "null"}");
Report("cooldown-roundtrip", cdBefore != null && cdMid != null && cdAfter != null && cdAfter.Equals(cdBefore),
    "cooldown_until preserved through ability->backpack->ability");
var t7err = NewErrors();
if (t7err.Count > 0) Console.WriteLine("T7 errors: " + string.Join(" | ", t7err));

// ---- T5: pickup_drop range check ----
MarkErrors();
conn.Reducers.DropItem(abil);
Thread.Sleep(700);
var drop = conn.Db.NearbyLootDrops.Iter().FirstOrDefault();
if (drop == null) { Report("pickup-range", false, "no drop row appeared"); }
else
{
    var here = Pos()!;
    conn.Reducers.ReportMovement(here.X + 64, here.Y, 0f, 100f); Thread.Sleep(300);
    conn.Reducers.ReportMovement(here.X + 128, here.Y, 0f, 100f); Thread.Sleep(300);
    conn.Reducers.ReportMovement(here.X + 192, here.Y, 0f, 100f); Thread.Sleep(500);
    conn.Reducers.PickupDrop(drop.DropId);
    Thread.Sleep(700);
    var e5 = NewErrors();
    var stillThere = conn.Db.NearbyLootDrops.Iter().Any(d => d.DropId == drop.DropId);
    Report("pickup-range", e5.Any(m => m.Contains("too far")) && stillThere, "errors: " + string.Join(" | ", e5));
    conn.Reducers.ReportMovement(here.X + 128, here.Y, 0f, 100f); Thread.Sleep(300);
    conn.Reducers.ReportMovement(here.X + 64, here.Y, 0f, 100f); Thread.Sleep(300);
    conn.Reducers.ReportMovement(here.X, here.Y, 0f, 0f); Thread.Sleep(500);
    MarkErrors();
    conn.Reducers.PickupDrop(drop.DropId);
    Thread.Sleep(700);
    Report("pickup-near", !conn.Db.NearbyLootDrops.Iter().Any(d => d.DropId == drop.DropId), "picked up after walking back; errors: " + string.Join(" | ", NewErrors()));
}

// ---- T6: take_from_chest range check ----
MarkErrors();
conn.Reducers.TakeFromChest(chestItemId);
Thread.Sleep(700);
Report("chest-range", NewErrors().Any(m => m.Contains("too far")), "errors: " + string.Join(" | ", NewErrors()));

// ---- T4: report_hit incoming counter (5 valid self-hits; verify via SQL after) ----
var hpBefore = conn.Db.LocalPlayerData.Iter().FirstOrDefault()?.Hp;
MarkErrors();
for (int i = 0; i < 5; i++) { conn.Reducers.ReportHit(new SequenceStepRef.Single(weakStepId), nearEnemyId); Thread.Sleep(30); }
Thread.Sleep(1000);
var hpAfter = conn.Db.LocalPlayerData.Iter().FirstOrDefault()?.Hp;
var e4 = NewErrors();
Report("hit-incoming", e4.Count == 0 && hpAfter < hpBefore, $"hp {hpBefore} -> {hpAfter}, errors: {string.Join(" | ", e4)}");

Console.WriteLine("DONE");
running = false;
conn.Disconnect();
