using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace VillagePlebs
{
    internal static class Talk
    {
        private static readonly (string id, string meaning, string[] words, string reply)[] Intents =
        {
            ("e:PutAway", "Put away what it carries into the chests.", new[] { "put away", "put your things away", "store your", "stash", "unload", "empty your bag" }, null),
            ("e:Restock", "Fill its food slots from the chests.", new[] { "restock", "stock up", "get food", "grab food", "get some food" }, null),
            ("e:BetterGear", "Look in the chests for better gear and wear it.", new[] { "gear up", "better gear", "new gear", "check gear", "check your gear", "armour up", "armor up", "upgrade your gear" }, null),
            ("e:RepairGear", "Repair its own gear at a workbench or forge.", new[] { "repair your gear", "fix your gear", "repair your stuff", "fix your stuff", "repair gear" }, null),
            ("e:RepairBase", "Repair the base with its hammer.", new[] { "repair the base", "fix the base", "repair base", "repair the walls", "fix the walls", "repair" }, null),
            ("e:Fires", "Feed the fires.", new[] { "feed the fire", "fires", "fuel", "fire" }, null),
            ("e:Cook", "Cook the raw food it carries.", new[] { "cook" }, null),
            ("e:Harvest", "Harvest the ripe crops at home.", new[] { "harvest", "reap", "bring in the crops", "pick the crops" }, null),
            ("e:Plant", "Plant crops on the tilled ground at home.", new[] { "plant", "sow", "farm" }, null),
            ("e:Fish", "Go fishing near home.", new[] { "go fishing", "fishing", "catch fish", "fish" }, null),
            ("e:Smelt", "Run the smelters near home.", new[] { "smelt", "smelter", "furnace", "kiln" }, null),
            ("e:Shop", "Go shopping at the traders.", new[] { "go shopping", "shopping", "shop", "buy" }, null),
            ("follow", "Follow the player and fight beside them (go on an adventure together).", new[] { "follow", "come with", "with me", "let's go", "lets go", "adventure", "join" }, Words.P("villagepleb_talk_reply_follow")),
            ("come", "Come to the player right now.", new[] { "come here", "come", "here", "to me", "over here" }, Words.P("villagepleb_talk_reply_come")),
            ("stay", "Stay where it is.", new[] { "stay", "wait", "hold", "stop" }, Words.P("villagepleb_talk_reply_stay")),
            ("guard", "Guard the spot where it stands.", new[] { "guard", "watch", "protect this" }, Words.P("villagepleb_talk_reply_guard")),
            ("home", "Go home and live its own life there (gather, store, craft).", new[] { "go home", "home", "live", "gather", "work", "chop", "mine" }, Words.P("villagepleb_talk_reply_home")),
            ("aggressive", "Fight aggressively.", new[] { "aggressive", "attack everything", "kill", "go wild" }, Words.P("villagepleb_talk_reply_aggressive")),
            ("defensive", "Fight defensively, stay close and careful.", new[] { "defensive", "careful", "be careful", "stay close" }, Words.P("villagepleb_talk_reply_defensive")),
            ("passive", "Avoid fighting.", new[] { "passive", "don't fight", "dont fight", "no fighting", "peace" }, Words.P("villagepleb_talk_reply_passive")),
            ("balanced", "Fight normally.", new[] { "balanced", "normal", "fight normally" }, Words.P("villagepleb_talk_reply_balanced")),
            ("auto", "Decide for itself how to fight and help, by what the player is doing.", new[] { "your call", "decide", "up to you", "auto", "use your judgement", "use your judgment" }, Words.P("villagepleb_talk_reply_auto")),
            ("grave", "Go and get its things back from its tombstone.", new[] { "grave", "tombstone", "your stuff", "your things", "your gear" }, Words.P("villagepleb_talk_reply_grave")),
        };

        public static void Heard(string text)
        {
            Player me = Player.m_localPlayer;
            if (me == null || string.IsNullOrWhiteSpace(text)) return;
            string t = text.Trim();
            foreach (Humanoid c in Companion.All().Where(h => Companion.CanCommand(h, me) && Vector3.Distance(h.transform.position, me.transform.position) < 150f))
            {
                string name = Companion.NameOf(c);
                if (!StartsWithName(t, name)) continue;
                string rest = t.Substring(name.Length).TrimStart(',', ':', ' ', '!', '.').Trim();
                if (rest.Length == 0) { Say(c, Words.P("villagepleb_talk_yes")); return; }
                Do(c, me, ByWords(rest), rest);
                return;
            }
        }

        private static bool StartsWithName(string text, string name)
        {
            if (text.Length < name.Length) return false;
            string head = text.Substring(0, name.Length);
            bool same = string.Compare(head, name, StringComparison.OrdinalIgnoreCase) == 0
                        || string.Compare(Plain(head), Plain(name), StringComparison.OrdinalIgnoreCase) == 0;
            return same && (text.Length == name.Length || !char.IsLetterOrDigit(text[name.Length]));
        }

        private static string Plain(string s) => new string(s.Normalize(NormalizationForm.FormD).Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

        private static string ByWords(string rest)
        {
            string r = rest.ToLowerInvariant();
            foreach (var i in Intents) if (i.words.Any(w => r.Contains(w))) return i.id;
            return null;
        }

        private static void Do(Humanoid c, Player me, string intent, string said)
        {
            var match = Intents.FirstOrDefault(i => i.id == intent);
            if (intent == null || match.id == null) { Say(c, Words.P("villagepleb_talk_dont_follow")); return; }
            if (intent.StartsWith("e:") && Enum.TryParse(intent.Substring(2), out Errand errand))
            {
                BrainState st = Brain.Claim(c, out string why);
                if (st == null) { VillagePlebsPlugin.Tell(why); return; }
                string result = Errands.Run(st, errand);
                Brain.Get(c)?.Remember(Words.P("villagepleb_talk_you_said", said, errand));
                VillagePlebsPlugin.Instance?.Note($"{me.GetPlayerName()} told {Companion.NameOf(c)} \"{said}\" -> errand {errand}: {result}");
                return;
            }
            bool ok = true;
            switch (intent)
            {
                case "follow": ok = Home.Follow(c); break;
                case "come":
                    ok = Home.Follow(c);
                    if (ok && Vector3.Distance(c.transform.position, me.transform.position) > 15f) Brain.TeleportBehind(c, me, "villagepleb_brain_came_to");
                    break;
                case "stay": ok = Companion.Write(c, z => z.Set(Keys.Order, (int)Order.Stay)); break;
                case "guard": ok = Companion.Write(c, z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, c.transform.position); }); break;
                case "home": ok = Home.GoHome(c, true); break;
                case "aggressive": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Aggressive)); break;
                case "defensive": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Defensive)); break;
                case "passive": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Passive)); break;
                case "balanced": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Balanced)); break;
                case "auto": ok = Companion.Write(c, z => z.Set(Keys.Style, (int)Style.Auto)); break;
                case "grave":
                    if (!Grave.Has(c)) { Say(c, Words.P("villagepleb_talk_no_grave")); return; }
                    BrainState gst = Brain.Claim(c, out string gwhy);
                    if (gst == null) { VillagePlebsPlugin.Tell(gwhy); return; }
                    Grave.Fetch(gst, null);
                    break;
            }
            if (!ok) { Say(c, Words.P("villagepleb_talk_busy")); return; }
            Say(c, match.reply);
            Brain.Get(c)?.Remember(Words.P("villagepleb_talk_you_said", said, intent));
            VillagePlebsPlugin.Instance?.Note($"{me.GetPlayerName()} told {Companion.NameOf(c)} \"{said}\" -> {intent}");
        }

        internal static void Say(Humanoid c, string text)
        {
            if (c == null || Chat.instance == null) return;
            // Long enough to read: 10 s, and more for a longer line (up to 20 s).
            string shown = Words.Show(text);
            Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.3f, 30f, Mathf.Clamp(6f + shown.Length * 0.08f, 10f, 20f), "", shown, false);
        }

        public const string ChattyKey = "vp_chatty";
        public static bool Chatty(Component c) => Companion.Zdo(c)?.GetBool(ChattyKey, true) ?? true;

        private static readonly Dictionary<string, float> LastSaid = new Dictionary<string, float>();
        private static readonly AccessTools.FieldRef<Chat, float> HideTimer =
            AccessTools.Field(typeof(Chat), "m_hideTimer") != null ? AccessTools.FieldRefAccess<Chat, float>("m_hideTimer") : null;

        public static bool Hush;

        public static void Tell(Humanoid c, string text, string topic = null, float minutes = 0f)
        {
            if (c == null || string.IsNullOrEmpty(text)) return;
            if (Hush) { Activity.Log(c, "(away) " + text); return; }
            if (topic != null)
            {
                string key = Companion.IdOf(c) + ":" + topic;
                if (LastSaid.TryGetValue(key, out float at) && Time.time - at < minutes * 60f) return;
                LastSaid[key] = Time.time;
            }
            Say(c, text);
            Activity.Log(c, "said: " + text);
            if (!Chatty(c)) return;
            Player local = Player.m_localPlayer;
            if (local != null && Companion.IsMine(c, local)) ToChat(Companion.NameOf(c), text);
            else Net.SendSays(Companion.MasterId(c), Companion.NameOf(c), text);
        }

        public static void Mention(Humanoid c, string text, string topic = null, float minutes = 0f)
        {
            if (c == null || string.IsNullOrEmpty(text)) return;
            if (Hush) { Activity.Log(c, "(away) " + text); return; }
            if (topic != null)
            {
                string key = Companion.IdOf(c) + ":" + topic;
                if (LastSaid.TryGetValue(key, out float at) && Time.time - at < minutes * 60f) return;
                LastSaid[key] = Time.time;
            }
            Say(c, text);
            Activity.Log(c, "said: " + text);
        }

        public static void ToChat(string name, string text)
        {
            if (Chat.instance == null) return;
            Chat.instance.AddString(name, Words.Show(text), Talker.Type.Normal);
            // The chat window fades once its timer passes the game's delay (about 10 s): started back, it stays some 25 s.
            if (HideTimer != null) HideTimer(Chat.instance) = -15f;
        }

        public static void Forget() => LastSaid.Clear();
    }

    [HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
    internal static class Chat_SendText
    {
        private static void Postfix(string text)
        {
            try { Talk.Heard(text); }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn("Companion chat: " + e.Message); }
        }
    }
}
