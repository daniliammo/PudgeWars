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
            arena = Object.FindAnyObjectByType<Arena>();
            arena.passiveGold = 0; // tests count gold exactly; the trickle has its own test
            var input = Object.FindAnyObjectByType<PlayerInputController>();
            input.enabled = false; // tests drive the player directly
            hook = input.GetComponent<HookAbility>();
            player = hook.GetComponent<Hookable>();
            foreach (var brain in Object.FindObjectsByType<BotBrain>()) brain.enabled = false;
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
            Assert.AreEqual(1, arena.ScoreOf(player).kills, "the kill is not in the player's own score");
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
        IEnumerator PlayerKillsBot(Hookable bot)
        {
            foreach (var b in Hookable.All.Where(h => h.team == Team.Bots && h != bot)) b.transform.position = new Vector3(10f, 0f, 12f);
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
            var bot = Hookable.All.First(h => h.team == Team.Bots);
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
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            for (int i = 0; i < 6; i++) arena.Streaks.RegisterKill(bot, Time.time); // RAMPAGE ×2
            Assert.AreEqual(2, arena.Streaks.RampageCount(bot));
            yield return PlayerKillsBot(bot);
            // kills 1–4 (up to Ultra Kill) × 5, the two Rampages × 10
            Assert.AreEqual(133 + 66 + 4 * 5 + 2 * 10, player.GetComponent<HeroInventory>().Gold);
        }

        [UnityTest]
        public IEnumerator KillWithoutASeriesPaysNoBonus()
        {
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            arena.Streaks.RegisterKill(bot, Time.time); // a single kill is no series yet
            yield return PlayerKillsBot(bot);
            Assert.AreEqual(arena.KillGold(bot), player.GetComponent<HeroInventory>().Gold);
            Assert.AreEqual(1, Object.FindAnyObjectByType<GoldPopups>().ActiveCount, "no bonus popup without a series");
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
        public IEnumerator HookPullsAnAllyToPudgeUnharmed()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            var ally = bots[0];
            ally.team = Team.Player;
            for (int i = 1; i < bots.Count; i++) bots[i].transform.position = new Vector3(-12f + 2f * i, 0f, 12f);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -12f), Quaternion.identity);
            ally.transform.position = new Vector3(0f, 0f, -6f);

            Hookable caught = null;
            hook.TargetCaught += h => caught = h;
            Assert.IsTrue(hook.TryCast(ally.transform.position));
            float t = 0f;
            while (hook.IsBusy && t < 5f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(ally, caught, "the hook passed through the ally");
            Assert.IsTrue(ally.IsAlive && !ally.IsFalling && !ally.IsHooked, "the ally was not let go unharmed");
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
        IEnumerator SetUpTugOfWar(Hookable ally, Hookable enemy, HookAbility rescuerHook, Hookable rescuer, System.Action<HookAbility> done)
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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

            Assert.IsTrue(rescuerHook.TryCast(ally.transform.position));
            t = 0f;
            while (ally.HeldBy == null && t < 2f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(rescuerHook, ally.HeldBy, "the rescuer's hook did not hold the ally");
            Assert.AreEqual(enemyHook, ally.HookedBy, "a held ally must stay on the enemy's hook");
            done(enemyHook);
        }

        [UnityTest]
        public IEnumerator AllyHookHoldsAHeroOnAnEnemysHookAndPullsItHomeWhenItLetsGo()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            var enemy = bots[0];
            var ally = bots[1];
            ally.team = Team.Player;
            HookAbility enemyHook = null;
            yield return SetUpTugOfWar(ally, enemy, hook, player, h => enemyHook = h);

            // tug of war: the enemy pulls at half speed and drags the rescuer after the ally
            var allyFrom = ally.transform.position;
            var playerFrom = player.transform.position;
            yield return new WaitForSeconds(0.5f);
            float pulled = Vector3.Distance(allyFrom, ally.transform.position);
            Assert.AreEqual(enemyHook.pullSpeed / 3f * 0.5f, pulled, 0.15f, "the held ally is not pulled three times slower");
            Assert.Greater(Vector3.Distance(playerFrom, player.transform.position), 0.2f, "the rescuer is not dragged after the ally");
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
        IEnumerator PullAllyAwayFromAnEnemyThrow(Vector3? allyRun, Vector3? enemyAim, System.Action<Hookable> done)
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            Hookable caught = null;
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
            Hookable ally = null;
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
            Hookable ally = null;
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            Assert.AreEqual("У/С/С 0/0/1", Object.FindAnyObjectByType<ScoreHud>().label.text, "rescues are the third number");
        }

        [UnityTest]
        public IEnumerator BotLeadsItsHookToHoldAnAllyOnAnEnemysHook()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            brain.throwInterval = new Vector2(30f, 30f); // only the rescue throw
            brain.pauseRange = new Vector2(30f, 30f);    // and no wandering off before it
            brain.enabled = true;
            var rescueHook = rescuer.GetComponent<HookAbility>();
            Hookable caught = null;
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
        public IEnumerator BotInterceptCountsWindupFlightAndTheHeadsReach()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
        public IEnumerator BotDoesNotThrowThroughAnAlly()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            var bars = Object.FindAnyObjectByType<HealthBars>();
            var block = new MaterialPropertyBlock();
            Color Marker(Hookable h)
            {
                h.transform.Find("TeamMarker").GetComponent<Renderer>().GetPropertyBlock(block);
                return block.GetColor("_BaseColor");
            }
            Color Bar(Hookable h) => bars.Of(h).fill.GetComponent<Image>().color;

            Assert.AreEqual(player.allyMarkerColor, Marker(player));
            Assert.AreEqual(bot.enemyMarkerColor, Marker(bot));
            Assert.AreEqual(bars.playerColor, Bar(player));
            Assert.AreEqual(bars.enemyColor, Bar(bot));

            // a bot on the player's team is an ally: green, whatever its prefab says
            bot.team = Team.Player;
            yield return null;
            Assert.AreEqual(bot.allyMarkerColor, Marker(bot), "an allied bot's marker stayed red");
            Assert.AreEqual(bars.playerColor, Bar(bot), "an allied bot's health bar stayed red");

            // the local player plays for Dire (as the host in a network game): its own marker stays green, Radiant is red
            player.team = Team.Bots;
            bot.team = Team.Player;
            yield return null;
            Assert.AreEqual(player.allyMarkerColor, Marker(player), "the local player's own marker must be green");
            Assert.AreEqual(bot.enemyMarkerColor, Marker(bot));
            Assert.AreEqual(bars.enemyColor, Bar(bot));
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
            Assert.AreEqual(arena.KillGold(victim), player.GetComponent<HeroInventory>().Gold, "the tethered kill belongs to the player");
            Assert.IsFalse(hook.chain.enabled);
        }

        [UnityTest]
        public IEnumerator CaughtCharacterLosesHookInWindupButNotInFlight()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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

            // already flying: the hook flies on, and a miss vanishes at full range
            yield return new WaitUntil(() => hook.IsReady);
            Assert.IsTrue(hook.TryCast(aim));
            yield return new WaitForSeconds(hook.castDelay + 0.2f);
            Assert.IsTrue(hook.IsFlying);
            player.AttachTo(bot.GetComponent<HookAbility>());
            Assert.IsTrue(hook.IsFlying && hook.HeadVisible, "a hook in flight must fly on when the owner is caught");
            float t = 0f;
            while (hook.IsBusy && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(hook.IsBusy, "a missed hook of a caught owner did not vanish");
            Assert.IsFalse(hook.HeadVisible);
            player.Release();
        }

        [UnityTest]
        public IEnumerator HookFlyingWhenTheOwnerIsCaughtDragsItsCatchIntoThePit()
        {
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
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

        [UnityTest]
        public IEnumerator ShopSellsAetherLensThatExtendsHookRange()
        {
            var shop = Object.FindAnyObjectByType<ShopUI>();
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
            Assert.AreEqual(lens.icon, shop.slots[0].sprite);
            Assert.AreEqual("Куплено", row.buyLabel.text);
            Assert.IsFalse(row.ShortageShown, "an owned item lacks no gold");
            row.OnPointerExit(new PointerEventData(EventSystem.current));
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
            var shop = Object.FindAnyObjectByType<ShopUI>();
            Assert.IsNotNull(EventSystem.current, "no EventSystem");
            yield return null;
            AssertPointerHits(shop.toggleButton);
            shop.Toggle();
            yield return new WaitWhile(() => shop.IsAnimating);
            AssertPointerHits(shop.rows[0].buyButton);
            AssertPointerHits(shop.closeButton);
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
            foreach (var brain in Object.FindObjectsByType<BotBrain>())
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
            var bars = Object.FindAnyObjectByType<HealthBars>();
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
                Assert.AreEqual(Color.black, health.GetComponent<Outline>().effectColor, "the health digits need a black outline to read on any fill");
                Assert.Greater(health.transform.GetSiblingIndex(), root.Find("Inner").GetSiblingIndex(), "the digits must be drawn over the fill");
            }
            CaptureScreen("health_bars");

            // a dead hero's bar goes away and comes back full with the respawn
            var bot = heroes.First(h => h.team == Team.Bots);
            bot.StartFalling(Vector3.zero);
            yield return new WaitUntil(() => !bot.IsAlive);
            yield return null;
            Assert.AreEqual(0f, bot.Health);
            Assert.AreEqual($"0/{Mathf.CeilToInt(bot.maxHealth)}", bars.HealthLabel(bot).text);
            Assert.IsFalse(bars.Of(bot).root.gameObject.activeSelf, "a dead hero's bar is still shown");
            yield return new WaitUntil(() => bot.IsAlive);
            yield return null;
            Assert.IsTrue(bars.Of(bot).root.gameObject.activeSelf);
            Assert.AreEqual(1f, bars.Of(bot).fill.anchorMax.x, 1e-4f);
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
            var bot = Hookable.All.First(h => h.team == Team.Bots);
            foreach (var b in Hookable.All.Where(h => h.team == Team.Bots && h != bot)) b.transform.position = new Vector3(10f, 0f, 12f);
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

            // death: red from the sides and most of the colour gone, until the respawn
            Assert.AreEqual(0f, feedback.BloodAlpha);
            Assert.IsFalse(feedback.PostProcessing, "post-processing must stay off while alive");
            player.StartFalling(Vector3.zero);
            t = 0f;
            while (player.IsAlive && t < 3f) { t += Time.deltaTime; yield return null; }
            Assert.IsFalse(player.IsAlive);
            yield return new WaitForSeconds(feedback.greyIn + 0.2f);
            Assert.Greater(feedback.BloodAlpha, 0.3f, "no red at the sides after death");
            Assert.AreEqual(1f, feedback.GreyWeight, 0.01f, "the picture did not lose its colour");
            Assert.IsTrue(feedback.PostProcessing);
            Capture("death_effects");

            t = 0f;
            while (!player.IsAlive && t < arena.respawnDelay + 2f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(player.IsAlive, "the player did not respawn");
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
            var bots = Hookable.All.Where(h => h.team == Team.Bots).ToList();
            float back = arena.pitHalfDepth + arena.sideDepth - 0.6f, front = arena.pitHalfDepth + 0.6f;
            var spots = new[] { new Vector3(-6f, 0f, back), new Vector3(6f, 0f, back), new Vector3(-6f, 0f, front), new Vector3(6f, 0f, front) };
            for (int i = 0; i < bots.Count; i++) bots[i].transform.position = spots[i % spots.Length];

            bool InFrame(Hookable h)
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
        public IEnumerator ClickedHeroesGoldAndItemsReplaceThePlayersOnTheBar()
        {
            yield return null;
            var shop = Object.FindAnyObjectByType<ShopUI>();
            var lens = shop.rows[0].item;
            var bot = Hookable.All.First(h => h.team == Team.Bots);
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
            Assert.AreEqual(lens.icon, shop.slots[0].sprite, "the bar does not show the clicked hero's items");
            Assert.IsTrue(shop.statsPanel.gameObject.activeSelf, "no stats of the inspected hero");
            var stats = shop.statsPanel.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
            CollectionAssert.IsSubsetOf(new[] { "У/С/С", "0/0/0", "Здоровье", "670/670", "Скорость", "280", "Дальность хука", "Перезарядка" }, stats);
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
            Hookable.All.First(h => h.team == Team.Bots).GetComponent<HeroInventory>().AddGold(arena.KillGold(player));
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
                Assert.IsTrue(text.TryGetComponent<Outline>(out var outline), text.name + " has no outline");
                Assert.AreEqual(0f, outline.effectColor.r + outline.effectColor.g + outline.effectColor.b, 1e-3f, text.name + "'s outline is not black");
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
            var announcer = Object.FindAnyObjectByType<KillAnnouncer>();
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
            var announcer = Object.FindAnyObjectByType<KillAnnouncer>();
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
