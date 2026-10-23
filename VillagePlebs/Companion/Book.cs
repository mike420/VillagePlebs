using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace VillagePlebs
{
    internal class BookData
    {
        public long Id, MasterId;
        public string MasterName = "";
        public readonly List<string> Entries = new List<string>();
        public readonly Dictionary<string, int> Tally = new Dictionary<string, int>();
        public string Missions = "";
        public bool Fresh;
        public float AskedAt = -99f, UsedAt, OfferedAt, ParkSentAt;
        public int OfferTries, ParkTries, LastParked;
        public bool Dirty;

        public readonly List<string> NewEntries = new List<string>();
        public readonly Dictionary<string, int> NewCounts = new Dictionary<string, int>();
        public string NewMissions;

        public string JournalText => string.Join("\n", Entries);
        public string TallyText => string.Join(";", Tally.Select(kv => $"{kv.Key}={kv.Value}"));
        public bool HasNews => NewEntries.Count > 0 || NewCounts.Count > 0 || NewMissions != null;
    }

    internal static class Book
    {
        private const string RpcAsk = "VillagePlebs_BookAsk", RpcWhole = "VillagePlebs_Book", RpcNews = "VillagePlebs_BookNews";
        private const string RpcOffer = "VillagePlebs_BookOffer", RpcTaken = "VillagePlebs_BookTaken", RpcParked = "VillagePlebs_BookParked";
        public static readonly string[] RpcNames = { RpcAsk, RpcWhole, RpcNews, RpcOffer, RpcTaken, RpcParked };
        private const char Field = '\u001e', Item = '\u001f';

        public const string OldEntriesKey = "vp_journal", OldTallyKey = "vp_tally", OldMissionsKey = "vp_missions";

        public const string ParkedKey = "vp_booknews";
        private const int ParkedMax = 1500;
        private static float _nextPark;

        private static readonly Dictionary<long, BookData> Books = new Dictionary<long, BookData>();
        private static float _nextTick, _nextSave;

        public static void Stop()
        {
            if (Player.m_localPlayer != null) SaveProfiles(Player.m_localPlayer);
            Books.Clear();
        }

        public static void Register(ZRoutedRpc rpc)
        {
            Books.Clear();
            rpc.Register<string>(RpcAsk, OnAsk);
            rpc.Register<string>(RpcWhole, OnWhole);
            rpc.Register<string>(RpcNews, OnNews);
            rpc.Register<string>(RpcOffer, OnOffer);
            rpc.Register<string>(RpcTaken, OnTaken);
            rpc.Register<string>(RpcParked, OnParked);
        }

        public static BookData Of(Component c)
        {
            ZDO z = Companion.Zdo(c);
            return z == null ? new BookData() : Of(Companion.IdOf(c), z);
        }

        public static BookData Of(long id, ZDO z, long master = 0L)
        {
            if (Player.m_localPlayer == null) return new BookData { Id = id };
            if (!Books.TryGetValue(id, out BookData b)) Books[id] = b = Make(id, z, master);
            else if (!b.Fresh && master != 0L && b.MasterId == 0L) b.MasterId = master;
            if (!b.Fresh && Ours(b)) Books[id] = b = Make(id, z, b.MasterId);
            if (z != null && b.MasterId == 0L) { b.MasterId = z.GetLong(Keys.Master, 0L); b.MasterName = z.GetString(Keys.MasterName, ""); }
            b.UsedAt = Time.time;
            if (!b.Fresh) Ask(b);
            return b;
        }

        private static BookData Make(long id, ZDO z, long master)
        {
            var b = new BookData { Id = id, MasterId = master };
            Player me = Player.m_localPlayer;
            if (z != null) { b.MasterId = z.GetLong(Keys.Master, 0L); b.MasterName = z.GetString(Keys.MasterName, ""); }
            Profile prof = me != null ? Profile.Find(me, id) : null;
            if (prof != null && b.MasterId == 0L) b.MasterId = me.GetPlayerID();
            if (Ours(b) && string.IsNullOrEmpty(b.MasterName)) b.MasterName = me.GetPlayerName();
            if (Ours(b))
            {
                if (prof != null) Fill(b, prof.JournalText, prof.TallyText, prof.MissionText);
                if (z != null && HasOld(z)) Join(b, z.GetString(OldEntriesKey, ""), z.GetString(OldTallyKey, ""), z.GetString(OldMissionsKey, ""));
                b.Fresh = true;
            }
            else if (z != null && HasOld(z)) { Fill(b, z.GetString(OldEntriesKey, ""), z.GetString(OldTallyKey, ""), z.GetString(OldMissionsKey, "")); b.Fresh = true; }
            return b;
        }

        private static bool Ours(BookData b) => Player.m_localPlayer != null && b.MasterId != 0L && b.MasterId == Player.m_localPlayer.GetPlayerID();

        private static bool HasOld(ZDO z) => z.GetString(OldEntriesKey, "") != "" || z.GetString(OldTallyKey, "") != "" || z.GetString(OldMissionsKey, "") != "";

        private static void Fill(BookData b, string journal, string tally, string missions)
        {
            b.Entries.Clear();
            b.Entries.AddRange((journal ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            b.Tally.Clear();
            foreach (string part in (tally ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.LastIndexOf('=');
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) b.Tally[part.Substring(0, eq)] = n;
            }
            b.Missions = missions ?? "";
        }

        public static void AddEntry(Humanoid c, string entry)
        {
            BookData b = Of(c);
            entry = Plain(entry);
            Entry(b, entry);
            b.NewEntries.Add(entry);
            if (b.NewEntries.Count > Journal.MaxEntries) b.NewEntries.RemoveRange(0, b.NewEntries.Count - Journal.MaxEntries);
        }

        public static int AddCount(Humanoid c, string what, int n)
        {
            BookData b = Of(c);
            what = Plain(what).Replace(';', ',').Replace('=', '-');
            b.NewCounts[what] = (b.NewCounts.TryGetValue(what, out int had) ? had : 0) + n;
            return Count(b, what, n);
        }

        public static void SetMissions(Humanoid c, string text)
        {
            BookData b = Of(c);
            text = Plain(text);
            if (b.Missions == text) return;
            b.Missions = text;
            b.NewMissions = text;
            b.Dirty = true;
        }

        private static string Plain(string s) => (s ?? "").Replace(Item, ' ').Replace(Field, ' ');

        private static void Entry(BookData b, string entry)
        {
            b.Entries.Add(entry);
            if (b.Entries.Count > Journal.MaxEntries) b.Entries.RemoveRange(0, b.Entries.Count - Journal.MaxEntries);
            b.Dirty = true;
        }

        private static int Count(BookData b, string what, int n)
        {
            b.Tally[what] = (b.Tally.TryGetValue(what, out int had) ? had : 0) + n;
            b.Dirty = true;
            return b.Tally[what];
        }

        public static void Tick()
        {
            if (Time.time < _nextTick || ZNet.instance == null || ZRoutedRpc.instance == null) return;
            _nextTick = Time.time + 1f;
            Player me = Player.m_localPlayer;
            if (me == null) return;
            HashSet<string> online = Online();

            var running = new Dictionary<long, ZDO>();
            foreach (Humanoid c in Companion.All()) if (c.GetComponent<ZNetView>().IsOwner()) running[Companion.IdOf(c)] = Companion.Zdo(c);
            foreach (var kv in running) if (kv.Value.GetString(ParkedKey, "") != "") Of(kv.Key, kv.Value);
            bool park = Time.time >= _nextPark;
            if (park) _nextPark = Time.time + 5f;

            foreach (BookData b in Books.Values)
            {
                running.TryGetValue(b.Id, out ZDO z);
                bool masterHere = online.Contains(b.MasterName);
                if (b.HasNews)
                {
                    if (Ours(b)) { if (online.Count > 1) Send(b); else Clear(b); }
                    else if (masterHere) Send(b);
                    else if (park && z != null) Park(b, z);
                }
                string parked = z?.GetString(ParkedKey, "") ?? "";
                if (parked != "")
                {
                    if (Ours(b)) { Apply(b, parked.Split(Item)); z.Set(ParkedKey, ""); }
                    else if (masterHere && Due(ref b.ParkSentAt, ref b.ParkTries))
                        Invoke(MasterPeer(b), RpcParked, Head(b) + Field + parked);
                }
                if (!b.Fresh && Time.time - b.UsedAt < 10f) Ask(b);
            }

            foreach (Humanoid c in Companion.All())
            {
                ZNetView v = c.GetComponent<ZNetView>();
                if (!v.IsOwner() || !HasOld(v.GetZDO())) continue;
                ZDO z = v.GetZDO();
                BookData b = Of(c);
                string journal = z.GetString(OldEntriesKey, ""), tally = z.GetString(OldTallyKey, ""), missions = z.GetString(OldMissionsKey, "");
                if (Ours(b))
                {
                    Join(b, journal, tally, missions);
                    ClearOld(z);
                    VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)}'s journal moved off its network data (it was sent to every player with each step)");
                }
                else if (online.Contains(b.MasterName) && Due(ref b.OfferedAt, ref b.OfferTries))
                    Invoke(MasterPeer(b), RpcOffer, string.Join(Field.ToString(), Head(b), journal, tally, missions));
            }

            if (Time.time >= _nextSave) { _nextSave = Time.time + 5f; SaveProfiles(me); }
        }

        private static bool Due(ref float at, ref int tries)
        {
            if (tries >= 8 || Time.time < at) return false;
            at = Time.time + Mathf.Min(300f, 10f * Mathf.Pow(2f, tries));
            tries++;
            return true;
        }

        private static long MasterPeer(BookData b)
        {
            foreach (ZNet.PlayerInfo p in ZNet.instance.GetPlayerList())
                if (p.m_name == b.MasterName && !p.m_characterID.IsNone()) return p.m_characterID.UserID;
            return ZRoutedRpc.Everybody;
        }

        private static void Join(BookData b, string journal, string tally, string missions)
        {
            var old = new BookData();
            Fill(old, journal, tally, missions);
            var known = new HashSet<string>(old.Entries);
            List<string> entries = old.Entries.Concat(b.Entries.Where(e => !known.Contains(e))).ToList();
            b.Entries.Clear();
            b.Entries.AddRange(entries.Skip(Math.Max(0, entries.Count - Journal.MaxEntries)));
            foreach (var kv in old.Tally) b.Tally[kv.Key] = Math.Max(kv.Value, b.Tally.TryGetValue(kv.Key, out int had) ? had : 0);
            if (old.Missions != "") b.Missions = old.Missions;
            b.Fresh = b.Dirty = true;
        }

        private static void ClearOld(ZDO z) { z.Set(OldEntriesKey, ""); z.Set(OldTallyKey, ""); z.Set(OldMissionsKey, ""); }

        private static int OldStamp(ZDO z) =>
            string.Join(Field.ToString(), z.GetString(OldEntriesKey, ""), z.GetString(OldTallyKey, ""), z.GetString(OldMissionsKey, "")).GetStableHashCode();

        private static void OnOffer(long sender, string payload)
        {
            string[] f = (payload ?? "").Split(Field);
            if (f.Length < 4 || !ParseHead(f[0], out long id, out long master)) return;
            if (Player.m_localPlayer == null || master == 0L || Player.m_localPlayer.GetPlayerID() != master) return;
            if (Profile.Find(Player.m_localPlayer, id) == null) return;
            BookData b = Of(id, null, master);
            Join(b, f[1], f[2], f[3]);
            SaveProfiles(Player.m_localPlayer);
            string stamp = string.Join(Field.ToString(), f[1], f[2], f[3]).GetStableHashCode().ToString(CultureInfo.InvariantCulture);
            Invoke(sender, RpcTaken, id.ToString(CultureInfo.InvariantCulture) + "|" + stamp + "|old");
            VillagePlebsPlugin.Instance?.Note($"A companion's journal ({id}) was handed over by the game running it");
        }

        private static void OnParked(long sender, string payload)
        {
            int at = (payload ?? "").IndexOf(Field);
            if (at < 0 || !ParseHead(payload.Substring(0, at), out long id, out long master)) return;
            if (Player.m_localPlayer == null || master == 0L || Player.m_localPlayer.GetPlayerID() != master) return;
            if (Profile.Find(Player.m_localPlayer, id) == null) return;
            string parked = payload.Substring(at + 1);
            int stamp = parked.GetStableHashCode();
            BookData b = Of(id, null, master);
            if (b.LastParked != stamp)
            {
                Apply(b, parked.Split(Item));
                b.LastParked = stamp;
                SaveProfiles(Player.m_localPlayer);
            }
            Invoke(sender, RpcTaken, id.ToString(CultureInfo.InvariantCulture) + "|" + stamp.ToString(CultureInfo.InvariantCulture) + "|parked");
        }

        private static void OnTaken(long sender, string payload)
        {
            string[] f = (payload ?? "").Split('|');
            if (f.Length < 2 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stamp)) return;
            Humanoid c = Companion.All().FirstOrDefault(x => Companion.IdOf(x) == id);
            ZDO z = Companion.Zdo(c);
            if (z == null || !c.GetComponent<ZNetView>().IsOwner()) return;
            Books.TryGetValue(id, out BookData b);
            if (f.Length >= 3 && f[2] == "parked")
            {
                if (z.GetString(ParkedKey, "").GetStableHashCode() != stamp) { if (b != null) b.ParkTries = 0; return; }
                z.Set(ParkedKey, "");
                if (b != null) b.ParkTries = 0;
                VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)}'s news from while its player was away was handed over");
                return;
            }
            if (!HasOld(z) || OldStamp(z) != stamp) { if (b != null) b.OfferTries = 0; return; }
            ClearOld(z);
            VillagePlebsPlugin.Instance?.Note($"{Companion.NameOf(c)}'s journal moved off its network data (its player's game has it)");
        }

        public static void SaveProfiles(Player me)
        {
            foreach (BookData b in Books.Values.Where(x => x.Dirty && x.Fresh && Ours(x)))
            {
                b.Dirty = false;
                Profile prof = Profile.Find(me, b.Id);
                if (prof == null) continue;
                prof.JournalText = b.JournalText; prof.TallyText = b.TallyText; prof.MissionText = b.Missions;
                Profile.Save(me, prof);
            }
        }

        private static HashSet<string> Online() =>
            new HashSet<string>(ZNet.instance.GetPlayerList().Where(p => !p.m_characterID.IsNone()).Select(p => p.m_name));

        public static void Leaving()
        {
            if (ZNet.instance == null) return;
            HashSet<string> online = Online();
            foreach (Humanoid c in Companion.All())
            {
                if (!c.GetComponent<ZNetView>().IsOwner() || !Books.TryGetValue(Companion.IdOf(c), out BookData b)) continue;
                if (b.HasNews && !Ours(b) && !online.Contains(b.MasterName)) Park(b, Companion.Zdo(c));
            }
        }

        private static void Park(BookData b, ZDO z)
        {
            var entries = new List<string>();
            var counts = new Dictionary<string, int>();
            string missions = null;
            foreach (string it in z.GetString(ParkedKey, "").Split(Item).Concat(Items(b)))
            {
                if (it.Length == 0) continue;
                string rest = it.Substring(1);
                if (it[0] == 'e') entries.Add(rest);
                else if (it[0] == 'm') missions = rest;
                else if (it[0] == 'c' && SplitCount(rest, out string what, out int n)) counts[what] = (counts.TryGetValue(what, out int had) ? had : 0) + n;
            }
            Clear(b);
            string Build() => string.Join(Item.ToString(), entries.Select(e => "e" + e)
                .Concat(counts.Select(kv => "c" + kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture)))
                .Concat(missions != null ? new[] { "m" + missions } : new string[0]));
            string text = Build();
            if (text.Length > ParkedMax && missions != null) { missions = null; text = Build(); }
            while (text.Length > ParkedMax && entries.Count > 0) { entries.RemoveAt(0); text = Build(); }
            z.Set(ParkedKey, text);
        }

        private static void Clear(BookData b) { b.NewEntries.Clear(); b.NewCounts.Clear(); b.NewMissions = null; }

        private static string Head(BookData b) => b.Id.ToString(CultureInfo.InvariantCulture) + "|" + b.MasterId.ToString(CultureInfo.InvariantCulture);

        private static bool ParseHead(string head, out long id, out long master)
        {
            master = 0L;
            string[] f = head.Split('|');
            return long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && f.Length >= 2 && long.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out master);
        }

        private static List<string> Items(BookData b)
        {
            var items = new List<string>();
            items.AddRange(b.NewEntries.Select(e => "e" + e));
            items.AddRange(b.NewCounts.Select(kv => "c" + kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture)));
            if (b.NewMissions != null) items.Add("m" + b.NewMissions);
            return items;
        }

        private static bool SplitCount(string text, out string what, out int n)
        {
            int eq = text.LastIndexOf('=');
            what = eq > 0 ? text.Substring(0, eq) : null;
            n = 0;
            return eq > 0 && int.TryParse(text.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        }

        private static void Send(BookData b, IEnumerable<string> items = null)
        {
            List<string> list = items?.Where(x => x.Length > 0).ToList();
            if (list == null) { list = Items(b); Clear(b); }
            if (list.Count == 0) return;
            Invoke(ZRoutedRpc.Everybody, RpcNews, Head(b) + Item + string.Join(Item.ToString(), list));
        }

        private static void OnNews(long sender, string payload)
        {
            if (sender == ZDOMan.GetSessionID()) return;
            string[] items = (payload ?? "").Split(Item);
            if (!ParseHead(items[0], out long id, out long master)) return;
            BookData b;
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == master) b = Of(id, null, master);
            else if (!Books.TryGetValue(id, out b)) return;
            Apply(b, items.Skip(1));
        }

        private static void Apply(BookData b, IEnumerable<string> items)
        {
            foreach (string it in items)
            {
                if (it.Length == 0) continue;
                string rest = it.Substring(1);
                if (it[0] == 'e') Entry(b, rest);
                else if (it[0] == 'm') { b.Missions = rest; b.Dirty = true; }
                else if (it[0] == 'c' && SplitCount(rest, out string what, out int n)) Count(b, what, n);
            }
        }

        private static void Ask(BookData b)
        {
            if (Time.time - b.AskedAt < 5f || ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.GetPlayerList().Count < 2) return;
            b.AskedAt = Time.time;
            Invoke(ZRoutedRpc.Everybody, RpcAsk, Head(b));
        }

        private static void OnAsk(long sender, string payload)
        {
            if (sender == ZDOMan.GetSessionID() || !ParseHead(payload ?? "", out long id, out long master)) return;
            BookData b;
            if (Player.m_localPlayer != null && master != 0L && Player.m_localPlayer.GetPlayerID() == master) b = Of(id, null, master);
            else
            {
                Humanoid c = Companion.All().FirstOrDefault(x => Companion.IdOf(x) == id);
                if (c == null || !c.GetComponent<ZNetView>().IsOwner() || !Books.TryGetValue(id, out b) || !b.Fresh || Online().Contains(b.MasterName)) return;
            }
            if (!b.Fresh) return;
            Invoke(sender, RpcWhole, string.Join(Field.ToString(), id.ToString(CultureInfo.InvariantCulture), b.JournalText, b.TallyText, b.Missions));
        }

        private static void OnWhole(long sender, string payload)
        {
            string[] f = (payload ?? "").Split(Field);
            if (f.Length < 4 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) || !Books.TryGetValue(id, out BookData b) || b.Fresh) return;
            Fill(b, f[1], f[2], f[3]);
            foreach (string e in b.NewEntries) Entry(b, e);
            foreach (var kv in b.NewCounts) Count(b, kv.Key, kv.Value);
            if (b.NewMissions != null) b.Missions = b.NewMissions;
            b.Fresh = true;
        }

        private static void Invoke(long target, string rpc, string payload)
        {
            try { ZRoutedRpc.instance?.InvokeRoutedRPC(target, rpc, payload); }
            catch (Exception e) { VillagePlebsPlugin.Instance?.Warn($"Could not send {rpc}: {e.Message}"); }
        }
    }
}
