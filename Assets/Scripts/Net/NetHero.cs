using System.Collections.Generic;
using System.Linq;
using Mirror;
using PudgeWars.Gameplay;
using UnityEngine;

namespace PudgeWars.Net
{
    /// <summary>
    /// Network side of a hero. The server simulates the hero with the usual gameplay components and publishes what
    /// clients need to show it: team and nickname, alive / falling / caught, health and mana, multi-kill count, its own kills / deaths /
    /// rescues / times rescued, gold and items, the hook's head and cooldown, animations and gold popups (the position goes with
    /// NetworkTransform), and the hook's catches. The owning client sends its input instead of moving the hero itself:
    /// movement direction, hook casts and purchases.
    /// </summary>
    public class NetHero : NetworkBehaviour
    {
        static readonly List<NetHero> onServer = new();
        /// <summary>Heroes spawned by this server (players and bots).</summary>
        public static IReadOnlyList<NetHero> OnServer => onServer;

        [Tooltip("Set before spawning, the same on server and clients (bot or player hero).")]
        public bool isBot;

        [SyncVar(hook = nameof(OnTeamChanged))] Team team;
        [SyncVar(hook = nameof(OnNicknameChanged))] string nickname;
        [SyncVar] byte heroFlags = AliveFlag;
        [SyncVar] float health;
        [SyncVar] float regen;
        [SyncVar] float mana;
        [SyncVar] float maxMana;
        [SyncVar] float manaRegen;
        [SyncVar] int multiKill;
        [SyncVar] int kills;
        [SyncVar] int deaths;
        [SyncVar] int rescues;
        [SyncVar] int rescued;
        [SyncVar(hook = nameof(OnGoldChanged))] int gold;
        [SyncVar(hook = nameof(OnItemsChanged))] string items = "";
        [SyncVar] string animState;
        // timed effects from items: "catalogIndex:endsAt(network time):duration" per effect, comma-separated
        [SyncVar(hook = nameof(OnEffectsChanged))] string effects = "";
        [SyncVar] byte hookFlags;
        [SyncVar] Vector3 hookHead;
        [SyncVar] Vector3 hookDirection;
        [SyncVar] double hookReadyAt;

        const byte AliveFlag = 1, FallingFlag = 2, HookedFlag = 4;
        const byte BusyFlag = 1, VisibleFlag = 2, FlyingFlag = 4;
        const float InputResendInterval = 0.5f;

        public Team Team => team;

        Pudge hookable;
        HookAbility hook;
        HeroInventory inventory;
        PudgeMotor motor;
        PudgeAnimation anim;
        NetworkTransformBase netTransform;
        NetPrefabs prefabs;
        float lastReadyAt = float.NaN;
        int lastEffectsVersion = -1;
        Vector3 sentMove, sentLook;
        float sentAt;

        void Awake()
        {
            hookable = GetComponent<Pudge>();
            hook = GetComponent<HookAbility>();
            inventory = GetComponent<HeroInventory>();
            motor = GetComponent<PudgeMotor>();
            anim = GetComponent<PudgeAnimation>();
            netTransform = GetComponent<NetworkTransformBase>();
            prefabs = NetPrefabs.Load();
            syncInterval = 0f; // with every server send, as NetworkTransform: the hook's head is interpolated from it
        }

        /// <summary>Server, before spawning: whose hero this is.</summary>
        public void Setup(Team side, string nick)
        {
            team = side;
            nickname = nick;
            hookable.team = side;
            hookable.nickname = nick;
            name = $"{(isBot ? "Bot" : "Player")} {nick}";
        }

        // server //////////////////////////////////////////////////////////////

        public override void OnStartServer()
        {
            onServer.Add(this);
            if (inventory != null)
            {
                inventory.Changed += PublishInventory;
                inventory.GoldAdded += RpcGoldAdded;
                inventory.Used += OnItemUsed;
            }
            if (anim != null) anim.Played += OnAnimationPlayed;
            hookable.Respawned += OnRespawned;
            if (hook != null) hook.TargetCaught += OnTargetCaught;
            PublishInventory();
        }

        public override void OnStopServer()
        {
            onServer.Remove(this);
            if (inventory != null)
            {
                inventory.Changed -= PublishInventory;
                inventory.GoldAdded -= RpcGoldAdded;
                inventory.Used -= OnItemUsed;
            }
            if (anim != null) anim.Played -= OnAnimationPlayed;
            hookable.Respawned -= OnRespawned;
            if (hook != null) hook.TargetCaught -= OnTargetCaught;
        }

        void PublishInventory()
        {
            gold = inventory != null ? inventory.Gold : 0;
            // "index:charges" per slot, "-1:0" for an empty one (the slots keep their places)
            items = inventory != null && prefabs != null
                ? string.Join(",", Enumerable.Range(0, HeroInventory.SlotCount)
                    .Select(slot => $"{(inventory.ItemIn(slot) != null ? prefabs.IndexOf(inventory.ItemIn(slot)) : -1)}:{inventory.ChargesAt(slot)}"))
                : "";
        }

