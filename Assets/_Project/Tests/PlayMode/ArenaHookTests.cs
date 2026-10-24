using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
            SceneManager.LoadScene("Arena");
            yield return null;
            yield return null;
            arena = Object.FindFirstObjectByType<Arena>();
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
        public IEnumerator PlayerStaysOnOwnSide()
        {
            var motor = player.GetComponent<PudgeMotor>();
            motor.SetMoveInput(Vector3.forward);
            yield return new WaitForSeconds(3f);
            Assert.Greater(player.transform.position.z, -arena.pitHalfDepth - 1f, "player should have reached the pit edge");
            motor.SetMoveInput(Vector3.zero);
            Assert.Less(player.transform.position.z, -arena.pitHalfDepth);
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
