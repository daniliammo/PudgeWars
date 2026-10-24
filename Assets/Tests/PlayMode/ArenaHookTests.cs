using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PudgeWars.Tests
{
    public class ArenaHookTests
    {
        Arena arena;
        HookAbility hook;
        Hookable player;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            AudioListener.volume = 0f; // tests run silent: no hook or announcer sounds
            SceneManager.LoadScene("Arena");
            yield return null;
            yield return null;
            arena = Object.FindFirstObjectByType<Arena>();
            arena.passiveGold = 0; // tests count gold exactly; the trickle has its own test
            var input = Object.FindFirstObjectByType<PlayerInputController>();
            input.enabled = false; // tests drive the player directly
            hook = input.GetComponent<HookAbility>();
            player = hook.GetComponent<Hookable>();
            foreach (var brain in Object.FindObjectsByType<BotBrain>(FindObjectsSortMode.None)) brain.enabled = false;
        }

        [UnityTest]
        public IEnumerator HookedBotFallsIntoPitDiesAndScores()
        {
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
            Assert.AreEqual(0, arena.Score);
            Assert.IsTrue(hook.TryCast(bot.transform.position));

            bool caught = false, dropped = false;
            hook.TargetCaught += _ => caught = true;
            hook.TargetDropped += _ => dropped = true;
            float t = 0f;
            int shot = 0;
            while (arena.Score == 0 && t < 6f)
            {
                t += Time.deltaTime;
                if (shot < 12 && t > 0.3f + shot * 0.15f) Capture($"hit_{shot++:00}");
                yield return null;
            }
            Assert.IsTrue(caught, "hook did not catch the bot");
            Assert.IsTrue(dropped, "bot was not released over the pit");
            Assert.AreEqual(1, arena.Score);
            Assert.IsFalse(bot.IsAlive);
            Assert.AreEqual(arena.killGold, player.GetComponent<HeroInventory>().Gold, "no gold for the kill");

            yield return new WaitUntil(() => !hook.IsBusy);
            Assert.IsTrue(hook.IsReady, "cooldown was not reset by the hit");
            Assert.AreEqual(0f, hook.CooldownRemaining);

            yield return new WaitForSeconds(arena.respawnDelay + 0.5f);
            Assert.IsTrue(bot.IsAlive, "bot did not respawn");
            var botHook = bot.GetComponent<HookAbility>();
            Assert.IsFalse(botHook.chain.enabled, "bot's chain came back after respawn");
            Assert.Greater(bot.transform.position.z, arena.pitHalfDepth);
            yield return new WaitUntil(() => hook.IsReady);
            Capture("after_respawn");
        }

        [UnityTest]
        public IEnumerator MissReturnsHookWithoutScore()
        {
            foreach (var b in Hookable.All.Where(h => h.team == Team.Bots)) b.transform.position = new Vector3(7f, 0f, 8f);
            Assert.IsTrue(hook.TryCast(player.transform.position + new Vector3(-3f, 0f, 10f)));
            Assert.IsTrue(hook.IsBusy);
            float t = 0f;
            while (hook.IsBusy && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(hook.IsBusy, "hook did not come back");
            Assert.AreEqual(0, arena.Score);
            Assert.AreEqual(0, arena.Deaths);
            Assert.IsFalse(hook.IsReady, "a miss must keep the cooldown");
            Assert.Greater(hook.CooldownRemaining, 0f);
            yield return new WaitForSeconds(hook.cooldown + 0.1f);
            Assert.IsTrue(hook.IsReady);
        }

        [UnityTest]
        public IEnumerator HookHeadSpinsInFlightAndCatchesWideLikeInDota()
        {
            // 1.1 m off the flight line: out of reach of the old 0.35 m head, within Dota's 1.24 m
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            foreach (var b in Hookable.All.Where(h => h.team == Team.Bots && h != bot)) b.transform.position = new Vector3(12f, 0f, 14f);
            var p = player.transform.position;
            bot.transform.position = new Vector3(p.x + 1.1f, 0f, arena.pitHalfDepth + 1.5f);
            Assert.IsTrue(hook.TryCast(new Vector3(p.x, 0f, bot.transform.position.z)));

            bool caught = false;
            hook.TargetCaught += _ => caught = true;
            yield return new WaitUntil(() => hook.hookHead.gameObject.activeSelf);
            float firstRoll = hook.hookHead.eulerAngles.z;
            bool spun = false;
            float t = 0f;
            while (!caught && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
                if (!caught) spun |= Mathf.Abs(Mathf.DeltaAngle(firstRoll, hook.hookHead.eulerAngles.z)) > 30f;
            }
            Assert.IsTrue(spun, "the hook head did not spin in flight");
            Assert.IsTrue(caught, "the wide hook missed a bot 1.1 m off its line");
        }

        [UnityTest]
        public IEnumerator BotHooksPlayerWhoFallsIntoPitAndRespawns()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 12f);
            var bot = bots[0];
            bot.transform.position = new Vector3(player.transform.position.x + 1f, 0f, arena.pitHalfDepth + 1f);
            var brain = bot.GetComponent<BotBrain>();
            brain.aimError = 0f;
            brain.leadChance = 0f;
            brain.throwInterval = Vector2.zero;
            brain.enabled = true;

            float t = 0f;
            int shot = 0;
            while (arena.Deaths == 0 && t < 8f)
            {
                t += Time.deltaTime;
                if (shot < 8 && t > 0.3f + shot * 0.2f) Capture($"bot_{shot++:00}");
                yield return null;
            }
            brain.enabled = false;
            Assert.AreEqual(1, arena.Deaths, "bot did not kill the player");
            Assert.AreEqual(0, arena.Score);
            Assert.AreEqual(arena.killGold, bot.GetComponent<HeroInventory>().Gold, "bot got no gold for the kill");
            Assert.AreEqual(0, player.GetComponent<HeroInventory>().Gold);
            Assert.IsFalse(player.IsAlive);
            Assert.IsFalse(hook.TryCast(Vector3.zero), "a dead player must not throw");

            yield return new WaitForSeconds(arena.respawnDelay + 0.5f);
            Assert.IsTrue(player.IsAlive, "player did not respawn");
            Assert.Less(player.transform.position.z, -arena.pitHalfDepth);
        }

        [UnityTest]
        public IEnumerator HookStaysHiddenAfterOwnerDiesMidThrowAndRespawns()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 12f);
            var bot = bots[0];
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1f);

            // the player throws away from the bot, the bot's hook gets there first
            Assert.IsTrue(hook.TryCast(player.transform.position + new Vector3(6f, 0f, 10f)));
            var brain = bot.GetComponent<BotBrain>();
            brain.aimError = 0f;
            brain.leadChance = 0f;
            brain.throwInterval = Vector2.zero;
            brain.enabled = true;

            float t = 0f;
            while (arena.Deaths == 0 && t < 8f) { t += Time.deltaTime; yield return null; }
            brain.enabled = false;
            Assert.AreEqual(1, arena.Deaths, "bot did not kill the player");
            Assert.IsFalse(hook.chain.enabled, "player's chain is visible while dead");

            yield return new WaitUntil(() => player.IsAlive);
            yield return null;
            Assert.IsFalse(hook.chain.enabled, "player's chain came back after respawn");
            Assert.IsFalse(hook.hookHead.gameObject.activeSelf, "player's hook head came back after respawn");
            Assert.IsFalse(hook.IsBusy);
        }

        [UnityTest]
        public IEnumerator BotAimsWithOrWithoutLead()
        {
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            bot.transform.position = new Vector3(0f, 0f, arena.pitHalfDepth + 1f);
            var brain = bot.GetComponent<BotBrain>();
            var motor = player.GetComponent<PudgeMotor>();
            motor.SetMoveInput(Vector3.right);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(player.PlanarVelocity.x, 1f, "player should be running sideways");

            var direct = brain.AimOffset(player, lead: false);
            var led = brain.AimOffset(player, lead: true);
            var toPlayer = player.transform.position - bot.transform.position;
            toPlayer.y = 0f;
            Assert.Less(Vector3.Distance(direct, toPlayer), 1e-3f, "without lead the bot aims at the current position");
            Assert.Greater(led.x - direct.x, 1f, "with lead the aim point moves ahead of the runner");
            motor.SetMoveInput(Vector3.zero);
        }

        [UnityTest]
        public IEnumerator CaughtPlayerDragsHisOwnTargetIntoThePit()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            var victim = bots[0];   // caught by the player
            var hunter = bots[1];   // catches the player meanwhile
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 13f);
            player.transform.position = new Vector3(0f, 0f, -arena.pitHalfDepth - 1f);
            victim.transform.position = new Vector3(0f, 0f, 8.5f);
            hunter.transform.position = new Vector3(3f, 0f, arena.pitHalfDepth + 1f);
            hook.pullSpeed = 2f; // slow pull so the hunter's hook arrives while the victim is still on its side

            bool caught = false;
            hook.TargetCaught += _ => caught = true;
            Assert.IsTrue(hook.TryCast(victim.transform.position));
            float t = 0f;
            while (!caught && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(caught, "player's hook missed the victim");

            var brain = hunter.GetComponent<BotBrain>();
            brain.aimError = 0f;
            brain.leadChance = 0f;
            brain.throwInterval = Vector2.zero;
            brain.enabled = true;

            t = 0f;
            int shot = 0;
            while ((arena.Deaths == 0 || arena.Score == 0) && t < 8f)
            {
                t += Time.deltaTime;
                if (shot < 10 && t > 0.2f + shot * 0.2f) Capture($"tether_{shot++:00}");
                yield return null;
            }
            brain.enabled = false;
            Assert.AreEqual(1, arena.Deaths, "the hunter did not kill the player");
            Assert.AreEqual(1, arena.Score, "the player's target was not dragged into the pit");
            Assert.IsFalse(victim.IsAlive);
            Assert.AreEqual(arena.killGold, player.GetComponent<HeroInventory>().Gold, "the tethered kill belongs to the player");
            Assert.IsFalse(hook.chain.enabled);
        }

        [UnityTest]
        public IEnumerator CaughtCharacterLosesItsOwnHook()
        {
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            bot.transform.position = new Vector3(10f, 0f, 12f);
            Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.forward * 10f));
            yield return new WaitForSeconds(hook.castDelay + 0.2f);
            Assert.IsTrue(hook.IsBusy);

            player.AttachTo(bot.GetComponent<HookAbility>());
            Assert.IsFalse(hook.IsBusy, "own hook must break off when the owner is caught");
            Assert.IsFalse(hook.hookHead.gameObject.activeSelf);
            player.Release();
            Assert.IsTrue(player.IsControllable);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeroTurnsAtDotaRateBeforeMovingAndCasting()
        {
            var motor = player.GetComponent<PudgeMotor>();
            var start = player.transform.position;
            float expected = (180f - PudgeMotor.FacingTolerance) / motor.TurnSpeedDegrees;

            // move backwards: first a turn in place, no movement until within 11.5°
            motor.SetMoveInput(Vector3.back);
            float t0 = Time.time;
            while (Vector3.Angle(player.transform.forward, Vector3.back) > PudgeMotor.FacingTolerance)
            {
                Assert.Less(Vector3.Distance(player.transform.position, start), 1e-3f, "moved before facing the direction");
                Assert.Less(Time.time - t0, 1f, "turn takes too long");
                yield return null;
            }
            float turnTime = Time.time - t0;
            Assert.Greater(turnTime, expected * 0.5f, "turn was instant");
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Vector3.Distance(player.transform.position, start), 0.3f, "did not start moving after the turn");
            motor.SetMoveInput(Vector3.zero);
            yield return null;

            // cast behind: the hero turns first, the hook leaves after turn + cast delay
            Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.forward * 5f));
            Assert.Greater(Vector3.Angle(player.transform.forward, Vector3.forward), 150f, "cast must not snap the rotation");
            t0 = Time.time;
            while (!hook.hookHead.gameObject.activeSelf && Time.time - t0 < 2f) yield return null;
            Assert.Greater(Time.time - t0, hook.castDelay + expected * 0.5f, "hook left before the hero turned");
            Assert.Less(Vector3.Angle(player.transform.forward, Vector3.forward), PudgeMotor.FacingTolerance + 1f);
        }

        [UnityTest]
        public IEnumerator ShopSellsAetherLensThatExtendsHookRange()
        {
            var shop = Object.FindFirstObjectByType<ShopUI>();
            var inventory = player.GetComponent<HeroInventory>();
            var row = shop.rows[0];
            var lens = row.item;
            Assert.AreEqual(2275, lens.cost);
            Assert.IsNotNull(lens.icon, "item icon missing");
            Assert.IsFalse(shop.IsOpen);
            shop.Toggle();
            Assert.IsTrue(shop.IsOpen);
            Assert.IsFalse(row.buyButton.interactable, "buy must be disabled without gold");
            Assert.IsFalse(inventory.TryBuy(lens));

            inventory.AddGold(lens.cost);
            Assert.IsTrue(row.buyButton.interactable);
            Assert.AreEqual(lens.cost.ToString(), shop.goldLabel.text);
            row.buyButton.onClick.Invoke();
            Assert.AreEqual(0, inventory.Gold);
            Assert.IsTrue(inventory.Owns(lens));
            Assert.AreEqual(hook.range + 2.75f, hook.EffectiveRange, 1e-4f);
            Assert.AreEqual(lens.icon, shop.slots[0].sprite);
            Assert.AreEqual("Куплено", row.buyLabel.text);
            yield return null;
            CaptureScreen("shop_open");
            inventory.AddGold(10000);
            Assert.IsFalse(inventory.TryBuy(lens), "Aether Lens does not stack");
            shop.Close();

            // the hook really flies 15.75 m now
            foreach (var b in Hookable.All.Where(h => h.team == Team.Bots)) b.transform.position = new Vector3(12f, 0f, 14f);
            Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.forward * 10f));
            yield return new WaitUntil(() => hook.hookHead.gameObject.activeSelf);
            float maxDist = 0f;
            while (hook.IsBusy)
            {
                var d = hook.hookHead.position - player.transform.position;
                maxDist = Mathf.Max(maxDist, new Vector3(d.x, 0f, d.z).magnitude);
                yield return null;
            }
            // range is measured from the hero's centre, like the aim lines
            Assert.AreEqual(hook.EffectiveRange, maxDist, 0.05f);
        }

        [UnityTest]
        public IEnumerator ShopButtonsReceivePointerClicks()
        {
            var shop = Object.FindFirstObjectByType<ShopUI>();
            Assert.IsNotNull(EventSystem.current, "no EventSystem");
            yield return null;
            AssertPointerHits(shop.toggleButton);
            shop.Toggle();
            yield return null;
            AssertPointerHits(shop.rows[0].buyButton);
            AssertPointerHits(shop.closeButton);
        }

        /// <summary>What the EventSystem does for a mouse click: raycast at the button's centre and hit it first.</summary>
        static void AssertPointerHits(Button button)
        {
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners); // screen pixels for an overlay canvas
            var data = new PointerEventData(EventSystem.current) { position = (corners[0] + corners[2]) * 0.5f };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Assert.IsNotEmpty(hits, $"pointer does not reach '{button.name}' (no raycaster?)");
            var top = hits[0].gameObject.transform;
            Assert.IsTrue(top == button.transform || top.IsChildOf(button.transform), $"'{button.name}' is covered by '{top.name}'");
            Assert.IsNotNull(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), "hit object does not handle clicks");
        }

        [UnityTest]
        public IEnumerator PlayerStaysWithinTheWalls()
        {
            var motor = player.GetComponent<PudgeMotor>();
            motor.SetMoveInput(Vector3.back + Vector3.left);
            yield return new WaitForSeconds(6f);
            motor.SetMoveInput(Vector3.zero);
            var p = player.transform.position;
            Assert.GreaterOrEqual(p.z, -(arena.pitHalfDepth + arena.sideDepth - player.radius) - 1e-3f, "walked through the back wall");
            Assert.GreaterOrEqual(p.x, -arena.halfWidth + player.radius - 1e-3f, "walked through the side wall");
            Assert.IsTrue(player.IsAlive);
        }

        [UnityTest]
        public IEnumerator CarelessStepOverThePitEdgeDropsTheHeroWithoutAKill()
        {
            // the player once hooked by a bot: walking into the pit later still credits nobody
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            player.AttachTo(bot.GetComponent<HookAbility>());
            player.Release();
            Assert.AreEqual(bot, player.LastHookedBy);

            var died = false;
            player.Died += _ => died = true;
            var motor = player.GetComponent<PudgeMotor>();
            motor.SetMoveInput(Vector3.forward);
            float t = 0f;
            while (!player.IsFalling && t < 6f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(player.IsFalling, "the pit edge did not let the player through");
            Assert.Less(Mathf.Abs(player.transform.position.z), arena.pitHalfDepth);
            Assert.IsFalse(player.IsControllable);
            Assert.IsNull(player.LastHookedBy);
            // the run cycle must not cut the fall animation off
            yield return null;
            yield return null;
            var animator = player.GetComponentInChildren<Animator>();
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName(PudgeAnimation.Death)
                          || animator.GetNextAnimatorStateInfo(0).IsName(PudgeAnimation.Death), "no fall animation");
            yield return new WaitUntil(() => died);
            Assert.AreEqual(1, arena.Deaths);
            Assert.AreEqual(0, bot.GetComponent<HeroInventory>().Gold, "the bot got gold for a fall it did not cause");

            // a bot walking in is no point for the player (bots normally stop at the edge: let this one through)
            var other = Hookable.All.Last(h => h.team == Team.Bots);
            other.GetComponent<PudgeMotor>().keepOffPit = false;
            other.GetComponent<PudgeMotor>().SetMoveInput(Vector3.back);
            yield return new WaitUntil(() => !other.IsAlive);
            Assert.AreEqual(0, arena.Score);
            motor.SetMoveInput(Vector3.zero);
            other.GetComponent<PudgeMotor>().SetMoveInput(Vector3.zero);
        }

        /// <summary>Screenshot including the overlay UI (Camera.Render does not draw ScreenSpaceOverlay canvases).</summary>
        [UnityTest]
        public IEnumerator BotsWalkUpToThePitEdgeButDoNotFallIn()
        {
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            var motor = bot.GetComponent<PudgeMotor>();
            Assert.IsTrue(motor.keepOffPit, "bots must keep off the pit");
            motor.SetMoveInput(Vector3.back + Vector3.right * 0.3f);
            yield return new WaitForSeconds(4f);
            Assert.IsTrue(bot.IsAlive && !bot.IsFalling, "the bot fell in");
            Assert.AreEqual(arena.pitHalfDepth + motor.pitGap, bot.transform.position.z, 0.01f, "the bot did not reach the edge");
            motor.SetMoveInput(Vector3.zero);

            // left to themselves, bots wander right up to the brink now and then, and none falls in
            foreach (var brain in Object.FindObjectsByType<BotBrain>(FindObjectsSortMode.None))
            {
                brain.edgeChance = 1f;
                brain.throwInterval = new Vector2(1000f, 1000f);
                brain.enabled = true;
            }
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            float closest = float.MaxValue;
            for (float t = 0f; t < 10f; t += Time.deltaTime)
            {
                foreach (var b in bots) closest = Mathf.Min(closest, b.transform.position.z);
                yield return null;
            }
            Assert.That(bots.All(b => b.IsAlive && !b.IsFalling), "a wandering bot fell into the pit");
            Assert.Less(closest, arena.pitHalfDepth + 0.3f, "bots never came to the edge");
            Assert.AreEqual(0, arena.Score);
        }

        [UnityTest]
        public IEnumerator HeroesHaveNicknamesAndDotaHealthBars()
        {
            var bars = Object.FindFirstObjectByType<HealthBars>();
            Assert.IsNotNull(bars, "no health bars in the HUD");
            yield return null;
            var heroes = Hookable.All.ToList();
            Assert.AreEqual(heroes.Count, bars.Count);
            Assert.That(heroes.Select(h => h.nickname), Is.All.Not.Empty, "every hero needs a nickname");
            Assert.That(heroes.Select(h => h.Nickname), Is.Unique);
            foreach (var hero in heroes)
            {
                var (root, fill, name) = bars.Of(hero);
                Assert.IsTrue(root.gameObject.activeSelf, hero.Nickname + " has no bar shown");
                Assert.LessOrEqual(root.anchoredPosition.y, ((RectTransform)bars.transform).rect.yMax - bars.topMargin + 1e-3f, hero.Nickname + "'s bar is off the top of the screen");
                Assert.AreEqual(hero.Nickname, name.text);
                Assert.AreEqual(1f, fill.anchorMax.x, 1e-4f, "a live hero is at full health");
                Assert.AreEqual(hero.team == Team.Player ? bars.playerColor : bars.enemyColor, fill.GetComponent<Image>().color);
                Assert.AreEqual(Mathf.CeilToInt(hero.maxHealth / bars.segmentHealth) - 1, root.GetComponentsInChildren<Transform>().Count(t => t.name == "Notch"));
            }
            CaptureScreen("health_bars");

            // a dead hero's bar goes away and comes back full with the respawn
            var bot = heroes.First(h => h.team == Team.Bots);
            bot.StartFalling(Vector3.zero);
            yield return new WaitUntil(() => !bot.IsAlive);
            yield return null;
            Assert.AreEqual(0f, bot.Health);
            Assert.IsFalse(bars.Of(bot).root.gameObject.activeSelf, "a dead hero's bar is still shown");
            yield return new WaitUntil(() => bot.IsAlive);
            yield return null;
            Assert.IsTrue(bars.Of(bot).root.gameObject.activeSelf);
            Assert.AreEqual(1f, bars.Of(bot).fill.anchorMax.x, 1e-4f);
        }

        [UnityTest]
        public IEnumerator AbilityBarShowsHookCooldownAndAimLines()
        {
            var input = player.GetComponent<PlayerInputController>();
            var bar = Object.FindFirstObjectByType<AbilityBar>();
            var view = bar.slots[PlayerInputController.HookSlot];
            Assert.AreEqual("Q", view.keyLabel.text);
            Assert.IsNotNull(view.icon.sprite, "hook icon missing");

            // aim lines: two parallel edges, hook width apart, full range long
            var aim = input.aimIndicator;
            var dir = hook.AimDirection(player.transform.position + Vector3.forward * 5f);
            aim.Show(hook.AimStart, dir, hook.AimLength, hook.HitWidth);
            Assert.IsTrue(aim.left.enabled && aim.right.enabled);
            Assert.AreEqual(hook.HitWidth, Vector3.Distance(aim.left.GetPosition(0), aim.right.GetPosition(0)), 1e-3f);
            Assert.AreEqual(hook.AimLength, Vector3.Distance(aim.left.GetPosition(0), aim.left.GetPosition(1)), 1e-3f);
            Assert.Greater(hook.TipLength, 0.5f, "hook model tip not measured");
            yield return null;
            CaptureScreen("aim");
            aim.Hide();

            // a miss: the cooldown is written over the icon in seconds and milliseconds
            Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.forward * 10f));
            float t = 0f;
            while (hook.IsBusy && t < 5f) { t += Time.deltaTime; yield return null; }
            yield return null;
            Assert.IsFalse(input.SelectAbility(PlayerInputController.HookSlot), "a hook on cooldown must not be selectable");
            StringAssert.IsMatch(@"^\d+\.\d{3}$", view.cooldownLabel.text);
            Assert.IsTrue(view.cooldownOverlay.enabled);
            CaptureScreen("cooldown");

            yield return new WaitForSeconds(hook.cooldown);
            Assert.AreEqual("", view.cooldownLabel.text);
            Assert.IsFalse(view.cooldownOverlay.enabled);
            Assert.IsTrue(input.SelectAbility(PlayerInputController.HookSlot));
            Assert.AreEqual(PlayerInputController.HookSlot, input.SelectedAbility);
        }

        [UnityTest]
        public IEnumerator AimLinesDisappearWhenAimingPlayerDiesAndStayHiddenAfterRespawn()
        {
            var input = player.GetComponent<PlayerInputController>();
            var aim = input.aimIndicator;
            input.enabled = true;
            Assert.IsTrue(input.SelectAbility(PlayerInputController.HookSlot));
            var dir = hook.AimDirection(player.transform.position + Vector3.forward * 5f);
            aim.Show(hook.AimStart, dir, hook.AimLength, hook.HitWidth);

            player.StartFalling(Vector3.zero);
            yield return null;
            Assert.AreEqual(PlayerInputController.NoAbility, input.SelectedAbility, "selection must drop when the player loses control");
            Assert.IsFalse(aim.left.enabled || aim.right.enabled, "aim lines left on the ground");

            float t = 0f;
            while (player.IsAlive && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(player.IsAlive);
            yield return new WaitForSeconds(arena.respawnDelay + 0.5f);
            Assert.IsTrue(player.IsAlive, "player did not respawn");
            Assert.IsFalse(aim.left.enabled || aim.right.enabled, "respawn switched the aim lines back on");
            input.enabled = false;
        }

        [UnityTest]
        public IEnumerator CameraViewsCycleOwnSideEnemySideFirstPerson()
        {
            var follow = Camera.main.GetComponent<TopDownCamera>();
            Assert.AreEqual(TopDownCamera.View.OwnSide, follow.Mode);
            Assert.Greater(follow.transform.forward.z, 0f, "own view looks toward the enemy");
            Assert.Less(follow.transform.position.z, player.transform.position.z);
            Assert.Less(Vector3.Distance(Vector3.forward, follow.MoveForward), 1e-4f);
            yield return null;
            CaptureScreen("view_own");

            follow.NextView();
            yield return null;
            Assert.AreEqual(TopDownCamera.View.EnemySide, follow.Mode);
            Assert.Less(follow.transform.forward.z, 0f, "enemy view looks back at the player's side");
            Assert.Greater(follow.transform.position.z, arena.pitHalfDepth, "enemy view stands over the enemy side");
            Assert.Less(Vector3.Distance(Vector3.back, follow.MoveForward), 1e-4f, "W moves up the screen");
            CaptureScreen("view_enemy");

            follow.NextView();
            yield return null;
            Assert.AreEqual(TopDownCamera.View.FirstPerson, follow.Mode);
            var eye = follow.transform.position - player.transform.position;
            Assert.AreEqual(follow.eyeHeight, eye.y, 1e-3f);
            Assert.Greater(Vector3.Dot(follow.transform.forward, player.transform.forward), 0.9f, "looks where the hero faces");
            Assert.Less(Vector3.Distance(Vector3.forward, follow.MoveForward), 1e-4f, "W moves where the view looks");
            Assert.IsTrue(follow.crosshair.activeSelf, "no crosshair in first person");
            // batchmode has no window to lock the cursor in, so the request is checked, the release below for real
            Assert.IsTrue(follow.WantsCursorCaptured, "first person must capture the mouse");
            CaptureScreen("view_first_person");

            // shooter controls: the mouse turns the view and W with it
            follow.AddLook(new Vector2(90f, 0f));
            Assert.Less(Vector3.Distance(Vector3.right, follow.MoveForward), 1e-4f);
            follow.AddLook(new Vector2(-90f, 0f));

            // strafing: the hero keeps facing the look direction and moves sideways
            var motor = player.GetComponent<PudgeMotor>();
            var start = player.transform.position;
            motor.SetLookDirection(Vector3.forward);
            motor.SetMoveInput(Vector3.right);
            yield return new WaitForSeconds(0.4f);
            motor.SetMoveInput(Vector3.zero);
            motor.SetLookDirection(null);
            Assert.Greater(player.transform.position.x - start.x, 0.5f, "did not strafe");
            Assert.Less(Vector3.Angle(player.transform.forward, Vector3.forward), 1f, "strafing turned the hero");

            follow.NextView();
            Assert.AreEqual(TopDownCamera.View.OwnSide, follow.Mode);
            Assert.IsFalse(follow.crosshair.activeSelf);
            Assert.IsFalse(follow.WantsCursorCaptured);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "leaving first person must free the mouse");
        }

        [UnityTest]
        public IEnumerator BotsBuyItemsWithTheirGold()
        {
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            var wallet = bot.GetComponent<HeroInventory>();
            var shopper = bot.GetComponent<BotShopper>();
            Assert.IsNotNull(shopper, "bots have no shopper");
            var lens = shopper.catalog.First();
            var botHook = bot.GetComponent<HookAbility>();

            wallet.AddGold(lens.cost - 1);
            yield return new WaitForSeconds(shopper.checkInterval + 0.2f);
            Assert.IsFalse(wallet.Owns(lens), "bought without enough gold");

            wallet.AddGold(1);
            yield return new WaitForSeconds(shopper.checkInterval + 0.2f);
            Assert.IsTrue(wallet.Owns(lens), "bot did not buy Aether Lens");
            Assert.AreEqual(0, wallet.Gold);
            Assert.AreEqual(botHook.range + lens.castRangeBonus, botHook.EffectiveRange, 1e-4f);
        }

        [UnityTest]
        public IEnumerator GoldRewardShowsAmountAndCoinsOverTheHero()
        {
            var popups = Object.FindFirstObjectByType<GoldPopups>();
            Assert.IsNotNull(popups, "no gold popups layer in the HUD");
            Assert.IsNotNull(popups.coinSprite, "coin picture (Gold.png) missing");
            player.GetComponent<HeroInventory>().AddGold(arena.killGold);
            Assert.AreEqual(1, popups.ActiveCount);
            Assert.AreEqual($"+{arena.killGold} монет", popups.LastText);
            yield return new WaitForSeconds(0.3f);
            var coins = popups.GetComponentsInChildren<Image>().Where(i => i.name == "Coin").ToList();
            Assert.AreEqual(popups.coinCount, coins.Count);
            Assert.That(coins.Select(c => c.color.a), Is.All.InRange(0.5f, 0.95f), "coins must fade steadily from the start");
            CaptureScreen("gold_popup");
            for (int f = 0; f < 4; f++)
            {
                yield return new WaitForSeconds(0.3f);
                CaptureScreen($"gold_popup_{f}");
            }
            yield return new WaitForSeconds(popups.lifetime - 1.5f + 0.1f);
            Assert.AreEqual(0, popups.ActiveCount, "popup did not go away");
        }

        [UnityTest]
        public IEnumerator EveryHeroGetsTwoGoldEverySecondWithoutPopup()
        {
            var popups = Object.FindFirstObjectByType<GoldPopups>();
            var wallets = Hookable.All.Select(h => h.GetComponent<HeroInventory>()).ToList();
            arena.passiveGold = 2;
            yield return new WaitForSeconds(0.1f);
            var before = wallets.Select(w => w.Gold).ToList();
            yield return new WaitForSeconds(3f);
            for (int i = 0; i < wallets.Count; i++)
                Assert.AreEqual(6, wallets[i].Gold - before[i], 2, wallets[i].name + " got the wrong trickle");
            Assert.AreEqual(0, popups.ActiveCount, "the trickle must not show \"+N монет\" or coins");
        }

        [UnityTest]
        public IEnumerator HudUsesCyrillicFontForCyrillicTextAndLatinFontOtherwise()
        {
            var fonts = Object.FindFirstObjectByType<HudFonts>();
            Assert.IsNotNull(fonts.latin, "Latin font missing");
            Assert.IsNotNull(fonts.cyrillic, "Cyrillic font missing");
            yield return null;
            var shop = Object.FindFirstObjectByType<ShopUI>();
            var keyLabel = Object.FindFirstObjectByType<AbilityBar>().slots[0].keyLabel;
            Assert.AreEqual(fonts.latin, keyLabel.font, "\"Q\" must use the Latin font");
            Assert.AreEqual(fonts.latin, shop.goldLabel.font, "gold digits must use the Latin font");
            foreach (var text in fonts.GetComponentsInChildren<Text>(true))
                Assert.AreEqual(HudFonts.HasCyrillic(text.text) ? fonts.cyrillic : fonts.latin, text.font, text.name + ": " + text.text);

            // a label switching language at runtime switches its font
            shop.goldLabel.text = "Нет";
            yield return null;
            Assert.AreEqual(fonts.cyrillic, shop.goldLabel.font);
            CaptureScreen("fonts");
        }

        [Test]
        public void MultiKillsChainWithinWindowAndStartOverAfterIt()
        {
            var streaks = new KillStreaks { multiKillWindow = 18f };
            var hero = new object();
            CollectionAssert.AreEqual(new[] { KillAnnouncement.FirstBlood }, streaks.RegisterKill(hero, 0f), "the first kill of the match");
            CollectionAssert.AreEqual(new[] { KillAnnouncement.DoubleKill }, streaks.RegisterKill(hero, 10f));
            CollectionAssert.AreEqual(new[] { KillAnnouncement.TripleKill }, streaks.RegisterKill(hero, 27f), "the window counts from the previous kill");
            CollectionAssert.AreEqual(new[] { KillAnnouncement.UltraKill }, streaks.RegisterKill(hero, 30f));
            Assert.AreEqual(0, streaks.RampageCount(hero));
            CollectionAssert.AreEqual(new[] { KillAnnouncement.Rampage }, streaks.RegisterKill(hero, 31f));
            Assert.AreEqual(1, streaks.RampageCount(hero));
            CollectionAssert.AreEqual(new[] { KillAnnouncement.Rampage }, streaks.RegisterKill(hero, 32f), "kills after Rampage stay Rampage");
            Assert.AreEqual(2, streaks.RampageCount(hero), "Rampages in a row are counted");

            CollectionAssert.IsEmpty(streaks.RegisterKill(hero, 32f + 18.5f), "a long pause must reset the multi-kill");
            Assert.AreEqual(1, streaks.MultiKillCount(hero));
            Assert.AreEqual(0, streaks.RampageCount(hero), "the Rampage count starts over with the multi-kill");
            CollectionAssert.AreEqual(new[] { KillAnnouncement.DoubleKill }, streaks.RegisterKill(hero, 60f));

            // series are per hero, and a death breaks the multi-kill
            var other = new object();
            CollectionAssert.IsEmpty(streaks.RegisterKill(other, 60f));
            Assert.IsFalse(streaks.RegisterDeath(other), "a single kill is no series");
            Assert.IsTrue(streaks.RegisterDeath(hero), "Double Kill was broken by the death");
            CollectionAssert.IsEmpty(streaks.RegisterKill(hero, 61f));
        }

        [Test]
        public void SeriesRunsOutOfTimeOnceButGodlikeOnlyEndsWithDeath()
        {
            var streaks = new KillStreaks { multiKillWindow = 18f, godlikeStreak = 9 };
            var hero = new object();
            streaks.RegisterKill(hero, 0f);
            streaks.RegisterKill(hero, 5f);
            CollectionAssert.IsEmpty(streaks.ExpireSeries(23f), "still within the window");
            CollectionAssert.AreEqual(new[] { hero }, streaks.ExpireSeries(23.1f));
            CollectionAssert.IsEmpty(streaks.ExpireSeries(30f), "reported once");
            Assert.IsFalse(streaks.RegisterDeath(hero), "the series was already over");

            streaks.RegisterKill(hero, 100f);
            CollectionAssert.IsEmpty(streaks.ExpireSeries(200f), "a lone kill is no series");

            for (int i = 0; i < 9; i++) streaks.RegisterKill(hero, 300f + i * 60f);
            CollectionAssert.IsEmpty(streaks.ExpireSeries(2000f), "Godlike does not run out of time");
            Assert.IsTrue(streaks.RegisterDeath(hero), "Godlike was broken by the death");
        }

        [Test]
        public void GodlikeAfterNineKillsWithoutDyingWhateverThePauses()
        {
            var streaks = new KillStreaks { multiKillWindow = 18f, godlikeStreak = 9 };
            var hero = new object();
            streaks.RegisterKill(new object(), -100f); // First Blood goes to someone else
            for (int i = 0; i < 8; i++) CollectionAssert.IsEmpty(streaks.RegisterKill(hero, i * 60f));
            CollectionAssert.AreEqual(new[] { KillAnnouncement.Godlike }, streaks.RegisterKill(hero, 8 * 60f));
            CollectionAssert.AreEqual(new[] { KillAnnouncement.Godlike }, streaks.RegisterKill(hero, 9 * 60f), "every kill beyond is Godlike too");
            CollectionAssert.AreEqual(new[] { KillAnnouncement.DoubleKill }, streaks.RegisterKill(hero, 9 * 60f + 1f), "a multi-kill wins over a repeated Godlike");

            streaks.RegisterDeath(hero);
            Assert.AreEqual(0, streaks.Streak(hero));
            for (int i = 0; i < 8; i++) streaks.RegisterKill(hero, 1000f + i * 60f);
            // reaching Godlike with a multi-kill announces both
            CollectionAssert.AreEqual(new[] { KillAnnouncement.DoubleKill, KillAnnouncement.Godlike }, streaks.RegisterKill(hero, 1000f + 7 * 60f + 1f));
        }

        [UnityTest]
        public IEnumerator AnnouncerShowsMultiKillsInTurnForPlayerAndEnemies()
        {
            var announcer = Object.FindFirstObjectByType<KillAnnouncer>();
            Assert.IsNotNull(announcer, "no kill announcer in the HUD");
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            var announced = new List<KillAnnouncement>();
            arena.KillAnnounced += (_, kind) => announced.Add(kind);

            arena.ReportDeath(bots[0], player);
            yield return null;
            CollectionAssert.AreEqual(new[] { KillAnnouncement.FirstBlood }, announced);
            Assert.AreEqual("FIRST BLOOD", announcer.LastText);
            yield return new WaitForSeconds(announcer.duration + 0.1f);
            Assert.IsFalse(announcer.IsShowing, "a single kill is not a multi-kill");

            arena.ReportDeath(bots[1], player);
            arena.ReportDeath(bots[2], player);
            yield return null;
            CollectionAssert.AreEqual(new[] { KillAnnouncement.FirstBlood, KillAnnouncement.DoubleKill, KillAnnouncement.TripleKill }, announced);
            Assert.IsTrue(announcer.IsShowing);
            Assert.AreEqual("DOUBLE KILL", announcer.LastText);
            Assert.AreEqual(1, announcer.Queued, "Triple Kill must wait for Double Kill to finish");
            CaptureScreen("double_kill");

            yield return new WaitForSeconds(announcer.duration + 0.1f);
            Assert.AreEqual("TRIPLE KILL", announcer.LastText);
            yield return new WaitForSeconds(announcer.duration + 0.1f);
            Assert.IsFalse(announcer.IsShowing);

            // a kill of the enemy's own team is not counted; an enemy's multi-kill is shown with its name
            // the player dying ends the Triple Kill series, naming who did it
            var ended = new List<(Hookable hero, SeriesEnd reason, Hookable killer)>();
            arena.SeriesEnded += (hero, reason, killer) => ended.Add((hero, reason, killer));
            arena.ReportDeath(bots[3], bots[0]);
            arena.ReportDeath(player, bots[0]);
            arena.ReportDeath(player, bots[0]);
            yield return null;
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((player, SeriesEnd.Killed, bots[0]), ended[0]);
            Assert.AreEqual("СЕРИЯ ПРЕРВАНА", announcer.LastText);
            Assert.AreEqual($"Тебя сбил {bots[0].Nickname}", announcer.LastSubtitle);
            CaptureScreen("series_ended_killed");
            yield return new WaitForSeconds(announcer.duration + 0.1f);
            Assert.AreEqual("DOUBLE KILL", announcer.LastText);
            Assert.That(announcer.GetComponentsInChildren<Text>().Select(t => t.text), Has.Member(bots[0].Nickname));
            CaptureScreen("enemy_double_kill");
        }

        [UnityTest]
        public IEnumerator AnnouncerCountsRampagesInARow()
        {
            var announcer = Object.FindFirstObjectByType<KillAnnouncer>();
            announcer.duration = 0.2f;
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            var shown = new List<string>();
            for (int i = 0; i < 7; i++) arena.ReportDeath(bots[i % bots.Count], player);
            for (int i = 0; i < 100 && (announcer.IsShowing || shown.Count == 0); i++)
            {
                if (shown.Count == 0 || shown[^1] != announcer.LastText)
                {
                    shown.Add(announcer.LastText);
                    if (announcer.LastText == "RAMPAGE ×3") CaptureScreen("rampage_x3");
                }
                yield return new WaitForSeconds(0.05f);
            }
            CollectionAssert.AreEqual(new[] { "FIRST BLOOD", "DOUBLE KILL", "TRIPLE KILL", "ULTRA KILL", "RAMPAGE", "RAMPAGE ×2", "RAMPAGE ×3" }, shown);
        }

        [UnityTest]
        public IEnumerator SeriesEndedByTimeIsAnnouncedWithTheReason()
        {
            var announcer = Object.FindFirstObjectByType<KillAnnouncer>();
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            arena.Streaks.multiKillWindow = 0.5f;
            var ended = new List<(Hookable hero, SeriesEnd reason, Hookable killer)>();
            arena.SeriesEnded += (hero, reason, killer) => ended.Add((hero, reason, killer));

            arena.ReportDeath(bots[0], player);
            arena.ReportDeath(bots[1], player);
            yield return new WaitForSeconds(0.3f);
            CollectionAssert.IsEmpty(ended);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, ended.Count, "the Double Kill series did not run out");
            Assert.AreEqual((player, SeriesEnd.Time, (Hookable)null), ended[0]);

            // FIRST BLOOD and DOUBLE KILL go first
            yield return new WaitUntil(() => announcer.LastText == "СЕРИЯ ПРЕРВАНА");
            Assert.AreEqual("Время вышло", announcer.LastSubtitle);
            CaptureScreen("series_ended_time");

            // an enemy's series ended by time names the enemy
            arena.ReportDeath(player, bots[2]);
            arena.ReportDeath(player, bots[2]);
            yield return new WaitUntil(() => ended.Count == 2);
            Assert.AreEqual((bots[2], SeriesEnd.Time, (Hookable)null), ended[1]);
            yield return new WaitUntil(() => announcer.Queued == 0 && announcer.LastText == "СЕРИЯ ПРЕРВАНА");
            Assert.AreEqual($"{bots[2].Nickname}: время вышло", announcer.LastSubtitle);
        }

        static void CaptureScreen(string name)
        {
            var dir = System.Environment.GetEnvironmentVariable("PUDGE_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var cam = Camera.main;
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            Capture(name);
            canvas.renderMode = mode;
        }

        static void Capture(string name)
        {
            var dir = System.Environment.GetEnvironmentVariable("PUDGE_PREVIEW_DIR");
            var cam = Camera.main;
            if (string.IsNullOrEmpty(dir) || cam == null) return;
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(960, 540, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