        void OnAnimationPlayed(string state, float fade, float lockSeconds)
        {
            animState = state;
            RpcPlayAnimation(state, fade, lockSeconds);
        }

        void OnTargetCaught(Pudge target) => RpcTargetCaught(target != null ? target.gameObject : null);

        [ClientRpc]
        void RpcTargetCaught(GameObject target)
        {
            if (!isServer && hook != null) hook.ApplyRemoteCatch(target != null ? target.GetComponent<Pudge>() : null);
        }

        // without this the clients would see the hero slide out of the pit to its spawn point
        void OnRespawned(Pudge who)
        {
            if (netTransform != null) netTransform.ServerTeleport(who.transform.position, who.transform.rotation);
        }

        /// <summary>Server: the state clients show, refreshed every frame (sent ~30 times a second).</summary>
        void Publish()
        {
            heroFlags = (byte)((hookable.IsAlive ? AliveFlag : 0) | (hookable.IsFalling ? FallingFlag : 0) | (hookable.IsHooked ? HookedFlag : 0));
            health = Mathf.Ceil(hookable.Health); // whole points, as the bars show it: not resent for every bit of regen
            regen = Mathf.Round(hookable.HealthRegen * 10f) / 10f;
            mana = Mathf.Ceil(hookable.Mana);
            maxMana = hookable.MaxMana;
            manaRegen = Mathf.Round(hookable.ManaRegen * 100f) / 100f;
            if (hookable.EffectsVersion != lastEffectsVersion && prefabs != null)
            {
                lastEffectsVersion = hookable.EffectsVersion;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                effects = string.Join(",", hookable.TimedEffects().Select(e =>
                    $"{prefabs.IndexOf(e.item)}:{(NetworkTime.time + (e.until - Time.time)).ToString(ci)}:{e.duration.ToString(ci)}"));
            }
            multiKill = Arena.Instance != null ? Arena.Instance.Streaks.MultiKillCount(hookable) : 0;
            var score = Arena.Instance != null ? Arena.Instance.ScoreOf(hookable) : default;
            kills = score.kills;
            deaths = score.deaths;
            rescues = score.rescues;
            rescued = score.rescued;
            if (hook == null) return;
            hookFlags = (byte)((hook.IsBusy ? BusyFlag : 0) | (hook.HeadVisible ? VisibleFlag : 0) | (hook.IsFlying ? FlyingFlag : 0));
            if (hook.HeadVisible)
            {
                hookHead = hook.hookHead.position;
                hookDirection = hook.HeadDirection;
            }
            // ready time moves to the network clock only when it changes, so it is not resent every frame
            if (hook.ReadyAt != lastReadyAt)
            {
                lastReadyAt = hook.ReadyAt;
                hookReadyAt = NetworkTime.time + Mathf.Max(0f, hook.ReadyAt - Time.time);
            }
        }

        [Command]
        void CmdInput(Vector3 move, Vector3 look)
        {
            if (motor == null) return;
            motor.SetMoveInput(Vector3.ClampMagnitude(new Vector3(move.x, 0f, move.z), 1f));
            motor.SetLookDirection(look.sqrMagnitude > 1e-6f ? look : (Vector3?)null);
        }

        [Command]
        void CmdCast(Vector3 point)
        {
            if (hook != null) hook.TryCast(point);
        }

        [Command]
        void CmdBuy(int index)
        {
            var item = prefabs != null ? prefabs.ItemAt(index) : null;
            if (item != null && inventory != null) inventory.TryBuy(item);
        }

        [Command]
        void CmdUseItem(int slot, Vector3 point, int tree)
        {
            if (inventory != null) inventory.TryUse(slot, point, tree);
        }

        [Command]
        void CmdSellItem(int slot)
        {
            if (inventory != null) inventory.TrySell(slot);
        }

        [Command]
        void CmdMoveItem(int from, int to)
        {
            if (inventory != null) inventory.TryMove(from, to);
        }

        // clients /////////////////////////////////////////////////////////////

        public override void OnStartClient()
        {
            if (isServer) return; // host: the server's objects are shown as they are
            ApplyIdentity();
            ApplyInventory();
            OnEffectsChanged(null, effects);
            if (!string.IsNullOrEmpty(animState)) anim?.Play(animState, 0f);
            Show();
        }

        public override void OnStartLocalPlayer()
        {
            // the own hero gets the controls; on a pure client its casts and purchases go to the server
            if (!TryGetComponent<PlayerInputController>(out _)) gameObject.AddComponent<PlayerInputController>();
            if (hook != null) hook.CastRequested += CmdCast;
            if (inventory != null)
            {
                inventory.BuyRequested += RequestBuy;
                inventory.UseRequested += CmdUseItem;
                inventory.SellRequested += CmdSellItem;
                inventory.MoveRequested += CmdMoveItem;
            }
        }

        public override void OnStopLocalPlayer()
        {
            if (hook != null) hook.CastRequested -= CmdCast;
            if (inventory != null)
            {
                inventory.BuyRequested -= RequestBuy;
                inventory.UseRequested -= CmdUseItem;
                inventory.SellRequested -= CmdSellItem;
                inventory.MoveRequested -= CmdMoveItem;
            }
        }

