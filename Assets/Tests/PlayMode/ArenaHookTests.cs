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
        Pudge player;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            AudioListener.volume = 0f; // tests run silent: no hook or announcer sounds
            SceneManager.LoadScene("Arena");
            yield return null;
            yield return null;
            arena = Object.FindAnyObjectByType<Arena>();
            arena.passiveGold = 0; // tests count gold exactly; the trickle has its own test
            var input = Object.FindAnyObjectByType<PlayerInputController>();
            input.enabled = false; // tests drive the player directly
            hook = input.GetComponent<HookAbility>();
            // the hook's mana cost has its own test: the others throw as often as they like
            foreach (var h in Object.FindObjectsByType<HookAbility>()) h.manaCost = 0f;
            player = hook.GetComponent<Pudge>();
            foreach (var brain in Object.FindObjectsByType<BotBrain>()) brain.enabled = false;
        }

        [UnityTest]
        public IEnumerator HealthRegenerates()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            bot.Damage(200f, null);
            float before = bot.Health;
            Assert.AreEqual(bot.maxHealth - 200f, before, 0.01f);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(before + bot.healthRegen, bot.Health, bot.healthRegen * 0.2f, "health must come back at healthRegen per second");
        }

        [UnityTest]
        public IEnumerator HookDamageKillsAndTheBodyFliesOn()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            bot.healthRegen = 0f;
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 2.5f);
            bot.Damage(bot.maxHealth - hook.hitDamage - 5f, null); // the catch and a little drag finish it
            Assert.IsTrue(bot.IsAlive);
            Assert.IsTrue(hook.TryCast(bot.transform.position));
            float t = 0f;
            while (bot.IsAlive && t < 4f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(bot.IsAlive, "the hook's damage did not kill the bot");
            Assert.IsFalse(bot.IsHooked, "a dead bot must be let go");
            Assert.IsNotNull(bot.Body, "no body was left");
            Assert.AreEqual(1, arena.ScoreOf(player).kills, "the hook's kill belongs to the player");
            Assert.Greater(bot.transform.position.z, arena.pitHalfDepth, "the bot died over the pit, not of the hook");
            t = 0f;
            while (hook.IsBusy && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(hook.IsBusy, "the hook did not come back after its catch died");
        }

        [UnityTest]
        public IEnumerator QuellingBladeAddsSixteenToTheHooksHitAndToEveryMetreOnAHero()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            bot.healthRegen = 0f;
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 2.5f);
            hook.damagePerMeter = 10f;
            var blade = TestItem(ItemAction.ChopTree, 100, 0, stacks: false, range: 3.5f, cooldown: 4f);
            blade.heroDamageBonus = 16f;
            blade.creepDamageBonus = 4f;
            var inventory = player.GetComponent<HeroInventory>();
            inventory.AddGold(blade.cost, popup: false);
            Assert.IsTrue(inventory.TryBuy(blade));
            Assert.AreEqual(16f, inventory.DamageBonusAgainst(bot), "a hero takes +16");
            Assert.AreEqual(hook.hitDamage + 16f, hook.HitDamageAgainst(bot));

            Assert.IsTrue(hook.TryCast(bot.transform.position));
            float t = 0f;
            while (!bot.IsHooked && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(bot.IsHooked, "the hook missed the bot");
            Assert.AreEqual(bot.maxHealth - hook.hitDamage - 16f, bot.Health, 0.01f, "Quelling Blade must add 16 to the hook's hit on a hero");

            // every metre dragged takes 10 + 16
            Assert.AreEqual(26f, hook.DamagePerMeterAgainst(bot));
            var caughtAt = bot.transform.position;
            var last = caughtAt;
            float afterHit = bot.Health;
            while (bot.IsHooked && t < 6f)
            {
                last = bot.transform.position;
                t += Time.deltaTime;
                yield return null;
            }
            var dragged = new Vector2(last.x - caughtAt.x, last.z - caughtAt.z).magnitude;
            Assert.Greater(dragged, 1f, "the bot was hardly dragged");
            float lost = afterHit - (bot.IsAlive ? bot.Health : 0f);
            Assert.AreEqual(dragged * 26f, lost, 26f * 0.5f, "every metre dragged must take 10 + 16 with Quelling Blade");
        }

        [UnityTest]
        public IEnumerator HookedBotFallsIntoPitDiesAndScores()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
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
            Assert.AreEqual(1, arena.ScoreOf(player).kills, "the kill is not in the player's own score");
            Assert.AreEqual("У/С/С 1/0/0", Object.FindAnyObjectByType<ScoreHud>().label.text, "the own kill is not in the corner");
            Assert.AreEqual(1, arena.ScoreOf(bot).deaths, "the death is not in the bot's own score");
            Assert.AreEqual(0, arena.ScoreOf(player).deaths);
            Assert.AreEqual(arena.KillGold(bot), player.GetComponent<HeroInventory>().Gold, "no gold for the kill");
            Assert.AreEqual(133, arena.KillGold(bot), "a level-1 kill is worth 125 + 1 × 8");

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

        /// <summary>The player hooks the bot, put right at the pit edge, into the pit.</summary>
        IEnumerator PlayerKillsBot(Pudge bot)
        {
            foreach (var b in Pudge.All.Where(h => h.team == Team.Bots && h != bot)) b.transform.position = new Vector3(10f, 0f, 12f);
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
            yield return null;
            int score = arena.Score;
            Assert.IsTrue(hook.TryCast(bot.transform.position));
            float t = 0f;
            while (arena.Score == score && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(score + 1, arena.Score, "the player did not kill the bot");
        }

        [UnityTest]
        public IEnumerator BreakingAKillSeriesPaysHalfTheKillPlusFivePerKill()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            for (int i = 0; i < 3; i++) arena.Streaks.RegisterKill(bot, Time.time); // a Triple Kill on the go
            yield return PlayerKillsBot(bot);
            int expected = arena.KillGold(bot) + arena.KillGold(bot) / 2 + 3 * 5;
            Assert.AreEqual(133 + 66 + 15, expected);
            Assert.AreEqual(expected, player.GetComponent<HeroInventory>().Gold, "no bonus for breaking the series");

            // two popups: the kill reward and, above it, the bonus
            var popups = Object.FindAnyObjectByType<GoldPopups>();
            var amounts = popups.GetComponentsInChildren<Text>(true).Where(t => t.name == "Amount").Select(t => t.text).ToList();
            CollectionAssert.AreEqual(new[] { "+133 монет", "+81 монет" }, amounts, "the series bonus must come as its own popup");
            var offsets = popups.StackOffsets.ToList();
            Assert.Greater(offsets[1], offsets[0], "the bonus popup must stack above the kill reward");
        }

        [UnityTest]
        public IEnumerator BreakingRampagesInARowPaysTenPerRampageAndFivePerKillBefore()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            for (int i = 0; i < 6; i++) arena.Streaks.RegisterKill(bot, Time.time); // RAMPAGE ×2
            Assert.AreEqual(2, arena.Streaks.RampageCount(bot));
            yield return PlayerKillsBot(bot);
            // kills 1–4 (up to Ultra Kill) × 5, the two Rampages × 10
            Assert.AreEqual(133 + 66 + 4 * 5 + 2 * 10, player.GetComponent<HeroInventory>().Gold);
        }

        [UnityTest]
        public IEnumerator KillWithoutASeriesPaysNoBonus()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            arena.Streaks.RegisterKill(bot, Time.time); // a single kill is no series yet
            yield return PlayerKillsBot(bot);
            Assert.AreEqual(arena.KillGold(bot), player.GetComponent<HeroInventory>().Gold);
            Assert.AreEqual(1, Object.FindAnyObjectByType<GoldPopups>().ActiveCount, "no bonus popup without a series");
        }

        [UnityTest]
        public IEnumerator MissReturnsHookWithoutScore()
        {
            foreach (var b in Pudge.All.Where(h => h.team == Team.Bots)) b.transform.position = new Vector3(7f, 0f, 8f);
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
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            foreach (var b in Pudge.All.Where(h => h.team == Team.Bots && h != bot)) b.transform.position = new Vector3(12f, 0f, 14f);
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
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
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
            Assert.AreEqual(arena.KillGold(player), bot.GetComponent<HeroInventory>().Gold, "bot got no gold for the kill");
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
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
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
        public IEnumerator HookPullsAnAllyToPudgeAndHurtsItByTheDistance()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var ally = bots[0];
            ally.team = Team.Player;
            ally.healthRegen = 0f;
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 12f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -12f), Quaternion.identity);
            ally.transform.position = new Vector3(0f, 0f, -6f);

            Pudge caught = null;
            var caughtAt = Vector3.zero;
            hook.TargetCaught += h =>
            {
                caught = h;
                caughtAt = h.transform.position;
            };
            Assert.IsTrue(hook.TryCast(ally.transform.position));
            float t = 0f;
            bool hitChecked = false;
            while (hook.IsBusy && t < 5f)
            {
                if (caught != null && !hitChecked)
                {
                    // the catch itself takes 110 at once, dragging adds the rest
                    hitChecked = true;
                    Assert.AreEqual(ally.maxHealth - hook.hitDamage, ally.Health, 0.01f, "the catch must take 110 at once");
                }
                t += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(ally, caught, "the hook passed through the ally");
            var dragged = ally.transform.position - caughtAt;
            float expected = hook.hitDamage + new Vector3(dragged.x, 0f, dragged.z).magnitude * hook.damagePerMeter;
            Assert.AreEqual(ally.maxHealth - expected, ally.Health, 3f, "the hook must take 110 plus the distance dragged, from allies too");
            Assert.Less(ally.Health, ally.maxHealth - hook.hitDamage - 20f, "dragging took next to nothing");
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling && !ally.IsHooked, "the ally was not let go alive");
            Assert.IsTrue(ally.IsControllable);
            var d = ally.transform.position - player.transform.position;
            Assert.Less(new Vector3(d.x, 0f, d.z).magnitude, 2f, "the ally was not pulled to Pudge");
            Assert.AreEqual(0, arena.Score);
            Assert.AreEqual(0, arena.Deaths);
            Assert.Greater(hook.CooldownRemaining, 0f, "catching an ally must not refresh the hook");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling, "the ally fell after being let go");
        }

        /// <summary>
        /// The enemy at z = 4 catches the ally at z = -8 with a slow pull; the rescuer stands beside the ally and holds
        /// it with its hook. Hands the enemy's hook to <paramref name="done"/>.
        /// </summary>
        IEnumerator SetUpTugOfWar(Pudge ally, Pudge enemy, HookAbility rescuerHook, Pudge rescuer, System.Action<HookAbility> done)
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            foreach (var b in bots.Where(b => b != ally && b != enemy && b != rescuer))
                b.transform.position = new Vector3(-12f + 2f * bots.IndexOf(b), 0f, 12f);
            enemy.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4f), Quaternion.LookRotation(Vector3.back));
            ally.transform.position = new Vector3(0f, 0f, -8f);
            rescuer.transform.SetPositionAndRotation(new Vector3(3f, 0f, -8f), Quaternion.LookRotation(Vector3.left));
            var enemyHook = enemy.GetComponent<HookAbility>();
            enemyHook.pullSpeed = 2f; // slow pull: the rescue throw has time
            yield return null;

            Assert.IsTrue(enemyHook.TryCast(ally.transform.position));
            float t = 0f;
            while (!ally.IsHooked && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(enemyHook, ally.HookedBy, "the enemy did not catch the ally");

            yield return null; // from this frame on the ally is dragged after the enemy's head (it jumps to it first)

            // thrown where the dragged ally will be when the head gets there: a throw at where it stands now passes
            // behind it
            var pull = enemy.transform.position - ally.transform.position;
            pull.y = 0f;
            float eta = rescuerHook.castDelay + Vector3.Distance(rescuer.transform.position, ally.transform.position) / rescuerHook.flySpeed;
            Assert.IsTrue(rescuerHook.TryCast(ally.transform.position + pull.normalized * enemyHook.pullSpeed * eta));
            t = 0f;
            while (ally.HeldBy == null && t < 2f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(rescuerHook, ally.HeldBy, "the rescuer's hook did not hold the ally");
            Assert.AreEqual(enemyHook, ally.HookedBy, "a held ally must stay on the enemy's hook");
            done(enemyHook);
        }

        [UnityTest]
        public IEnumerator AllyHookHoldsAHeroOnAnEnemysHookAndPullsItHomeWhenItLetsGo()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            HookAbility enemyHook = null;
            yield return SetUpTugOfWar(ally, enemy, hook, player, h => enemyHook = h);

            // tug of war: the enemy pulls at half speed and drags the rescuer after the ally
            var allyFrom = ally.transform.position;
            var playerFrom = player.transform.position;
            float Chain() => Vector3.Distance(player.transform.position, ally.transform.position);
            float chain = Chain();
            yield return new WaitForSeconds(0.5f);
            float pulled = Vector3.Distance(allyFrom, ally.transform.position);
            Assert.AreEqual(enemyHook.pullSpeed / 3f * 0.5f, pulled, 0.15f, "the held ally is not pulled three times slower");
            // the ally goes off at an angle to the chain: the rescuer follows by less than the ally's way, on a taut chain
            Assert.Greater(Vector3.Distance(playerFrom, player.transform.position), 0.1f, "the rescuer is not dragged after the ally");
            Assert.AreEqual(chain, Chain(), 0.1f, "the chain does not stay taut");
            Assert.AreEqual(0, arena.RescuesOf(Team.Player), "holding is not a rescue yet");

            // the enemy's hook lets go (its owner was caught): the rescuer pulls the ally home
            enemyHook.Cancel();
            float t = 0f;
            while (hook.IsBusy && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling && !ally.IsHooked, "the rescued ally was not let go unharmed");
            var d = ally.transform.position - player.transform.position;
            Assert.Less(new Vector3(d.x, 0f, d.z).magnitude, 2f, "the ally was not pulled to the rescuer");
            Assert.AreEqual(0, arena.Deaths);
            Assert.AreEqual(1, arena.RescuesOf(Team.Player), "the rescue was not counted");
            Assert.AreEqual(0, arena.RescuesOf(Team.Bots));
            Assert.AreEqual(1, arena.ScoreOf(player).rescues, "the rescue is not in the rescuer's own score");
            Assert.AreEqual(0, arena.ScoreOf(ally).rescues);
            Assert.AreEqual(arena.KillGold(ally) / 2, player.GetComponent<HeroInventory>().Gold, "a rescue pays half the kill reward");
            Assert.AreEqual(0, ally.GetComponent<HeroInventory>().Gold, "the rescued hero gets nothing");
        }

        /// <summary>
        /// The enemy at z = 4 throws at the ally standing at z = -9 (or, with <paramref name="enemyAim"/>, elsewhere);
        /// the player beside the ally pulls it away first. Returns through <paramref name="done"/> whether it was a rescue.
        /// </summary>
        IEnumerator PullAllyAwayFromAnEnemyThrow(Vector3? allyRun, Vector3? enemyAim, System.Action<Pudge> done)
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 12f);
            enemy.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4f), Quaternion.LookRotation(Vector3.back));
            ally.transform.SetPositionAndRotation(new Vector3(0f, 0f, -9f), Quaternion.LookRotation(Vector3.back));
            player.transform.SetPositionAndRotation(new Vector3(3f, 0f, -9f), Quaternion.LookRotation(Vector3.left));
            yield return null;
            yield return null;

            var enemyHook = enemy.GetComponent<HookAbility>();
            Assert.IsTrue(enemyHook.TryCast(enemyAim ?? ally.transform.position));
            if (allyRun is { } run)
            {
                ally.GetComponent<PudgeMotor>().SetMoveInput(run);
                yield return new WaitForSeconds(0.15f); // under way, able to dodge
            }
            Pudge caught = null;
            hook.TargetCaught += h => caught = h;
            var lead = allyRun is { } r ? r * (ally.GetComponent<PudgeMotor>().moveSpeed * 0.4f) : Vector3.zero;
            Assert.IsTrue(hook.TryCast(ally.transform.position + lead));
            float t = 0f;
            while (caught == null && t < 2f) { t += Time.deltaTime; yield return null; }
            ally.GetComponent<PudgeMotor>().SetMoveInput(Vector3.zero);
            Assert.AreEqual(ally, caught, "the player's hook did not reach the ally first");
            Assert.IsTrue(enemyHook.IsBusy, "the enemy's hook must still be on its way");
            done(ally);
        }

        [UnityTest]
        public IEnumerator PullingAStandingAllyOutOfASureEnemyHookIsARescue()
        {
            Pudge ally = null;
            yield return PullAllyAwayFromAnEnemyThrow(null, null, a => ally = a);
            Assert.AreEqual(1, arena.RescuesOf(Team.Player), "the enemy hook would surely have caught the standing ally");
            Assert.AreEqual(arena.KillGold(ally) / 2, player.GetComponent<HeroInventory>().Gold, "a rescue pays half the kill reward");
            float t = 0f;
            while (hook.IsBusy && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling, "the rescued ally died");
        }

        [UnityTest]
        public IEnumerator PullingAnAllyThatRunsTooLateOutOfASureEnemyHookIsARescue()
        {
            // the ally runs, but toward the enemy: the head would still reach it
            Pudge ally = null;
            yield return PullAllyAwayFromAnEnemyThrow(Vector3.forward, null, a => ally = a);
            Assert.AreEqual(1, arena.RescuesOf(Team.Player), "running did not get the ally out of the hook's way");
            Assert.AreEqual(arena.KillGold(ally) / 2, player.GetComponent<HeroInventory>().Gold);
        }

        [UnityTest]
        public IEnumerator PullingAnAllyThatRunsClearIsNoRescue()
        {
            // the ally runs sideways: it gets off the hook's line before the head arrives
            yield return PullAllyAwayFromAnEnemyThrow(Vector3.left, null, _ => { });
            Assert.AreEqual(0, arena.RescuesOf(Team.Player), "the ally was dodging the hook by itself");
            Assert.AreEqual(0, player.GetComponent<HeroInventory>().Gold);
        }

        [UnityTest]
        public IEnumerator PullingAnAllyTheEnemyHookMissesIsNoRescue()
        {
            // the enemy throws well to the side: the ally was never in danger
            yield return PullAllyAwayFromAnEnemyThrow(null, new Vector3(-10f, 0f, -9f), _ => { });
            Assert.AreEqual(0, arena.RescuesOf(Team.Player), "the enemy hook would have missed the ally");
        }

        [UnityTest]
        public IEnumerator EnemyFallingIntoThePitInATugOfWarBreaksItsChain()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            HookAbility enemyHook = null;
            yield return SetUpTugOfWar(ally, enemy, hook, player, h => enemyHook = h);

            // the enemy goes into the pit while the player holds the ally: its chain breaks, it does not drag the ally
            enemy.StartFalling(Vector3.zero);
            Assert.IsFalse(enemyHook.IsBusy, "the falling enemy's chain did not break");
            Assert.IsFalse(ally.HookedBy == enemyHook, "the ally is still on the falling enemy's hook");
            float t = 0f;
            while (hook.IsBusy && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling && !ally.IsHooked, "the ally was dragged after the falling enemy");
            var d = ally.transform.position - player.transform.position;
            Assert.Less(new Vector3(d.x, 0f, d.z).magnitude, 2f, "the rescuer did not pull the ally home");
            Assert.AreEqual(1, arena.RescuesOf(Team.Player), "the rescue was not counted");
        }

        [UnityTest]
        public IEnumerator TugOfWarLostAtThePitBreaksTheRescuersChain()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            HookAbility enemyHook = null;
            yield return SetUpTugOfWar(ally, enemy, hook, player, h => enemyHook = h);
            enemyHook.pullSpeed = 6f; // hurry up: 3 m/s while held

            bool allyDied = false;
            ally.Died += _ => allyDied = true;
            float t = 0f;
            while (!allyDied && t < 6f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(allyDied, "the ally was not dragged into the pit");
            Assert.IsTrue(player.IsAlive && !player.IsFalling, "the rescuer must stay on the edge");
            Assert.IsFalse(hook.IsBusy, "the rescuer's chain did not break");
            Assert.IsFalse(hook.HeadVisible);
            Assert.IsFalse(arena.IsOverPit(player.transform.position));
            Assert.AreEqual(0, arena.RescuesOf(Team.Player));
            Assert.AreEqual(0, player.GetComponent<HeroInventory>().Gold);
        }

        [UnityTest]
        public IEnumerator HeldHeroLeavingTheGameBreaksTheRescuersChain()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            yield return SetUpTugOfWar(ally, enemy, hook, player, _ => { });

            Object.Destroy(ally.gameObject); // a disconnect, or a bot taken out for a joining player
            yield return null;
            yield return null;
            Assert.IsFalse(hook.IsBusy, "the rescuer is stuck holding a hero that left the game");
            Assert.IsFalse(hook.HeadVisible);
            Assert.IsTrue(player.GetComponent<PudgeMotor>().CanMove, "the rescuer cannot move");
        }

        [UnityTest]
        public IEnumerator RescuedPlayerSeesWhoSavedHimAndTheTeamCountsIt()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            var allyHook = ally.GetComponent<HookAbility>();
            HookAbility enemyHook = null;
            yield return SetUpTugOfWar(player, enemy, allyHook, ally, h => enemyHook = h);

            enemyHook.Cancel();
            float t = 0f;
            while (player.HookedBy != allyHook && t < 1f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(allyHook, player.HookedBy, "the ally did not pull the player home");
            yield return null;

            var announcer = Object.FindAnyObjectByType<KillAnnouncer>();
            Assert.AreEqual("СПАСЁН", announcer.LastText);
            Assert.AreEqual($"Тебя спас {ally.Nickname}", announcer.LastSubtitle);
            Assert.AreEqual(1, arena.RescuesOf(Team.Player));
            Assert.AreEqual(1, arena.ScoreOf(ally).rescues, "the rescue is the ally's own");
            Assert.AreEqual(1, arena.ScoreOf(player).rescued, "the player was rescued once");
            Assert.AreEqual(0, arena.ScoreOf(player).rescues);
            var hud = Object.FindAnyObjectByType<ScoreHud>();
            Assert.AreEqual("У/С/С 0/0/0", hud.label.text, "the corner shows the player's own score: an ally's rescue is not the player's");
            Assert.AreEqual("Команда 0/0/1", hud.TeamLabel.text, "the team's rescues are on the team line");
        }

        [UnityTest]
        public IEnumerator AnAllysKillIsTheTeamsNotThePlayersOwn()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var ally = bots[0];
            var victim = bots[1];
            ally.team = Team.Player;
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 13f);
            ally.transform.SetPositionAndRotation(new Vector3(6f, 0f, -arena.pitHalfDepth - 1.5f), Quaternion.identity);
            victim.transform.position = new Vector3(6f, 0f, arena.pitHalfDepth + 1.5f);
            yield return null;

            Assert.IsTrue(ally.GetComponent<HookAbility>().TryCast(victim.transform.position));
            float t = 0f;
            while (arena.KillsOf(Team.Player) == 0 && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(1, arena.KillsOf(Team.Player), "the ally did not kill the bot");
            Assert.AreEqual(1, arena.ScoreOf(ally).kills, "the kill is the ally's own");
            Assert.AreEqual(0, arena.ScoreOf(player).kills, "an ally's kill must not count as the player's");
            var hud = Object.FindAnyObjectByType<ScoreHud>();
            Assert.AreEqual("У/С/С 0/0/0", hud.label.text, "the corner shows the player's own score");
            Assert.AreEqual("Команда 1/0/0", hud.TeamLabel.text, "the team line counts the ally's kill");
            Assert.AreEqual(0, player.GetComponent<HeroInventory>().Gold, "the ally's kill reward is not the player's");
        }

        [UnityTest]
        public IEnumerator BotLeadsItsHookToHoldAnAllyOnAnEnemysHook()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var ally = bots[0];
            var rescuer = bots[1];
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 14f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4f), Quaternion.identity);
            ally.transform.position = new Vector3(0f, 0f, 8f);
            // to the side of the drag line: the head must be thrown ahead of the ally, not at it
            rescuer.transform.SetPositionAndRotation(new Vector3(6f, 0f, 5f), Quaternion.LookRotation(Vector3.left));
            hook.pullSpeed = 4f;
            yield return null;
            yield return null;

            var brain = rescuer.GetComponent<BotBrain>();
            brain.rescueChance = 1f;
            brain.rescueReaction = Vector2.zero;
            brain.pullOutReaction = new Vector2(30f, 30f); // only the tug of war, no pulling out before the catch
            brain.throwInterval = new Vector2(30f, 30f); // only the rescue throw
            brain.pauseRange = new Vector2(30f, 30f);    // and no wandering off before it
            brain.enabled = true;
            var rescueHook = rescuer.GetComponent<HookAbility>();
            Pudge caught = null;
            rescueHook.TargetCaught += h => caught = h;

            Assert.IsTrue(hook.TryCast(ally.transform.position));
            float t = 0f;
            while (caught == null && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ally, caught, "the bot's hook did not reach its dragged ally");
            Assert.AreEqual(rescueHook, ally.HeldBy);
            brain.enabled = false;

            // the player's hook lets go: the bot pulls its ally home
            hook.Cancel();
            t = 0f;
            while (rescueHook.IsBusy && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling && !ally.IsHooked, "the rescued ally died");
            Assert.AreEqual(1, arena.RescuesOf(Team.Bots));
        }

        [UnityTest]
        public IEnumerator BotPullsAnAllyOutOfTheWayOfAnEnemyHookAfterAReaction()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var ally = bots[0];
            var rescuer = bots[1];
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 14f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -7f), Quaternion.identity);
            ally.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4.5f), Quaternion.LookRotation(Vector3.back));
            rescuer.transform.SetPositionAndRotation(new Vector3(3f, 0f, 6f), Quaternion.LookRotation(Vector3.left));
            yield return null;
            yield return null;

            var brain = rescuer.GetComponent<BotBrain>();
            brain.rescueChance = 1f;
            brain.pullOutReaction = new Vector2(0.1f, 0.1f);
            brain.throwInterval = new Vector2(30f, 30f); // only the rescue throw
            brain.pauseRange = new Vector2(30f, 30f);    // and no wandering off before it
            brain.enabled = true;
            var rescueHook = rescuer.GetComponent<HookAbility>();
            Pudge rescued = null, hit = null;
            rescueHook.TargetCaught += h => rescued = h;
            hook.TargetCaught += h => hit = h;

            // the player throws at the standing ally: the bot is not quick off the mark, but its head gets there first
            Assert.IsTrue(hook.TryCast(ally.transform.position));
            yield return new WaitForSeconds(0.05f);
            Assert.IsFalse(rescueHook.IsBusy, "the bot threw before its reaction time");
            float t = 0f;
            while (rescued == null && hit == null && t < 2f) { t += Time.deltaTime; yield return null; }
            brain.enabled = false;
            Assert.AreEqual(ally, rescued, "the bot did not pull its ally out of the way of the player's hook");
            Assert.AreNotEqual(ally, hit, "the player's hook caught the ally");
            Assert.AreEqual(1, arena.RescuesOf(Team.Bots), "pulling the ally out of a sure hook is a rescue");
            Assert.AreEqual(1, arena.ScoreOf(rescuer).rescues);
        }

        [UnityTest]
        public IEnumerator BotInterceptCountsWindupFlightAndTheHeadsReach()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var bot = bots[0];
            var target = bots[1];
            bot.transform.SetPositionAndRotation(new Vector3(0f, 0f, 12f), Quaternion.LookRotation(Vector3.back));
            target.transform.position = new Vector3(5f, 0f, 6f);
            yield return null;

            var brain = bot.GetComponent<BotBrain>();
            var botHook = bot.GetComponent<HookAbility>();
            var v = new Vector3(-4f, 0f, -2f);
            Assert.IsTrue(brain.Intercept(target, v, out var aim));
            var to = target.transform.position - bot.transform.position;
            to.y = 0f;
            float delay = bot.GetComponent<PudgeMotor>().TimeToFace(to) + botHook.castDelay;
            float total = (aim - to).magnitude / v.magnitude; // the target walks to the meeting point for this long
            float flight = total - delay;
            Assert.Greater(flight, 0f);
            float reach = botHook.hitRadius + target.radius + botHook.LaunchOffset;
            Assert.AreEqual(botHook.flySpeed * flight + reach, aim.magnitude, 0.05f, "the head does not get to the meeting point in time");
            Assert.Greater(Vector3.Dot(aim - to, v.normalized), 0.5f, "the aim does not lead the moving target");
        }

        [UnityTest]
        public IEnumerator PudgesHookingEachOtherAtOnceBothFallIntoThePit()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var bot = bots[0];
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 14f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -6f), Quaternion.LookRotation(Vector3.forward));
            bot.transform.SetPositionAndRotation(new Vector3(0f, 0f, 6f), Quaternion.LookRotation(Vector3.back));
            var botHook = bot.GetComponent<HookAbility>();
            yield return null;

            bool playerDied = false, botDied = false;
            player.Died += _ => playerDied = true;
            bot.Died += _ => botDied = true;
            Assert.IsTrue(hook.TryCast(bot.transform.position));
            Assert.IsTrue(botHook.TryCast(player.transform.position));

            // well before the tether timeout: the two must not hang in place
            float t = 0f;
            while (!(playerDied && botDied) && t < 3.5f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(botDied, "the bot did not fall into the pit");
            Assert.IsTrue(playerDied, "the player did not fall into the pit");
            Assert.AreEqual(1, arena.Score, "the player's hook did not get the kill");
            Assert.AreEqual(1, arena.Deaths);
        }

        [UnityTest]
        public IEnumerator HeroesDoNotWalkThroughEachOtherButSlideAlong()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var bot = bots[0];
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 14f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -12.5f), Quaternion.identity);
            bot.transform.position = new Vector3(0f, 0f, -10.5f);
            var motor = player.GetComponent<PudgeMotor>();
            float minGap = player.radius + bot.radius - 0.01f;
            float Gap()
            {
                var d = player.transform.position - bot.transform.position;
                return new Vector3(d.x, 0f, d.z).magnitude;
            }

            // head-on: the player stops at the bot
            motor.SetMoveInput(Vector3.forward);
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                yield return null;
                Assert.GreaterOrEqual(Gap(), minGap, "the player walked into the bot");
            }
            Assert.Less(player.transform.position.z, bot.transform.position.z, "the player walked through the bot");

            // slightly off-centre: the player slides around the bot and goes on (stopping short of the pit)
            player.transform.position = new Vector3(0.3f, 0f, -12.5f);
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                yield return null;
                Assert.GreaterOrEqual(Gap(), minGap, "the player walked into the bot");
            }
            motor.SetMoveInput(Vector3.zero);
            Assert.Greater(player.transform.position.z, bot.transform.position.z, "the player did not slide past the bot");
        }

        [UnityTest]
        public IEnumerator OverlappingAlliesDriftApartAndDoNotSinkIntoEachOtherAtThePitEdge()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            foreach (var brain in Object.FindObjectsByType<BotBrain>()) brain.enabled = false;
            var (a, b) = (bots[0], bots[1]);
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 14f);
            float touch = a.radius + b.radius;
            float Gap()
            {
                var d = a.transform.position - b.transform.position;
                return new Vector3(d.x, 0f, d.z).magnitude;
            }

            // one on top of the other (a respawn, an ally let go): they drift apart
            a.transform.position = b.transform.position = new Vector3(2f, 0f, 9f);
            yield return new WaitForSeconds(1f);
            Assert.GreaterOrEqual(Gap(), touch - 0.05f, "allies on top of each other stay inside each other");

            // one stands at the pit edge, the other walks along the edge into it: it stops, it does not sink in
            float edge = arena.pitHalfDepth + a.GetComponent<PudgeMotor>().pitGap;
            b.transform.position = new Vector3(0f, 0f, edge);
            a.transform.SetPositionAndRotation(new Vector3(-touch, 0f, edge + 0.2f), Quaternion.LookRotation(new Vector3(1f, 0f, -1f)));
            var motor = a.GetComponent<PudgeMotor>();
            motor.SetMoveInput(new Vector3(1f, 0f, -1f).normalized);
            float least = Gap();
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                yield return null;
                least = Mathf.Min(least, Gap());
            }
            motor.SetMoveInput(Vector3.zero);
            Assert.GreaterOrEqual(least, touch - 0.05f, "pressed against the edge, the bot sank into its ally");
        }

        [UnityTest]
        public IEnumerator TopBarShowsTheTeamsNetWorthUnderTheScore()
        {
            var hud = Object.FindAnyObjectByType<ScoreHud>();
            yield return null;
            Assert.IsNotNull(hud.RadiantWorth, "no net worth under the score");
            var coin = hud.RadiantWorth.transform.parent.Find("GoldIcon");
            Assert.IsNotNull(coin, "no gold icon between the teams' net worth");
            Assert.IsNotNull(coin.GetComponent<Image>().sprite, "the gold icon has no picture");
            foreach (var h in Pudge.All) h.GetComponent<HeroInventory>().AddGold(-h.GetComponent<HeroInventory>().Gold, popup: false);
            var lens = Object.FindAnyObjectByType<ShopUI>().rows.First(r => r.item.castRangeBonus > 0f).item; // Aether Lens
            var inventory = player.GetComponent<HeroInventory>();
            inventory.AddGold(lens.cost + 300, popup: false);
            Assert.IsTrue(inventory.TryBuy(lens));
            Pudge.All.First(h => h.team == Team.Bots).GetComponent<HeroInventory>().AddGold(1000, popup: false);
            Assert.AreEqual(lens.cost + 300, inventory.NetWorth, "the net worth is the gold and what the items cost");

            hud.ShowWorth();
            int radiant = ScoreHud.NetWorthOf(Team.Player), dire = ScoreHud.NetWorthOf(Team.Bots);
            Assert.AreEqual(lens.cost + 300, radiant);
            Assert.AreEqual(1000, dire);
            string Spaced(int v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
            StringAssert.EndsWith(Spaced(radiant), hud.RadiantWorth.text);
            StringAssert.Contains($"+{Spaced(radiant - dire)}", hud.RadiantWorth.text, "the leader's lead is not shown");
            Assert.AreEqual(FontStyle.Bold, hud.RadiantWorth.fontStyle);
            Assert.AreEqual("1 000", hud.DireWorth.text);
        }

        [UnityTest]
        public IEnumerator MatchClockRunsOverTheScore()
        {
            var hud = Object.FindAnyObjectByType<ScoreHud>();
            yield return null;
            Assert.IsNotNull(hud.Clock, "no match clock");
            Assert.Greater(hud.Clock.transform.parent.position.y, hud.RadiantKills.transform.parent.position.y, "the clock must stand over the score");
            Assert.AreEqual("0:00", hud.Clock.text);
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual("0:01", hud.Clock.text);
            Assert.AreEqual("12:05", ScoreHud.FormatClock(12 * 60 + 5));
            Assert.AreEqual("1:02:03", ScoreHud.FormatClock(3600 + 2 * 60 + 3));
            arena.ResetMatch();
            yield return null;
            Assert.AreEqual("0:00", hud.Clock.text, "a new match starts its clock anew");
        }

        [UnityTest]
        public IEnumerator BotDoesNotThrowThroughAnAlly()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var bot = bots[0];
            var ally = bots[1];
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 14f);
            bot.transform.SetPositionAndRotation(new Vector3(0f, 0f, 10f), Quaternion.LookRotation(Vector3.back));
            ally.transform.position = new Vector3(0.5f, 0f, 6f);
            player.transform.position = new Vector3(0f, 0f, -4f);
            yield return null;
            yield return null; // velocities settle after the teleport

            var brain = bot.GetComponent<BotBrain>();
            var aim = brain.AimOffset(player, lead: false);
            Assert.IsFalse(brain.PathClear(aim, player), "an ally stands in the hook's path");
            ally.transform.position = new Vector3(6f, 0f, 6f);
            yield return null;
            yield return null;
            Assert.IsTrue(brain.PathClear(aim, player), "the path is clear once the ally steps aside");
        }

        [UnityTest]
        public IEnumerator MarkersAndHealthBarsAreColouredByTheLocalPlayersTeam()
        {
            yield return null;
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            var bars = Object.FindAnyObjectByType<HealthBars>();
            var block = new MaterialPropertyBlock();
            Color Marker(Pudge h)
            {
                h.transform.Find("TeamMarker").GetComponent<Renderer>().GetPropertyBlock(block);
                return block.GetColor("_BaseColor");
            }
            Color Bar(Pudge h) => bars.Of(h).fill.GetComponent<Image>().color;
            // a colour read back from a property block (linear colour space) differs in the last digits
            void AssertColor(Color expected, Color actual, string message = null) =>
                Assert.Less(((Vector4)(expected - actual)).magnitude, 1e-3f, $"{message} expected {expected}, was {actual}");

            AssertColor(player.allyMarkerColor, Marker(player));
            AssertColor(bot.enemyMarkerColor, Marker(bot));
            Assert.AreEqual(bars.playerColor, Bar(player));
            Assert.AreEqual(bars.enemyColor, Bar(bot));

            // a bot on the player's team is an ally: green, whatever its prefab says
            bot.team = Team.Player;
            yield return null;
            AssertColor(bot.allyMarkerColor, Marker(bot), "an allied bot's marker stayed red");
            Assert.AreEqual(bars.playerColor, Bar(bot), "an allied bot's health bar stayed red");

            // the local player plays for Dire (as the host in a network game): its own marker stays green, Radiant is red
            player.team = Team.Bots;
            bot.team = Team.Player;
            yield return null;
            AssertColor(player.allyMarkerColor, Marker(player), "the local player's own marker must be green");
            AssertColor(bot.enemyMarkerColor, Marker(bot));
            Assert.AreEqual(bars.enemyColor, Bar(bot));
        }

        [UnityTest]
        public IEnumerator BotAimsWithOrWithoutLead()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
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
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
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
            Assert.AreEqual(arena.KillGold(victim), player.GetComponent<HeroInventory>().Gold, "the tethered kill belongs to the player");
            Assert.IsFalse(hook.chain.enabled);
        }

        [UnityTest]
        public IEnumerator CaughtCharacterLosesHookInWindupButNotInFlightAndAMissComesBack()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            for (int i = 0; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 12f);
            var bot = bots[0];
            var aim = player.transform.position + player.transform.forward * 10f;

            // still winding up: the hook breaks off
            Assert.IsTrue(hook.TryCast(aim));
            yield return null;
            Assert.IsTrue(hook.IsBusy && !hook.HeadVisible);
            player.AttachTo(bot.GetComponent<HookAbility>());
            Assert.IsFalse(hook.IsBusy, "a hook not yet thrown must break off when the owner is caught");
            player.Release();
            Assert.IsTrue(player.IsControllable);

            // already flying: the hook flies on, and a miss comes back to the caught owner instead of vanishing
            yield return new WaitUntil(() => hook.IsReady);
            Assert.IsTrue(hook.TryCast(aim));
            yield return new WaitForSeconds(hook.castDelay + 0.2f);
            Assert.IsTrue(hook.IsFlying);
            player.AttachTo(bot.GetComponent<HookAbility>());
            Assert.IsTrue(hook.IsFlying && hook.HeadVisible, "a hook in flight must fly on when the owner is caught");
            var head = hook.hookHead.position;
            float t = 0f;
            while (hook.IsBusy && t < 4f)
            {
                Assert.IsTrue(hook.HeadVisible, "a missed hook of a caught owner vanished");
                head = hook.hookHead.position;
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(hook.IsBusy, "the missed hook never came back");
            var left = head - player.transform.position;
            Assert.Less(new Vector3(left.x, 0f, left.z).magnitude, 2f, "the hook vanished away from the player instead of coming back");
            player.Release();

            // on its way back when the owner is caught: it comes back all the same
            yield return new WaitUntil(() => hook.IsReady);
            Assert.IsTrue(hook.TryCast(aim));
            t = 0f;
            while (hook.IsFlying || !hook.HeadVisible) { t += Time.deltaTime; Assert.Less(t, 3f, "the hook did not turn back"); yield return null; }
            Assert.IsTrue(hook.IsBusy && !hook.IsFlying, "the hook is not on its way back");
            player.AttachTo(bot.GetComponent<HookAbility>());
            Assert.IsTrue(hook.IsBusy && hook.HeadVisible, "a returning missed hook vanished when the owner was caught");
            t = 0f;
            while (hook.IsBusy && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(hook.IsBusy, "the returning hook never came back");

            // a falling owner: the empty hook comes back to it all the same, down into the pit
            player.Release();
            yield return new WaitUntil(() => hook.IsReady);
            Assert.IsTrue(hook.TryCast(aim));
            t = 0f;
            while (hook.IsFlying || !hook.HeadVisible) { t += Time.deltaTime; Assert.Less(t, 3f, "the hook did not turn back"); yield return null; }
            player.StartFalling(Vector3.zero);
            Assert.IsTrue(hook.IsBusy && hook.HeadVisible, "a returning missed hook vanished when the owner started falling");
            t = 0f;
            while (hook.IsBusy && t < 4f)
            {
                head = hook.hookHead.position;
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(hook.IsBusy, "the returning hook never came back to the falling owner");
            left = head - player.transform.position;
            Assert.Less(new Vector3(left.x, 0f, left.z).magnitude, 2f, "the hook vanished away from the falling owner instead of coming back");
            Assert.Less(head.y, 0f, "the hook did not follow its owner down into the pit");
        }

        [UnityTest]
        public IEnumerator HookFlyingWhenTheOwnerIsCaughtDragsItsCatchIntoThePit()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var victim = bots[0];   // the player's hook reaches it after the player is caught
            var hunter = bots[1];   // catches the player first
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 13f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -arena.pitHalfDepth - 1f), Quaternion.identity);
            victim.transform.position = new Vector3(0f, 0f, 8.5f);
            hunter.transform.position = new Vector3(3f, 0f, arena.pitHalfDepth + 1f);
            var toPlayer = player.transform.position - hunter.transform.position;
            hunter.transform.rotation = Quaternion.LookRotation(new Vector3(toPlayer.x, 0f, toPlayer.z));
            var hunterHook = hunter.GetComponent<HookAbility>();
            yield return null;

            bool playerCaught = false, victimCaught = false;
            hunterHook.TargetCaught += h => playerCaught |= h == player;
            hook.TargetCaught += h => victimCaught |= h == victim;
            Assert.IsTrue(hook.TryCast(victim.transform.position));
            Assert.IsTrue(hunterHook.TryCast(player.transform.position));

            float t = 0f;
            while ((arena.Deaths == 0 || arena.Score == 0) && t < 6f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(playerCaught, "the hunter did not catch the player");
            Assert.IsTrue(victimCaught, "the player's hook did not fly on to the victim");
            Assert.AreEqual(1, arena.Deaths, "the player did not die");
            Assert.AreEqual(1, arena.Score, "the victim was not dragged into the pit");
            Assert.IsFalse(victim.IsAlive);
            Assert.AreEqual(arena.KillGold(victim), player.GetComponent<HeroInventory>().Gold, "the dragged kill belongs to the player");
        }

        [UnityTest]
        public IEnumerator OwnerCarriedByAnAllyPullsItsCatchOn()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var victim = bots[0];   // caught by the player
            var ally = bots[1];     // hooks the player home meanwhile
            ally.team = Team.Player;
            for (int i = 2; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 13f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -arena.pitHalfDepth - 1f), Quaternion.identity);
            victim.transform.position = new Vector3(0f, 0f, 8.5f);
            ally.transform.SetPositionAndRotation(new Vector3(0f, 0f, -6f), Quaternion.identity);
            hook.pullSpeed = 2f; // slow pull: the victim is still far from the pit when the player is let go
            hook.tetherTimeout = 30f; // only the player getting free may end the taut chain here
            yield return null;

            bool caught = false;
            hook.TargetCaught += h => caught |= h == victim;
            Assert.IsTrue(hook.TryCast(victim.transform.position));
            float t = 0f;
            while (!caught && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(caught, "player's hook missed the victim");

            var allyHook = ally.GetComponent<HookAbility>();
            Assert.IsTrue(allyHook.TryCast(player.transform.position));
            t = 0f;
            while (!player.IsHooked && t < 2f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(allyHook, player.HookedBy, "the ally's hook did not catch the player");
            Assert.IsFalse(hook.IsTethered, "an ally only carries the player: its hook must pull on, not hang on a taut chain");
            Assert.IsTrue(hook.IsBusy && victim.IsHooked, "the carried player's hook let its catch go");

            t = 0f;
            while (player.IsHooked && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(player.IsControllable, "the ally did not let the player go");
            Assert.IsTrue(victim.IsHooked && !victim.IsFalling, "the victim must still be on the player's hook");

            t = 0f;
            while (arena.Score == 0 && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(1, arena.Score, "the freed player did not pull the victim on into the pit");
            Assert.AreEqual(arena.KillGold(victim), player.GetComponent<HeroInventory>().Gold, "the kill belongs to the player");
            Assert.IsFalse(hook.IsTethered);
        }

        [UnityTest]
        public IEnumerator OwnerCarriedByAnAllyKeepsItsThrownHook()
        {
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            var ally = bots[0]; // hooks the player home while the player's own hook is out
            ally.team = Team.Player;
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 13f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -12f), Quaternion.identity);
            ally.transform.SetPositionAndRotation(new Vector3(-6f, 0f, -12f), Quaternion.LookRotation(Vector3.right));
            yield return null;

            // thrown to the right, away from everyone: a miss
            Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.right * 10f));
            yield return new WaitForSeconds(hook.castDelay + 0.2f);
            Assert.IsTrue(hook.IsFlying);

            var allyHook = ally.GetComponent<HookAbility>();
            Assert.IsTrue(allyHook.TryCast(player.transform.position));
            float t = 0f;
            while (!player.IsHooked && t < 2f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(allyHook, player.HookedBy, "the ally's hook did not catch the player");

            // the hook flies on and comes back to the player wherever it is carried, instead of vanishing
            var head = hook.hookHead.position;
            t = 0f;
            while (hook.IsBusy && t < 5f)
            {
                Assert.IsTrue(hook.HeadVisible, "the player's hook vanished when an ally caught the player");
                head = hook.hookHead.position;
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(hook.IsBusy, "the player's hook never came back");
            var left = head - player.transform.position;
            Assert.Less(new Vector3(left.x, 0f, left.z).magnitude, 2f, "the hook vanished away from the player instead of coming back");
            Assert.IsTrue(player.IsAlive && !player.IsFalling);
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

        static ItemDef TestItem(ItemAction action, int cost, int charges, bool stacks, float range, float cooldown = 0f)
        {
            var item = ScriptableObject.CreateInstance<ItemDef>();
            item.displayName = action.ToString();
            item.action = action;
            item.cost = cost;
            item.charges = charges;
            item.stacks = stacks;
            item.unique = action == ItemAction.ChopTree;
            item.castRange = range;
            item.cooldown = cooldown;
            item.regenBonus = 7f;
            item.regenDuration = 16f;
            item.plantedTreeBonus = 2f;
            item.treeLifetime = 0f;
            return item;
        }

        [UnityTest]
        public IEnumerator ShopSellsTangoIronBranchAndQuellingBlade()
        {
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var sold = shop.rows.Select(r => r.item).ToList();
            var tango = sold.FirstOrDefault(i => i.action == ItemAction.EatTree);
            var branch = sold.FirstOrDefault(i => i.action == ItemAction.PlantTree);
            var blade = sold.FirstOrDefault(i => i.action == ItemAction.ChopTree);
            Assert.IsNotNull(tango, "no Tango in the shop: Pudge Wars → Build Test Arena");
            Assert.IsNotNull(branch, "no Iron Branch in the shop: Pudge Wars → Build Test Arena");
            Assert.IsNotNull(blade, "no Quelling Blade in the shop: Pudge Wars → Build Test Arena");
            Assert.AreEqual(70, tango.cost);
            Assert.AreEqual(50, branch.cost);
            Assert.AreEqual(3, tango.charges);
            Assert.AreEqual(0f, branch.treeLifetime, "the branch's tree stands for good: Pudge Wars → Build Test Arena");
            Assert.AreEqual(16f, blade.heroDamageBonus, "Quelling Blade: +16 damage on heroes: Pudge Wars → Build Test Arena");
            Assert.AreEqual(4f, blade.creepDamageBonus, "Quelling Blade: +4 damage on creeps");
            var quell = blade.abilities?.FirstOrDefault(a => !a.active);
            Assert.IsNotNull(quell, "Quelling Blade has no passive ability: Pudge Wars → Build Test Arena");
            StringAssert.Contains("+16 урона по героям и +4 по крипам", quell.description, "Quelling Blade's description: Pudge Wars → Build Test Arena");
            Assert.IsTrue(sold.All(i => i.icon != null), "an item has no icon");
            var net = PudgeWars.Net.NetPrefabs.Load();
            Assert.IsTrue(sold.All(i => net.IndexOf(i) >= 0), "an item is missing from the network catalog: Pudge Wars → Build Test Arena");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TangoEatsATreeIronBranchPlantsOneAndQuellingBladeCutsOne()
        {
            var trees = TerrainTrees.Instance;
            Assert.IsNotNull(trees, "the arena has no trees: Pudge Wars → Build Test Arena");
            var inventory = player.GetComponent<HeroInventory>();
            var tango = TestItem(ItemAction.EatTree, 70, 3, stacks: true, range: 1.65f);
            var branch = TestItem(ItemAction.PlantTree, 50, 1, stacks: false, range: 2f);
            var blade = TestItem(ItemAction.ChopTree, 100, 0, stacks: false, range: 3.5f, cooldown: 4f);
            inventory.AddGold(1000, popup: false);

            // Tango stacks its charges in one slot
            Assert.IsTrue(inventory.TryBuy(tango));
            Assert.IsTrue(inventory.TryBuy(tango));
            Assert.AreEqual(1, inventory.Items.Count, "a second Tango must not take a slot");
            Assert.AreEqual(6, inventory.ChargesAt(0));
            Assert.IsTrue(inventory.TryBuy(branch));
            Assert.IsTrue(inventory.TryBuy(blade));
            Assert.IsFalse(inventory.TryBuy(blade), "one Quelling Blade is enough");

            // a tree next to the player: out of reach first, then eaten
            int tree = 0;
            var foot = trees.Position(tree);
            player.transform.position = foot + Vector3.right * 6f;
            player.healthRegen = 0f;
            player.Damage(300f, null);
            Assert.IsFalse(inventory.TryUse(0, foot, tree), "a tree 6 m away is out of Tango's reach");
            player.transform.position = foot + Vector3.right * 1.3f;
            Assert.IsTrue(inventory.TryUse(0, foot, tree));
            Assert.IsFalse(trees.IsStanding(tree), "the eaten tree still stands");
            Assert.AreEqual(5, inventory.ChargesAt(0));
            Assert.AreEqual(7f, player.HealthRegen, 1e-4f, "Tango regenerates 7 per second");
            float health = player.Health;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(health + 7f, player.Health, 1.5f);

            // Iron Branch plants a tree where it is clear; the branch is used up
            var spot = Enumerable.Range(0, 16)
                .Select(i => player.transform.position + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * 2f)
                .FirstOrDefault(trees.CanPlant);
            Assert.IsTrue(trees.CanPlant(spot), "no clear spot to plant a tree near the player");
            int obstacles = Obstacle.All.Count;
            Assert.IsTrue(inventory.TryUse(1, spot, HeroInventory.NoTree));
            Assert.AreEqual(2, inventory.Items.Count, "the branch must be used up");
            int planted = trees.Nearest(spot, 0.5f);
            Assert.IsTrue(TerrainTrees.IsPlanted(planted), "no planted tree where the branch was used");
            Assert.AreEqual(obstacles + 1, Obstacle.All.Count, "heroes must walk around the planted tree");
            Assert.IsFalse(trees.CanPlant(spot), "a second tree on the same spot");
            CaptureScreen("planted_tree");

            // Tango on a planted tree feeds twice as much
            Assert.IsTrue(inventory.TryUse(0, spot, planted));
            Assert.IsFalse(trees.IsStanding(planted));
            Assert.AreEqual(7f + 14f, player.HealthRegen, 1e-4f, "a planted tree must feed twice");

            // Quelling Blade cuts a tree within 3.5 m, then needs 4 s
            int next = trees.Nearest(player.transform.position, 100f);
            player.transform.position = trees.Position(next) + Vector3.back * 3f;
            int bladeSlot = inventory.SlotOf(blade);
            Assert.AreEqual(1, bladeSlot, "a used-up branch's slot is closed up: the blade moves toward Z");
            Assert.IsTrue(inventory.TryUse(bladeSlot, trees.Position(next), next));
            Assert.IsFalse(trees.IsStanding(next));
            Assert.Greater(inventory.CooldownLeft(bladeSlot), 3.9f);
            // the slot shows its key and a dark radial shade with the seconds left, as the ability bar does
            var shop = Object.FindAnyObjectByType<ShopUI>();
            yield return null;
            var slotIcon = shop.slots[bladeSlot].transform;
            Assert.AreEqual(ShopUI.SlotKeys[bladeSlot], slotIcon.Find("Key").GetComponent<Text>().text);
            var shade = slotIcon.Find("Cooldown").GetComponent<Image>();
            Assert.IsTrue(shade.enabled, "no cooldown shade over the slot");
            Assert.AreEqual(Image.FillMethod.Radial360, shade.fillMethod, "the shade must run round as on the ability bar");
            Assert.Greater(shade.fillAmount, 0.9f);
            Assert.GreaterOrEqual(shade.color.a, 0.8f, "the shade must be dark enough to see");
            StringAssert.IsMatch(@"^\d+\.\d$", slotIcon.Find("CooldownTime").GetComponent<Text>().text);
            int another = trees.Nearest(player.transform.position, 100f);
            Assert.IsFalse(inventory.TryUse(bladeSlot, trees.Position(another), another), "Quelling Blade must wait out its cooldown");
            Assert.IsTrue(inventory.Owns(blade), "Quelling Blade is not used up");

            // a planted tree goes by itself; death ends Tango's regeneration
            int shortLived = trees.Plant(spot, 0.3f);
            Assert.IsTrue(trees.IsStanding(shortLived));
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(trees.IsStanding(shortLived), "the planted tree did not go after its time");
            Assert.AreEqual(obstacles - 1, Obstacle.All.Count, "planted trees must leave no obstacles behind");

            // hovering a slot tells all about its item; a right click sells it for half (Tango: of the charges left)
            var lens = TestItem(ItemAction.None, 2000, 0, stacks: false, range: 0f);
            lens.castRangeBonus = 2.75f;
            inventory.AddGold(lens.cost, popup: false);
            Assert.IsTrue(inventory.TryBuy(lens));
            int lensSlot = inventory.SlotOf(lens);
            Assert.AreEqual(HeroInventory.SlotCount - 1, lensSlot, "a passive item keeps to the far slot");
            var tangoPointer = shop.slots[0].GetComponent<ItemSlotPointer>();
            Assert.IsNotNull(tangoPointer, "the slot does not listen to the pointer");
            tangoPointer.OnPointerEnter(new PointerEventData(EventSystem.current));
            Assert.IsNotNull(shop.TooltipText, "no description over a hovered slot");
            StringAssert.Contains("EatTree", shop.TooltipText);
            StringAssert.Contains($"Продать за {tango.cost * 4 / 3 / 2}", shop.TooltipText, "the sale price is half the charges left");
            Assert.IsTrue(shop.TooltipAbilities.Any(a => a.active), "Tango's use must be in a blue (active) frame");
            tangoPointer.OnPointerExit(new PointerEventData(EventSystem.current));
            Assert.IsNull(shop.TooltipText, "the description stayed after the pointer left");
            int gold = inventory.Gold;
            int value = inventory.SellValue(bladeSlot);
            Assert.AreEqual(blade.cost / 2, value);
            shop.slots[bladeSlot].GetComponent<ItemSlotPointer>().OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right });
            Assert.AreEqual(gold + value, inventory.Gold, "a right click must sell the item for half its cost");
            Assert.IsFalse(inventory.Owns(blade));
            Assert.AreEqual(lensSlot, inventory.SlotOf(lens), "selling a usable item must not move the passive one");
            Assert.IsNull(inventory.ItemIn(bladeSlot), "the sold item's slot must be free (nothing after it to close up)");
            player.StartFalling(Vector3.zero);
            yield return new WaitUntil(() => !player.IsAlive);
            Assert.AreEqual(0f, player.HealthRegen, 1e-4f, "Tango's regeneration must end with death");
        }

        [UnityTest]
        public IEnumerator HookCostsManaAndBarsShowItAsText()
        {
            hook.manaCost = 120f;
            var bars = Object.FindAnyObjectByType<HealthBars>();
            var abilities = Object.FindAnyObjectByType<AbilityBar>();
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            bot.transform.position = new Vector3(20f, 0f, 20f); // out of the way
            yield return null;
            yield return null;
            Assert.AreEqual("267/267", bars.ManaLabel(player).text, "no mana as text on the bar");
            Assert.AreEqual("+0.8", bars.ManaRegenLabel(player).text, "no mana regeneration as text on the bar");
            Assert.IsNotNull(bars.OwnRim(player), "the own bar has no white rim");
            Assert.AreEqual(Color.white, bars.OwnRim(player).GetComponent<Image>().color, "the own bar's rim must be white");
            Assert.IsNull(bars.OwnRim(bot), "another hero's bar must not have the rim");
            Assert.AreEqual("120", abilities.ManaCostLabel.text, "no mana cost on the hook's slot");

            // a throw takes 120 mana; without 120 there is no throw
            float mana = player.Mana;
            Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.forward * 5f));
            yield return null;
            // turning and winding up take nothing: the mana goes as the hook flies out
            while (!hook.HeadVisible)
            {
                Assert.GreaterOrEqual(player.Mana, mana - 0.01f, "the mana went before the hook flew out");
                yield return null;
            }
            Assert.AreEqual(mana - 120f, player.Mana, 1f, "the hook must take 120 mana");
            yield return new WaitUntil(() => hook.IsReady);
            player.SpendMana(player.Mana - 50f);
            Assert.IsFalse(hook.HasMana);
            Assert.IsFalse(hook.TryCast(player.transform.position + Vector3.forward * 5f), "the hook was thrown without the mana");
            yield return null;
            Assert.AreNotEqual(Color.white, abilities.slots[0].icon.color, "the hook's icon must turn blue without the mana");
            var input = player.GetComponent<PlayerInputController>();
            bool noMana = false;
            input.AbilityNoMana += () => noMana = true;
            Assert.IsFalse(input.SelectAbility(PlayerInputController.HookSlot), "the hook was picked without the mana");
            Assert.IsTrue(noMana, "no \"no mana\" for the hook asked without mana");
        }

        [UnityTest]
        public IEnumerator ArcaneBootsFromBootsAndRingOfBasiliusWithAuraAndManaForAllies()
        {
            var inventory = player.GetComponent<HeroInventory>();
            var input = player.GetComponent<PlayerInputController>();
            var motor = player.GetComponent<PudgeMotor>();
            var mask = TestItem(ItemAction.None, 175, 0, stacks: false, range: 0f);
            mask.manaRegenBonus = 0.7f;
            var ringRecipe = TestItem(ItemAction.None, 250, 0, stacks: false, range: 0f);
            var ring = TestItem(ItemAction.None, 425, 0, stacks: false, range: 0f);
            ring.components = new[] { mask, ringRecipe };
            ring.manaRegenBonus = 1.5f;
            ring.auraManaRegen = 1f;
            ring.auraRadius = 8f;
            var slippers = TestItem(ItemAction.None, 140, 0, stacks: false, range: 0f);
            slippers.moveSpeedBonus = 0.1f;
            slippers.agilityBonus = 3f;
            var bootsRecipe = TestItem(ItemAction.None, 360, 0, stacks: false, range: 0f);
            var boots = TestItem(ItemAction.None, 500, 0, stacks: false, range: 0f);
            boots.components = new[] { slippers, bootsRecipe };
            boots.moveSpeedBonus = 0.45f;
            boots.boots = true;
            var arcane = TestItem(ItemAction.RestoreAllyMana, 925, 0, stacks: false, range: 8f, cooldown: 55f);
            arcane.unique = true;
            arcane.components = new[] { boots, ring };
            arcane.manaRegenBonus = 1.5f;
            arcane.moveSpeedBonus = 0.45f;
            arcane.boots = true;
            arcane.auraManaRegen = 1f;
            arcane.auraRadius = 8f;
            arcane.restoreMana = 175f;
            var enemy = Pudge.All.First(h => h.team != player.team);
            var ally = Pudge.All.First(h => h.team != player.team && h != enemy);
            ally.team = player.team; // a teammate for the aura and the mana
            try
            {
                yield return null;
                inventory.AddGold(10000, popup: false);
                // Slippers of Agility: +10 speed, +3 agility; with the recipe they become Boots of Speed
                Assert.IsTrue(inventory.TryBuy(slippers));
                Assert.AreEqual(2.8f + 0.1f, motor.MoveSpeed, 1e-4f, "Slippers of Agility must give +10 speed");
                Assert.AreEqual(3f, inventory.AgilityBonus, 1e-4f);
                Assert.AreEqual(360, inventory.PriceOf(boots));
                Assert.IsTrue(inventory.TryBuy(bootsRecipe));
                Assert.IsTrue(inventory.Owns(boots), "the slippers and the recipe did not become Boots of Speed");
                Assert.AreEqual(2.8f + 0.45f, motor.MoveSpeed, 1e-4f);
                // other slippers add up with boots; two boots do not
                Assert.IsTrue(inventory.TryBuy(slippers));
                Assert.AreEqual(2.8f + 0.45f + 0.1f, motor.MoveSpeed, 1e-4f, "slippers must add up with boots");
                Assert.IsTrue(inventory.TrySell(inventory.SlotOf(slippers)));
                Assert.IsTrue(inventory.TrySell(inventory.SlotOf(boots)));
                Assert.IsTrue(inventory.TryBuy(mask));
                Assert.AreEqual(0.8f + 0.7f, player.ManaRegen, 1e-4f, "Sage's Mask must give +0.7");
                // the mask counts toward the boots through the ring: only the slippers, the boots' recipe and the ring's are left
                Assert.AreEqual(140 + 360 + 250, inventory.PriceOf(arcane));
                int gold = inventory.Gold;
                Assert.IsTrue(inventory.TryBuy(arcane));
                Assert.AreEqual(gold - 750, inventory.Gold);
                Assert.IsTrue(inventory.Owns(arcane));
                Assert.AreEqual(1, inventory.Items.Count, "the mask must go into the boots");
                Assert.AreEqual(2.8f + 0.45f, motor.MoveSpeed, 1e-4f, "Arcane Boots must give +45 speed");
                Assert.AreEqual(0.8f + 1.5f + 1f, player.ManaRegen, 1e-4f, "Arcane Boots: +1.5 and Basilius Aura +1 on the owner itself");

                // the aura: allies within 8 m, not enemies
                enemy.transform.position = player.transform.position + Vector3.right * 3f;
                Assert.AreEqual(0f, enemy.AuraManaRegen, 1e-4f, "an enemy must not get the aura");
                ally.transform.position = player.transform.position + Vector3.right * 5f;
                Assert.AreEqual(1f, ally.AuraManaRegen, 1e-4f, "an ally within 8 m must get the aura");
                Assert.AreEqual(0.8f + 1f, ally.ManaRegen, 1e-4f);
                ally.transform.position = player.transform.position + Vector3.right * 9f;
                Assert.AreEqual(0f, ally.AuraManaRegen, 1e-4f, "an ally beyond 8 m must not get the aura");
                ally.transform.position = player.transform.position + Vector3.right * 5f;

                // the use: at once, on the key or the slot, +175 mana to the allies around, the owner too; then 55 s
                Assert.IsTrue(player.SpendMana(200f) && enemy.SpendMana(200f) && ally.SpendMana(200f));
                float own = player.Mana, enemyMana = enemy.Mana, allyMana = ally.Mana;
                int slot = inventory.SlotOf(arcane);
                int bursts = ManaBurst.ActiveCount, sounds = ManaBurst.SoundsPlayed;
                Assert.IsFalse(input.SelectItem(slot), "Arcane Boots must be used at once, not wait for a target");
                // the blue burst: a faint wave on the ground, a rune circle and winding strands on the owner and the
                // ally (not the enemy), and Dota's sound
                Assert.AreEqual(bursts + 1 + 2 * 2, ManaBurst.ActiveCount, "no mana burst (Resources/Effects/ManaBurst.shader)");
                Assert.AreEqual(sounds + 1, ManaBurst.SoundsPlayed, "no Arcane Boots sound (Resources/Audio/Items/arcane_boots)");
                Assert.AreEqual(own + 175f, player.Mana, 0.5f, "the owner did not get 175 mana");
                Assert.AreEqual(enemyMana, enemy.Mana, 0.5f, "an enemy got the mana");
                Assert.AreEqual(allyMana + 175f, ally.Mana, 0.5f, "an ally within reach did not get 175 mana");
                Assert.AreEqual(55f, inventory.CooldownLeft(slot), 0.1f);
                Assert.IsFalse(inventory.TryUse(slot, player.transform.position, HeroInventory.NoTree), "used again during the cooldown");
            }
            finally
            {
                foreach (var item in new[] { mask, ringRecipe, ring, slippers, bootsRecipe, boots, arcane }) Object.Destroy(item);
            }
        }

        [UnityTest]
        public IEnumerator RingOfBasiliusGivesTheKillerAShareOfTheVictimsMana()
        {
            var inventory = player.GetComponent<HeroInventory>();
            var ring = TestItem(ItemAction.None, 425, 0, stacks: false, range: 0f);
            ring.manaRegenBonus = 1.5f;
            ring.auraManaRegen = 1f;
            ring.auraRadius = 8f;
            ring.killManaSteal = 0.075f;
            try
            {
                inventory.AddGold(ring.cost, popup: false);
                Assert.IsTrue(inventory.TryBuy(ring));
                Assert.AreEqual(0.8f + 1.5f + 1f, player.ManaRegen, 1e-4f, "the ring's aura must work on its owner too");
                Assert.IsTrue(player.SpendMana(150f));
                var bot = Pudge.All.First(h => h.team == Team.Bots);
                bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
                float before = 0f;
                bot.Died += _ => before = player.Mana; // just before the arena counts the kill
                Assert.IsTrue(hook.TryCast(bot.transform.position));
                yield return new WaitUntil(() => !bot.IsAlive);
                Assert.AreEqual(bot.MaxMana, bot.ManaAtDeath, 1f, "the victim's mana at its death is not kept");
                Assert.AreEqual(before + bot.ManaAtDeath * 0.075f, player.Mana, 0.2f, "the killer did not get 7.5 % of the victim's mana");
            }
            finally
            {
                Object.Destroy(ring);
            }
        }

        [UnityTest]
        public IEnumerator AssembledItemsInheritTheirPartsBonusesAndAbilities()
        {
            var inventory = player.GetComponent<HeroInventory>();
            var slippers = TestItem(ItemAction.None, 140, 0, stacks: false, range: 0f);
            slippers.moveSpeedBonus = 0.1f;
            slippers.agilityBonus = 3f;
            var boots = TestItem(ItemAction.None, 500, 0, stacks: false, range: 0f);
            boots.components = new[] { slippers };
            boots.moveSpeedBonus = 0.45f;
            boots.boots = true;
            var ring = TestItem(ItemAction.None, 425, 0, stacks: false, range: 0f);
            ring.manaRegenBonus = 1.5f;
            ring.auraManaRegen = 1f;
            ring.auraRadius = 8f;
            ring.killManaSteal = 0.075f;
            var arcane = TestItem(ItemAction.RestoreAllyMana, 925, 0, stacks: false, range: 8f, cooldown: 55f);
            arcane.components = new[] { boots, ring };
            arcane.manaRegenBonus = 1.5f;
            arcane.restoreMana = 175f;
            // a part with a use, in an item without one of its own
            var axe = TestItem(ItemAction.ChopTree, 100, 0, stacks: false, range: 3.5f, cooldown: 4f);
            axe.heroDamageBonus = 16f;
            var big = TestItem(ItemAction.None, 600, 0, stacks: false, range: 0f);
            big.components = new[] { axe, slippers };
            try
            {
                // the boots keep the slippers' agility; their own speed is bigger than the slippers'
                Assert.AreEqual(3f, boots.AgilityBonus, 1e-4f, "the boots did not inherit the slippers' agility");
                Assert.AreEqual(0.45f, boots.MoveSpeedBonus, 1e-4f);
                // Arcane Boots: the ring's vampirism and aura, the boots' speed and agility, their own regeneration
                Assert.AreEqual(0.075f, arcane.KillManaSteal, 1e-4f, "Arcane Boots did not inherit the ring's mana vampirism");
                Assert.AreEqual(1f, arcane.AuraManaRegen, 1e-4f, "Arcane Boots did not inherit Basilius Aura");
                Assert.AreEqual(8f, arcane.AuraRadius, 1e-4f);
                Assert.AreEqual(0.45f, arcane.MoveSpeedBonus, 1e-4f, "Arcane Boots did not inherit the boots' speed");
                Assert.IsTrue(arcane.IsBoots, "Arcane Boots are boots through Boots of Speed");
                Assert.AreEqual(3f, arcane.AgilityBonus, 1e-4f, "the agility did not come down two levels");
                Assert.AreEqual(1.5f, arcane.ManaRegenBonus, 1e-4f);
                // without a use of its own the item takes its part's: Quelling Blade's
                Assert.AreEqual(axe, big.Active, "the part's use was not inherited");
                Assert.IsTrue(big.TargetsTree);
                Assert.AreEqual(16f, big.HeroDamageBonus, 1e-4f);
                Assert.AreEqual(0.1f, big.MoveSpeedBonus, 1e-4f);

                // in the game: vampirism on a kill with Arcane Boots alone
                inventory.AddGold(arcane.cost, popup: false);
                Assert.IsTrue(inventory.TryBuy(arcane));
                Assert.AreEqual(0.075f, inventory.KillManaSteal, 1e-4f);
                Assert.AreEqual(3f, inventory.AgilityBonus, 1e-4f);
                Assert.IsTrue(player.SpendMana(150f));
                var bot = Pudge.All.First(h => h.team == Team.Bots);
                bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
                float before = 0f;
                bot.Died += _ => before = player.Mana;
                Assert.IsTrue(hook.TryCast(bot.transform.position));
                yield return new WaitUntil(() => !bot.IsAlive);
                Assert.AreEqual(before + bot.ManaAtDeath * 0.075f, player.Mana, 0.2f, "Arcane Boots give no mana for a kill");
            }
            finally
            {
                foreach (var item in new[] { slippers, boots, ring, arcane, axe, big }) Object.Destroy(item);
            }
        }

        [UnityTest]
        public IEnumerator ManaItemsAndCraftingAetherLens()
        {
            var inventory = player.GetComponent<HeroInventory>();
            var booster = TestItem(ItemAction.None, 800, 0, stacks: false, range: 0f);
            booster.manaBonus = 250f;
            var stone = TestItem(ItemAction.None, 700, 0, stacks: false, range: 0f);
            stone.manaRegenBonus = 1.75f;
            var recipe = TestItem(ItemAction.None, 775, 0, stacks: false, range: 0f);
            recipe.sound = ItemSound.Recipe;
            var lens = TestItem(ItemAction.None, 2275, 0, stacks: false, range: 0f);
            lens.unique = true;
            lens.castRangeBonus = 2.75f;
            lens.components = new[] { booster, stone, recipe };
            var branch = TestItem(ItemAction.PlantTree, 50, 1, stacks: false, range: 2f);
            try
            {
                // every unit has mana and regenerates it; Pudge at level 1 as in Dota 2
                yield return null;
                Assert.AreEqual(267f, player.MaxMana, 1e-3f);
                Assert.AreEqual(0.8f, player.ManaRegen, 1e-4f);
                Assert.AreEqual(player.MaxMana, player.Mana, 1e-3f, "a hero starts with full mana");
                foreach (var unit in Unit.AllUnits) Assert.Greater(unit.MaxMana, 0f, $"{unit.name} has no mana");

                // Energy Booster: +250 maximum mana, and the 250 come into the current mana at once
                inventory.AddGold(10000, popup: false);
                Assert.IsTrue(player.SpendMana(100f));
                float before = player.Mana;
                Assert.IsTrue(inventory.TryBuy(booster));
                yield return null;
                Assert.AreEqual(517f, player.MaxMana, 1e-3f, "Energy Booster must give +250 mana");
                Assert.AreEqual(before + 250f, player.Mana, 1f, "the 250 the booster adds to the maximum must come into the mana");
                // selling it only cuts the mana down to the lower maximum
                Assert.IsTrue(inventory.TrySell(inventory.SlotOf(booster)));
                yield return null;
                Assert.AreEqual(267f, player.Mana, 1f);
                Assert.IsTrue(inventory.TryBuy(booster));
                yield return null;
                Assert.AreEqual(517f, player.Mana, 1f);
                Assert.IsTrue(player.SpendMana(300f));
                Assert.IsFalse(player.SpendMana(1000f), "more mana than there is was spent");

                // the price of an assembled item counts only the parts still missing
                Assert.AreEqual(700 + 775, inventory.PriceOf(lens));
                // Void Stone: +1.75 per second
                Assert.IsTrue(inventory.TryBuy(stone));
                Assert.AreEqual(0.8f + 1.75f, player.ManaRegen, 1e-4f, "Void Stone must give +1.75 mana regeneration");
                float mana = player.Mana;
                yield return new WaitForSeconds(1f);
                Assert.AreEqual(mana + 2.55f, player.Mana, 0.3f, "mana does not regenerate");

                // the recipe completes the lens: the three parts become it
                int gold = inventory.Gold;
                Assert.IsTrue(inventory.TryBuy(recipe));
                Assert.AreEqual(gold - 775, inventory.Gold);
                Assert.IsTrue(inventory.Owns(lens), "the parts did not combine into Aether Lens");
                Assert.IsFalse(inventory.Owns(booster) || inventory.Owns(stone) || inventory.Owns(recipe), "the parts stayed after combining");
                Assert.AreEqual(1, inventory.Items.Count);
                Assert.IsFalse(inventory.CanBuy(lens), "a second lens must not be bought");
                Assert.IsTrue(inventory.TrySell(inventory.SlotOf(lens)));

                // bought whole with nothing of it: all its parts are paid for
                gold = inventory.Gold;
                Assert.AreEqual(2275, inventory.PriceOf(lens));
                Assert.IsTrue(inventory.TryBuy(lens));
                Assert.AreEqual(gold - 2275, inventory.Gold);
                Assert.IsTrue(inventory.Owns(lens));
                Assert.IsTrue(inventory.TrySell(inventory.SlotOf(lens)));

                // a full inventory: the part that completes the lens can still be bought
                for (int i = 0; i < HeroInventory.SlotCount - 2; i++) Assert.IsTrue(inventory.TryBuy(branch));
                Assert.IsTrue(inventory.TryBuy(booster));
                Assert.IsTrue(inventory.TryBuy(stone));
                Assert.AreEqual(HeroInventory.SlotCount, inventory.Items.Count);
                Assert.IsFalse(inventory.CanBuy(branch), "a full inventory took another item");
                Assert.IsTrue(inventory.CanBuy(recipe), "the recipe that completes the lens must go into a full inventory");
                Assert.IsTrue(inventory.TryBuy(recipe));
                Assert.IsTrue(inventory.Owns(lens));
                Assert.AreEqual(HeroInventory.SlotCount - 1, inventory.Items.Count, "the lens must take one slot of its three parts");
            }
            finally
            {
                foreach (var item in new[] { booster, stone, recipe, lens, branch }) Object.Destroy(item);
            }
        }

        [UnityTest]
        public IEnumerator ItemDescriptionHasPriceByTheNameAbilityFramesAndLore()
        {
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var inventory = player.GetComponent<HeroInventory>();
            yield return null;
            Assert.IsFalse(shop.rows.Any(r => r.item.recipe), "recipes must not be sold on their own: Pudge Wars → Build Test Arena");
            var ring = TestItem(ItemAction.None, 425, 0, stacks: false, range: 0f);
            ring.displayName = "Ring";
            ring.manaRegenBonus = 1.5f;
            ring.lore = "Кольцо, служившее наградой величайшим магам.";
            ring.abilities = new[] { new ItemAbility { name = "Basilius Aura", active = false, description = "+1 маны в секунду" } };
            var boots = TestItem(ItemAction.RestoreAllyMana, 925, 0, stacks: false, range: 8f, cooldown: 55f);
            boots.displayName = "Boots";
            boots.components = new[] { ring };
            boots.abilities = new[] { new ItemAbility { name = "Replenish Mana", active = true, description = "+175 маны" } };
            try
            {
                var row = (RectTransform)shop.rows[0].transform;
                shop.ShowTooltip(boots, -1, row);
                var text = shop.TooltipText;
                Assert.IsNotNull(text);
                // the name and the price come first
                StringAssert.StartsWith("Boots\n925", text, "the price must stand by the name at the top");
                var frames = shop.TooltipAbilities.ToList();
                Assert.AreEqual(2, frames.Count, "the own ability and the one inherited from the part");
                Assert.AreEqual(("Активная: Replenish Mana", true), frames[0]);
                Assert.AreEqual(("Пассивная: Basilius Aura", false), frames[1]);
                StringAssert.Contains("Перезарядка 55 с", text, "the use's cooldown must be in its frame");
                StringAssert.Contains("+1.5 к восстановлению маны", text, "the inherited bonus is not shown");
                StringAssert.DoesNotContain("Собирается", text, "the description must not tell what the item is made of");
                StringAssert.DoesNotContain("Кольцо, служившее", text, "the part's lore must not come with it");
                shop.ShowTooltip(ring, -1, row);
                StringAssert.Contains("Кольцо, служившее наградой величайшим магам.", shop.TooltipText);
                var loreText = Object.FindObjectsByType<Text>().First(t => t.text == ring.lore);
                Assert.AreEqual(FontStyle.Italic, loreText.fontStyle, "the lore must be in italics");
                shop.HideTooltip();

                // a sale says its price over the hero (the test boots are made of the ring alone: it would go into them at once)
                boots.components = null;
                var popups = Object.FindAnyObjectByType<GoldPopups>();
                inventory.AddGold(ring.cost, popup: false);
                Assert.IsTrue(inventory.TryBuy(ring));
                int value = inventory.SellValue(inventory.SlotOf(ring));
                Assert.IsTrue(inventory.TrySell(inventory.SlotOf(ring)));
                Assert.AreEqual($"+{value} монет", popups.LastText, "the sale's price did not pop up");
            }
            finally
            {
                Object.Destroy(ring);
                Object.Destroy(boots);
            }
        }

        [UnityTest]
        public IEnumerator EffectsShowAsRoundIconsOverTheAbilityBarWithDescriptions()
        {
            var abilities = Object.FindAnyObjectByType<AbilityBar>();
            var inventory = player.GetComponent<HeroInventory>();
            yield return null;
            var bar = abilities.Effects;
            Assert.IsNotNull(bar, "no effect row over the ability bar");
            var tango = TestItem(ItemAction.EatTree, 70, 3, stacks: true, range: 1.65f);
            tango.displayName = "Tango";
            tango.abilities = new[] { new ItemAbility { name = "Eat Tree", active = true, description = "" } };
            var ring = TestItem(ItemAction.None, 425, 0, stacks: false, range: 0f);
            ring.auraManaRegen = 1f;
            ring.auraRadius = 8f;
            ring.abilities = new[] { new ItemAbility { name = "Basilius Aura", active = false, description = "" } };
            try
            {
                yield return new WaitForSeconds(0.2f);
                Assert.AreEqual(0, bar.IconCount, "effects shown with none on the hero");
                // Tango eaten: a timed effect with its picture; the aura of the own ring: one that holds
                player.AddRegen(7f, 16f, tango);
                inventory.AddGold(ring.cost, popup: false);
                Assert.IsTrue(inventory.TryBuy(ring));
                yield return new WaitForSeconds(0.2f);
                Assert.AreEqual(2, bar.IconCount);
                Assert.AreEqual(tango, bar.Shown[0].item);
                Assert.IsTrue(bar.Shown[0].IsTimed);
                Assert.AreEqual(ring, bar.Shown[1].item, "no Basilius Aura among the effects");
                Assert.IsFalse(bar.Shown[1].IsTimed);
                // over the ability bar, in the middle
                var barCorners = new Vector3[4];
                var abilityCorners = new Vector3[4];
                ((RectTransform)bar.transform).GetWorldCorners(barCorners);
                ((RectTransform)abilities.slots[0].transform.parent).GetWorldCorners(abilityCorners);
                Assert.GreaterOrEqual(barCorners[0].y, abilityCorners[1].y, "the effects must stand over the ability bar");
                Assert.AreEqual((abilityCorners[0].x + abilityCorners[2].x) / 2f, (barCorners[0].x + barCorners[2].x) / 2f, 1f, "the effects must be centred");
                // hovering tells what it is
                var icon = bar.GetComponentsInChildren<EffectIconPointer>()[0];
                icon.OnPointerEnter(new PointerEventData(EventSystem.current));
                yield return null;
                StringAssert.Contains("Eat Tree", bar.TipText);
                StringAssert.Contains("+7 здоровья в секунду", bar.TipText);
                StringAssert.Contains("Осталось", bar.TipText);
                icon.OnPointerExit(new PointerEventData(EventSystem.current));
                yield return null;
                Assert.IsNull(bar.TipText, "the description stayed after the pointer left");
                var aura = bar.GetComponentsInChildren<EffectIconPointer>()[1];
                aura.OnPointerEnter(new PointerEventData(EventSystem.current));
                yield return null;
                StringAssert.Contains("Basilius Aura", bar.TipText);
                StringAssert.Contains("+1 маны в секунду", bar.TipText);
                aura.OnPointerExit(new PointerEventData(EventSystem.current));
            }
            finally
            {
                Object.Destroy(tango);
                Object.Destroy(ring);
            }
        }

        [UnityTest]
        public IEnumerator ItemsAreDraggedBetweenSlotsWithShopSounds()
        {
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var inventory = player.GetComponent<HeroInventory>();
            yield return null;
            var tango = TestItem(ItemAction.EatTree, 90, 3, stacks: true, range: 1.65f);
            var lens = TestItem(ItemAction.None, 2000, 0, stacks: false, range: 0f);
            var branch = TestItem(ItemAction.PlantTree, 50, 1, stacks: false, range: 2f);
            inventory.AddGold(tango.cost + lens.cost + branch.cost, popup: false);
            int sounds = shop.SoundsPlayed;
            Assert.IsTrue(shop.Buy(tango));
            Assert.AreEqual(sounds + 1, shop.SoundsPlayed, "no purchase sound (Resources/Audio/Shop/buy)");
            Assert.IsTrue(shop.Buy(lens));
            Assert.IsTrue(shop.Buy(branch));
            Assert.AreEqual(0, inventory.SlotOf(tango));
            Assert.AreEqual(1, inventory.SlotOf(branch));
            Assert.AreEqual(HeroInventory.SlotCount - 1, inventory.SlotOf(lens));

            // dragging the lens onto Tango's slot swaps them, with the pick-up and the drop sounds
            var from = shop.slots[HeroInventory.SlotCount - 1].GetComponent<ItemSlotPointer>();
            var to = shop.slots[0].GetComponent<ItemSlotPointer>();
            var drag = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerDrag = from.gameObject };
            sounds = shop.SoundsPlayed;
            from.OnBeginDrag(drag);
            Assert.AreEqual(HeroInventory.SlotCount - 1, shop.DraggedSlot, "the item is not picked");
            Assert.AreEqual(sounds + 1, shop.SoundsPlayed, "no pick-up sound");
            from.OnDrag(drag);
            to.OnDrop(drag);
            from.OnEndDrag(drag);
            Assert.AreEqual(-1, shop.DraggedSlot);
            Assert.AreEqual(sounds + 2, shop.SoundsPlayed, "no drop sound (and only one)");
            Assert.AreEqual(0, inventory.SlotOf(lens), "the dragged item is not in the slot it was dropped on");
            Assert.AreEqual(HeroInventory.SlotCount - 1, inventory.SlotOf(tango), "the item there did not take the dragged one's place");
            Assert.AreEqual(3, inventory.ChargesAt(inventory.SlotOf(tango)), "the charges did not go with the item");
            Assert.AreEqual(lens.icon, shop.slots[0].sprite);

            // into an empty slot it just moves; let go off the slots, it stays
            Assert.IsTrue(shop.Move(1, 3));
            Assert.AreEqual(3, inventory.SlotOf(branch));
            Assert.IsNull(inventory.ItemIn(1));
            sounds = shop.SoundsPlayed;
            var drop = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerDrag = to.gameObject };
            to.OnBeginDrag(drop);
            to.OnEndDrag(drop);
            Assert.AreEqual(0, inventory.SlotOf(lens), "an item let go off the slots must stay where it was");
            Assert.AreEqual(sounds + 2, shop.SoundsPlayed);

            // the arranged slots stay: a used-up item leaves its slot empty instead of the others closing up
            var trees = TerrainTrees.Instance;
            var spot = Enumerable.Range(0, 16)
                .Select(i => player.transform.position + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * 1.5f)
                .FirstOrDefault(trees.CanPlant);
            Assert.IsTrue(inventory.TryUse(3, spot, HeroInventory.NoTree), "the branch was not planted");
            Assert.IsNull(inventory.ItemIn(3), "the used-up branch's slot is not empty");
            Assert.AreEqual(0, inventory.SlotOf(lens), "the arranged items must not close up");
            Assert.AreEqual(HeroInventory.SlotCount - 1, inventory.SlotOf(tango));

            // another hero's bar cannot be rearranged
            shop.Inspect(Pudge.All.First(h => h.team == Team.Bots).GetComponent<HeroInventory>());
            Assert.IsFalse(shop.Move(0, 1));
            shop.Inspect(null);
        }

        [UnityTest]
        public IEnumerator ShopSellsAetherLensThatExtendsHookRange()
        {
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var inventory = player.GetComponent<HeroInventory>();
            var row = shop.rows.First(r => r.item.castRangeBonus > 0f); // Aether Lens
            var lens = row.item;
            Assert.AreEqual(2275, lens.cost);
            Assert.IsNotNull(lens.icon, "item icon missing");
            Assert.IsFalse(shop.IsOpen);
            shop.Toggle();
            Assert.IsTrue(shop.IsOpen);
            Assert.IsFalse(row.buyButton.interactable, "buy must be disabled without gold");
            Assert.IsFalse(inventory.TryBuy(lens));

            // under the mouse the row tells how much gold is missing, and follows the gold
            Assert.IsFalse(row.ShortageShown, "shortage shown without hovering");
            row.OnPointerEnter(new PointerEventData(EventSystem.current));
            Assert.IsTrue(row.ShortageShown);
            Assert.AreEqual("Не хватает: 2275 золота", row.shortageLabel.text);
            inventory.AddGold(1000);
            Assert.AreEqual("Не хватает: 1275 золота", row.shortageLabel.text);
            yield return new WaitWhile(() => shop.IsAnimating);
            CaptureScreen("shop_shortage");
            inventory.AddGold(lens.cost - 1000);
            Assert.IsFalse(row.ShortageShown, "shortage shown with enough gold");

            Assert.IsTrue(row.buyButton.interactable);
            Assert.AreEqual(lens.cost.ToString(), shop.goldLabel.text);
            row.buyButton.onClick.Invoke();
            Assert.AreEqual(0, inventory.Gold);
            Assert.IsTrue(inventory.Owns(lens));
            Assert.AreEqual(hook.range + 2.75f, hook.EffectiveRange, 1e-4f);
            int lensSlot = HeroInventory.SlotCount - 1;
            Assert.AreEqual(lensSlot, inventory.SlotOf(lens), "a passive item goes to the far slot");
            Assert.AreEqual(lens.icon, shop.slots[lensSlot].sprite);
            Assert.IsFalse(shop.slots[lensSlot].transform.Find("Key").gameObject.activeSelf, "a passive item has no key to press");
            Assert.AreEqual("Куплено", row.buyLabel.text);
            Assert.IsFalse(row.ShortageShown, "an owned item lacks no gold");
            row.OnPointerExit(new PointerEventData(EventSystem.current));
            yield return null;
            CaptureScreen("shop_open");
            inventory.AddGold(10000);
            Assert.IsFalse(inventory.TryBuy(lens), "Aether Lens does not stack");
            shop.Close();

            // the hook really flies 15.75 m now
            foreach (var b in Pudge.All.Where(h => h.team == Team.Bots)) b.transform.position = new Vector3(12f, 0f, 14f);
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
            var shop = Object.FindAnyObjectByType<ShopUI>();
            Assert.IsNotNull(EventSystem.current, "no EventSystem");
            yield return null;
            AssertPointerHits(shop.toggleButton);
            shop.Toggle();
            yield return new WaitWhile(() => shop.IsAnimating);
            AssertPointerHits(shop.rows[0].buyButton);
            AssertPointerHits(shop.closeButton);
            // a long shop is cut to the screen: its top (and the close button) stays on it
            var panelCorners = new Vector3[4];
            ((RectTransform)shop.panel.transform).GetWorldCorners(panelCorners);
            Assert.LessOrEqual(panelCorners[1].y, Screen.height + 0.5f, "the shop's top is off the screen");
        }

        [UnityTest]
        public IEnumerator ShopPanelFliesInFromTheScreenEdgeAndBackOut()
        {
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var rt = (RectTransform)shop.panel.transform;
            var canvasRect = (RectTransform)rt.parent;
            shop.slideDuration = 1f; // long enough that a slow first frame cannot skip the flight
            float LeftEdge() // panel's left side in canvas units from the canvas's right edge
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                return canvasRect.InverseTransformPoint(corners[0]).x - canvasRect.rect.xMax;
            }

            shop.Toggle();
            Assert.IsTrue(shop.IsOpen);
            Assert.IsTrue(shop.panel.activeSelf);
            Assert.GreaterOrEqual(LeftEdge(), -1f, "the panel must start beyond the screen edge");
            yield return null;
            yield return null;
            Assert.IsTrue(shop.IsAnimating, "the panel jumped into place instead of flying in");
            float midway = LeftEdge();
            Assert.Less(midway, 0f, "the panel did not start moving in");
            yield return new WaitWhile(() => shop.IsAnimating);
            Assert.Less(LeftEdge(), midway);
            Assert.AreEqual(-24f, rt.anchoredPosition.x, 1e-3f, "the panel did not land in its place");

            shop.Close();
            Assert.IsFalse(shop.IsOpen);
            yield return null;
            Assert.IsTrue(shop.panel.activeSelf, "the panel vanished instead of flying out");
            yield return new WaitUntil(() => !shop.panel.activeSelf);
            Assert.GreaterOrEqual(LeftEdge(), -1f, "the panel was switched off before it left the screen");
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
            var bot = Pudge.All.First(h => h.team == Team.Bots);
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
            var other = Pudge.All.Last(h => h.team == Team.Bots);
            other.GetComponent<PudgeMotor>().keepOffPit = false;
            other.GetComponent<PudgeMotor>().SetMoveInput(Vector3.back);
            yield return new WaitUntil(() => !other.IsAlive);
            Assert.AreEqual(0, arena.Score);
            motor.SetMoveInput(Vector3.zero);
            other.GetComponent<PudgeMotor>().SetMoveInput(Vector3.zero);
        }

        [UnityTest]
        public IEnumerator FallenHeroLeavesABleedingBodyInThePitUntilTheRespawn()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            bot.transform.position = new Vector3(0f, 0f, arena.pitHalfDepth - 0.3f);
            bot.StepIntoPit(Vector3.back * 3f);
            var body = bot.Body;
            Assert.IsNotNull(body, "no body was left by the fall");
            var parts = body.GetComponentsInChildren<Rigidbody>();
            Assert.GreaterOrEqual(parts.Length, 11, "the body is not a ragdoll (belly, chest, head, arms, legs)");
            Assert.IsTrue(bot.GetComponentsInChildren<Renderer>().All(r => !r.enabled), "the hero is still shown over its body");

            yield return new WaitUntil(() => !bot.IsAlive);
            for (int shot = 0; shot < 6; shot++)
            {
                Capture($"body_{shot:00}");
                yield return new WaitForSeconds(0.25f);
            }
            Assert.Less(parts.Max(p => p.position.y), -3f, "the body did not fall to the pit floor");
            Assert.Greater(BloodSplatter.GroundStainCount, 0, "the body's fall splashed no blood");
            Assert.IsNotNull(bot.Body, "the body is gone before the respawn");

            yield return new WaitUntil(() => bot.IsAlive);
            Assert.IsNull(bot.Body, "the respawned hero still has a body");
            Assert.IsTrue(bot.GetComponentsInChildren<Renderer>().All(r => r.enabled), "the respawned hero is not shown");
            yield return new WaitForSeconds(body.sinkTime + 0.2f);
            Assert.IsTrue(body == null, "the body did not go after the respawn");
            Assert.Greater(BloodSplatter.GroundStainCount, 0, "the blood went with the body");
        }

        [UnityTest]
        public IEnumerator CameraFollowsTheHeadOfTheOwnBodyIntoThePit()
        {
            var follow = Camera.main.GetComponent<TopDownCamera>();
            Assert.AreEqual(TopDownCamera.View.OwnSide, follow.Mode);
            player.transform.position = new Vector3(0f, 0f, -arena.pitHalfDepth + 0.3f);
            player.StepIntoPit(Vector3.forward * 3f);
            var head = player.Body.Head;
            Assert.AreNotEqual(player.Body.transform, head, "the body has no head bone");
            yield return new WaitUntil(() => !player.IsAlive);
            yield return new WaitForSeconds(1.5f);
            Capture("camera_on_body");
            var expected = head.position + follow.lookAhead + follow.offset; // the own view of the Radiant player
            Assert.Less(Vector3.Distance(follow.transform.position, expected), 0.3f, "the camera did not follow the body's head");
            Assert.Less(follow.transform.position.y, follow.offset.y - 2f, "the camera did not go down after the body");

            yield return new WaitUntil(() => player.IsAlive);
            yield return new WaitForSeconds(1f);
            Assert.Less(Vector3.Distance(follow.transform.position, player.transform.position + follow.lookAhead + follow.offset), 0.3f,
                "the camera did not come back to the respawned hero");
        }

        /// <summary>Screenshot including the overlay UI (Camera.Render does not draw ScreenSpaceOverlay canvases).</summary>
        [UnityTest]
        public IEnumerator BotsWalkUpToThePitEdgeButDoNotFallIn()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            var motor = bot.GetComponent<PudgeMotor>();
            Assert.IsTrue(motor.keepOffPit, "bots must keep off the pit");
            motor.SetMoveInput(Vector3.back + Vector3.right * 0.3f);
            yield return new WaitForSeconds(4f);
            Assert.IsTrue(bot.IsAlive && !bot.IsFalling, "the bot fell in");
            Assert.AreEqual(arena.pitHalfDepth + motor.pitGap, bot.transform.position.z, 0.01f, "the bot did not reach the edge");
            motor.SetMoveInput(Vector3.zero);

            // left to themselves, bots wander right up to the brink now and then, and none falls in
            foreach (var brain in Object.FindObjectsByType<BotBrain>())
            {
                brain.edgeChance = 1f;
                brain.throwInterval = new Vector2(1000f, 1000f);
                brain.enabled = true;
            }
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
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
        public IEnumerator HealthBarShowsRegenerationAheadOfTheFillAndItsRate()
        {
            var bars = Object.FindAnyObjectByType<HealthBars>();
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            yield return null;
            foreach (var hero in new[] { bot, player })
            {
                var label = bars.RegenLabel(hero);
                Assert.IsTrue(label.gameObject.activeSelf, "no regeneration shown next to the bar");
                Assert.AreEqual("+2.5", label.text, "the bar shows health regenerated per second");
                var labelRect = (RectTransform)label.transform;
                Assert.LessOrEqual(Mathf.Abs(labelRect.localPosition.x) + labelRect.rect.width / 2f, bars.size.x / 2f + 0.01f, "the rate must stand inside the bar");
                Assert.AreEqual(TextAnchor.MiddleRight, label.alignment, "the rate keeps to the bar's right end, clear of the health");
                Assert.AreEqual(hero.team == LocalPlayer.Team ? bars.playerRegenColor : bars.enemyRegenColor,
                    bars.RegenStrip(hero).GetComponent<Image>().color, "dark green for allies, dark red for enemies");
            }

            // a hurt hero: the dark strip runs ahead of the fill over the health the next seconds bring back
            bot.Damage(300f, null);
            yield return null;
            float fill = bars.Of(bot).fill.anchorMax.x, strip = bars.RegenStrip(bot).anchorMax.x;
            Assert.AreEqual(bot.healthRegen * bars.regenPreviewSeconds / bot.maxHealth, strip - fill, 0.005f,
                "the strip must cover the health regenerated in regenPreviewSeconds");
            Assert.Less(bars.RegenStrip(bot).GetSiblingIndex(), bars.Of(bot).fill.GetSiblingIndex(), "the strip lies under the fill");
            CaptureScreen("health_regen");

            // nothing comes back to the dead
            bot.StartFalling(Vector3.zero);
            yield return new WaitUntil(() => !bot.IsAlive);
            yield return null;
            Assert.IsFalse(bars.RegenLabel(bot).gameObject.activeSelf, "a dead hero does not regenerate");
            Assert.AreEqual(0f, bars.RegenStrip(bot).anchorMax.x, 1e-4f);
        }

        [UnityTest]
        public IEnumerator FirstPersonShowsTheOwnHealthBarAtTheBottom()
        {
            var bars = Object.FindAnyObjectByType<HealthBars>();
            var view = Camera.main.GetComponent<TopDownCamera>();
            yield return null;
            var root = bars.Of(player).root;
            while (!view.IsFirstPerson) view.NextView();
            yield return null;
            Assert.IsTrue(root.gameObject.activeSelf, "no own health bar in first person");
            var area = ((RectTransform)bars.transform).rect;
            Assert.AreEqual(area.yMin + bars.firstPersonBottom, root.anchoredPosition.y, 0.01f, "the own bar must stand at the bottom of the screen");
            Assert.AreEqual(0f, root.anchoredPosition.x, 0.01f);
            Assert.Greater(root.localScale.x, 1f, "the bottom bar should be bigger");
            Assert.IsFalse(bars.Of(player).name.gameObject.activeSelf, "the own name must not cover the ability bar");
            // the ability bar rises over it: both at the bottom, the bar under the abilities
            var abilities = Object.FindAnyObjectByType<AbilityBar>();
            Assert.IsTrue(abilities.Raised, "the ability bar did not rise over the own health bar");
            var abilityRect = (RectTransform)abilities.slots[0].transform.parent;
            var barCorners = new Vector3[4];
            var abilityCorners = new Vector3[4];
            root.GetWorldCorners(barCorners);
            abilityRect.GetWorldCorners(abilityCorners);
            Assert.Greater(abilityCorners[0].y, barCorners[1].y, "the health bar must be under the ability bar");
            CaptureScreen("first_person_health");
            while (view.IsFirstPerson) view.NextView();
            yield return null;
            Assert.AreEqual(1f, root.localScale.x, 1e-4f, "the bar went back over the head at its size");
            Assert.AreNotEqual(area.yMin + bars.firstPersonBottom, root.anchoredPosition.y);
            Assert.IsFalse(abilities.Raised, "the ability bar stayed raised out of first person");
            Assert.IsTrue(bars.Of(player).name.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator PickedHeroOrTreeIsMarkedWithASwayingRingAndRipples()
        {
            var input = player.GetComponent<PlayerInputController>();
            var marker = SelectionMarker.Instance;
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            yield return null;
            // the game starts with the own hero picked, as Space picks it
            Assert.AreEqual(player, marker.SelectedUnit, "the own hero is not picked at the start");
            marker.Clear();
            yield return null;
            Assert.IsFalse(marker.IsShown, "nothing is picked now");

            // Space picks the own hero: green
            input.PickOwnHero();
            yield return null;
            Assert.AreEqual(player, marker.SelectedUnit);
            Assert.IsTrue(marker.IsShown, "no ring under the own hero");
            Assert.AreEqual(marker.allyColor, marker.CurrentColor, "the own side's ring must be green");

            // an enemy: red, see-through, swaying, with rings rippling out
            marker.Select(bot);
            yield return null;
            Assert.AreEqual(marker.enemyColor, marker.CurrentColor, "an enemy's ring must be red");
            var ring = marker.Ring;
            Assert.Less(ring.startColor.a, 1f, "the ring must be see-through");
            var centre = bot.transform.position;
            float Radius(LineRenderer line, int i) => new Vector2(line.GetPosition(i).x - centre.x, line.GetPosition(i).z - centre.z).magnitude;
            var around = Enumerable.Range(0, ring.positionCount).Select(i => Radius(ring, i)).ToList();
            Assert.Greater(around.Max() - around.Min(), 0.01f, "the ring's edge does not sway");
            float before = Radius(ring, 0);
            float ripple = Radius(marker.Ripples[0], 0);
            yield return new WaitForSeconds(0.2f);
            Assert.AreNotEqual(before, Radius(ring, 0), "the ring stands still");
            Assert.AreNotEqual(ripple, Radius(marker.Ripples[0], 0), "the ripple does not move");
            Assert.IsTrue(marker.Ripples.All(r => r.enabled && r.startColor.a < ring.startColor.a + 1e-4f), "the ripples must be fainter than the ring");
            Assert.Greater(marker.Ripples.Max(r => Radius(r, 0)), Radius(ring, 0), "the ripples must spread out of the ring");
            CaptureScreen("selection_enemy");

            // a tree: white
            var trees = TerrainTrees.Instance;
            marker.SelectTree(0);
            yield return null;
            Assert.AreEqual(marker.treeColor, marker.CurrentColor, "a tree's ring must be white");
            Assert.Less(marker.CurrentColor.a, 1f);
            trees.Fell(0, trees.Position(0) + Vector3.right);
            yield return null;
            Assert.IsFalse(marker.IsShown, "the ring stayed after the tree was cut down");

            // a dead hero's ring goes
            marker.Select(bot);
            bot.StartFalling(Vector3.zero);
            yield return null;
            Assert.IsFalse(marker.IsShown, "a falling hero keeps its ring");
        }

        [UnityTest]
        public IEnumerator HeroesHaveNicknamesAndDotaHealthBars()
        {
            var bars = Object.FindAnyObjectByType<HealthBars>();
            Assert.IsNotNull(bars, "no health bars in the HUD");
            yield return null;
            var heroes = Pudge.All.ToList();
            Assert.AreEqual(heroes.Count, bars.Count);
            Assert.That(heroes.Select(h => h.nickname), Is.All.Not.Empty, "every hero needs a nickname");
            Assert.That(heroes.Select(h => h.Nickname), Is.Unique);
            foreach (var hero in heroes)
            {
                var (root, fill, name) = bars.Of(hero);
                Assert.IsTrue(root.gameObject.activeSelf, hero.Nickname + " has no bar shown");
                Assert.LessOrEqual(root.anchoredPosition.y, ((RectTransform)bars.transform).rect.yMax - bars.topMargin + 1e-3f, hero.Nickname + "'s bar is off the top of the screen");
                Assert.AreEqual(hero.team == Team.Bots ? "Бот " + hero.Nickname : hero.Nickname, name.text, "bots are marked over the head");
                var level = bars.LevelLabel(hero);
                Assert.AreEqual(hero.Level.ToString(), level.text, "the level circle shows the hero's level");
                Assert.AreEqual(bars.levelTextColor, level.color, "the level digit is light gold");
                Assert.Less(level.transform.parent.localPosition.y, name.transform.localPosition.y, "the level sits below the name");
                var multiKill = bars.MultiKillLabel(hero);
                Assert.IsFalse(multiKill.gameObject.activeSelf, "no kills yet — no multi-kill mark");
                Assert.Less(multiKill.transform.localPosition.x, level.transform.parent.localPosition.x, "the multi-kill sits left of the level");
                Assert.AreEqual(1f, fill.anchorMax.x, 1e-4f, "a live hero is at full health");
                Assert.AreEqual(hero.team == Team.Player ? bars.playerColor : bars.enemyColor, fill.GetComponent<Image>().color);
                Assert.AreEqual(Mathf.CeilToInt(hero.maxHealth / bars.segmentHealth) - 1, root.GetComponentsInChildren<Transform>().Count(t => t.name == "Notch"));
                var health = bars.HealthLabel(hero);
                Assert.AreEqual($"{Mathf.CeilToInt(hero.Health)}/{Mathf.CeilToInt(hero.maxHealth)}", health.text, "the bar shows current / max health");
                Assert.AreEqual(Color.white, health.color);
                var outline = health.GetComponent<TextOutline>();
                Assert.IsNotNull(outline, "the health digits need a solid outline (uGUI's Outline leaves gaps)");
                Assert.AreEqual(Color.black, outline.color, "the health digits need a black outline to read on any fill");
                Assert.IsNull(health.GetComponent<Outline>(), "a second, gappy outline");
                Assert.Greater(health.transform.GetSiblingIndex(), root.Find("Inner").GetSiblingIndex(), "the digits must be drawn over the fill");
            }
            CaptureScreen("health_bars");

            // a dead hero's bar stays a while at 0 over its body: the fill is gone at once, the white trail of the
            // lost health holds, then runs down gradually; then the bar goes away and comes back full with the respawn
            var bot = heroes.First(h => h.team == Team.Bots);
            bot.StartFalling(Vector3.zero);
            yield return new WaitUntil(() => !bot.IsAlive);
            yield return null;
            float deathTime = Time.time;
            Assert.AreEqual(0f, bot.Health);
            Assert.AreEqual($"0/{Mathf.CeilToInt(bot.maxHealth)}", bars.HealthLabel(bot).text);
            Assert.IsTrue(bars.Of(bot).root.gameObject.activeSelf, "a dead hero's bar went away at once");
            Assert.AreEqual(0f, bars.Of(bot).fill.anchorMax.x, 1e-4f, "the fill did not drop at once");
            Assert.AreEqual(1f, bars.Trail(bot).anchorMax.x, 0.05f, "no trail where the health was");
            yield return new WaitForSeconds(bars.trailHold + 0.3f);
            float trail = bars.Trail(bot).anchorMax.x;
            Assert.That(trail, Is.GreaterThan(0.05f).And.LessThan(0.95f), "the trail must run down gradually, not at once");
            CaptureScreen("health_trail");
            yield return new WaitUntil(() => bars.Trail(bot).anchorMax.x <= 1e-4f);
            Assert.IsTrue(bars.Of(bot).root.gameObject.activeSelf, "the bar went away before its time");
            yield return new WaitUntil(() => !bars.Of(bot).root.gameObject.activeSelf);
            Assert.AreEqual(bars.deadShowTime, Time.time - deathTime, 0.2f, "the dead hero's bar did not stay its time");
            Assert.IsFalse(bot.IsAlive);
            yield return new WaitUntil(() => bot.IsAlive);
            yield return null;
            Assert.IsTrue(bars.Of(bot).root.gameObject.activeSelf);
            Assert.AreEqual(1f, bars.Of(bot).fill.anchorMax.x, 1e-4f);
            Assert.AreEqual(1f, bars.Trail(bot).anchorMax.x, 1e-4f, "the trail did not come back with the health");
            Assert.AreEqual(1f, bars.Of(bot).root.GetComponent<CanvasGroup>().alpha, 1e-4f, "the respawned hero's bar stayed faded");
            Assert.AreEqual($"{Mathf.CeilToInt(bot.maxHealth)}/{Mathf.CeilToInt(bot.maxHealth)}", bars.HealthLabel(bot).text);

            // a multi-kill shows as "×N" next to the level from a Double Kill on; a death or the window running out clears it
            arena.Streaks.RegisterKill(bot, Time.time);
            yield return null;
            Assert.IsFalse(bars.MultiKillLabel(bot).gameObject.activeSelf, "a single kill is no multi-kill");
            arena.Streaks.RegisterKill(bot, Time.time);
            yield return null;
            Assert.IsTrue(bars.MultiKillLabel(bot).gameObject.activeSelf, "the multi-kill mark is not shown");
            Assert.AreEqual("×2", bars.MultiKillLabel(bot).text);
            arena.Streaks.RegisterDeath(bot);
            yield return null;
            Assert.IsFalse(bars.MultiKillLabel(bot).gameObject.activeSelf, "the multi-kill mark outlived the death");
            arena.Streaks.RegisterKill(bot, Time.time);
            arena.Streaks.RegisterKill(bot, Time.time);
            arena.Streaks.ExpireSeries(Time.time + arena.Streaks.multiKillWindow + 1f);
            yield return null;
            Assert.IsFalse(bars.MultiKillLabel(bot).gameObject.activeSelf, "the multi-kill mark outlived its window");
        }

        [UnityTest]
        public IEnumerator HookHitsShakeTheCameraAndDeathTurnsThePictureRedAndGrey()
        {
            var follow = Object.FindAnyObjectByType<TopDownCamera>();
            var feedback = follow.GetComponent<CameraFeedback>();
            Assert.IsNotNull(feedback, "the camera has no feedback effects");
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            foreach (var b in Pudge.All.Where(h => h.team == Team.Bots && h != bot)) b.transform.position = new Vector3(10f, 0f, 12f);
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
            yield return null;
            Assert.AreEqual(0f, follow.Trauma);

            // the own hook's catch jolts the view, and the jolt dies down
            bool caught = false;
            hook.TargetCaught += _ => caught = true;
            Assert.IsTrue(hook.TryCast(bot.transform.position));
            float t = 0f;
            while (!caught && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(caught, "the hook missed the bot");
            Assert.Greater(follow.Trauma, 0.2f, "no shake on the own hook's catch");
            t = 0f;
            while ((follow.Trauma > 0f || hook.IsBusy) && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(0f, follow.Trauma, "the shake did not die down");

            // the own hero caught: a harder jolt
            player.AttachTo(bot.GetComponent<HookAbility>());
            yield return null;
            Assert.Greater(follow.Trauma, 0.4f, "no shake when the player is caught");
            player.Release();

            // another hero's respawn is not the player's: no revive sound
            t = 0f;
            while (!bot.IsAlive && t < arena.respawnDelay + 2f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(bot.IsAlive, "the bot did not respawn");
            Assert.IsFalse(follow.TryGetComponent<AudioSource>(out var quiet) && quiet.isPlaying, "the revive sound played for a bot");

            // death: red from the sides and most of the colour gone, until the respawn
            Assert.AreEqual(0f, feedback.BloodAlpha);
            Assert.IsFalse(feedback.PostProcessing, "post-processing must stay off while alive");
            player.StartFalling(Vector3.zero);
            t = 0f;
            while (player.IsAlive && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(player.IsAlive);
            float diedAt = Time.time;
            yield return null;
            Assert.Greater(follow.Trauma, 0.5f, "no shake on death");
            Assert.IsNotNull(feedback.reviveSound, "no revive sound (Resources/Audio/revive)");
            Assert.IsTrue(follow.TryGetComponent<AudioSource>(out var revive) && revive.isPlaying, "the revive sound did not start at the death");
            yield return new WaitForSeconds(feedback.greyIn + 0.2f);
            Assert.Greater(feedback.BloodAlpha, 0.3f, "no red at the sides after death");
            Assert.AreEqual(1f, feedback.GreyWeight, 0.01f, "the picture did not lose its colour");
            Assert.IsTrue(feedback.PostProcessing);
            Capture("death_effects");

            // the hero is back on the revive sound's 3rd second
            t = 0f;
            while (!player.IsAlive && t < arena.respawnDelay + 2f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(player.IsAlive, "the player did not respawn");
            Assert.AreEqual(arena.respawnDelay, Time.time - diedAt, 0.2f, "the respawn delay");
            Assert.IsTrue(revive.isPlaying, "the revive sound stopped before the respawn");
            Assert.AreEqual(feedback.reviveLead, revive.time, 0.25f, "the hero is not back on the revive sound's 3rd second");
            yield return new WaitForSeconds(feedback.recover + 0.2f);
            Assert.AreEqual(0f, feedback.BloodAlpha, "the red stayed after the respawn");
            Assert.AreEqual(0f, feedback.GreyWeight, "the colour did not come back");
            Assert.IsFalse(feedback.PostProcessing, "post-processing stayed on after the respawn");
        }

        [UnityTest]
        public IEnumerator AbilityBarShowsHookCooldownAndAimLines()
        {
            var input = player.GetComponent<PlayerInputController>();
            var bar = Object.FindAnyObjectByType<AbilityBar>();
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

            // Q draws the reach ring around the hero, as an item does: the tip at full range
            input.enabled = true;
            Assert.IsTrue(input.SelectAbility(PlayerInputController.HookSlot));
            yield return null;
            var ring = input.RangeRing;
            Assert.IsNotNull(ring, "no reach ring with the hook picked");
            var edge = ring.GetPosition(0) - player.transform.position;
            Assert.AreEqual(hook.AimLength, new Vector3(edge.x, 0f, edge.z).magnitude, 0.01f, "the ring must show how far the hook's tip reaches");
            Assert.AreEqual(hook.AimStart.y, ring.GetPosition(0).y, 0.01f, "the ring must lie at the aim lines' height, or it looks shorter than them");
            input.Deselect();
            Assert.IsNull(input.RangeRing, "the ring stayed after the hook was dropped");
            input.enabled = false;

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
        public IEnumerator EnemySideViewKeepsEveryEnemyInFrameAsThePlayerComesUp()
        {
            var cam = Camera.main;
            var follow = cam.GetComponent<TopDownCamera>();
            follow.SetView(TopDownCamera.View.EnemySide);
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            float back = arena.pitHalfDepth + arena.sideDepth - 0.6f, front = arena.pitHalfDepth + 0.6f;
            var spots = new[] { new Vector3(-6f, 0f, back), new Vector3(6f, 0f, back), new Vector3(-6f, 0f, front), new Vector3(6f, 0f, front) };
            for (int i = 0; i < bots.Count; i++) bots[i].transform.position = spots[i % spots.Length];

            bool InFrame(Pudge h)
            {
                var v = cam.WorldToViewportPoint(h.transform.position + Vector3.up);
                return v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
            }

            // from the back wall of the own side up to the pit edge: the enemies must not slip out of view
            foreach (float z in new[] { -(back), -8f, -(front) })
            {
                player.transform.position = new Vector3(0f, 0f, z);
                follow.SnapToTarget();
                yield return null;
                Assert.Greater(cam.transform.position.z, arena.pitHalfDepth + arena.sideDepth, $"the camera walked onto the enemy side (player at z = {z})");
                foreach (var bot in bots) Assert.IsTrue(InFrame(bot), $"{bot.Nickname} at {bot.transform.position} is out of view with the player at z = {z}");
                Assert.IsTrue(InFrame(player), $"the player at z = {z} is out of view");
            }
            follow.SetView(TopDownCamera.View.OwnSide);
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
        public IEnumerator PhoneCameraButtonSwitchesViewsAndAFingerTurnsFirstPerson()
        {
            var touch = Object.FindAnyObjectByType<TouchControls>();
            var follow = Camera.main.GetComponent<TopDownCamera>();
            touch.showOnDesktop = true; // the phone controls, driven by the mouse
            yield return null;
            yield return null;
            var button = touch.CameraButton;
            Assert.IsTrue(button.gameObject.activeSelf, "no camera button with the phone controls");
            var corner = (RectTransform)button.transform;
            Assert.AreEqual(new Vector2(0f, 1f), corner.anchorMin, "the camera button belongs in the top-left corner");
            Assert.IsNotNull(button.image.sprite, "the camera icon did not load");
            Assert.IsFalse(touch.LookZone.gameObject.activeSelf, "the look zone is only for first person");

            button.onClick.Invoke();
            Assert.AreEqual(TopDownCamera.View.EnemySide, follow.Mode);
            button.onClick.Invoke();
            Assert.AreEqual(TopDownCamera.View.FirstPerson, follow.Mode);
            Assert.IsFalse(follow.WantsCursorCaptured, "a phone has no cursor to lock");
            yield return null;
            Assert.IsTrue(touch.LookZone.gameObject.activeSelf, "no look zone in first person");
            Assert.AreEqual(0, touch.LookZone.transform.GetSiblingIndex(), "the look zone must lie under the sticks");

            // a third finger dragging over the empty screen turns the view (90° to the right here)
            Assert.Less(Vector3.Distance(Vector3.forward, follow.MoveForward), 1e-3f);
            var finger = new PointerEventData(EventSystem.current) { pointerId = 7 };
            touch.LookZone.OnPointerDown(finger);
            finger.delta = new Vector2(90f / follow.touchSensitivity * Screen.height / 1080f, 0f);
            touch.LookZone.OnDrag(finger);
            yield return null;
            touch.LookZone.OnPointerUp(finger);
            Assert.Less(Vector3.Distance(Vector3.right, follow.MoveForward), 1e-3f, "the finger did not turn the view");
            Assert.IsTrue(follow.crosshair.activeSelf);

            button.onClick.Invoke();
            Assert.AreEqual(TopDownCamera.View.OwnSide, follow.Mode);
            yield return null;
            Assert.IsFalse(touch.LookZone.gameObject.activeSelf, "the look zone outlived first person");
            touch.showOnDesktop = false;
        }

        [UnityTest]
        public IEnumerator BotsBuyItemsWithTheirGold()
        {
            var bot = Pudge.All.First(h => h.team == Team.Bots);
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
        public IEnumerator ClickedHeroesGoldAndItemsReplaceThePlayersOnTheBar()
        {
            yield return null;
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var lens = shop.rows.First(r => r.item.castRangeBonus > 0f).item; // Aether Lens
            var bot = Pudge.All.First(h => h.team == Team.Bots);
            var botWallet = bot.GetComponent<HeroInventory>();
            botWallet.AddGold(lens.cost + 300, popup: false);
            Assert.IsTrue(botWallet.TryBuy(lens));
            player.GetComponent<HeroInventory>().AddGold(40, popup: false);
            Assert.AreEqual("40", shop.goldLabel.text);

            // a click on the bot's figure picks it, a click on empty ground picks nobody
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(bot.transform.position + Vector3.up * 1f);
            Assert.AreEqual(bot, HeroPicker.PickAt(screen, cam), "a click on the bot did not pick it");
            var ground = (Vector2)cam.WorldToScreenPoint(new Vector3(13f, 0f, 0f));
            Assert.IsNull(HeroPicker.PickAt(ground, cam), "a click on empty ground picked a hero");

            Assert.IsFalse(shop.inspectLabel.transform.parent.gameObject.activeSelf, "no inspect tab for the player's own data");
            shop.Inspect(botWallet);
            Assert.AreEqual(botWallet, shop.Shown);
            Assert.IsTrue(shop.inspectLabel.transform.parent.gameObject.activeSelf, "no mark that another hero is inspected");
            Assert.AreEqual($"Осмотр: Бот {bot.Nickname}", shop.inspectLabel.text);
            Assert.AreEqual("300", shop.goldLabel.text, "the bar does not show the clicked hero's gold");
            // a passive item keeps to the far slot
            Assert.AreEqual(lens.icon, shop.slots[botWallet.SlotOf(lens)].sprite, "the bar does not show the clicked hero's items");
            Assert.IsTrue(shop.statsPanel.gameObject.activeSelf, "no stats of the inspected hero");
            var stats = shop.statsPanel.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
            CollectionAssert.IsSubsetOf(new[] { "У/С/С", "0/0/0", "Спасён", "Здоровье", "670/670", "Скорость", "280", "Дальность хука", "Перезарядка" }, stats);
            StringAssert.Contains("+275", stats[stats.IndexOf("Дальность хука") + 1], "the lens's bonus to the hook range is not shown");
            StringAssert.StartsWith("1300", stats[stats.IndexOf("Дальность хука") + 1]);
            botWallet.AddGold(50, popup: false);
            Assert.AreEqual("350", shop.goldLabel.text, "the bar does not follow the clicked hero's gold");
            Assert.IsFalse(shop.Buy(lens), "the player has no gold for the lens: buying must stay the player's own");

            shop.Inspect(null);
            Assert.AreEqual("40", shop.goldLabel.text, "the player's own gold did not come back");
            Assert.IsFalse(shop.inspectLabel.transform.parent.gameObject.activeSelf, "the inspect tab stayed after going back");
            Assert.IsFalse(shop.statsPanel.gameObject.activeSelf, "the stats stayed after going back");
            Assert.IsNull(shop.slots[0].sprite);
        }

        [UnityTest]
        public IEnumerator HookSoundsAndPudgeVoiceFollowTheHook()
        {
            Assert.IsNotNull(hook.sounds.cast, "no hook throw sound (Resources/Audio/Hook/hook_throw)");
            Assert.IsNotNull(hook.sounds.impact, "no hook impact sound (Resources/Audio/Hook/hook_impact)");
            Assert.IsNotNull(hook.sounds.retractStop, "no hook return sound (Resources/Audio/Hook/hook_retract_stop)");
            foreach (var topic in new[] { "pud_spawn", "pud_ability_hook", "pud_ability_hook_miss", "pud_kill", "pud_death", "pud_respawn", "pud_laugh", "pudge_background" })
                Assert.IsNotNull(HeroVoice.RandomLine(topic), $"no voice lines {topic} in Resources/Audio/Voice");
            var voice = Object.FindAnyObjectByType<HeroVoice>();
            Assert.IsNotNull(voice, "the hero's voice is not created at start");

            var bot = Pudge.All.First(h => h.team == Team.Bots);
            bot.transform.position = new Vector3(player.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
            yield return new WaitForSeconds(0.5f); // the spawn line may go on: the hit cuts it off
            Assert.IsTrue(hook.TryCast(bot.transform.position));
            yield return new WaitUntil(() => bot.IsHooked);
            yield return null;
            Assert.AreEqual("pud_ability_hook", voice.LastTopic, "Pudge says nothing when the hook hits an enemy");
            yield return new WaitUntil(() => arena.ScoreOf(player).kills == 1);
            yield return new WaitForSeconds(0.5f);
            StringAssert.StartsWith("pud_", voice.LastTopic);
            Assert.AreNotEqual("pud_ability_hook", voice.LastTopic, "no kill line (first blood) after the kill");

            // a falling hero cuts its line off for the death line and says nothing else until it is back
            Assert.IsTrue(voice.IsSpeaking, "the kill line is already over: nothing to cut off");
            player.StartFalling(Vector3.zero);
            yield return null;
            Assert.AreEqual("pud_death", voice.LastTopic, "the kill line goes on instead of the death line");
            Assert.IsTrue(voice.IsSpeaking);
            yield return new WaitUntil(() => !player.IsAlive);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual("pud_death", voice.LastTopic, "another line came after the death line");
            yield return new WaitUntil(() => player.IsAlive && !player.IsFalling);
            yield return null;
            Assert.AreEqual("pud_respawn", voice.LastTopic, "no respawn line");
        }

        [UnityTest]
        public IEnumerator GoldRewardShowsAmountAndCoinsOverTheHero()
        {
            var popups = Object.FindAnyObjectByType<GoldPopups>();
            Assert.IsNotNull(popups, "no gold popups layer in the HUD");
            Assert.IsNotNull(popups.coinSprite, "coin picture (Gold.png) missing");
            player.GetComponent<HeroInventory>().AddGold(arena.KillGold(player));
            Assert.AreEqual(1, popups.ActiveCount);
            Assert.AreEqual($"+{arena.KillGold(player)} монет", popups.LastText);
            Assert.IsNotNull(popups.coinSound, "coin sound (coinsondesk.mp3) missing");
            Assert.AreEqual(1, popups.SoundsPlayed, "the local player's reward must clink");
            Pudge.All.First(h => h.team == Team.Bots).GetComponent<HeroInventory>().AddGold(arena.KillGold(player));
            Assert.AreEqual(1, popups.SoundsPlayed, "another hero's reward must be silent for the local player");
            Assert.AreEqual(2, popups.ActiveCount, "another hero's reward still shows its popup");
            yield return new WaitForSeconds(0.3f);
            var coins = popups.GetComponentsInChildren<Image>().Where(i => i.name == "Coin").ToList();
            Assert.AreEqual(popups.coinCount * 2, coins.Count);
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
            var popups = Object.FindAnyObjectByType<GoldPopups>();
            var wallets = Pudge.All.Select(h => h.GetComponent<HeroInventory>()).ToList();
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
            var fonts = Object.FindAnyObjectByType<HudFonts>();
            Assert.IsNotNull(fonts.latin, "Latin font missing");
            Assert.IsNotNull(fonts.cyrillic, "Cyrillic font missing");
            yield return null;
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var keyLabel = Object.FindAnyObjectByType<AbilityBar>().slots[0].keyLabel;
            Assert.AreEqual(fonts.latin, keyLabel.font, "\"Q\" must use the Latin font");
            Assert.AreEqual(fonts.latin, shop.goldLabel.font, "gold digits must use the Latin font");
            foreach (var text in fonts.GetComponentsInChildren<Text>(true))
            {
                Assert.AreEqual(HudFonts.HasCyrillic(text.text) ? fonts.cyrillic : fonts.latin, text.font, text.name + ": " + text.text);
                // a solid outline (TextOutline), or uGUI's on the labels that set their own
                var solid = text.GetComponent<TextOutline>();
                var plain = text.GetComponent<Outline>();
                Assert.IsTrue(solid != null || plain != null, text.name + " has no outline");
                var outlineColor = solid != null ? solid.color : plain.effectColor;
                Assert.AreEqual(0f, outlineColor.r + outlineColor.g + outlineColor.b, 1e-3f, text.name + "'s outline is not black");
                if (solid != null && plain == null && HudFonts.IsNumber(text.text) && text.transform.IsChildOf(shop.goldLabel.transform.parent))
                    Assert.Greater(solid.bold, 0f, text.name + ": numbers must be drawn bold");
            }
            Assert.AreEqual("У/С/С 0/0/0", Object.FindAnyObjectByType<ScoreHud>().label.text, "short own score in the corner");

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
            var announcer = Object.FindAnyObjectByType<KillAnnouncer>();
            Assert.IsNotNull(announcer, "no kill announcer in the HUD");
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
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
            var ended = new List<(Pudge hero, SeriesEnd reason, Pudge killer)>();
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
            var announcer = Object.FindAnyObjectByType<KillAnnouncer>();
            announcer.duration = 0.2f;
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
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
            var announcer = Object.FindAnyObjectByType<KillAnnouncer>();
            var bots = Pudge.All.Where(h => h.team == Team.Bots).ToList();
            arena.Streaks.multiKillWindow = 0.5f;
            var ended = new List<(Pudge hero, SeriesEnd reason, Pudge killer)>();
            arena.SeriesEnded += (hero, reason, killer) => ended.Add((hero, reason, killer));

            arena.ReportDeath(bots[0], player);
            arena.ReportDeath(bots[1], player);
            yield return new WaitForSeconds(0.3f);
            CollectionAssert.IsEmpty(ended);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, ended.Count, "the Double Kill series did not run out");
            Assert.AreEqual((player, SeriesEnd.Time, (Pudge)null), ended[0]);

            // FIRST BLOOD and DOUBLE KILL go first
            yield return new WaitUntil(() => announcer.LastText == "СЕРИЯ ПРЕРВАНА");
            Assert.AreEqual("Время вышло", announcer.LastSubtitle);
            CaptureScreen("series_ended_time");

            // an enemy's series ended by time names the enemy
            arena.ReportDeath(player, bots[2]);
            arena.ReportDeath(player, bots[2]);
            yield return new WaitUntil(() => ended.Count == 2);
            Assert.AreEqual((bots[2], SeriesEnd.Time, (Pudge)null), ended[1]);
            yield return new WaitUntil(() => announcer.Queued == 0 && announcer.LastText == "СЕРИЯ ПРЕРВАНА");
            Assert.AreEqual($"{bots[2].Nickname}: время вышло", announcer.LastSubtitle);
        }

        static void CaptureScreen(string name)
        {
            var dir = System.Environment.GetEnvironmentVariable("PUDGE_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var canvas = Object.FindAnyObjectByType<HudFonts>().GetComponent<Canvas>();
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