        void RequestBuy(ItemDef item)
        {
            int index = prefabs != null ? prefabs.IndexOf(item) : -1;
            if (index >= 0) CmdBuy(index);
        }

        void OnTeamChanged(Team _, Team __) => ApplyIdentity();
        void OnNicknameChanged(string _, string __) => ApplyIdentity();
        void OnGoldChanged(int _, int __) => ApplyInventory();
        void OnItemsChanged(string _, string __) => ApplyInventory();

        void ApplyIdentity()
        {
            if (isServer) return;
            hookable.team = team;
            hookable.nickname = nickname;
            name = $"{(isBot ? "Bot" : "Player")} {nickname}";
        }

        void ApplyInventory()
        {
            if (isServer || inventory == null || prefabs == null) return;
            var owned = new List<(ItemDef, int)>();
            if (!string.IsNullOrEmpty(items))
                foreach (var slot in items.Split(','))
                {
                    var parts = slot.Split(':');
                    var item = int.TryParse(parts[0], out var i) ? prefabs.ItemAt(i) : null;
                    int left = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : 0;
                    owned.Add((item, left)); // an empty slot too: the places matter
                }
            inventory.ApplyRemoteState(gold, owned);
        }

        /// <summary>Client: the server's state of the hero and its hook.</summary>
        void Show()
        {
            hookable.ApplyRemoteState((heroFlags & AliveFlag) != 0, (heroFlags & FallingFlag) != 0, (heroFlags & HookedFlag) != 0);
            hookable.ApplyRemoteHealth(health);
            hookable.ApplyRemoteRegen(regen);
            hookable.ApplyRemoteMana(mana, maxMana, manaRegen);
            if (Arena.Instance != null)
            {
                Arena.Instance.ApplyRemoteMultiKill(hookable, multiKill);
                Arena.Instance.ApplyRemoteHeroScore(hookable, new HeroScore { kills = kills, deaths = deaths, rescues = rescues, rescued = rescued });
            }
            if (hook != null) hook.ApplyRemoteState((hookFlags & BusyFlag) != 0, (float)(hookReadyAt - NetworkTime.time));
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            base.OnDeserialize(reader, initialState);
            if (isServer || hook == null || NetworkClient.connection == null) return;
            // stamped as NetworkTransform stamps the heroes' positions (the send time plus one send interval), so the
            // head is shown on their timeline and a dragged hero stays at it
            hook.AddRemoteHead(NetworkClient.connection.remoteTimeStamp + NetworkServer.sendInterval,
                (hookFlags & VisibleFlag) != 0, (hookFlags & FlyingFlag) != 0, hookHead, hookDirection);
        }

        [ClientRpc]
        void RpcPlayAnimation(string state, float fade, float lockSeconds)
        {
            if (isServer || anim == null) return;
            if (lockSeconds > 0f) anim.PlayAction(state, lockSeconds, fade);
            else anim.Play(state, fade);
        }

        void OnItemUsed(int slot, ItemDef item)
        {
            int index = prefabs != null ? prefabs.IndexOf(item) : -1;
            if (index >= 0) RpcItemUsed(index);
        }

        /// <summary>The server's hero used an item: clients play its effect (Arcane Boots' burst).</summary>
        [ClientRpc]
        void RpcItemUsed(int index)
        {
            if (!isServer && inventory != null && prefabs != null) inventory.ShowUsed(prefabs.ItemAt(index));
        }

        void OnEffectsChanged(string _, string now)
        {
            if (isServer || prefabs == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var list = new List<(ItemDef, float, float)>();
            foreach (var entry in (now ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length < 3 || !int.TryParse(parts[0], out var index)) continue;
                var item = prefabs.ItemAt(index);
                if (item == null || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out var endsAt)
                    || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, ci, out var duration)) continue;
                list.Add((item, Time.time + (float)(endsAt - NetworkTime.time), duration));
            }
            hookable.ApplyRemoteEffects(list);
        }

        [ClientRpc]
        void RpcGoldAdded(int amount)
        {
            if (!isServer && inventory != null) inventory.ShowGoldAdded(amount);
        }

        /// <summary>Owning client: sends the movement input when it changes (and now and then, in case).</summary>
        void SendInput()
        {
            var move = motor.MoveInput;
            var look = motor.LookDirection ?? Vector3.zero;
            bool changed = (move - sentMove).sqrMagnitude > 1e-4f || (look - sentLook).sqrMagnitude > 1e-4f;
            if (!changed && Time.time - sentAt < InputResendInterval) return;
            sentMove = move;
            sentLook = look;
            sentAt = Time.time;
            CmdInput(move, look);
        }

        void Update()
        {
            if (isServer) Publish();
            else if (isClient)
            {
                Show();
                if (hook != null) hook.ShowRemote(NetworkTime.unscaledTime); // the time NetworkTransform shows the heroes at
                if (isLocalPlayer && motor != null) SendInput();
            }
        }
    }
}
